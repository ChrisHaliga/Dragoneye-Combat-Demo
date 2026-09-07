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
    /// Plays out a computer creature's turn, one decision at a time.
    ///
    /// A coroutine, but not for pacing: the fight is watched through <see cref="CombatPlayback"/>,
    /// which shows each thing the turn did at its own speed, and nothing here waits on a clock.
    /// It yields for one reason -- a clash or a swing can suspend the fight on a person's answer,
    /// and the brain has to wait that out rather than read a stopped turn as a finished one.
    ///
    /// The brain is asked again after every action rather than for a whole plan, so a kill or a
    /// blocked route changes what it does next instead of playing out a stale plan. It decides
    /// nothing itself: what to do is the brain's, whether it may be done is the host's.
    /// </summary>
    public sealed class BrainTurnRunner
    {
        readonly IBrainHost m_Host;
        readonly ICreatureBrain m_Brain;
        readonly CreatureRegistry m_Creatures;
        readonly ArenaBoard m_Board;

        // Creatures already complained about, so a toothless one does not warn every round.
        readonly HashSet<uint> m_Warned = new HashSet<uint>();

        public BrainTurnRunner(IBrainHost host, ICreatureBrain brain, CreatureRegistry creatures,
            ArenaBoard board)
        {
            m_Host = host;
            m_Brain = brain;
            m_Creatures = creatures;
            m_Board = board;
        }

        public IEnumerator Run(CreatureState actor)
        {
            WarnIfToothless(actor);

            // Bounded because a brain that returns an action it cannot perform would otherwise spin
            // forever. The cap is generous enough that hitting it means a bug, and it is logged.
            var budget = 32;

            while (budget-- > 0)
            {
                // A clash or a swing suspends the fight, so the brain waits it out. Bounded in
                // practice by the watchdogs, which settle a question whose asker has gone.
                yield return new WaitWhile(() => m_Host.IsBusy);

                if (!m_Host.CanAct(actor))
                {
                    break;
                }

                bool acted;

                // A brain that throws must not take the fight with it. An exception out of a
                // coroutine stops the coroutine and nothing else: Unity logs it, the turn never
                // ends, and because a turn only ends here the whole match stops with no way
                // forward and nothing on screen to say why. Ending the turn is always available
                // and always better than that.
                try
                {
                    var decision = m_Brain.Decide(ViewOf(actor, includeHand: true),
                        OtherViews(actor), m_Board);

                    acted = decision.Action == BrainAction.UseSkill
                        ? m_Host.UseSkillOn(actor, decision.SkillId, CreatureFor(decision.TargetId))
                        : decision.Action == BrainAction.Move
                            && m_Host.Move(actor, decision.Destination);
                }
                catch (System.Exception exception)
                {
                    Debug.LogError($"{m_Brain.GetType().Name} threw while deciding for "
                        + $"{actor.DisplayName}; ending its turn.");
                    Debug.LogException(exception);
                    break;
                }

                if (!acted)
                {
                    break;
                }

                // A frame between actions, so anything an action set in motion -- a question to a
                // person, a watchdog -- has run before the next decision is asked for.
                yield return null;
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
        /// skills authored by the content step, so the usual cause is that it has not been run.
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
                + "Premade creatures are authored by the character content step.");
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
