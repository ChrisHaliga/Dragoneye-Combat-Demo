using System.Collections;
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

        /// <summary>
        /// Swapped wholesale to change the opponent. Not serialised: brains are code, not assets,
        /// and a ScriptableObject wrapper would be indirection for a choice nobody is authoring yet.
        /// </summary>
        readonly ICreatureBrain m_Brain = new BasicBrain();

        ArenaBoard m_Board;
        Coroutine m_BrainTurn;

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
        public bool ServerMove(CreatureState actor, Hex destination)
        {
            if (!CanAct(actor))
            {
                return false;
            }

            var cost = m_Board.CostTo(actor.Cell, destination);

            var plan = ActionResolver.Resolve(true, true, actor.CurrentAp,
                targetOccupied: false, targetIsEnemy: false, distanceToTarget: 0, moveCost: cost);

            if (!plan.IsAllowed || plan.Action != BoardAction.Move)
            {
                return false;
            }

            if (!actor.ServerSpendAp(plan.Cost))
            {
                return false;
            }

            actor.Unit.ServerSetCell(destination);
            return true;
        }

        /// <summary>
        /// Server only. Resolves an attack and clears the target off the board if it dies.
        /// </summary>
        /// <returns>False if the attack was refused.</returns>
        public bool ServerAttack(CreatureState actor, CreatureState target)
        {
            if (!CanAct(actor) || target == null || !target.IsAlive)
            {
                return false;
            }

            var plan = ActionResolver.Resolve(true, true, actor.CurrentAp,
                targetOccupied: true, targetIsEnemy: target.Party != actor.Party,
                distanceToTarget: Hex.Distance(actor.Cell, target.Cell), moveCost: -1);

            if (!plan.IsAllowed || plan.Action != BoardAction.Attack)
            {
                return false;
            }

            if (!actor.ServerSpendAp(plan.Cost))
            {
                return false;
            }

            if (target.ServerApplyDamage(CombatRules.AttackDamage))
            {
                Kill(target);
            }

            return true;
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
            && TurnState.Current != null
            && TurnState.Current.IsActive(actor);

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

            while (budget-- > 0 && CanAct(actor))
            {
                var decision = m_Brain.Decide(ViewOf(actor), OtherViews(actor), m_Board);

                var acted = decision.Action == BoardAction.Attack
                    ? ServerAttack(actor, CreatureFor(decision.TargetId))
                    : decision.Action == BoardAction.Move && ServerMove(actor, decision.Destination);

                if (!acted)
                {
                    break;
                }

                yield return new WaitForSeconds(m_BrainActionDelay);
            }

            if (budget <= 0)
            {
                Debug.LogWarning($"{nameof(BasicBrain)} exhausted its action budget; ending the turn.",
                    this);
            }

            m_BrainTurn = null;
            ServerEndTurn();
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
        void Kill(CreatureState creature)
        {
            TurnState.Current?.ServerRemove(creature.TurnId);

            var networkObject = creature.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn();
            }

            ResolveOutcome();
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

        BrainView ViewOf(CreatureState creature) =>
            new BrainView(creature.TurnId, creature.Cell, creature.Party,
                creature.CurrentAp, creature.CurrentHp);

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
