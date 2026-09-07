using System.Collections.Generic;
using System.Linq;
using Dragoneye.Hex.Systems;
using NUnit.Framework;
using UnityEngine;
using Cell = Dragoneye.Hex.Cell;
using Hex = Dragoneye.Hex.Hex;
using HexLayout = Dragoneye.Hex.HexLayout;
using HexMap = Dragoneye.Hex.HexMap;
using HexTile = Dragoneye.Hex.HexTile;
using Wall = Dragoneye.Hex.Wall;

namespace Dragoneye.Hex.Tests
{
    public class HexPathfinderTests
    {
        static HexMap Map(int radius) =>
            new HexMap(new HexLayout(1f, Vector3.zero),
                Hex.Range(Hex.Zero, radius).Select(h => new HexTile(h, null)));

        static GridRules Grid(int radius) => new GridRules(Map(radius));

        static Cell C(Hex hex) => Cell.Whole(hex);

        static HashSet<Cell> Cells(IEnumerable<Hex> hexes) => new HashSet<Cell>(hexes.Select(Cell.Whole));

        static List<Cell> Path(IGridRules grid, Hex from, Hex to, params Hex[] blocked)
        {
            var path = new List<Cell>();
            HexPathfinder.TryFindPath(grid, C(from), C(to), Cells(blocked), path, out _);
            return path;
        }

        [Test]
        public void APathExcludesTheStartAndIncludesTheDestination()
        {
            // The count is the cost, so an off-by-one here becomes an off-by-one in AP.
            var path = Path(Grid(4), Hex.Zero, new Hex(2, 0));

            Assert.AreEqual(2, path.Count);
            Assert.AreEqual(C(new Hex(2, 0)), path[path.Count - 1]);
            CollectionAssert.DoesNotContain(path, C(Hex.Zero));
        }

        [Test]
        public void CostMatchesDistanceOnAnEmptyMap()
        {
            var grid = Grid(5);

            foreach (var target in Hex.Range(Hex.Zero, 4))
            {
                if (target == Hex.Zero)
                {
                    continue;
                }

                Assert.AreEqual(Hex.Distance(Hex.Zero, target),
                    HexPathfinder.CostTo(grid, C(Hex.Zero), C(target), null),
                    $"{target} should cost its distance when nothing is in the way");
            }
        }

        [Test]
        public void RoutesGoAroundBlockedCells()
        {
            var grid = Grid(4);
            var ring = Hex.Zero.Neighbors().ToArray();

            // Ring one fully blocked except one gap: the route must find the gap.
            var blocked = Cells(ring.Skip(1));
            var path = new List<Cell>();

            Assert.IsTrue(HexPathfinder.TryFindPath(grid, C(Hex.Zero), C(new Hex(2, 0)), blocked, path, out _));
            Assert.AreEqual(C(ring[0]), path[0], "The only gap in the ring is the only first step");
            CollectionAssert.IsNotSubsetOf(blocked, path);
        }

        [Test]
        public void ACompletelyWalledInStartHasNoRoute()
        {
            var blocked = Cells(Hex.Zero.Neighbors());

            Assert.AreEqual(-1, HexPathfinder.CostTo(Grid(4), C(Hex.Zero), C(new Hex(2, 0)), blocked));
        }

        [Test]
        public void ABlockedDestinationIsNotReachable()
        {
            // Attacking resolves separately; a cell with something standing on it is never a
            // destination, which is what stops a move being priced onto an occupied tile.
            var target = C(new Hex(2, 0));

            Assert.AreEqual(-1,
                HexPathfinder.CostTo(Grid(4), C(Hex.Zero), target, new HashSet<Cell> { target }));
        }

        [Test]
        public void OffMapDestinationsAreNotReachable()
        {
            Assert.AreEqual(-1, HexPathfinder.CostTo(Grid(2), C(Hex.Zero), C(new Hex(40, -40)), null));
        }

        [Test]
        public void TheStartIsNeverItsOwnDestination()
        {
            Assert.AreEqual(-1, HexPathfinder.CostTo(Grid(3), C(Hex.Zero), C(Hex.Zero), null));
        }

        [Test]
        public void MaxCostStopsTheSearchShort()
        {
            var grid = Grid(6);
            var far = C(new Hex(5, 0));

            Assert.AreEqual(-1, HexPathfinder.CostTo(grid, C(Hex.Zero), far, null, maxCost: 3));
            Assert.AreEqual(5, HexPathfinder.CostTo(grid, C(Hex.Zero), far, null, maxCost: 5));
        }

        [Test]
        public void EveryStepIsAdjacentToTheLast()
        {
            // A path with a jump in it would be walked as a teleport by anything following it.
            var path = Path(Grid(5), Hex.Zero, new Hex(3, -2));
            var previous = C(Hex.Zero);

            foreach (var step in path)
            {
                Assert.AreEqual(1, Cell.Distance(previous, step), $"{previous} to {step} is not a step");
                previous = step;
            }
        }

        [Test]
        public void DearGroundIsWalkedAroundWhenTheWayRoundIsCheaper()
        {
            // Straight through costs one step plus five for the mud; round it costs three.
            var grid = new Priced(Grid(3), new Hex(1, 0), 5);
            var path = new List<Cell>();

            Assert.IsTrue(HexPathfinder.TryFindPath(grid, C(Hex.Zero), C(new Hex(2, 0)), null, path, out var cost));
            Assert.AreEqual(3, cost);
            CollectionAssert.DoesNotContain(path, C(new Hex(1, 0)));
        }

        [Test]
        public void TheCostOfARouteIsWhatItsCellsCostToEnter()
        {
            var grid = new Priced(Grid(3), new Hex(1, 0), 2);

            // Every route to (1, 0) ends by entering it, so every one pays its two.
            Assert.AreEqual(2, HexPathfinder.CostTo(grid, C(Hex.Zero), C(new Hex(1, 0)), null));
        }

        [Test]
        public void TheGridDecidesAdjacencyNotTheCoordinate()
        {
            // A grid that only ever steps North: the coordinate's six neighbours mean nothing.
            var grid = new Northbound(Grid(4));

            Assert.AreEqual(2, HexPathfinder.CostTo(grid, C(Hex.Zero), C(new Hex(0, 2)), null));
            Assert.AreEqual(-1, HexPathfinder.CostTo(grid, C(Hex.Zero), C(new Hex(1, 0)), null));
        }

        [Test]
        public void ReachIsEveryCellWithinTheBudgetWithItsCost()
        {
            var reach = new Dictionary<Cell, int>();

            HexPathfinder.Reachable(Grid(4), C(Hex.Zero), 2, null, reach);

            Assert.AreEqual(18, reach.Count, "two rings, the start left out");
            Assert.IsFalse(reach.ContainsKey(C(Hex.Zero)));
            Assert.AreEqual(1, reach[C(new Hex(1, 0))]);
            Assert.AreEqual(2, reach[C(new Hex(2, 0))]);
        }

        [Test]
        public void DegenerateInputsDoNotThrow()
        {
            var path = new List<Cell>();

            Assert.IsFalse(HexPathfinder.TryFindPath(null, C(Hex.Zero), C(new Hex(1, 0)), null, path, out _));
            Assert.IsFalse(HexPathfinder.TryFindPath(Grid(2), C(Hex.Zero), C(new Hex(1, 0)), null, null, out _));
            Assert.AreEqual(-1, HexPathfinder.CostTo(null, C(Hex.Zero), C(new Hex(1, 0)), null));
        }

        [Test]
        public void ThePathListIsClearedBeforeUse()
        {
            var path = new List<Cell> { C(new Hex(9, 9)) };

            HexPathfinder.TryFindPath(Grid(3), C(Hex.Zero), C(new Hex(1, 0)), null, path, out _);

            CollectionAssert.DoesNotContain(path, C(new Hex(9, 9)));
        }

        /// <summary>An open grid with one tile priced dearer than the rest.</summary>
        sealed class Priced : IGridRules
        {
            readonly IGridRules m_Inner;
            readonly Hex m_Dear;
            readonly int m_Steps;

            public Priced(IGridRules inner, Hex dear, int steps)
            {
                m_Inner = inner;
                m_Dear = dear;
                m_Steps = steps;
            }

            public HexMap Map => m_Inner.Map;
            public bool Contains(Cell cell) => m_Inner.Contains(cell);
            public bool IsWalkable(Cell cell) => m_Inner.IsWalkable(cell);
            public int StepsToEnter(Cell cell) => cell.Tile == m_Dear ? m_Steps : m_Inner.StepsToEnter(cell);
            public void Neighbours(Cell from, List<Cell> into) => m_Inner.Neighbours(from, into);
            public void CellsOf(Hex tile, List<Cell> into) => m_Inner.CellsOf(tile, into);
            public Wall HalfEdge(Hex tile, int halfEdge) => m_Inner.HalfEdge(tile, halfEdge);
            public Wall Ray(Hex tile, int ray) => m_Inner.Ray(tile, ray);
        }

        /// <summary>A grid on which the only step is North.</summary>
        sealed class Northbound : IGridRules
        {
            readonly IGridRules m_Inner;

            public Northbound(IGridRules inner) => m_Inner = inner;

            public HexMap Map => m_Inner.Map;
            public bool Contains(Cell cell) => m_Inner.Contains(cell);
            public bool IsWalkable(Cell cell) => m_Inner.IsWalkable(cell);
            public int StepsToEnter(Cell cell) => 1;
            public void CellsOf(Hex tile, List<Cell> into) => m_Inner.CellsOf(tile, into);
            public Wall HalfEdge(Hex tile, int halfEdge) => Wall.None;
            public Wall Ray(Hex tile, int ray) => Wall.None;

            public void Neighbours(Cell from, List<Cell> into)
            {
                var north = Cell.Whole(from.Tile.Neighbor(HexDirection.North));

                if (m_Inner.Contains(north))
                {
                    into.Add(north);
                }
            }
        }
    }
}
