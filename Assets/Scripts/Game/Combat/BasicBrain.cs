using System.Collections.Generic;
using Dragoneye.Combat;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// The simplest opponent that is still an opponent: use the hardest-hitting skill you can afford
    /// on the nearest enemy, otherwise close until one of them reaches.
    ///
    /// No threat assessment, no kiting, no target priority beyond distance. That is the intended
    /// level -- it exists so a solo match does not deadlock on the first enemy turn, and so the turn
    /// system has something real to drive. Replace the whole class when the game deserves better;
    /// nothing depends on it beyond <see cref="ICreatureBrain"/>.
    ///
    /// It picks a skill because there is no generic attack any more. That also means a creature
    /// authored with nothing but Take a Breath will walk up and stand there, which is correct: it
    /// has nothing to do, and inventing a punch for it would put a rule back that the content model
    /// deliberately removed.
    ///
    /// Stateless and deterministic. Two peers running it on the same board reach the same decision,
    /// and a test can assert on the decision rather than on what happened afterwards.
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
            if (!target.HasValue)
            {
                return BrainDecision.Pass;
            }

            var enemy = target.Value;
            var distance = Hex.Distance(actor.Cell, enemy.Cell);
            var best = BestSkillAgainst(actor, distance);

            if (best != null)
            {
                return BrainDecision.UseSkill(best.Id, enemy.Id);
            }

            return Approach(actor, enemy, board);
        }

        /// <summary>
        /// The hardest-hitting skill this creature can use on somebody that far away, right now.
        ///
        /// Affordability comes from <see cref="SkillRules"/>, which is the same check the server
        /// runs when the order arrives -- so the brain cannot decide on something that will then be
        /// refused and burn one of its action budget doing nothing.
        ///
        /// Ties break on id, so two equal skills are not chosen by list order.
        /// </summary>
        static SkillSpec BestSkillAgainst(BrainView actor, int distance)
        {
            SkillSpec best = null;

            foreach (var skill in actor.Skills)
            {
                if (skill == null
                    || skill.Target != SkillTarget.Creature
                    || skill.Effect.Kind != SkillEffectKind.Damage
                    || !CombatRules.InRange(distance, skill.Range))
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
        /// How far this creature can reach with anything it could still pay for.
        ///
        /// What it stops walking at. Distance is unknown here, so range is not checked -- this asks
        /// "how close would I have to be", which is the question that decides where to stop.
        /// </summary>
        static int Reach(BrainView actor)
        {
            var reach = 0;

            foreach (var skill in actor.Skills)
            {
                if (skill == null
                    || skill.Target != SkillTarget.Creature
                    || skill.Effect.Kind != SkillEffectKind.Damage
                    || skill.Range <= reach)
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
        /// Stopping early matters now that reaching somebody costs AP of its own: a creature that
        /// spends its whole turn closing to melee when it is carrying a bow arrives with nothing left
        /// to loose.
        /// </summary>
        static BrainDecision Approach(BrainView actor, BrainView enemy, IBoardQuery board)
        {
            if (actor.CurrentAp < CombatRules.MoveCostPerTile)
            {
                return BrainDecision.Pass;
            }

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

            for (var i = 0; i <= last; i++)
            {
                if (CombatRules.InRange(Hex.Distance(bestPath[i], enemy.Cell), reach))
                {
                    return BrainDecision.MoveTo(bestPath[i]);
                }
            }

            return BrainDecision.MoveTo(bestPath[last]);
        }
    }
}
