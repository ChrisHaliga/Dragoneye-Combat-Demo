using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Plays out a computer creature's turn, one decision at a time, at a pace a person can follow.
    ///
    /// A coroutine rather than a loop because the actions should be watchable. The brain is asked
    /// again after every action rather than for a whole plan, so a kill or a blocked route changes
    /// what it does next instead of playing out a stale plan.
    ///
    /// It decides nothing itself: what to do is the brain's, whether it may be done is the host's,
    /// and this only sequences the two with the pauses between.
    /// </summary>
    public sealed class BrainTurnRunner
    {
        readonly IBrainHost m_Host;
        readonly ICreatureBrain m_Brain;
        readonly CreatureRegistry m_Creatures;
        readonly ArenaBoard m_Board;

        readonly float m_ActionDelay;
        readonly float m_SkillDwell;
        readonly float m_MoveWaitLimit;

        // Creatures already complained about, so a toothless one does not warn every round.
        readonly HashSet<uint> m_Warned = new HashSet<uint>();

        public BrainTurnRunner(IBrainHost host, ICreatureBrain brain, CreatureRegistry creatures,
            ArenaBoard board, float actionDelay, float skillDwell, float moveWaitLimit)
        {
            m_Host = host;
            m_Brain = brain;
            m_Creatures = creatures;
            m_Board = board;
            m_ActionDelay = actionDelay;
            m_SkillDwell = skillDwell;
            m_MoveWaitLimit = moveWaitLimit;
        }

        public IEnumerator Run(CreatureState actor)
        {
            WarnIfToothless(actor);

            yield return new WaitForSeconds(m_ActionDelay);

            // Bounded because a brain that returns an action it cannot perform would otherwise spin
            // forever. The cap is generous enough that hitting it means a bug, and it is logged.
            var budget = 32;

            while (budget-- > 0)
            {
                // A clash or a swing suspends the fight, so the brain waits it out rather than
                // reading a stopped turn as a finished one. Bounded in practice by the watchdogs,
                // which settle a question whose asker has gone.
                yield return new WaitWhile(() => m_Host.IsBusy);

                if (!m_Host.CanAct(actor))
                {
                    break;
                }

                var decision = m_Brain.Decide(ViewOf(actor, includeHand: true),
                    OtherViews(actor), m_Board);

                var acted = decision.Action == BrainAction.UseSkill
                    ? m_Host.UseSkillOn(actor, decision.SkillId, CreatureFor(decision.TargetId))
                    : decision.Action == BrainAction.Move
                        && m_Host.Move(actor, decision.Destination);

                if (!acted)
                {
                    break;
                }

                // The rules resolved the instant the decision was made; the board has not caught up
                // yet. Waiting for it is the difference between a turn a player can follow and four
                // creatures teleporting at once.
                yield return WalkedIt(actor);

                yield return new WaitForSeconds(decision.Action == BrainAction.UseSkill
                    ? m_SkillDwell
                    : m_ActionDelay);
            }

            if (budget <= 0)
            {
                Debug.LogWarning($"{m_Brain.GetType().Name} exhausted its action budget; ending the turn.");
            }

            m_Host.EndTurn();
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
            if (actor == null || !m_Warned.Add(actor.TurnId))
            {
                return;
            }

            var skills = actor.GetComponent<SkillCommands>();

            if (skills != null && skills.Skills.Count > 0)
            {
                return;
            }

            Debug.LogWarning($"{actor.DisplayName} has no skills, so it can only walk. "
                + "Premade creatures are authored by ClaudeCode > Set Up Everything.");
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

        CreatureState CreatureFor(uint turnId) =>
            m_Creatures != null ? m_Creatures.ByTurnId(turnId) : null;

        /// <summary>
        /// A creature as a brain sees it, including what it can do.
        ///
        /// Skills and elements are only filled in for the creature being asked to decide. Reading
        /// another creature's hand would be the brain cheating, and the pool is private to its
        /// controller for exactly that reason.
        /// </summary>
        static BrainView ViewOf(CreatureState creature, bool includeHand = false)
        {
            if (!includeHand)
            {
                return new BrainView(creature.TurnId, creature.Cell, creature.Party,
                    creature.CurrentAp, creature.CurrentHp, stepCost: creature.StepCost);
            }

            var skills = creature.GetComponent<SkillCommands>();
            var pool = creature.GetComponent<CreaturePool>();

            return new BrainView(creature.TurnId, creature.Cell, creature.Party,
                creature.CurrentAp, creature.CurrentHp,
                skills != null ? skills.Skills : null,
                pool != null ? pool.ServerLedger : default,
                creature.StepCost);
        }

        List<BrainView> OtherViews(CreatureState actor)
        {
            var views = new List<BrainView>();

            if (m_Creatures == null)
            {
                return views;
            }

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
