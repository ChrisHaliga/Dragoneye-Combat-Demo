using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex.Systems;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Runs the fight on the server: starts the rounds, executes actions, kills creatures, and
    /// decides when it is over.
    ///
    /// Every rule it applies lives somewhere pure -- <see cref="CombatRules"/>,
    /// <see cref="ActionResolver"/>, <see cref="TurnOrder"/>, <see cref="HexPathfinder"/>,
    /// <see cref="ICreatureBrain"/>. This is the part that cannot be pure: it touches replicated
    /// state, spawns and despawns, and paces the computer's turns so they can be watched.
    ///
    /// Three things it used to do itself are done by collaborators now, each behind an interface
    /// that names exactly what it needs from here: <see cref="ClashConductor"/> runs a clash from
    /// swing to reveal, <see cref="OpportunityConductor"/> holds an action back while the creatures
    /// it walks away from decide whether to swing, and <see cref="BrainTurnRunner"/> paces a
    /// computer creature's turn. What is left is what happens on the board.
    ///
    /// Server only. Clients ask for actions through <see cref="UnitCommands"/> and read the result.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatDirector : MonoBehaviour, IClashHost, IOpportunityHost, IBrainHost
    {
        [SerializeField, Tooltip("Every creature on the board.")]
        CreatureRegistry m_Creatures;

        [SerializeField, Tooltip("Occupancy, for costing routes.")]
        UnitIndex m_Units;

        [SerializeField, Tooltip("The arena being fought over.")]
        ArenaMap m_Map;

        [SerializeField, Min(0f), Tooltip("Pause between a computer creature's actions, so a turn "
             + "can be followed rather than resolving in a single frame.")]
        float m_BrainActionDelay = 0.45f;

        [SerializeField, Min(0f), Tooltip("Extra pause after a computer creature uses a skill, so "
             + "what it did can be read before the next thing happens.")]
        float m_BrainSkillDwell = 1.6f;

        [SerializeField, Min(0f), Tooltip("Longest a turn will wait for a unit to finish walking "
             + "before carrying on regardless.")]
        float m_MoveWaitLimit = 4f;

        ArenaBoard m_Board;
        ClashConductor m_Clashes;
        OpportunityConductor m_Opportunities;
        BrainTurnRunner m_BrainRunner;
        Coroutine m_BrainTurn;

        /// <summary>The director for the match in progress, or null outside one.</summary>
        public static CombatDirector Current { get; private set; }

        void Awake()
        {
            Current = this;
            m_Board = new ArenaBoard(m_Map, m_Units);
            m_Clashes = new ClashConductor(this);
            m_Opportunities = new OpportunityConductor(this, m_Creatures);

            // Swapped wholesale to change the opponent. Not serialised: brains are code, not
            // assets, and a ScriptableObject wrapper would be indirection for a choice nobody is
            // authoring yet.
            m_BrainRunner = new BrainTurnRunner(this, new BasicBrain(), m_Creatures, m_Board,
                m_BrainActionDelay, m_BrainSkillDwell, m_MoveWaitLimit);
        }

        void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        /// <summary>Whether an attack is waiting on somebody's answer.</summary>
        public bool IsClashPending => m_Clashes != null && m_Clashes.IsPending;

        /// <summary>
        /// Whether the fight is stopped on anybody's question at all.
        ///
        /// Wider than <see cref="IsClashPending"/>: between one swing settling and the next being
        /// offered there is no clash open, and a creature that read only the clash would take that
        /// gap as its turn resuming -- and act in the middle of its own interrupted move.
        /// </summary>
        public bool IsBusy => IsClashPending || (m_Opportunities != null && m_Opportunities.IsPending);

        /// <summary>Whether this creature is stood next to that one and looking at it.</summary>
        public static bool Watches(CreatureState watcher, CreatureState mover) =>
            OpportunityConductor.Watches(watcher, mover);

        /// <summary>Whether that creature walking to this tile would give this one a swing.</summary>
        public static bool Provokes(CreatureState watcher, CreatureState mover, Hex destination) =>
            OpportunityConductor.Provokes(watcher, mover, destination);

        // ---------- the match ----------

        /// <summary>Server only. Opens the fight once every creature is on the board.</summary>
        public void ServerBeginMatch()
        {
            if (!IsServer || TurnState.Current == null || m_Creatures == null)
            {
                return;
            }

            var combatants = new List<Combatant>();

            foreach (var creature in m_Creatures.All)
            {
                if (creature != null && creature.IsAlive)
                {
                    combatants.Add(new Combatant(creature.TurnId, creature.Speed));
                }
            }

            TurnState.Current.ServerBegin(combatants, IsStillFighting);
            BeginTurn();
        }

        /// <summary>
        /// Server only. Ends the active creature's turn and passes play on.
        ///
        /// The only way a turn ends. There is deliberately no automatic end when AP runs out: a
        /// player may want to hold what is left, and taking the decision away would foreclose
        /// reactions later. The HUD prompts instead.
        /// </summary>
        public void ServerEndTurn()
        {
            if (!IsServer || TurnState.Current == null || TurnState.Current.IsOver)
            {
                return;
            }

            // Not while somebody is being asked anything. The turn does not belong entirely to the
            // active player any more, and ending it out from under a defender mid-decision would
            // resolve their clash into a turn that had already moved on.
            if (IsBusy)
            {
                return;
            }

            StopBrainTurn();

            if (!TurnState.Current.ServerAdvance(IsStillFighting))
            {
                ResolveOutcome();
                return;
            }

            BeginTurn();
        }

        void BeginTurn()
        {
            var active = ActiveCreature();

            if (active == null)
            {
                return;
            }

            active.ServerRefillAp();

            // Toughness. Health comes back a little every turn and armour never does, which is
            // the whole difference between the two bars.
            if (active.Regen > 0)
            {
                var healed = active.ServerHeal(active.Regen);

                if (healed > 0)
                {
                    CombatAnnouncer.Current?.ServerRecovered(active.TurnId, healed);
                }
            }

            if (active.IsComputerControlled)
            {
                m_BrainTurn = StartCoroutine(m_BrainRunner.Run(active));
            }
        }

        void StopBrainTurn()
        {
            if (m_BrainTurn != null)
            {
                StopCoroutine(m_BrainTurn);
                m_BrainTurn = null;
            }
        }

        // ---------- moving ----------

        /// <summary>
        /// Server only. Walks a creature, and turns it.
        ///
        /// The facing is part of the move rather than a follow-up, which DE-006 is explicit about:
        /// there is no turn action, so a creature that could move and then turn for free would have
        /// one. A caller that does not care which way it ends up facing gets the direction of
        /// travel, which is what a creature that walked somewhere is looking at.
        ///
        /// Anybody the walk leaves behind gets their swing first. The move is put away and taken
        /// out again once they have all had their answer, whether it landed or not.
        /// </summary>
        public bool ServerMove(CreatureState actor, Hex destination, Facing? facing = null)
        {
            if (!CanAct(actor))
            {
                return false;
            }

            // Priced before anybody is asked anything. An action that was going to be refused
            // must not cost its owner a round of swings on the way to being refused -- that would
            // make an unaffordable move into a way of draining the elements of everybody adjacent.
            if (!CanAffordMove(actor, destination))
            {
                return false;
            }

            if (m_Opportunities.TryInterrupt(PendingAction.Move(actor, destination, facing)))
            {
                return true;
            }

            return PerformMove(actor, destination, facing);
        }

        /// <summary>
        /// Whether this creature could walk there right now, without walking there.
        ///
        /// The same arithmetic <see cref="PerformMove"/> does, asked before the move is suspended.
        /// </summary>
        bool CanAffordMove(CreatureState actor, Hex destination)
        {
            var cost = m_Board.CostTo(actor.Cell, destination);

            var plan = ActionResolver.Resolve(true, true, actor.CurrentAp,
                targetOccupied: false, moveSteps: cost, stepCost: actor.StepCost);

            return plan.IsAllowed && plan.Action == BoardAction.Move;
        }

        /// <summary>
        /// The move itself, once nobody is owed a swing at it.
        ///
        /// Re-costs the route rather than trusting the requested destination, so a client that asks
        /// for a hex it cannot afford is refused with the same arithmetic the cursor showed it.
        /// </summary>
        public bool PerformMove(CreatureState actor, Hex destination, Facing? facing)
        {
            if (actor == null || !actor.IsAlive)
            {
                return false;
            }

            var cost = m_Board.CostTo(actor.Cell, destination);

            var plan = ActionResolver.Resolve(true, true, actor.CurrentAp,
                targetOccupied: false, moveSteps: cost, stepCost: actor.StepCost);

            if (!plan.IsAllowed || plan.Action != BoardAction.Move)
            {
                return false;
            }

            if (!actor.ServerSpendAp(plan.Cost))
            {
                return false;
            }

            // Read before the move, because afterwards the two hexes are the same one and the
            // bearing between them is meaningless.
            var travelled = ThreatGeometry.Bearing(actor.Cell, destination);

            actor.Unit.ServerSetCell(destination);
            actor.ServerFace(facing ?? travelled);
            return true;
        }

        // ---------- skills ----------

        /// <summary>
        /// Server only. Uses a skill, spending both costs and applying the effect.
        ///
        /// Both costs come off together or neither does: AP is taken first, and the element spend
        /// is checked before it so a refused element cannot leave the AP gone. DE-002 requires a
        /// creature that cannot pay either cost to be unable to use the skill at all.
        /// </summary>
        public bool ServerUseSkill(CreatureState actor, int skillId, Hex target,
            out SkillRefusal refusal, Element? element = null) =>
            UseSkill(actor, skillId, target, out refusal, element, provoked: false);

        /// <summary>
        /// The skill, replayed from the top once nobody is owed a swing at its approach.
        ///
        /// Everything it checks may have changed while the swings landed -- health, action points,
        /// who is standing where -- so it is checked again. What it is not asked again is whether
        /// anybody wants a swing: they have all had one. The first cut of this replayed through the
        /// same door it came in by, and the same watchers were asked twice about one walk.
        /// </summary>
        public void ReplaySkill(CreatureState actor, int skillId, Hex target, Element element) =>
            UseSkill(actor, skillId, target, out _, element, provoked: true);

        bool UseSkill(CreatureState actor, int skillId, Hex target, out SkillRefusal refusal,
            Element? element, bool provoked)
        {
            refusal = SkillRefusal.NoSkill;

            if (!CanAct(actor))
            {
                refusal = SkillRefusal.NotYourTurn;
                return false;
            }

            var commands = actor.GetComponent<SkillCommands>();
            var pool = actor.GetComponent<CreaturePool>();

            if (commands == null || pool == null || !commands.TryGetSkill(skillId, out var skill))
            {
                return false;
            }

            // The choice settled before anything reads the skill. Everything downstream -- the cost
            // check, the commitment, the clash -- asks for one element, so resolving the pick here
            // means none of it has to know that skills with options exist.
            skill = SkillRules.Settle(skill, element, pool.ServerLedger);

            if (skill == null)
            {
                refusal = SkillRefusal.NotEnoughElement;
                return false;
            }

            var occupant = TargetAt(target);

            // A skill that has to walk into range is a walk, and it provokes like one. Checked
            // before anything is spent, so an interrupted skill can be replayed from the top.
            if (!provoked && WouldApproach(actor, skill, target, out var approach)
                && CanAffordApproach(actor, skill, target, pool)
                && m_Opportunities.TryInterrupt(
                    PendingAction.Skill(actor, skillId, target, approach, skill.Element)))
            {
                refusal = SkillRefusal.None;
                return true;
            }

            // Walking into range is part of using a skill, not a separate order the client sends
            // first. Doing it here is what keeps the promise the cursor made -- "Strike, 1.5 + 1
            // AP" is one decision, and a client that could send the two halves separately could be
            // interrupted between them and left standing in the open having paid for nothing.
            if (!ServerCloseTo(actor, skill, target, out refusal))
            {
                return false;
            }

            refusal = SkillRules.Check(skill, true, actor.CurrentAp, pool.ServerLedger,
                Describe(actor, occupant, target));

            if (refusal != SkillRefusal.None)
            {
                return false;
            }

            // Element first: it is the cost that can still fail on a race, and spending AP before
            // discovering that would charge for an action that never happened.
            //
            // Committed rather than spent outright. A spend announces itself to everybody, and an
            // attacker whose commitment appeared in their public reveal record the moment they
            // swung has already told the defender what is coming -- which is the one thing DE-005
            // exists to prevent. It is announced at the reveal, with the defender's.
            if (skill.ElementCost > 0
                && !pool.ServerCommit(skill.Element, skill.ElementCost, out _))
            {
                refusal = SkillRefusal.NotEnoughElement;
                return false;
            }

            if (!actor.ServerSpendAp(skill.ApCost))
            {
                refusal = SkillRefusal.NotEnoughAp;
                return false;
            }

            // Turning to strike is part of striking. DE-006: the attacker ends up facing whoever
            // they swung at, which opens their own flank to everybody they did not.
            if (occupant != null && occupant != actor)
            {
                actor.ServerFace(ThreatGeometry.Bearing(actor.Cell, target));
            }

            // A shot rolls before anybody answers it. The element is committed already -- the
            // arrow has left the bow -- so a miss spends it and shows it, and the defender is
            // never asked about an attack that did not arrive.
            if (skill.RollsToHit && IsContested(skill, actor, occupant))
            {
                var distance = Hex.Distance(actor.Cell, target);
                var cover = LineOfFire.CoverCount(actor.Cell, target, m_Units);
                var chance = SkillRules.HitChance(skill, distance, cover);

                if (!SkillRules.Hits(skill, distance, cover, Random.value))
                {
                    pool.ServerAnnounceCommitted();
                    commands.ServerRecordUse(skill.Id);
                    CombatAnnouncer.Current?.ServerMissed(actor.TurnId, skill.Id, occupant.TurnId,
                        chance);
                    return true;
                }
            }

            if (IsContested(skill, actor, occupant))
            {
                m_Clashes.Begin(actor, skill, occupant);
                return true;
            }

            LandUncontested(actor, skill, occupant);
            return true;
        }

        /// <summary>
        /// Whether using this skill on that target means walking first, and to where.
        ///
        /// The tile is the cheapest one the target is in reach from, which is the tile the walk
        /// will end on -- and so the tile the watchers care about.
        /// </summary>
        bool WouldApproach(CreatureState actor, SkillSpec skill, Hex target, out Hex approach)
        {
            approach = actor.Cell;

            if (skill.Target == SkillTarget.Self
                || CombatRules.InRange(Hex.Distance(actor.Cell, target), skill.Range))
            {
                return false;
            }

            return m_Board.TryTileInReach(actor.Cell, target, skill.Range, out approach, out _)
                && approach != actor.Cell;
        }

        /// <summary>
        /// Whether the walk and the skill together are affordable, before either happens.
        ///
        /// Only asked to decide whether the approach is worth interrupting. The real checks still
        /// run afterwards, on the replay; this exists so an unaffordable order cannot be used to
        /// bait swings out of everybody standing next to you.
        /// </summary>
        bool CanAffordApproach(CreatureState actor, SkillSpec skill, Hex target, CreaturePool pool)
        {
            var steps = m_Board.StepsToReach(actor.Cell, target, skill.Range);

            if (steps < 0
                || actor.CurrentAp < CombatRules.MoveCost(steps, actor.StepCost) + skill.ApCost)
            {
                return false;
            }

            return pool == null
                || SkillRules.CheckAffordable(skill, true, actor.CurrentAp, pool.ServerLedger)
                    == SkillRefusal.None;
        }

        /// <summary>
        /// Moves the actor to somewhere this skill would reach, if it does not already.
        ///
        /// Affordability is checked against the whole price before a single step is taken. A
        /// creature that walks halfway and then discovers it cannot pay has spent its turn on
        /// nothing, which is exactly the failure the combined plan exists to avoid.
        ///
        /// Self-directed skills never move: the one place they reach from is where the creature is
        /// standing.
        /// </summary>
        bool ServerCloseTo(CreatureState actor, SkillSpec skill, Hex target, out SkillRefusal refusal)
        {
            refusal = SkillRefusal.None;

            if (skill.Target == SkillTarget.Self
                || CombatRules.InRange(Hex.Distance(actor.Cell, target), skill.Range))
            {
                return true;
            }

            var steps = m_Board.StepsToReach(actor.Cell, target, skill.Range);

            if (steps < 0)
            {
                refusal = SkillRefusal.OutOfRange;
                return false;
            }

            var walk = CombatRules.MoveCost(steps, actor.StepCost);

            if (actor.CurrentAp < walk + skill.ApCost)
            {
                refusal = SkillRefusal.NotEnoughAp;
                return false;
            }

            // The move itself, not ServerMove: whether anybody gets a swing at this walk was
            // decided before the skill was allowed this far.
            return m_Board.TryTileInReach(actor.Cell, target, skill.Range, out var tile, out _)
                && PerformMove(actor, tile, null);
        }

        /// <summary>
        /// Whether using this opens a clash.
        ///
        /// Only an attack on somebody else is contested. A skill aimed at the user or at an ally
        /// has nobody on the other side of it, and a heal that stopped to ask its target whether
        /// they would like to resist it would be a bug with a straight face.
        /// </summary>
        static bool IsContested(SkillSpec skill, CreatureState actor, CreatureState target) =>
            skill != null
            && skill.IsContested
            && target != null
            && target != actor
            && target.IsAlive
            && target.Party != actor.Party;

        // ---------- what the collaborators ask for ----------

        /// <summary>Opens a clash for a swing at somebody walking past.</summary>
        public void BeginClash(CreatureState attacker, SkillSpec skill, CreatureState defender,
            Element? telegraphed) =>
            m_Clashes.Begin(attacker, skill, defender, telegraphed);

        /// <summary>Server only. The defender's answer, arriving from wherever they are.</summary>
        public bool ServerAnswerClash(CreatureState defender, IReadOnlyList<Element> answer,
            out DefenceRefusal refusal)
        {
            refusal = DefenceRefusal.AlreadyResolved;
            return IsServer && m_Clashes.Answer(defender, answer, out refusal);
        }

        /// <summary>Server only. Whether the creature offered a swing takes it.</summary>
        public bool ServerAnswerOpportunity(CreatureState watcher, bool swings) =>
            IsServer && m_Opportunities.Answer(watcher, swings);

        /// <summary>Applies whatever the clash left of the attack.</summary>
        public void LandContested(CreatureState attacker, SkillSpec skill, CreatureState defender,
            SkillEffect effect)
        {
            if (effect.Amount <= 0)
            {
                return;
            }

            if (skill.Effect.Kind == SkillEffectKind.Damage
                && defender.ServerApplyDamage(effect.Amount))
            {
                Kill(defender, attacker);
            }
        }

        /// <summary>
        /// Applies an uncontested skill once it has been paid for.
        ///
        /// Anything aimed at an enemy went through the clash conductor before it got here. What
        /// arrives is the rest: a heal, a breath, a swing at a tile or an ally -- things with
        /// nobody on the other side of them, which land as written.
        /// </summary>
        public void LandUncontested(CreatureState actor, SkillSpec skill, CreatureState target)
        {
            // Uncontested, so there is no window to keep empty: it was used in the open.
            actor.GetComponent<SkillCommands>()?.ServerRecordUse(skill.Id);

            // Returning elements is settled first because the announcement has to name the ones
            // that actually came back -- "regained PYR" is the whole content of the message, and
            // the pool decides how many there were.
            var returned = skill.Effect.Kind == SkillEffectKind.ReturnElement
                ? ReturnElements(actor, skill.Effect.Amount)
                : null;

            // Announced before the effect lands, so a blow that kills reads in the order it
            // happened: the swing, and then the body.
            CombatAnnouncer.Current?.ServerActed(actor.TurnId, skill.Id,
                target != null ? target.TurnId : 0u, target != null && target != actor, returned);

            switch (skill.Effect.Kind)
            {
                case SkillEffectKind.Damage:
                    if (target != null && target.ServerApplyDamage(skill.Effect.Amount))
                    {
                        Kill(target, actor);
                    }

                    break;

                case SkillEffectKind.Heal:
                    actor.ServerHeal(skill.Effect.Amount);
                    break;

                case SkillEffectKind.RestoreAp:
                    actor.ServerRestoreAp(Ap.FromWhole(skill.Effect.Amount));
                    break;

                case SkillEffectKind.ReturnElement:
                    // Already done, above.
                    break;
            }
        }

        /// <summary>A clash is over. A swing taken mid-move lets the move go on.</summary>
        public void ClashSettled() => m_Opportunities.Continue();

        // The brain's door into the fight is the same one a player uses.
        public bool Move(CreatureState actor, Hex destination) => ServerMove(actor, destination);

        public bool UseSkillOn(CreatureState actor, int skillId, CreatureState target) =>
            target != null && target.IsAlive
            && ServerUseSkill(actor, skillId, target.Cell, out _);

        public void EndTurn()
        {
            m_BrainTurn = null;
            ServerEndTurn();
        }

        /// <summary>
        /// Whether this creature may act right now: alive, and the one whose turn it is.
        ///
        /// Ownership and control are the caller's business -- <see cref="UnitCommands"/> checks the
        /// sender -- because the computer's own turns come through here with no client behind them.
        /// </summary>
        public bool CanAct(CreatureState actor) =>
            IsServer
            && actor != null
            && actor.IsAlive
            && !IsBusy
            && TurnState.Current != null
            && TurnState.Current.IsActive(actor);

        // ---------- the watchdogs ----------

        /// <summary>
        /// Notices a defender, or a creature offered a swing, who is no longer there.
        ///
        /// **There is no timer on a decision, deliberately.** A player who takes an hour over a
        /// clash is a player thinking about it, and this game does not measure skill in seconds --
        /// putting a clock on the one genuinely difficult choice in a turn would hand the win to
        /// whoever guesses fastest. An earlier version had one and it was wrong.
        ///
        /// A client that has *gone*, though, is not thinking. That is a closed socket rather than a
        /// slow decision, and the fight cannot wait on it.
        /// </summary>
        void Update()
        {
            if (!IsServer)
            {
                return;
            }

            var defender = m_Clashes.Defender;

            if (m_Clashes.IsPending && defender != null && !defender.IsComputerControlled
                && !IsStillConnected(defender))
            {
                Debug.Log("The defender left mid-clash; the attack resolves unopposed.", this);
                m_Clashes.Abandon();
            }

            var offered = m_Opportunities.Offered;

            if (offered != null && !offered.IsComputerControlled && !IsStillConnected(offered))
            {
                Debug.Log("A creature left while being offered a swing; it declines.", this);
                m_Opportunities.Abandon();
            }
        }

        static bool IsStillConnected(CreatureState creature)
        {
            var manager = NetworkManager.Singleton;

            return manager != null
                && manager.ConnectedClients.ContainsKey(creature.OwnerClientId);
        }

        // ---------- the board ----------

        /// <summary>
        /// Puts spent elements back, oldest first.
        ///
        /// Stops at the first refusal rather than running the loop out: a skill that returns three
        /// when two were spent returns two, and the creature has already paid its AP for the try.
        /// </summary>
        static List<Element> ReturnElements(CreatureState actor, int count)
        {
            var returned = new List<Element>();
            var pool = actor.GetComponent<CreaturePool>();

            if (pool == null)
            {
                return returned;
            }

            for (var i = 0; i < count && pool.ServerReturn(out var element, out _); i++)
            {
                returned.Add(element);
            }

            return returned;
        }

        CreatureState TargetAt(Hex hex) =>
            m_Units != null && m_Units.TryGet(hex, out var occupant)
                ? occupant.GetComponent<CreatureState>()
                : null;

        /// <summary>What the rules need to know about whatever is being aimed at.</summary>
        static SkillTargetInfo Describe(CreatureState actor, CreatureState target, Hex hex)
        {
            var distance = Hex.Distance(actor.Cell, hex);

            return target == null
                ? SkillTargetInfo.Tile(distance)
                : SkillTargetInfo.Creature(distance, target == actor,
                    target.Party == actor.Party, target.IsAlive);
        }

        /// <summary>
        /// Takes a dead creature off the board, and pays whoever put it there.
        ///
        /// The killer earns the victim's level, and only a character its owner brought can keep it:
        /// a premade somebody claimed for the afternoon is not theirs to level. Removed from the
        /// order before despawning, because the despawn tears down the component the order would
        /// otherwise be asked about.
        /// </summary>
        void Kill(CreatureState creature, CreatureState killer)
        {
            AwardXp(killer, creature);

            // Before the despawn, which takes the name with it.
            CombatAnnouncer.Current?.ServerFell(creature.TurnId);

            TurnState.Current?.ServerRemove(creature.TurnId);

            var networkObject = creature.GetComponent<NetworkObject>();

            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn();
            }

            ResolveOutcome();
        }

        static void AwardXp(CreatureState killer, CreatureState victim)
        {
            var characters = PlayerCharacters.Current;

            if (killer == null || victim == null || characters == null
                || !killer.IsPlayerCharacter || killer.Party == victim.Party)
            {
                return;
            }

            characters.ServerAwardXp(killer.BuildSlot, Progression.XpForKill(victim.Level),
                killer.TurnId);
        }

        /// <summary>Ends the match when only one side is left standing.</summary>
        void ResolveOutcome()
        {
            if (TurnState.Current == null || TurnState.Current.IsOver)
            {
                return;
            }

            var survivors = new List<Party>();

            foreach (var creature in m_Creatures.All)
            {
                if (creature != null && creature.IsAlive && !survivors.Contains(creature.Party))
                {
                    survivors.Add(creature.Party);
                }
            }

            // Zero survivors is a draw with nobody to award it to; treating it as "not over" would
            // hang the match, so the last party to hold the field is close enough for an MVP and the
            // case is reachable only if a creature could kill itself.
            if (survivors.Count == 1)
            {
                StopBrainTurn();
                TurnState.Current.ServerDeclareWinner(survivors[0]);
            }
            else if (survivors.Count == 0)
            {
                StopBrainTurn();
                TurnState.Current.ServerDeclareWinner(Party.Heroes);
            }
        }

        bool IsStillFighting(uint turnId)
        {
            var creature = CreatureFor(turnId);
            return creature != null && creature.IsAlive;
        }

        CreatureState ActiveCreature() =>
            TurnState.Current != null ? CreatureFor(TurnState.Current.ActiveId) : null;

        CreatureState CreatureFor(uint turnId) =>
            m_Creatures != null ? m_Creatures.ByTurnId(turnId) : null;
    }
}
