using Dragoneye.Combat;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;
using Dragoneye.Hex;

namespace Dragoneye.Game.Combat
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
    ///
    /// The pauses are the rules' own. An earlier version waited on the token to finish walking,
    /// which put a system downstream of a view: a headless server had no token to wait for, and a
    /// stuck one needed a clock to escape. Now the walk is priced in seconds per tile from the
    /// route the rules costed, and the view is left to draw it in its own time.
    /// </summary>
    public sealed class BrainTurnRunner
    {
        readonly IBrainHost m_Host;
        readonly ICreatureBrain m_Brain;
        readonly CreatureRegistry m_Creatures;
        readonly ArenaBoard m_Board;

        readonly float m_ActionDelay;
        readonly float m_SkillDwell;
        readonly float m_SecondsPerTile;

        // Creatures already complained about, so a toothless one does not warn every round.
        readonly HashSet<uint> m_Warned = new HashSet<uint>();

        public BrainTurnRunner(IBrainHost host, ICreatureBrain brain, CreatureRegistry creatures,
            ArenaBoard board, float actionDelay, float skillDwell, float secondsPerTile)
        {
            m_Host = host;
            m_Brain = brain;
            m_Creatures = creatures;
            m_Board = board;
            m_ActionDelay = actionDelay;
            m_SkillDwell = skillDwell;
            m_SecondsPerTile = secondsPerTile;
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

                // How far this is going to walk, read from the rules before it does. This is the
                // whole of what the pacing knows about movement.
                var tiles = TilesWalked(actor, decision);

                var acted = decision.Action == BrainAction.UseSkill
                    ? m_Host.UseSkillOn(actor, decision.SkillId, CreatureFor(decision.TargetId))
                    : decision.Action == BrainAction.Move
                        && m_Host.Move(actor, decision.Destination);

                if (!acted)
                {
                    break;
                }

                // The rules resolved the instant the decision was made. The pause is what lets a
                // person follow it: a beat per action, and a beat per tile walked.
                yield return new WaitForSeconds(tiles * m_SecondsPerTile
                    + (decision.Action == BrainAction.UseSkill ? m_SkillDwell : m_ActionDelay));
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

            var skills = actor.SkillCommands;

            if (skills != null && skills.Skills.Count > 0)
            {
                return;
            }

            Debug.LogWarning($"{actor.DisplayName} has no skills, so it can only walk. "
                + "Premade creatures are authored by ClaudeCode > Set Up Everything.");
        }

        /// <summary>
        /// Tiles a decision is about to walk, from the same search that will price it.
        ///
        /// A move walks its route; a skill walks to the nearest tile its target is in reach from,
        /// which may be none. Read before the action so it is the walk that is about to happen,
        /// not the one that already did.
        /// </summary>
        int TilesWalked(CreatureState actor, BrainDecision decision)
        {
            if (decision.Action == BrainAction.Move)
            {
                var steps = m_Board.CostTo(actor.Cell, decision.Destination);
                return steps < 0 ? 0 : steps;
            }

            if (decision.Action != BrainAction.UseSkill)
            {
                return 0;
            }

            var target = CreatureFor(decision.TargetId);
            var commands = actor.SkillCommands;

            if (target == null || commands == null
                || !commands.TryGetSkill(decision.SkillId, out var skill))
            {
                return 0;
            }

            var toReach = m_Board.StepsToReach(actor.Cell, target.Cell, skill.Range);
            return toReach < 0 ? 0 : toReach;
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

            var skills = creature.SkillCommands;
            var pool = creature.Pool;

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
