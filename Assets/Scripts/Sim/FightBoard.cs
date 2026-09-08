using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;

namespace Dragoneye.Sim
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Every question about the board, answered from the map's rules and who is standing on it.
    ///
    /// The seam between the fight and the grid. Walls, terrain cost and split tiles all live on
    /// the map side of it; occupancy comes through <see cref="IOccupancy"/>; this is where the
    /// two are read together, and nothing else reads either for a fighting decision. The server
    /// builds one over the fight's own table, a client builds one over replicated state, and
    /// both answer the same question the same way -- which is what lets a cursor promise a price
    /// the server will honour.
    /// </summary>
    public class FightBoard : IBoardQuery
    {
        readonly IGridRules m_Grid;
        readonly IOccupancy m_Occupancy;

        // Reused across calls. Every method that fills them consumes them before returning, and
        // PathTo hands back a copy, so no caller is left holding a buffer that later changes.
        readonly List<Cell> m_Path = new List<Cell>();
        readonly HashSet<Cell> m_Blocked = new HashSet<Cell>();
        readonly List<Cell> m_Cells = new List<Cell>();
        readonly Dictionary<Cell, int> m_Reach = new Dictionary<Cell, int>();

        public FightBoard(IGridRules grid, IOccupancy occupancy)
        {
            m_Grid = grid;
            m_Occupancy = occupancy;
        }

        public bool IsReady => Grid != null && m_Occupancy != null;

        /// <summary>The rules of the ground. Virtual so a client board can read a grid that gets rebuilt.</summary>
        public virtual IGridRules Grid => m_Grid;

        public IOccupancy Occupancy => m_Occupancy;

        // ---------- walking ----------

        public int CostTo(Cell from, Cell to) => TryPath(from, to, from, out var cost) ? cost : -1;

        public IReadOnlyList<Cell> PathTo(Cell from, Cell to) =>
            TryPath(from, to, from, out _) ? new List<Cell>(m_Path) : System.Array.Empty<Cell>();

        public IReadOnlyList<Cell> PathTo(Cell from, Cell to, Cell ignore) =>
            TryPath(from, to, ignore, out _) ? new List<Cell>(m_Path) : System.Array.Empty<Cell>();

        public bool IsOccupied(Cell cell) => IsReady && m_Occupancy.IsOccupied(cell);

        public int StepsToEnter(Cell cell) => IsReady ? Grid.StepsToEnter(cell) : 1;

        public void Neighbours(Cell of, List<Cell> into)
        {
            // Emptied even with no grid behind us. A board that is not ready yet has no neighbours
            // to offer, and leaving the caller's last answer in the list would have it read as
            // this one.
            into.Clear();

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
            m_Occupancy.CopyOccupiedTo(m_Blocked, from);
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
        /// that has a line to the target and nobody on it, priced by the route there. One search
        /// prices every candidate: the whole board is reachable from the start in one Dijkstra,
        /// and asking it thirty times for thirty candidates was the cost of the old cursor.
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

            Reachable(from, int.MaxValue, m_Reach);

            foreach (var hex in Hex.Range(target.Tile, reach))
            {
                Grid.CellsOf(hex, m_Cells);

                foreach (var candidate in m_Cells)
                {
                    if (candidate == target || m_Occupancy.IsOccupied(candidate)
                        || !m_Reach.TryGetValue(candidate, out var cost)
                        || (steps >= 0 && cost >= steps)
                        || !CombatRules.InRange(Cell.Distance(candidate, target), reach)
                        || !HasLine(candidate, target))
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

            Grid.Neighbours(from, m_Cells);

            foreach (var neighbour in m_Cells)
            {
                if (!m_Occupancy.IsOccupied(neighbour))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether anybody of another side is standing within reach, with a line to them.</summary>
        public bool HasEnemyInReach(Cell from, Party party, int reach)
        {
            if (!IsReady || reach <= 0)
            {
                return false;
            }

            foreach (var hex in Hex.Range(from.Tile, reach))
            {
                Grid.CellsOf(hex, m_Cells);

                foreach (var candidate in m_Cells)
                {
                    if (candidate == from || !m_Occupancy.TryGet(candidate, out var id)
                        || !CombatRules.InRange(Cell.Distance(from, candidate), reach))
                    {
                        continue;
                    }

                    if (m_Occupancy.IsAlive(id) && m_Occupancy.PartyOf(id) != party
                        && HasLine(from, candidate))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ---------- seeing ----------

        /// <summary>What the walls make of a line. Bodies are <see cref="ShotLines"/>' to add.</summary>
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
            m_Occupancy.CopyOccupiedTo(m_Blocked, from);
            m_Blocked.Remove(ignore);

            return HexPathfinder.TryFindPath(Grid, from, to, m_Blocked, m_Path, out cost);
        }
    }
}
