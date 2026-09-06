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
        bool m_ClashFlanked;

        // The action somebody is halfway through, and who still gets a swing at them for it.
        // Server only, and at most one: nothing else can act while a move is being interrupted.
        PendingAction m_Pending;
        readonly List<CreatureState> m_Watchers = new List<CreatureState>();
        CreatureState m_Offered;

        // Creatures already complained about, so a toothless one does not warn every round.
        readonly HashSet<uint> m_Warned = new HashSet<uint>();

        /// <summary>
        /// A move or a skill use that has been asked for and has not happened yet.
        ///
        /// It waits because leaving a tile somebody is watching gives them a swing at you, and the
        /// swing has to land before the walk does. Both kinds are held here rather than only moves,
        /// because a skill that walks into range is a walk -- and one that provoked nothing while an
        /// ordinary move did would be a way to leave for free.
        /// </summary>
        readonly struct PendingAction
        {
            public const int NoSkill = int.MinValue;

            public readonly CreatureState Actor;
            public readonly Hex Where;
            public readonly Facing? Facing;
            public readonly int SkillId;

            public PendingAction(CreatureState actor, Hex where, Facing? facing, int skillId)
            {
                Actor = actor;
                Where = where;
                Facing = facing;
                SkillId = skillId;
            }

            public bool Exists => Actor != null;

            public bool IsMove => SkillId == NoSkill;
        }

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

            // Priced before anybody is asked anything. An action that was going to be refused
            // must not cost its owner a round of swings on the way to being refused -- that would
            // make an unaffordable move into a way of draining the elements of everybody adjacent.
            if (!CanAffordMove(actor, destination))
            {
                return false;
            }

            // Anybody watching this tile gets their swing first. The move is put away and taken out
            // again once they have all had their answer, whether it landed or not.
            if (!m_Pending.Exists && Interrupt(actor,
                    new PendingAction(actor, destination, facing, PendingAction.NoSkill)))
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
                targetOccupied: false, moveSteps: cost);

            return plan.IsAllowed && plan.Action == BoardAction.Move;
        }

        /// <summary>The move itself, once nobody is owed a swing at it.</summary>
        bool PerformMove(CreatureState actor, Hex destination, Facing? facing)
        {
            if (actor == null || !actor.IsAlive)
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

            // A skill that has to walk into range is a walk, and it provokes like one. Checked
            // before anything is spent, so an interrupted skill can be replayed from the top.
            if (!m_Pending.Exists
                && skill.Target != SkillTarget.Self
                && !CombatRules.InRange(Hex.Distance(actor.Cell, target), skill.Range)
                && CanAffordApproach(actor, skill, target, pool)
                && Interrupt(actor, new PendingAction(actor, target, null, skillId)))
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

            if (steps < 0 || actor.CurrentAp < CombatRules.MoveCost(steps) + skill.ApCost)
            {
                return false;
            }

            return pool == null
                || SkillRules.CheckAffordable(skill, true, actor.CurrentAp, pool.ServerLedger)
                    == SkillRefusal.None;
        }

        /// <summary>Which way one hex lies from another, as a facing.</summary>
        static Facing Bearing(Hex from, Hex to) => Facing.Of((int)Hex.DirectionTo(from, to));

        /// <summary>
        /// Holds an action back while everybody watching this creature decides whether to swing.
        /// </summary>
        /// <returns>True when the action was suspended and will be run later.</returns>
        bool Interrupt(CreatureState actor, PendingAction action)
        {
            CollectWatchers(actor);

            if (m_Watchers.Count == 0)
            {
                return false;
            }

            m_Pending = action;
            AskNextOpportunity();
            return true;
        }

        /// <summary>
        /// Everybody who is next to this creature and looking at it.
        ///
        /// Enemies only, alive, adjacent, with the mover inside the three tiles they are watching
        /// and something left to spend on a swing. Read once, before anything moves, so the list
        /// cannot grow halfway through the queue as creatures turn to face each other.
        /// </summary>
        void CollectWatchers(CreatureState actor)
        {
            m_Watchers.Clear();

            if (m_Creatures == null || actor == null)
            {
                return;
            }

            foreach (var creature in m_Creatures.All)
            {
                if (CanTakeOpportunity(creature, actor))
                {
                    m_Watchers.Add(creature);
                }
            }
        }

        /// <summary>
        /// Whether this creature could swing at that one right now.
        ///
        /// Asked again when each offer goes out as well as when the queue is built: an earlier
        /// swing may have killed the mover, and a creature may have spent its last element
        /// answering one.
        /// </summary>
        static bool CanTakeOpportunity(CreatureState watcher, CreatureState mover)
        {
            if (!Watches(watcher, mover))
            {
                return false;
            }

            var swing = SwingOf(watcher);
            var pool = watcher.GetComponent<CreaturePool>();

            return swing != null && pool != null
                && pool.ServerLedger.CanSpend(swing.Element, swing.ElementCost);
        }

        /// <summary>
        /// The attack this creature would swing with, or null when it has none.
        ///
        /// A built character swings with its weapon; a premade with the first attack it was
        /// authored holding. Both are the same question -- what is in its hand -- asked of the two
        /// places that answer it.
        /// </summary>
        static SkillSpec SwingOf(CreatureState watcher)
        {
            if (watcher == null)
            {
                return null;
            }

            var characters = PlayerCharacters.Current;
            var loadout = watcher.IsPlayerCharacter && characters != null
                ? characters.LoadoutFor(watcher.BuildSlot)
                : null;

            // A built character swings with its weapon and with nothing else -- no weapon is no
            // reaction, which is the rule and not an oversight. A premade has no equipment to ask
            // about, so the first attack it was authored holding stands in for one.
            if (loadout != null)
            {
                return Opportunity.From(Opportunity.PrimaryOf(loadout));
            }

            var commands = watcher.GetComponent<SkillCommands>();

            return commands != null
                ? Opportunity.From(Opportunity.PrimaryOf(commands.Skills))
                : null;
        }

        /// <summary>
        /// Whether this creature is stood next to that one and looking at it.
        ///
        /// Public and without the element check on purpose. The HUD warns a player before they
        /// move, and it cannot see how much an enemy is holding -- that is the whole of DE-005 --
        /// so the warning is about position and facing, which are on the board for anybody to
        /// read. It can therefore warn about a swing that turns out to be unaffordable, which is
        /// the right way round: the alternative is a warning that quietly tells you what is in
        /// somebody hand.
        /// </summary>
        public static bool Watches(CreatureState watcher, CreatureState mover) =>
            watcher != null && mover != null && watcher != mover
            && watcher.IsAlive && mover.IsAlive
            && watcher.Party != mover.Party
            && Hex.Distance(watcher.Cell, mover.Cell) == Opportunity.Range
            && FacingRules.Threatens(watcher.Facing, Bearing(watcher.Cell, mover.Cell));

        /// <summary>
        /// Puts the offer to the next watcher, or runs the held action when there are none left.
        /// </summary>
        void AskNextOpportunity()
        {
            m_Offered = null;

            while (m_Watchers.Count > 0)
            {
                var watcher = m_Watchers[0];
                m_Watchers.RemoveAt(0);

                if (!m_Pending.Exists || !CanTakeOpportunity(watcher, m_Pending.Actor))
                {
                    continue;
                }

                m_Offered = watcher;

                if (watcher.IsComputerControlled)
                {
                    ServerAnswerOpportunity(watcher, TakesOpportunity());
                    return;
                }

                if (OpportunityCommands.Current != null)
                {
                    OpportunityCommands.Current.ServerOffer(watcher, m_Pending.Actor,
                        SwingOf(watcher));
                    return;
                }

                // No postbox in the arena, so nobody can be asked and nobody swings. Better than
                // hanging a move on a question that will never be answered.
                Debug.LogWarning("No opportunity commands in the arena; the swing is skipped.", this);
                m_Offered = null;
            }

            RunPendingAction();
        }

        /// <summary>
        /// Server only. Whether to swing, and with what. A null element declines.
        /// </summary>
        public bool ServerAnswerOpportunity(CreatureState watcher, bool swings)
        {
            if (!IsServer || m_Offered == null || watcher != m_Offered || !m_Pending.Exists)
            {
                return false;
            }

            var mover = m_Pending.Actor;
            m_Offered = null;

            OpportunityCommands.Current?.ServerClearOffer();

            if (!swings || !CanTakeOpportunity(watcher, mover))
            {
                AskNextOpportunity();
                return true;
            }

            var pool = watcher.GetComponent<CreaturePool>();
            var skill = SwingOf(watcher);

            // Committed, not spent: a swing hides what it is made of until the answer is in, the
            // same as any other attack.
            if (pool == null || skill == null
                || !pool.ServerCommit(skill.Element, skill.ElementCost, out _))
            {
                AskNextOpportunity();
                return true;
            }

            // Turning to swing, like any other attack, which opens the swinger own back in turn.
            watcher.ServerFace(Bearing(watcher.Cell, mover.Cell));

            BeginClash(watcher, skill, mover);
            return true;
        }

        /// <summary>
        /// Whether a computer creature takes its swing. Nearly always.
        ///
        /// It used to weigh the matchup and decline whenever the odds came out near even -- which,
        /// against a hand nobody has seen, is almost always. The result was a rule that never fired:
        /// the warning appeared, the player braced, and nothing happened, which is worse than not
        /// having the rule at all.
        ///
        /// A free attack is worth taking. The element is the only cost and it buys a chance at
        /// damage plus a forced answer out of the mover, so declining is the unusual choice, not the
        /// careful one. What is left is a small hold-back so that a creature down to its last few
        /// elements is not guaranteed to spend them the moment anybody walks past -- and so that a
        /// player cannot count on the reaction any more than they can count on it not coming.
        /// </summary>
        static bool TakesOpportunity() => UnityEngine.Random.value > HoldsBack;

        /// <summary>How often a computer creature keeps its element instead of swinging.</summary>
        const float HoldsBack = 0.15f;

        /// <summary>Runs the action everybody has now had their swing at.</summary>
        void RunPendingAction()
        {
            var pending = m_Pending;
            m_Pending = default;
            m_Watchers.Clear();

            if (!pending.Exists || !pending.Actor.IsAlive)
            {
                return;
            }

            if (pending.IsMove)
            {
                PerformMove(pending.Actor, pending.Where, pending.Facing);
                return;
            }

            // Replayed from the top. Everything it checks may have changed while the swings landed
            // -- health, action points, who is standing where -- and the check that suspended it
            // will not fire twice, because the action is no longer pending.
            ServerUseSkill(pending.Actor, pending.SkillId, pending.Where, out _);
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
                pool.ServerLedger, ElementMatchups.Table);

            m_ClashAttacker = actor;
            m_ClashDefender = target;
            m_ClashSkill = skill;
            m_ClashFlanked = flanked;

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
                ServerAnswerClash(defender, ChooseDefence(defender, m_ClashAttacker, request), out _);
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
            if (pool == null || !m_Clash.TryCommit(answer, pool.ServerLedger, out refusal))
            {
                return false;
            }

            foreach (var element in answer)
            {
                // Committed, not spent, for the same reason the attacker's was: both are announced
                // together once neither can be used to work out the other.
                pool.ServerCommit(element, 1, out _);
            }

            SettleClash(answer, declined: false);
            return true;
        }

        /// <summary>
        /// What a computer creature puts up.
        ///
        /// Weighed against exactly what a player is shown -- what the attacker has been proven to
        /// hold, and which elements their seen skills could arrive as -- and then rolled for. A
        /// defender that always answered optimally is a defender who can be hard-countered every
        /// time once somebody has learned the table, and a fight whose right answer never changes
        /// has one turn in it.
        ///
        /// The randomness lives here rather than in the rules, because the rules have to be able to
        /// give the same answer twice and this deliberately does not.
        /// </summary>
        static IReadOnlyList<Element> ChooseDefence(CreatureState defender, CreatureState attacker,
            DefenceRequest request)
        {
            var answer = new List<Element>();
            var options = new List<Element>(request.Options);
            var attack = CreatureKnowledge.PossibleAttacks(attacker);

            var pool = defender.GetComponent<CreaturePool>();
            var held = pool != null ? pool.ServerLedger.Pool : ElementCounts.Empty;

            while (answer.Count < request.Required && options.Count > 0)
            {
                if (!ClashDefenceOdds.TryChoose(options, attack, ElementMatchups.Table,
                        UnityEngine.Random.value, out var pick))
                {
                    break;
                }

                answer.Add(pick);

                // Only offered again if another one is actually held.
                var taken = 0;

                foreach (var chosen in answer)
                {
                    if (chosen == pick)
                    {
                        taken++;
                    }
                }

                if (held[pick] <= taken)
                {
                    options.Remove(pick);
                }
            }

            return answer;
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
            var flanked = m_ClashFlanked;

            // Cleared before anything else can run: applying the effect can kill a creature, which
            // ends the match, and a clash still standing at that point would suspend the next one.
            m_Clash = null;
            m_ClashAttacker = null;
            m_ClashDefender = null;
            m_ClashSkill = null;
            m_ClashFlanked = false;

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

            // In order, and only now. DE-005 asks for each side's expenditure after that side's own
            // reveal, which is what these two calls are -- until this point neither pool has said a
            // word about what left it.
            attacker.GetComponent<CreaturePool>()?.ServerAnnounceCommitted();
            defender.GetComponent<CreaturePool>()?
                .ServerAnnounceCommitted(keep: ClashRules.Refunds(reveal.Outcome));

            attacker.GetComponent<SkillCommands>()?.ServerRecordUse(skill.Id);

            ClashCommands.Current?.ServerAnnounce(attacker.TurnId, defender.TurnId, skill.Id,
                reveal.Attacker, reveal.Defender, reveal.Outcome);
            ResolveContested(attacker, skill, defender, clash.Scale(skill.Effect));

            // Caught from behind, a creature turns to face whoever did it.
            //
            // Flanking was worth too much without this. One creature walking round the back could
            // stand there and swing from the same tile every turn, doubling the defence cost for
            // free, and the only counter was to move -- which cost the defender their own turn. A
            // creature that has just been hit knows where it was hit from; it turning round is not
            // a favour, it is the least it would do. The flanker keeps the hit they earned and has
            // to earn the next one.
            //
            // After the effect, so the blow that landed is the one the position bought, and only if
            // there is still somebody to turn.
            if (flanked && defender.IsAlive && attacker.IsAlive)
            {
                defender.ServerFace(Bearing(defender.Cell, attacker.Cell));
            }

            // The attack is over, so whatever the pause was holding up can go on.
            ClashCommands.Current?.ServerClearPrompt();

            // A swing taken at somebody mid-move: the next watcher is asked, and when none are
            // left the move they were all reacting to finally happens.
            if (m_Pending.Exists)
            {
                AskNextOpportunity();
            }
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
                    // Already done, above.
                    break;
            }
        }

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
        /// Whether this creature may act right now: alive, and the one whose turn it is.
        ///
        /// Ownership and control are the caller's business -- <see cref="UnitCommands"/> checks the
        /// sender -- because the computer's own turns come through here with no client behind them.
        /// </summary>
        bool CanAct(CreatureState actor) =>
            IsServer
            && actor != null
            && actor.IsAlive
            && !IsBusy
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

        /// <summary>
        /// Whether the fight is stopped on anybody question at all.
        ///
        /// Wider than <see cref="IsClashPending"/>: between one swing settling and the next being
        /// offered there is no clash open, and a creature that read only the clash would take that
        /// gap as its turn resuming -- and act in the middle of its own interrupted move.
        /// </summary>
        public bool IsBusy => IsClashPending || m_Pending.Exists;

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
                yield return new WaitWhile(() => IsBusy);

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
            if (!IsServer)
            {
                return;
            }

            WatchForAbsentDefender();
            WatchForAbsentSwinger();
        }

        void WatchForAbsentDefender()
        {
            if (m_Clash == null || m_ClashDefender == null
                || m_ClashDefender.IsComputerControlled
                || IsStillConnected(m_ClashDefender))
            {
                return;
            }

            Debug.Log("The defender left mid-clash; the attack resolves unopposed.", this);
            SettleClash(null, declined: true);
        }

        /// <summary>
        /// Notices somebody who was offered a swing and is no longer there.
        ///
        /// The same reasoning as the clash watchdog, and the same absence of a timer: a player
        /// weighing an element against a walk is thinking, and this game does not price thinking.
        /// A closed socket is not thinking, and the creature waiting to move cannot be left
        /// standing there forever because of it.
        /// </summary>
        void WatchForAbsentSwinger()
        {
            if (m_Offered == null || m_Offered.IsComputerControlled
                || IsStillConnected(m_Offered))
            {
                return;
            }

            Debug.Log("A creature left while being offered a swing; it declines.", this);
            ServerAnswerOpportunity(m_Offered, false);
        }

        static bool IsStillConnected(CreatureState creature)
        {
            var manager = NetworkManager.Singleton;

            return manager != null
                && manager.ConnectedClients.ContainsKey(creature.OwnerClientId);
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
                pool != null ? pool.ServerLedger : default);
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
