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
                targetOccupied: false, moveSteps: cost);

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

            Resolve(actor, skill, occupant);
            return true;
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
                    if (target != null && target.ServerApplyDamage(skill.Effect.Amount))
                    {
                        Kill(target);
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
                var decision = m_Brain.Decide(ViewOf(actor, includeHand: true),
                    OtherViews(actor), m_Board);

                var acted = decision.Action == BrainAction.UseSkill
                    ? UseSkillOn(actor, decision.SkillId, CreatureFor(decision.TargetId))
                    : decision.Action == BrainAction.Move && ServerMove(actor, decision.Destination);

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
