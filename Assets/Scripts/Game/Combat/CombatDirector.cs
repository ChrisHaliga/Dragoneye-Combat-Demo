using System.Collections;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Hex.Systems;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Runs the fight on the server: starts the rounds, executes actions, kills creatures, takes the
    /// computer's turns and decides when it is over.
    ///
    /// Every rule it applies lives somewhere pure -- <see cref="CombatRules"/>,
    /// <see cref="ActionResolver"/>, <see cref="TurnOrder"/>, <see cref="HexPathfinder"/>,
    /// <see cref="ICreatureBrain"/>. This is the part that cannot be pure: it touches replicated
    /// state, spawns and despawns, and waits between AI actions so a turn is watchable. Keeping the
    /// decisions out of it is what keeps that testable.
    ///
    /// Server only. Clients ask for actions through <see cref="UnitCommands"/> and read the result.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatDirector : MonoBehaviour
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

        /// <summary>
        /// Swapped wholesale to change the opponent. Not serialised: brains are code, not assets,
        /// and a ScriptableObject wrapper would be indirection for a choice nobody is authoring yet.
        /// </summary>
        readonly ICreatureBrain m_Brain = new BasicBrain();

        ArenaBoard m_Board;
        Coroutine m_BrainTurn;

        // The attack that is waiting on an answer, and everything needed to finish it. Server only
        // -- a clash is decided where the fight is run, and the sequence itself is what decides it.
        ClashSequence m_Clash;
        CreatureState m_ClashAttacker;
        CreatureState m_ClashDefender;
        SkillSpec m_ClashSkill;

        // Creatures already complained about, so a toothless one does not warn every round.
        readonly HashSet<uint> m_Warned = new HashSet<uint>();

        /// <summary>The director for the match in progress, or null outside one.</summary>
        public static CombatDirector Current { get; private set; }

        void Awake()
        {
            Current = this;
            m_Board = new ArenaBoard(m_Map, m_Units);
        }

        void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        /// <summary>
        /// Server only. Opens the fight once every creature is on the board.
        /// </summary>
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

            // Not while somebody is being asked to answer an attack. The turn does not belong
            // entirely to the active player any more, and ending it out from under a defender
            // mid-decision would resolve their clash into a turn that had already moved on.
            if (IsClashPending)
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

        /// <summary>
        /// Server only. Moves a creature along the cheapest route, charging a point of AP per step.
        ///
        /// Re-costs the route rather than trusting the requested destination, so a client that asks
        /// for a hex it cannot afford is refused with the same arithmetic the cursor showed it.
        /// </summary>
        /// <returns>False if the move was refused.</returns>
        /// <summary>
        /// Server only. Walks a creature, and turns it.
        ///
        /// The facing is part of the move rather than a follow-up, which DE-006 is explicit about:
        /// there is no turn action, so a creature that could move and then turn for free would have
        /// one. A caller that does not care which way it ends up facing gets the direction of
        /// travel, which is what a creature that walked somewhere is looking at.
        /// </summary>
        public bool ServerMove(CreatureState actor, Hex destination, Facing? facing = null)
        {
            if (!CanAct(actor))
            {
                return false;
            }

            var cost = m_Board.CostTo(actor.Cell, destination);

            var plan = ActionResolver.Resolve(true, true, actor.CurrentAp,
                targetOccupied: false, moveSteps: cost);

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
            var travelled = Bearing(actor.Cell, destination);

            actor.Unit.ServerSetCell(destination);
            actor.ServerFace(facing ?? travelled);
            return true;
        }

        /// <summary>
        /// Server only. Uses a skill, spending both costs and applying the effect.
        ///
        /// Both costs come off together or neither does: AP is taken first, and the element spend
        /// is checked before it so a refused element cannot leave the AP gone. DE-002 requires a
        /// creature that cannot pay either cost to be unable to use the skill at all.
        /// </summary>
        public bool ServerUseSkill(CreatureState actor, int skillId, Hex target,
            out SkillRefusal refusal)
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

            var occupant = TargetAt(target);

            // Walking into range is part of using a skill, not a separate order the client sends
            // first. Doing it here is what keeps the promise the cursor made -- "Strike, 1.5 + 1
            // AP" is one decision, and a client that could send the two halves separately could be
            // interrupted between them and left standing in the open having paid for nothing.
            if (!ServerCloseTo(actor, skill, target, out refusal))
            {
                return false;
            }

            refusal = SkillRules.Check(skill, true, actor.CurrentAp, pool.Ledger,
                Describe(actor, occupant, target));

            if (refusal != SkillRefusal.None)
            {
                return false;
            }

            // Element first: it is the cost that can still fail on a race, and spending AP before
            // discovering that would charge for an action that never happened.
            if (skill.ElementCost > 0
                && !pool.ServerSpend(skill.Element, skill.ElementCost, out _))
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
                actor.ServerFace(Bearing(actor.Cell, target));
            }

            if (IsContested(skill, actor, occupant))
            {
                BeginClash(actor, skill, occupant);
                return true;
            }

            Resolve(actor, skill, occupant);
            return true;
        }

        /// <summary>Which way one hex lies from another, as a facing.</summary>
        static Facing Bearing(Hex from, Hex to) => Facing.Of((int)Hex.DirectionTo(from, to));

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

        /// <summary>
        /// Suspends the attack and asks the defender.
        ///
        /// The attacker's element has already left their pool by now -- DE-005 spends the
        /// commitment before anybody is asked anything, so an attack cannot be taken back once the
        /// defender has been made to think about it.
        ///
        /// What this holds is a <see cref="ClashSequence"/>, which is where every decision about
        /// the clash is made. This only carries messages to it and applies what it says.
        /// </summary>
        void BeginClash(CreatureState actor, SkillSpec skill, CreatureState target)
        {
            var pool = target.GetComponent<CreaturePool>();

            if (pool == null)
            {
                Resolve(actor, skill, target);
                return;
            }

            // Which way the blow arrived, from the defender's point of view.
            var flanked = FacingRules.IsFlank(target.Facing, Bearing(target.Cell, actor.Cell));

            var committed = new List<Element>();

            for (var i = 0; i < skill.ElementCost; i++)
            {
                committed.Add(skill.Element);
            }

            m_Clash = ClashSequence.Begin(committed,
                new ClashSide((int)actor.TurnId, advantage: actor.HasAdvantage),
                new ClashSide((int)target.TurnId, advantage: target.HasAdvantage,
                    disadvantage: flanked),
                pool.Ledger, ElementMatchups.Table);

            m_ClashAttacker = actor;
            m_ClashDefender = target;
            m_ClashSkill = skill;

            Ask(m_Clash.Request, target);
        }

        /// <summary>
        /// Puts the question to whoever is running the defender.
        ///
        /// A computer defender answers from <see cref="BasicBrain.Defend"/>, which is handed the
        /// prompt and its own pool and nothing else -- so it cannot answer better than a player
        /// could for want of information a player would not have. That is a property of the
        /// signature rather than of anybody's restraint.
        /// </summary>
        void Ask(DefenceRequest request, CreatureState defender)
        {
            if (!request.HasAnswer)
            {
                // Nothing to answer with. DE-005: the attack resolves unopposed rather than
                // stopping to ask a question with no answers on it.
                SettleClash(null, declined: true);
                return;
            }

            if (defender.IsComputerControlled)
            {
                // Through the same door a player's answer comes in by. This used to settle the
                // clash directly, which skipped committing the answer to the sequence -- so it was
                // still waiting when the reveal was asked for, and no computer creature ever took
                // any damage at all. Two callers doing different halves of one job.
                ServerAnswerClash(defender, ChooseDefence(defender, request), out _);
                return;
            }

            if (ClashCommands.Current != null)
            {
                ClashCommands.Current.ServerAsk(request, defender);
                return;
            }

            Debug.LogWarning("No clash commands in the arena; the attack resolves unopposed.", this);
            SettleClash(null, declined: true);
        }

        /// <summary>
        /// Server only. The defender's answer, arriving from wherever they are.
        ///
        /// Checked against the sequence rather than trusted: an answer naming elements the defender
        /// does not hold, or more than were asked for, is refused there and the clash stays open.
        /// </summary>
        public bool ServerAnswerClash(CreatureState defender, IReadOnlyList<Element> answer,
            out DefenceRefusal refusal)
        {
            refusal = DefenceRefusal.None;

            if (!IsServer || m_Clash == null || defender != m_ClashDefender)
            {
                refusal = DefenceRefusal.AlreadyResolved;
                return false;
            }

            if (answer == null || answer.Count == 0)
            {
                SettleClash(null, declined: true);
                return true;
            }

            var pool = defender.GetComponent<CreaturePool>();

            // Committed before anything is spent, so an answer the sequence refuses costs nothing
            // and the clash stays open for a better one.
            if (pool == null || !m_Clash.TryCommit(answer, pool.Ledger, out refusal))
            {
                return false;
            }

            foreach (var element in answer)
            {
                pool.ServerSpend(element, 1, out _);
            }

            SettleClash(answer, declined: false);
            return true;
        }

        /// <summary>What a computer creature puts up. See <see cref="ClashDefence"/>.</summary>
        static IReadOnlyList<Element> ChooseDefence(CreatureState defender, DefenceRequest request)
        {
            var pool = defender.GetComponent<CreaturePool>();

            return ClashDefence.Choose(request, pool != null ? pool.Pool : default);
        }

        /// <summary>
        /// Spends what the defender put up, reveals both sides, and applies what is left of the
        /// attack.
        ///
        /// The defender's spend happens here rather than when they chose, because DE-005 wants each
        /// side's expenditure emitted after that side's own reveal -- and because an answer refused
        /// by the sequence must not have cost anything.
        /// </summary>
        void SettleClash(IReadOnlyList<Element> answer, bool declined)
        {
            var clash = m_Clash;
            var attacker = m_ClashAttacker;
            var defender = m_ClashDefender;
            var skill = m_ClashSkill;

            // Cleared before anything else can run: applying the effect can kill a creature, which
            // ends the match, and a clash still standing at that point would suspend the next one.
            m_Clash = null;
            m_ClashAttacker = null;
            m_ClashDefender = null;
            m_ClashSkill = null;

            if (clash == null || attacker == null || defender == null || skill == null)
            {
                return;
            }

            if (declined)
            {
                clash.Decline();
            }

            if (!clash.TryReveal(out var reveal))
            {
                return;
            }

            AnnounceClash(attacker, defender, reveal);
            ResolveContested(attacker, skill, defender, clash.Scale(skill.Effect));

            // The attack is over, so whatever the pause was holding up can go on.
            ClashCommands.Current?.ServerClearPrompt();
        }

        /// <summary>What both sides put up, over the heads of the two creatures that put it up.</summary>
        static void AnnounceClash(CreatureState attacker, CreatureState defender, ClashReveal reveal)
        {
            CombatNotices.Raise(attacker.TurnId, ClashLabels.Committed(reveal.Attacker),
                NoticeTone.Loss);

            if (reveal.Defender.Count > 0)
            {
                CombatNotices.Raise(defender.TurnId, ClashLabels.Committed(reveal.Defender),
                    NoticeTone.Loss);
            }

            CombatNotices.Raise(defender.TurnId, ClashLabels.Describe(reveal.Outcome),
                reveal.Outcome == ClashOutcome.AttackerWins ? NoticeTone.Loss : NoticeTone.Gain);
        }

        /// <summary>
        /// Applies whatever the clash left of the attack.
        ///
        /// A separate path from <see cref="Resolve"/> only because the effect has been scaled and
        /// the target is already known; everything it can do, that does too.
        /// </summary>
        void ResolveContested(CreatureState actor, SkillSpec skill, CreatureState target,
            SkillEffect effect)
        {
            if (effect.Amount <= 0)
            {
                return;
            }

            if (skill.Effect.Kind == SkillEffectKind.Damage
                && target.ServerApplyDamage(effect.Amount, ReductionOf(target)))
            {
                Kill(target, actor);
            }
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

            var walk = CombatRules.MoveCost(steps);

            if (actor.CurrentAp < walk + skill.ApCost)
            {
                refusal = SkillRefusal.NotEnoughAp;
                return false;
            }

            return ServerWalkToReach(actor, skill, target);
        }

        /// <summary>
        /// Takes the cheapest route to somewhere within reach, one step at a time.
        ///
        /// The destination is recomputed rather than remembered, because <see cref="ServerMove"/>
        /// prices its own route and the cheapest tile to end on is the one the search already found.
        /// Stops the moment the target is in reach, which is what stops a bow walking into melee.
        /// </summary>
        bool ServerWalkToReach(CreatureState actor, SkillSpec skill, Hex target)
        {
            var best = default(Hex);
            var bestSteps = int.MaxValue;

            foreach (var candidate in Hex.Range(target, skill.Range))
            {
                if (candidate == target || m_Board.IsOccupied(candidate))
                {
                    continue;
                }

                var steps = m_Board.CostTo(actor.Cell, candidate);

                if (steps < 0 || steps >= bestSteps)
                {
                    continue;
                }

                bestSteps = steps;
                best = candidate;
            }

            return bestSteps != int.MaxValue && ServerMove(actor, best);
        }

        /// <summary>
        /// Applies a skill once it has been paid for.
        ///
        /// Creature-targeted skills are where a clash begins. Until DE-005 exists the effect lands
        /// directly, which is the same outcome an uncontested clash would produce -- so replacing
        /// this with a real contest is a change to one method rather than to every skill.
        /// </summary>
        void Resolve(CreatureState actor, SkillSpec skill, CreatureState target)
        {
            switch (skill.Effect.Kind)
            {
                case SkillEffectKind.Damage:
                    // The blow and the protection go in together, so what lands and what is
                    // announced over the defender's head are the same subtraction.
                    if (target != null && target.ServerApplyDamage(
                            skill.Effect.Amount, ReductionOf(target)))
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
                    ReturnElements(actor, skill.Effect.Amount);
                    break;
            }
        }

        /// <summary>
        /// Puts spent elements back, oldest first.
        ///
        /// Stops at the first refusal rather than running the loop out: a skill that returns three
        /// when two were spent returns two, and the creature has already paid its AP for the try.
        /// </summary>
        static void ReturnElements(CreatureState actor, int count)
        {
            var pool = actor.GetComponent<CreaturePool>();

            if (pool == null)
            {
                return;
            }

            for (var i = 0; i < count && pool.ServerReturn(out _, out _); i++)
            {
            }
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
        /// Whether this creature may act right now: alive, and the one whose turn it is.
        ///
        /// Ownership and control are the caller's business -- <see cref="UnitCommands"/> checks the
        /// sender -- because the computer's own turns come through here with no client behind them.
        /// </summary>
        bool CanAct(CreatureState actor) =>
            IsServer
            && actor != null
            && actor.IsAlive
            && !IsClashPending
            && TurnState.Current != null
            && TurnState.Current.IsActive(actor);

        /// <summary>
        /// Whether the fight is stopped waiting on somebody's answer.
        ///
        /// DE-005 suspends resolution partway, so a turn is not one uninterrupted stretch of the
        /// active player's own decisions any more. Everything that acts checks this: the attacker
        /// cannot spend the pause taking another action, and the turn cannot end out from under
        /// the defender being asked.
        /// </summary>
        public bool IsClashPending => m_Clash != null;

        void BeginTurn()
        {
            var active = ActiveCreature();
            if (active == null)
            {
                return;
            }

            active.ServerRefillAp();

            if (active.IsComputerControlled)
            {
                WarnIfToothless(active);
                m_BrainTurn = StartCoroutine(RunBrainTurn(active));
            }
        }

        /// <summary>
        /// Plays out a computer creature's turn, one decision at a time.
        ///
        /// A coroutine rather than a loop because the actions should be watchable. The brain is asked
        /// again after every action rather than for a whole plan, so a kill or a blocked route
        /// changes what it does next instead of playing out a stale plan.
        /// </summary>
        IEnumerator RunBrainTurn(CreatureState actor)
        {
            yield return new WaitForSeconds(m_BrainActionDelay);

            // Bounded because a brain that returns an action it cannot perform would otherwise spin
            // forever. The cap is generous enough that hitting it means a bug, and it is logged.
            var budget = 32;

            while (budget-- > 0)
            {
                // A clash suspends the fight, so the brain waits it out rather than reading a
                // stopped turn as a finished one. Breaking here would end its turn in the middle of
                // an attack it had already paid for, while the defender was still being asked.
                // Bounded in practice by the clash watchdog, which settles an unanswered one.
                yield return new WaitWhile(() => IsClashPending);

                if (!CanAct(actor))
                {
                    break;
                }

                var decision = m_Brain.Decide(ViewOf(actor, includeHand: true),
                    OtherViews(actor), m_Board);

                var acted = decision.Action == BrainAction.UseSkill
                    ? UseSkillOn(actor, decision.SkillId, CreatureFor(decision.TargetId))
                    : decision.Action == BrainAction.Move && ServerMove(actor, decision.Destination);

                if (!acted)
                {
                    break;
                }

                // The rules resolved the instant the decision was made; the board has not caught up
                // yet. Waiting for it is the difference between a turn a player can follow and four
                // creatures teleporting at once -- which is what this looked like, because the
                // director never asked whether anything had finished being drawn.
                yield return WalkedIt(actor);

                yield return new WaitForSeconds(decision.Action == BrainAction.UseSkill
                    ? m_BrainSkillDwell
                    : m_BrainActionDelay);
            }

            if (budget <= 0)
            {
                Debug.LogWarning($"{nameof(BasicBrain)} exhausted its action budget; ending the turn.",
                    this);
            }

            m_BrainTurn = null;
            ServerEndTurn();
        }

        /// <summary>
        /// Says so, once, when a computer creature has nothing it could ever do.
        ///
        /// A creature with an empty skill list walks up to somebody and ends its turn, which looks
        /// exactly like a broken brain and is in fact missing content. The premades ship with their
        /// skills authored by the setup step, so the usual cause is that it has not been run.
        /// </summary>
        void WarnIfToothless(CreatureState actor)
        {
            if (!m_Warned.Add(actor.TurnId))
            {
                return;
            }

            var skills = actor.GetComponent<SkillCommands>();

            if (skills != null && skills.Skills.Count > 0)
            {
                return;
            }

            Debug.LogWarning($"{actor.DisplayName} has no skills, so it can only walk. "
                + "Premade creatures are authored by ClaudeCode > Set Up Everything.", this);
        }

        /// <summary>
        /// Waits until this creature has finished walking to where the rules already put it.
        ///
        /// Capped, and tolerant of there being no view at all. A headless server draws nothing and
        /// must not sit here forever waiting for an animation that will never play; a client whose
        /// unit is stuck should lose a second, not the match.
        /// </summary>
        IEnumerator WalkedIt(CreatureState actor)
        {
            var view = actor != null ? actor.GetComponent<UnitView>() : null;

            if (view == null)
            {
                yield break;
            }

            var waited = 0f;

            while (view != null && view.IsMoving && waited < m_MoveWaitLimit)
            {
                waited += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// Notices a defender who is no longer there.
        ///
        /// **There is no timer on a decision, deliberately.** A player who takes an hour over a
        /// clash is a player thinking about it, and this game does not measure skill in seconds --
        /// putting a clock on the one genuinely difficult choice in a turn would hand the win to
        /// whoever guesses fastest. An earlier version had one and it was wrong.
        ///
        /// A client that has *gone*, though, is not thinking. That is a closed socket rather than a
        /// slow decision, and the fight cannot wait on it, so the attack resolves unopposed -- which
        /// is a choice the defender was entitled to make and costs them nothing they still had.
        /// </summary>
        void Update()
        {
            if (!IsServer || m_Clash == null || m_ClashDefender == null
                || m_ClashDefender.IsComputerControlled)
            {
                return;
            }

            var manager = NetworkManager.Singleton;

            if (manager != null
                && manager.ConnectedClients.ContainsKey(m_ClashDefender.OwnerClientId))
            {
                return;
            }

            Debug.Log("The defender left mid-clash; the attack resolves unopposed.", this);
            SettleClash(null, declined: true);
        }

        void StopBrainTurn()
        {
            if (m_BrainTurn != null)
            {
                StopCoroutine(m_BrainTurn);
                m_BrainTurn = null;
            }
        }

        /// <summary>
        /// Takes a dead creature off the board.
        ///
        /// Removed from the order before despawning, because the despawn tears down the component
        /// the order would otherwise be asked about.
        /// </summary>
        /// <summary>
        /// What the defender takes off every blow: its armour, plus anything else it is wearing.
        ///
        /// Resolved from the build rather than replicated, because every peer already has what it
        /// needs to work it out and only the server ever asks.
        /// </summary>
        static int ReductionOf(CreatureState creature)
        {
            var characters = PlayerCharacters.Current;

            if (characters == null || !creature.IsPlayerCharacter)
            {
                // A premade has no equipment to resolve. Authored damage reduction for them is a
                // decision nobody has taken yet, and pretending otherwise would be inventing one.
                return 0;
            }

            var loadout = characters.LoadoutFor(creature.BuildSlot);
            return loadout != null ? loadout.DamageReduction : 0;
        }

        /// <summary>
        /// Takes a dead creature off the board, and pays whoever put it there.
        ///
        /// The killer earns the victim's level, and only a character its owner brought can keep it:
        /// a premade somebody claimed for the afternoon is not theirs to level.
        /// </summary>
        void Kill(CreatureState creature, CreatureState killer)
        {
            AwardXp(killer, creature);

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

        /// <summary>Aims a brain's chosen skill at a creature, by the same path a player takes.</summary>
        bool UseSkillOn(CreatureState actor, int skillId, CreatureState target) =>
            target != null && target.IsAlive
            && ServerUseSkill(actor, skillId, target.Cell, out _);

        /// <summary>
        /// A creature as a brain sees it, including what it can do.
        ///
        /// Skills and elements are only filled in for the creature being asked to decide. Reading
        /// another creature's hand would be the brain cheating, and the pool is private to its
        /// controller for exactly that reason.
        /// </summary>
        BrainView ViewOf(CreatureState creature, bool includeHand = false)
        {
            if (!includeHand)
            {
                return new BrainView(creature.TurnId, creature.Cell, creature.Party,
                    creature.CurrentAp, creature.CurrentHp);
            }

            var skills = creature.GetComponent<SkillCommands>();
            var pool = creature.GetComponent<CreaturePool>();

            return new BrainView(creature.TurnId, creature.Cell, creature.Party,
                creature.CurrentAp, creature.CurrentHp,
                skills != null ? skills.Skills : null,
                pool != null ? pool.Ledger : default);
        }

        List<BrainView> OtherViews(CreatureState actor)
        {
            var views = new List<BrainView>();

            foreach (var creature in m_Creatures.All)
            {
                if (creature != null && creature != actor && creature.IsAlive)
                {
                    views.Add(ViewOf(creature));
                }
            }

            return views;
        }

    }
}
