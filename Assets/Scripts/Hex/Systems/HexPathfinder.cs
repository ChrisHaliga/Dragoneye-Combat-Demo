using System.Collections.Generic;

namespace Dragoneye.Hex.Systems
{
    /// <summary>
    /// Shortest walkable routes across a map.
    ///
    /// Breadth-first, not A*: every step costs exactly one, and with a uniform cost BFS already
    /// returns a shortest path. A* would add a heuristic and a priority queue to arrive at the same
    /// answer. The day terrain gains a movement cost this has to become Dijkstra -- that is the one
    /// change that would make BFS silently wrong rather than merely slower, so it is worth knowing
    /// which line to look at.
    ///
    /// Blocked cells are passed in rather than looked up. The pathfinder has no idea what a unit is,
    /// which is what lets it be tested with a set of coordinates and nothing else.
    /// </summary>
    public static class HexPathfinder
    {
        /// <summary>
        /// The cheapest route from <paramref name="from"/> to <paramref name="to"/>.
        ///
        /// The returned path excludes the starting hex and includes the destination, so its count is
        /// the number of steps taken -- which is also the cost. An empty result means unreachable.
        /// </summary>
        /// <param name="blocked">
        /// Cells that may not be entered, typically occupied ones. The destination is exempt only if
        /// it is not in this set; a caller wanting to path *next to* something should pass the
        /// neighbour it wants instead.
        /// </param>
        /// <param name="maxCost">Stop searching past this many steps. Negative means no limit.</param>
        public static bool TryFindPath(HexMap map, Hex from, Hex to, ICollection<Hex> blocked,
            List<Hex> path, int maxCost = -1)
        {
            path?.Clear();

            if (map == null || path == null || from == to || !CanEnter(map, to, blocked))
            {
                return false;
            }

            var cameFrom = new Dictionary<Hex, Hex>();
            var frontier = new Queue<Hex>();
            var depth = new Dictionary<Hex, int> { [from] = 0 };

            frontier.Enqueue(from);

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                var step = depth[current] + 1;

                if (maxCost >= 0 && step > maxCost)
                {
                    continue;
                }

                foreach (var next in current.Neighbors())
                {
                    if (depth.ContainsKey(next) || !CanEnter(map, next, blocked))
                    {
                        continue;
                    }

                    depth[next] = step;
                    cameFrom[next] = current;

                    if (next == to)
                    {
                        Rebuild(cameFrom, from, to, path);
                        return true;
                    }

                    frontier.Enqueue(next);
                }
            }

            return false;
        }

        /// <summary>
        /// The cost in steps of the cheapest route, or -1 if there is no route within
        /// <paramref name="maxCost"/>.
        ///
        /// Allocates a path it then discards; callers that need the route itself should ask for it
        /// directly rather than calling both.
        /// </summary>
        public static int CostTo(HexMap map, Hex from, Hex to, ICollection<Hex> blocked,
            int maxCost = -1)
        {
            var path = new List<Hex>();
            return TryFindPath(map, from, to, blocked, path, maxCost) ? path.Count : -1;
        }

        static bool CanEnter(HexMap map, Hex hex, ICollection<Hex> blocked) =>
            (blocked == null || !blocked.Contains(hex))
            && map.TryGetTile(hex, out var tile)
            && tile.IsWalkable;

        static void Rebuild(Dictionary<Hex, Hex> cameFrom, Hex from, Hex to, List<Hex> path)
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
