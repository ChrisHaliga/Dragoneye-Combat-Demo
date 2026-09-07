using System.Collections.Generic;
using Dragoneye.Combat;

namespace Dragoneye.Sim
{
    /// <summary>
    /// The computer's turn, one decision at a time.
    ///
    /// Not a loop and not a coroutine: a <see cref="Step"/> the caller takes when it likes. A
    /// server takes one a frame, so a question a decision opens is on somebody's screen before
    /// the next decision is asked for; a test takes them until the fight is over. The brain is
    /// asked again after every action rather than for a whole plan, so a kill or a blocked route
    /// changes what it does next instead of playing out a stale plan. It decides nothing itself:
    /// what to do is the brain's, whether it may be done is the fight's.
    /// </summary>
    public sealed partial class Fight
    {
        readonly ICreatureBrain m_Brain;

        // Bounded because a brain that returns an action it cannot perform would otherwise spin
        // forever. The cap is generous enough that hitting it means a bug, and it is said.
        const int ActionBudget = 32;
        int m_Budget;

        // Creatures already complained about, so a toothless one does not warn every round.
        readonly HashSet<uint> m_Warned = new HashSet<uint>();

        /// <summary>
        /// Takes one decision for the computer creature whose turn it is.
        ///
        /// Nothing happens while the fight is waiting on somebody's answer, while a person holds
        /// the turn, or once the fight is over. A brain that throws, or that keeps asking for
        /// things it cannot do, ends its turn rather than the match: a turn ends in exactly one
        /// place, and a computer that cannot reach it must not be able to stop everybody else.
        /// </summary>
        /// <returns>Whether anything was done. False means there is nothing for the computer to do now.</returns>
        public bool Step()
        {
            if (!HasBegun || IsOver || IsBusy)
            {
                return false;
            }

            var active = Creature(ActiveId);

            if (active == null || !active.IsComputerControlled)
            {
                return false;
            }

            if (m_Budget <= 0)
            {
                m_Listener.Warn($"{m_Brain.GetType().Name} exhausted its action budget for "
                    + $"creature {active.Id}; ending the turn.");
                EndTurn();
                return true;
            }

            WarnIfToothless(active);

            BrainDecision decision;

            try
            {
                decision = m_Brain.Decide(ViewOf(active, includeHand: true), OtherViews(active), m_Board);
            }
            catch (System.Exception exception)
            {
                m_Listener.Warn($"{m_Brain.GetType().Name} threw working out what creature "
                    + $"{active.Id} should do; ending its turn. {exception}");
                EndTurn();
                return true;
            }

            bool acted;

            try
            {
                acted = decision.Action == BrainAction.UseSkill
                    ? UseSkillOn(active, decision.SkillId, Creature(decision.TargetId))
                    : decision.Action == BrainAction.Move && Move(active.Id, decision.Destination);
            }
            catch (System.Exception exception)
            {
                m_Listener.Warn($"Creature {active.Id}'s {decision.Action} threw while being carried "
                    + $"out; ending its turn. {exception}");
                EndTurn();
                return true;
            }

            if (!acted)
            {
                EndTurn();
                return true;
            }

            m_Budget--;
            return true;
        }

        bool UseSkillOn(FightCreature actor, int skillId, FightCreature target) =>
            target != null && target.IsAlive
            && UseSkill(actor, skillId, target.Cell, out _, null, provoked: false);

        /// <summary>
        /// Says so, once, when a computer creature has nothing it could ever do.
        ///
        /// A creature with an empty skill list walks up to somebody and ends its turn, which looks
        /// exactly like a broken brain and is in fact missing content.
        /// </summary>
        void WarnIfToothless(FightCreature actor)
        {
            if (actor.Skills.Count > 0 || !m_Warned.Add(actor.Id))
            {
                return;
            }

            m_Listener.Warn($"Creature {actor.Id} has no skills, so it can only walk.");
        }

        /// <summary>
        /// A creature as a brain sees it, including what it can do.
        ///
        /// Skills and elements are only filled in for the creature being asked to decide. Reading
        /// another creature's hand would be the brain cheating, and the pool is private to its
        /// controller for exactly that reason.
        /// </summary>
        static BrainView ViewOf(FightCreature creature, bool includeHand)
        {
            if (!includeHand)
            {
                return new BrainView(creature.Id, creature.Cell, creature.Party, creature.Ap,
                    creature.Hp, stepCost: creature.StepCost);
            }

            return new BrainView(creature.Id, creature.Cell, creature.Party, creature.Ap,
                creature.Hp, creature.Skills, creature.Pool.Private, creature.StepCost);
        }

        List<BrainView> OtherViews(FightCreature actor)
        {
            var views = new List<BrainView>();

            foreach (var creature in m_Creatures)
            {
                if (creature != actor && creature.IsAlive && creature.OnBoard)
                {
                    views.Add(ViewOf(creature, includeHand: false));
                }
            }

            return views;
        }
    }
}
