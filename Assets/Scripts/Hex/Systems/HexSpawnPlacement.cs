using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Hex.Systems
{
    /// <summary>
    /// Where creatures start.
    ///
    /// Candidates are cells, not tiles, and only cells in the largest connected piece of the map.
    /// A split rim tile can have an area that is walled off from everything, and a creature
    /// placed there would spend the fight looking at a wall; the connected-piece rule is what
    /// stops that, and it costs one flood fill at spawn time.
    /// </summary>
    public static class HexSpawnPlacement
    {
        /// <summary>The nearest unclaimed standable cell to an anchor, searching outward by tile.</summary>
        public static Cell FindNearestFree(IGridRules grid, Cell anchor, ICollection<Cell> taken,
            ICollection<Cell> allowed = null, int maxRadius = 16)
        {
            if (grid == null)
            {
                return anchor;
            }

            var cells = new List<Cell>();

            for (var radius = 0; radius <= maxRadius; radius++)
            {
                foreach (var tile in Hex.Ring(anchor.Tile, radius))
                {
                    cells.Clear();
                    grid.CellsOf(tile, cells);

                    foreach (var candidate in cells)
                    {
                        if ((taken == null || !taken.Contains(candidate))
                            && (allowed == null || allowed.Contains(candidate))
                            && grid.IsWalkable(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return anchor;
        }

        /// <summary>
        /// One starting cell per side, spread evenly round the rim of the playable ground.
        /// </summary>
        public static IReadOnlyList<Cell> ChooseSpawns(IGridRules grid, int count,
            float startAngleDegrees = 90f)
        {
            var spawns = new List<Cell>();

            if (grid == null || grid.Map == null || count <= 0)
            {
                return spawns;
            }

            var candidates = new List<Cell>(Playable(grid));

            if (candidates.Count == 0)
            {
                return spawns;
            }

            var map = grid.Map;
            var center = map.WorldCenter();
            var radius = 0f;

            foreach (var cell in candidates)
            {
                radius = Mathf.Max(radius, Vector3.Distance(map.Layout.ToWorld(cell.Tile), center));
            }

            var taken = new HashSet<Cell>();

            for (var i = 0; i < count && taken.Count < candidates.Count; i++)
            {
                var angle = Mathf.Deg2Rad * (startAngleDegrees + 360f * i / count);
                var target = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                // Aim past the rim, then take the nearest real cell. This lands on the edge for a
                // convex map and degrades gracefully to "as far out as possible" for anything else.
                var best = default(Cell);
                var bestDistance = float.MaxValue;
                var found = false;

                foreach (var cell in candidates)
                {
                    if (taken.Contains(cell))
                    {
                        continue;
                    }

                    var distance = Vector3.SqrMagnitude(map.Layout.ToWorld(cell.Tile) - target);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = cell;
                        found = true;
                    }
                }

                if (found)
                {
                    taken.Add(best);
                    spawns.Add(best);
                }
            }

            return spawns;
        }

        /// <summary>
        /// A cell for every item, grouped: each group gets an anchor from <see cref="ChooseSpawns"/>
        /// and its members fill outward from it.
        /// </summary>
        public static IReadOnlyList<Cell> PlaceGrouped(IGridRules grid, IReadOnlyList<int> groupOfItem,
            int groupCount) =>
            PlaceGrouped(grid, groupOfItem, grid != null ? ChooseSpawns(grid, Mathf.Max(1, groupCount)) : null);

        /// <summary>
        /// A cell for every item, grouped, from anchors the map chose: one per group, in group
        /// order. A map with a shape says where each side starts; this fills outward from there.
        /// </summary>
        public static IReadOnlyList<Cell> PlaceGrouped(IGridRules grid, IReadOnlyList<int> groupOfItem,
            IReadOnlyList<Cell> anchors)
        {
            var cells = new List<Cell>();

            if (groupOfItem == null || grid == null || anchors == null)
            {
                return cells;
            }

            var playable = Playable(grid);
            var taken = new HashSet<Cell>();

            foreach (var group in groupOfItem)
            {
                var anchor = anchors.Count > 0
                    ? anchors[Mathf.Max(0, group) % anchors.Count]
                    : Cell.Whole(Hex.Zero);

                var cell = FindNearestFree(grid, anchor, taken, playable);
                taken.Add(cell);
                cells.Add(cell);
            }

            return cells;
        }

        /// <summary>
        /// Every standable cell in the largest connected piece of the map, sorted so the result is
        /// stable run to run.
        /// </summary>
        public static HashSet<Cell> Playable(IGridRules grid)
        {
            var best = new HashSet<Cell>();

            if (grid == null || grid.Map == null)
            {
                return best;
            }

            var seen = new HashSet<Cell>();
            var cells = new List<Cell>();
            var neighbours = new List<Cell>();

            // Tiles in a fixed order, so which piece wins a tie is the same every time.
            var tiles = new List<Hex>(grid.Map.Coordinates);
            tiles.Sort((a, b) => a.Q != b.Q ? a.Q.CompareTo(b.Q) : a.R.CompareTo(b.R));

            foreach (var tile in tiles)
            {
                cells.Clear();
                grid.CellsOf(tile, cells);

                foreach (var start in cells)
                {
                    if (seen.Contains(start) || !grid.IsWalkable(start))
                    {
                        continue;
                    }

                    var piece = new HashSet<Cell> { start };
                    var frontier = new Queue<Cell>();
                    frontier.Enqueue(start);
                    seen.Add(start);

                    while (frontier.Count > 0)
                    {
                        neighbours.Clear();
                        grid.Neighbours(frontier.Dequeue(), neighbours);

                        foreach (var next in neighbours)
                        {
                            if (seen.Add(next) && grid.IsWalkable(next))
                            {
                                piece.Add(next);
                                frontier.Enqueue(next);
                            }
                        }
                    }

                    if (piece.Count > best.Count)
                    {
                        best = piece;
                    }
                }
            }

            return best;
        }
    }
}
