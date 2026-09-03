using System.Collections.Generic;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// The simplest opponent that is still an opponent: hit what is next to you, otherwise walk
    /// towards the nearest enemy.
    ///
    /// No threat assessment, no kiting, no target priority beyond distance. That is the intended
    /// level -- it exists so a solo match does not deadlock on the first enemy turn, and so the turn
    /// system has something real to drive. Replace the whole class when the game deserves better;
    /// nothing depends on it beyond <see cref="ICreatureBrain"/>.
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

            if (CombatRules.InRange(distance) && actor.CurrentAp >= CombatRules.AttackApCost)
            {
                return BrainDecision.Attack(enemy.Id);
            }

            return Approach(actor, enemy, board);
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
        /// Steps toward the enemy, stopping where the AP runs out.
        ///
        /// Targets a hex adjacent to the enemy rather than the enemy's own hex, which is occupied and
        /// therefore never a valid destination. Walking the ring costs at most six route searches and
        /// removes the need for the pathfinder to understand "next to".
        /// </summary>
        static BrainDecision Approach(BrainView actor, BrainView enemy, IBoardQuery board)
        {
            if (actor.CurrentAp < CombatRules.MoveApPerTile)
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
            // creature that cannot close the whole gap this turn -- stopping still would let a
            // ranged opponent kite it forever once ranged attacks exist.
            var steps = actor.CurrentAp / CombatRules.MoveApPerTile;
            if (steps <= 0)
            {
                return BrainDecision.Pass;
            }

            var index = (steps < bestPath.Count ? steps : bestPath.Count) - 1;

            return BrainDecision.MoveTo(bestPath[index]);
        }
    }
}
