using System.Collections.Generic;

namespace Dragoneye.Hex.Systems
{
    /// <summary>
    /// The cheapest way from one cell to another, by the map's own rules.
    ///
    /// Dijkstra rather than the breadth-first search it used to be, because a step is not one
    /// step any more: difficult ground costs two, and a route through mud that is shorter in tiles
    /// can be dearer in steps. Adjacency comes from <see cref="IGridRules"/>, so walls are simply
    /// edges that are not there.
    ///
    /// The path returned is the cells walked, excluding the start and including the destination,
    /// and its cost is the steps paid to walk it -- which is what the rules price in AP.
    /// </summary>
    public static class HexPathfinder
    {
        /// <summary>
        /// Finds the cheapest route.
        /// </summary>
        /// <param name="blocked">Cells that cannot be walked through or onto. Occupied ones, usually.</param>
        /// <param name="maxCost">Give up past this many steps; negative for no limit.</param>
        /// <returns>False when there is no route; <paramref name="path"/> is then empty.</returns>
        public static bool TryFindPath(IGridRules grid, Cell from, Cell to, ICollection<Cell> blocked,
            List<Cell> path, out int cost, int maxCost = -1)
        {
            cost = -1;
            path?.Clear();

            if (grid == null || path == null || from == to || !CanEnter(grid, to, blocked))
            {
                return false;
            }

            var cameFrom = new Dictionary<Cell, Cell>();
            var best = new Dictionary<Cell, int> { [from] = 0 };
            var open = new List<Cell> { from };
            var neighbours = new List<Cell>();

            while (open.Count > 0)
            {
                // The cheapest open cell. A heap would be faster; the maps are small enough that a
                // scan is not the cost that matters, and it keeps the search readable.
                var index = 0;

                for (var i = 1; i < open.Count; i++)
                {
                    if (best[open[i]] < best[open[index]])
                    {
                        index = i;
                    }
                }

                var current = open[index];
                open.RemoveAt(index);

                var here = best[current];

                if (current == to)
                {
                    Rebuild(cameFrom, from, to, path);
                    cost = here;
                    return true;
                }

                grid.Neighbours(current, neighbours);

                foreach (var next in neighbours)
                {
                    if (!CanEnter(grid, next, blocked))
                    {
                        continue;
                    }

                    var through = here + grid.StepsToEnter(next);

                    if (maxCost >= 0 && through > maxCost)
                    {
                        continue;
                    }

                    if (best.TryGetValue(next, out var known) && known <= through)
                    {
                        continue;
                    }

                    best[next] = through;
                    cameFrom[next] = current;

                    if (!open.Contains(next))
                    {
                        open.Add(next);
                    }
                }
            }

            return false;
        }

        /// <summary>The steps a route costs, or -1 when there is none.</summary>
        public static int CostTo(IGridRules grid, Cell from, Cell to, ICollection<Cell> blocked,
            int maxCost = -1)
        {
            var path = new List<Cell>();
            return TryFindPath(grid, from, to, blocked, path, out var cost, maxCost) ? cost : -1;
        }

        /// <summary>
        /// Every cell reachable within a budget of steps, with what each costs to reach.
        ///
        /// The reach overlay and the brain's "walk as far as you can" both want this: the whole
        /// frontier at once rather than one destination priced at a time.
        /// </summary>
        public static void Reachable(IGridRules grid, Cell from, int budget, ICollection<Cell> blocked,
            Dictionary<Cell, int> into)
        {
            into.Clear();

            if (grid == null || budget <= 0)
            {
                return;
            }

            var open = new List<Cell> { from };
            var neighbours = new List<Cell>();
            into[from] = 0;

            while (open.Count > 0)
            {
                var index = 0;

                for (var i = 1; i < open.Count; i++)
                {
                    if (into[open[i]] < into[open[index]])
                    {
                        index = i;
                    }
                }

                var current = open[index];
                open.RemoveAt(index);

                grid.Neighbours(current, neighbours);

                foreach (var next in neighbours)
                {
                    if (!CanEnter(grid, next, blocked))
                    {
                        continue;
                    }

                    var through = into[current] + grid.StepsToEnter(next);

                    if (through > budget || (into.TryGetValue(next, out var known) && known <= through))
                    {
                        continue;
                    }

                    into[next] = through;

                    if (!open.Contains(next))
                    {
                        open.Add(next);
                    }
                }
            }

            into.Remove(from);
        }

        static bool CanEnter(IGridRules grid, Cell cell, ICollection<Cell> blocked) =>
            (blocked == null || !blocked.Contains(cell)) && grid.IsWalkable(cell);

        static void Rebuild(Dictionary<Cell, Cell> cameFrom, Cell from, Cell to, List<Cell> path)
        {
            var current = to;

            while (current != from)
            {
                path.Add(current);
                current = cameFrom[current];
            }

            path.Reverse();
        }
    }
}
