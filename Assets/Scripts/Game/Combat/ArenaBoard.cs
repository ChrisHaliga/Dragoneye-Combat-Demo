using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Every question about the board, answered from the map's rules and who is standing on it.
    ///
    /// The seam between the fight and the grid. Walls, terrain cost and split tiles all live on
    /// the map side of it; occupancy lives on the game side; this is where the two are read
    /// together, and nothing else reads either for a fighting decision.
    /// </summary>
    public sealed class ArenaBoard : IBoardQuery
    {
        readonly ArenaMap m_Map;
        readonly UnitIndex m_Units;

        // Reused across calls. Every method that fills them consumes them before returning, and
        // PathTo hands back a copy, so no caller is left holding a buffer that later changes.
        readonly List<Cell> m_Path = new List<Cell>();
        readonly HashSet<Cell> m_Blocked = new HashSet<Cell>();
        readonly List<Cell> m_Cells = new List<Cell>();
        readonly Dictionary<Cell, int> m_Reach = new Dictionary<Cell, int>();

        public ArenaBoard(ArenaMap map, UnitIndex units)
        {
            m_Map = map;
            m_Units = units;
        }

        public bool IsReady => m_Map != null && m_Map.Map != null && m_Map.Grid != null && m_Units != null;

        IGridRules Grid => m_Map != null ? m_Map.Grid : null;

        // ---------- walking ----------

        public int CostTo(Cell from, Cell to) => TryPath(from, to, from, out var cost) ? cost : -1;

        public IReadOnlyList<Cell> PathTo(Cell from, Cell to) =>
            TryPath(from, to, from, out _) ? new List<Cell>(m_Path) : System.Array.Empty<Cell>();

        public IReadOnlyList<Cell> PathTo(Cell from, Cell to, Cell ignore) =>
            TryPath(from, to, ignore, out _) ? new List<Cell>(m_Path) : System.Array.Empty<Cell>();

        public bool IsOccupied(Cell cell) => m_Units != null && m_Units.IsOccupied(cell);

        public int StepsToEnter(Cell cell) => IsReady ? Grid.StepsToEnter(cell) : 1;

        public void Neighbours(Cell of, List<Cell> into)
        {
            if (IsReady)
            {
                Grid.Neighbours(of, into);
            }
        }

        /// <summary>Every cell this creature could walk to for this many steps, and what each costs.</summary>
        public void Reachable(Cell from, int budget, Dictionary<Cell, int> into)
        {
            into.Clear();

            if (!IsReady)
            {
                return;
            }

            m_Blocked.Clear();
            m_Units.CopyOccupiedTo(m_Blocked, from);
            HexPathfinder.Reachable(Grid, from, budget, m_Blocked, into);
        }

        /// <summary>
        /// The reachable cell nearest the target, for a creature that cannot get all the way.
        ///
        /// Nearest by distance to the target, and among equals the cheapest to reach. Nothing
        /// further away than the start is offered.
        /// </summary>
        public bool TryClosest(Cell from, Cell target, int budget, out Cell tile)
        {
            tile = from;

            if (!IsReady || budget <= 0)
            {
                return false;
            }

            Reachable(from, budget, m_Reach);

            var bestDistance = Cell.Distance(from, target);
            var bestCost = int.MaxValue;
            var found = false;

            foreach (var pair in m_Reach)
            {
                var distance = Cell.Distance(pair.Key, target);

                if (distance < bestDistance || (distance == bestDistance && found && pair.Value < bestCost))
                {
                    bestDistance = distance;
                    bestCost = pair.Value;
                    tile = pair.Key;
                    found = true;
                }
            }

            return found;
        }

        // ---------- reaching ----------

        public int StepsToReach(Cell from, Cell target, int reach) =>
            TryTileInReach(from, target, reach, out _, out var steps) ? steps : -1;

        /// <summary>
        /// The cheapest cell to stand on to have the target in reach, with a line to it.
        ///
        /// Where the creature already is, if that will do. Otherwise every cell within the reach
        /// of the target -- every area of every tile, since a split tile is two places to stand --
        /// that has a line to the target and nobody on it, priced by the route there.
        /// </summary>
        public bool TryTileInReach(Cell from, Cell target, int reach, out Cell tile, out int steps)
        {
            if (CombatRules.InRange(Cell.Distance(from, target), reach) && HasLine(from, target))
            {
                tile = from;
                steps = 0;
                return true;
            }

            tile = from;
            steps = -1;

            if (!IsReady)
            {
                return false;
            }

            foreach (var hex in Hex.Range(target.Tile, reach))
            {
                m_Cells.Clear();
                Grid.CellsOf(hex, m_Cells);

                foreach (var candidate in m_Cells)
                {
                    if (candidate == target || m_Units.IsOccupied(candidate)
                        || !CombatRules.InRange(Cell.Distance(candidate, target), reach)
                        || !HasLine(candidate, target))
                    {
                        continue;
                    }

                    var cost = CostTo(from, candidate);

                    if (cost < 0 || (steps >= 0 && cost >= steps))
                    {
                        continue;
                    }

                    steps = cost;
                    tile = candidate;
                }
            }

            return steps >= 0;
        }

        public bool HasOpenNeighbour(Cell from)
        {
            if (!IsReady)
            {
                return false;
            }

            m_Cells.Clear();
            Grid.Neighbours(from, m_Cells);

            foreach (var neighbour in m_Cells)
            {
                if (!m_Units.IsOccupied(neighbour))
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasEnemyInReach(Cell from, Party party, int reach)
        {
            if (!IsReady || reach <= 0)
            {
                return false;
            }

            foreach (var hex in Hex.Range(from.Tile, reach))
            {
                m_Cells.Clear();
                Grid.CellsOf(hex, m_Cells);

                foreach (var candidate in m_Cells)
                {
                    if (candidate == from || !m_Units.TryGet(candidate, out var occupant)
                        || !CombatRules.InRange(Cell.Distance(from, candidate), reach))
                    {
                        continue;
                    }

                    var creature = occupant.GetComponent<CreatureState>();

                    if (creature != null && creature.IsAlive && creature.Party != party
                        && HasLine(from, candidate))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ---------- seeing ----------

        /// <summary>What the walls make of a line. Bodies are <see cref="LineOfFire"/>'s to add.</summary>
        public LineVerdict Line(Cell from, Cell to) =>
            IsReady ? LineOfSight.Verdict(Grid, from, to) : LineVerdict.Clear;

        public bool HasLine(Cell from, Cell to) => Line(from, to) != LineVerdict.Blocked;

        bool TryPath(Cell from, Cell to, Cell ignore, out int cost)
        {
            m_Path.Clear();
            cost = -1;

            if (!IsReady)
            {
                return false;
            }

            m_Blocked.Clear();
            m_Units.CopyOccupiedTo(m_Blocked, from);
            m_Blocked.Remove(ignore);

            return HexPathfinder.TryFindPath(Grid, from, to, m_Blocked, m_Path, out cost);
        }
    }
}
