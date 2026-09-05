using System.Collections.Generic;
using Dragoneye.Combat;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// How a computer creature answers an attack.
    ///
    /// Handed the prompt and its own pool, and nothing else. That is the whole guarantee: a
    /// computer defender cannot answer better than a player could for want of information a player
    /// would not have, because the information is not in the signature. Anything that changed that
    /// would have to change this line, which is a much louder thing to do than reading a field.
    ///
    /// It spends what it holds most of. That keeps its scarce elements for a turn when it is the
    /// one attacking, and it is the same reasoning a player uses with no idea what is coming.
    /// </summary>
    public static class ClashDefence
    {
        public static IReadOnlyList<Element> Choose(DefenceRequest request, ElementCounts pool)
        {
            var answer = new List<Element>();

            if (request.Options == null)
            {
                return answer;
            }

            var taken = ElementCounts.Empty;

            while (answer.Count < request.Required)
            {
                var best = -1;
                var most = 0;

                for (var i = 0; i < request.Options.Count; i++)
                {
                    var left = pool[request.Options[i]] - taken[request.Options[i]];

                    if (left > most)
                    {
                        most = left;
                        best = i;
                    }
                }

                if (best < 0)
                {
                    // Nothing left to put up. A defender required to commit two while holding one
                    // commits the one, which is a rule rather than a shortfall.
                    break;
                }

                answer.Add(request.Options[best]);
                taken = taken.Plus(request.Options[best], 1);
            }

            return answer;
        }
    }

    /// <summary>
    /// What a creature has decided it is doing this instant.
    ///
    /// Named, and returned from a pure assessment, so a test can assert on the reasoning rather than
    /// on the action that happened to fall out of it -- "it should be catching its breath here" is a
    /// clearer failure than "it should have returned skill 90".
    /// </summary>
    public enum BrainState
    {
        /// <summary>Nothing worth doing. End the turn.</summary>
        Idle,

        /// <summary>Something it holds reaches the enemy and it can pay for it.</summary>
        Striking,

        /// <summary>It can pay for nothing, but it can get some of it back.</summary>
        Recovering,

        /// <summary>It could act, but not from here.</summary>
        Closing
    }

    /// <summary>What a creature decided, and why.</summary>
    public readonly struct BrainPlan
    {
        public readonly BrainState State;

        /// <summary>The skill to use. Null except when <see cref="State"/> acts.</summary>
        public readonly SkillSpec Skill;

        /// <summary>The enemy being considered, if there is one.</summary>
        public readonly uint TargetId;

        public BrainPlan(BrainState state, SkillSpec skill = null, uint targetId = 0)
        {
            State = state;
            Skill = skill;
            TargetId = targetId;
        }
    }

    /// <summary>
    /// The simplest opponent that is still an opponent, as a state machine.
    ///
    /// Four states, assessed in order of what a creature would actually rather be doing: hit
    /// something if you can, get your breath back if you cannot, close the distance if that is what
    /// is missing, and otherwise stop. The order is the whole design -- there is no scoring, no
    /// lookahead and no memory, and adding any of those means replacing this class rather than
    /// growing it.
    ///
    /// Assessing and acting are separate calls. <see cref="Assess"/> is pure and says what state the
    /// creature is in and which skill it picked; <see cref="Decide"/> turns that into the one action
    /// the director will carry out. That split is what makes the reasoning testable without a board
    /// and without a scene.
    ///
    /// Every affordability question goes through <see cref="SkillRules"/>, which is the same check
    /// the server runs when the order arrives -- so a brain can never decide on something that will
    /// then be refused and burn one of its budgeted actions doing nothing.
    /// </summary>
    public sealed class BasicBrain : ICreatureBrain
    {
        public BrainDecision Decide(BrainView actor, IReadOnlyList<BrainView> others, IBoardQuery board)
        {
            if (others == null || board == null)
            {
                return BrainDecision.Pass;
            }

            var target = NearestEnemy(actor, others);
            var plan = Assess(actor, target);

            switch (plan.State)
            {
                case BrainState.Striking:
                case BrainState.Recovering:
                    return BrainDecision.UseSkill(plan.Skill.Id, plan.TargetId);

                case BrainState.Closing:
                    return Approach(actor, target.Value, board);

                default:
                    return BrainDecision.Pass;
            }
        }

        /// <summary>
        /// Which state this creature is in, and what it would use.
        ///
        /// Pure, and the whole of the decision. The order below is the priority: something that can
        /// hit does, something that cannot pay recovers rather than walking closer to a fight it
        /// still could not join, and only a creature that could act if it were somewhere else walks.
        /// </summary>
        public static BrainPlan Assess(BrainView actor, BrainView? target)
        {
            if (!target.HasValue)
            {
                return new BrainPlan(BrainState.Idle);
            }

            var enemy = target.Value;
            var distance = Hex.Distance(actor.Cell, enemy.Cell);

            var reaching = BestOffensive(actor, distance);

            if (reaching != null)
            {
                return new BrainPlan(BrainState.Striking, reaching, enemy.Id);
            }

            // Range is ignored here on purpose: this asks whether the creature could act at all if
            // it were standing somewhere else, which is what separates "walk" from "recover".
            var affordable = BestOffensive(actor, -1);

            if (affordable == null)
            {
                var recovery = Recovery(actor);

                if (recovery != null)
                {
                    // Aimed at itself, because that is what a self-directed skill is aimed at. The
                    // enemy is what prompted it, not what it lands on.
                    return new BrainPlan(BrainState.Recovering, recovery, actor.Id);
                }

                // Nothing to spend and nothing to get back. Walking would only put it somewhere else
                // with the same problem, but somewhere else is at least where the fight is.
            }

            return actor.CurrentAp >= CombatRules.MoveCostPerTile
                ? new BrainPlan(BrainState.Closing, null, enemy.Id)
                : new BrainPlan(BrainState.Idle);
        }

        /// <summary>
        /// The hardest-hitting skill this creature can pay for, optionally within a distance.
        ///
        /// A negative distance means "ignore range": the question is affordability alone. Ties break
        /// on id, so two equal skills are not chosen by the order somebody happened to author them.
        /// </summary>
        static SkillSpec BestOffensive(BrainView actor, int distance)
        {
            SkillSpec best = null;

            foreach (var skill in actor.Skills)
            {
                if (!IsOffensive(skill))
                {
                    continue;
                }

                if (distance >= 0 && !CombatRules.InRange(distance, skill.Range))
                {
                    continue;
                }

                if (SkillRules.CheckAffordable(skill, true, actor.CurrentAp, actor.Ledger)
                    != SkillRefusal.None)
                {
                    continue;
                }

                if (best == null
                    || skill.Effect.Amount > best.Effect.Amount
                    || (skill.Effect.Amount == best.Effect.Amount && skill.Id < best.Id))
                {
                    best = skill;
                }
            }

            return best;
        }

        /// <summary>
        /// Something that puts the creature back in a position to act: elements or action points.
        ///
        /// Take a Breath is the one every species has, and it is refused when nothing has been
        /// spent -- so a creature that has done nothing yet will not stand there breathing.
        /// </summary>
        static SkillSpec Recovery(BrainView actor)
        {
            foreach (var skill in actor.Skills)
            {
                if (skill == null
                    || skill.Target != SkillTarget.Self
                    || (skill.Effect.Kind != SkillEffectKind.ReturnElement
                        && skill.Effect.Kind != SkillEffectKind.RestoreAp))
                {
                    continue;
                }

                if (SkillRules.CheckAffordable(skill, true, actor.CurrentAp, actor.Ledger)
                    == SkillRefusal.None)
                {
                    return skill;
                }
            }

            return null;
        }

        static bool IsOffensive(SkillSpec skill) =>
            skill != null
            && skill.Target == SkillTarget.Creature
            && skill.Effect.Kind == SkillEffectKind.Damage;

        /// <summary>
        /// How far this creature can reach with anything it could still pay for.
        ///
        /// What it stops walking at. Distance is unknown here, so range is not checked -- this asks
        /// "how close would I have to be", which is what decides where to stop.
        /// </summary>
        static int Reach(BrainView actor)
        {
            var reach = 0;

            foreach (var skill in actor.Skills)
            {
                if (!IsOffensive(skill) || skill.Range <= reach)
                {
                    continue;
                }

                if (SkillRules.CheckAffordable(skill, true, actor.CurrentAp, actor.Ledger)
                    == SkillRefusal.None)
                {
                    reach = skill.Range;
                }
            }

            return reach;
        }

        /// <summary>
        /// Nearest by straight hex distance, not by route.
        ///
        /// Routes are the expensive question and this is the cheap one; picking a target by distance
        /// and then failing to reach it costs a turn, which for this brain is an acceptable outcome
        /// and not worth a search per candidate. Ties break on id so the choice is stable.
        /// </summary>
        static BrainView? NearestEnemy(BrainView actor, IReadOnlyList<BrainView> others)
        {
            BrainView? best = null;
            var bestDistance = int.MaxValue;

            foreach (var other in others)
            {
                if (other.Party == actor.Party || !CombatRules.IsAlive(other.CurrentHp))
                {
                    continue;
                }

                var distance = Hex.Distance(actor.Cell, other.Cell);

                if (distance < bestDistance
                    || (distance == bestDistance && best.HasValue && other.Id < best.Value.Id))
                {
                    bestDistance = distance;
                    best = other;
                }
            }

            return best;
        }

        /// <summary>
        /// Steps toward the enemy, stopping as soon as something it holds would reach.
        ///
        /// Targets a hex adjacent to the enemy rather than the enemy's own hex, which is occupied and
        /// therefore never a valid destination. Walking the ring costs at most six route searches and
        /// removes the need for the pathfinder to understand "next to".
        ///
        /// Stopping early matters because reaching somebody costs AP of its own: a creature that
        /// spends its whole turn closing to melee when it is carrying a bow arrives with nothing left
        /// to loose.
        /// </summary>
        static BrainDecision Approach(BrainView actor, BrainView enemy, IBoardQuery board)
        {
            var bestPath = default(IReadOnlyList<Hex>);
            var bestCost = int.MaxValue;

            foreach (var approach in enemy.Cell.Neighbors())
            {
                if (board.IsOccupied(approach))
                {
                    continue;
                }

                var path = board.PathTo(actor.Cell, approach);
                if (path == null || path.Count == 0 || path.Count >= bestCost)
                {
                    continue;
                }

                bestCost = path.Count;
                bestPath = path;
            }

            if (bestPath == null)
            {
                return BrainDecision.Pass;
            }

            // Affordable prefix of the route. Moving part of the way is the right answer for a
            // creature that cannot close the whole gap this turn -- standing still would let a
            // ranged opponent kite it forever.
            var steps = CombatRules.StepsAffordable(actor.CurrentAp);
            if (steps <= 0)
            {
                return BrainDecision.Pass;
            }

            var last = (steps < bestPath.Count ? steps : bestPath.Count) - 1;
            var reach = Reach(actor);
            var destination = bestPath[last];

            for (var i = 0; i <= last; i++)
            {
                if (CombatRules.InRange(Hex.Distance(bestPath[i], enemy.Cell), reach))
                {
                    destination = bestPath[i];
                    break;
                }
            }

            // Only ever a step that gets closer. Without this, a creature standing next to an enemy
            // it cannot afford to hit shuffles between the tiles around it -- every one of them is a
            // legal destination, none of them is an improvement, and it paces until the AP runs out.
            // Ending the turn is the honest answer: there is nothing it can do from anywhere.
            return Hex.Distance(destination, enemy.Cell) < Hex.Distance(actor.Cell, enemy.Cell)
                ? BrainDecision.MoveTo(destination)
                : BrainDecision.Pass;
        }
    }
}
