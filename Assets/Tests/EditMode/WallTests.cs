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
using AreaTable = Dragoneye.Hex.AreaTable;
using AreaLayout = Dragoneye.Hex.AreaLayout;
using AreaGeometry = Dragoneye.Hex.AreaGeometry;
using TileGeometry = Dragoneye.Hex.TileGeometry;
using Wall = Dragoneye.Hex.Wall;
using WallFlags = Dragoneye.Hex.WallFlags;
using HexDirection = Dragoneye.Hex.HexDirection;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// Walls through and between tiles: what they cut a tile into, where a step can go, and what
    /// a line meets. The full set lives in the scratch harness; these are the ones that must
    /// never regress inside the editor.
    /// </summary>
    public class WallTests
    {
        static readonly Wall Solid = new Wall(WallFlags.Solid);
        static readonly Wall Low = new Wall(WallFlags.BlocksMovement);
        static readonly Wall Curtain = new Wall(WallFlags.BlocksSight);

        static HexMap Map(int radius) =>
            new HexMap(new HexLayout(1f, Vector3.zero),
                Hex.Range(Hex.Zero, radius).Select(h => new HexTile(h, null)));

        static Cell C(Hex hex, int area = 0) => new Cell(hex, (byte)area);

        [Test]
        public void ALineThroughTheMiddleMakesTwoAreas()
        {
            var layout = AreaTable.For((1 << 0) | (1 << 6));

            Assert.AreEqual(2, layout.Count);
            Assert.AreEqual(0, layout.AreaOf(0), "the east half is numbered first");
            Assert.AreEqual(1, layout.AreaOf(6));
        }

        [Test]
        public void APieceNarrowerThanNinetyDegreesIsNotAnArea()
        {
            var layout = AreaTable.For((1 << 0) | (1 << 2));

            Assert.AreEqual(1, layout.Count);
            Assert.AreEqual(AreaLayout.Dead, layout.AreaOf(0));
            Assert.AreEqual(AreaLayout.Dead, layout.AreaOf(1));
        }

        [Test]
        public void NoPatternOfRaysMakesMoreThanFour()
        {
            var most = 0;

            for (var mask = 0; mask < 4096; mask++)
            {
                most = Mathf.Max(most, AreaTable.For(mask).Count);
            }

            Assert.AreEqual(4, most);
        }

        [Test]
        public void WholeTilesKeepTheOldBearing()
        {
            var map = Map(2);

            for (var d = 0; d < 6; d++)
            {
                var next = Hex.Zero.Neighbor((HexDirection)d);
                Assert.AreEqual((HexDirection)d, AreaGeometry.Direction(map, C(Hex.Zero), C(next)));
            }
        }

        [Test]
        public void ABearingOnABoundaryGoesToTheLowerDirection()
        {
            TileGeometry.RayEnd(1, out var x, out var z);
            Assert.AreEqual(HexDirection.North, TileGeometry.Bearing(x, z));

            TileGeometry.RayEnd(11, out x, out z);
            Assert.AreEqual(HexDirection.North, TileGeometry.Bearing(x, z), "330 degrees is North's too");
        }

        [Test]
        public void AWalledEdgeIsNeitherAWayOutNorAWayIn()
        {
            var map = Map(2);
            var grid = new GridRules(map);
            var north = Hex.Zero.Neighbor(HexDirection.North);

            map.SetEdge(Hex.Zero, HexDirection.North, Solid);

            var into = new List<Cell>();
            grid.Neighbours(C(Hex.Zero), into);
            CollectionAssert.DoesNotContain(into, C(north));

            into.Clear();
            grid.Neighbours(C(north), into);
            CollectionAssert.DoesNotContain(into, C(Hex.Zero));

            Assert.AreEqual(2, HexPathfinder.CostTo(grid, C(Hex.Zero), C(north), null));
        }

        [Test]
        public void TheHalvesOfASplitTileHaveNoPathBetweenThemButAWayRound()
        {
            var map = Map(2);
            var grid = new GridRules(map);

            map.SetRay(Hex.Zero, 0, Solid);
            map.SetRay(Hex.Zero, 6, Solid);

            Assert.AreEqual(2, map[Hex.Zero].Areas.Count);

            var into = new List<Cell>();
            grid.Neighbours(C(Hex.Zero, 0), into);
            Assert.AreEqual(4, into.Count, "the east half opens onto four tiles");

            Assert.AreEqual(2, HexPathfinder.CostTo(grid, C(Hex.Zero, 0), C(Hex.Zero, 1), null));
            Assert.AreEqual(1, Cell.Distance(C(Hex.Zero, 0), C(Hex.Zero, 1)));
        }

        [Test]
        public void ALineMeetsWhatIsInItsWay()
        {
            var map = Map(3);
            var grid = new GridRules(map);
            var ne = Hex.Zero.Neighbor(HexDirection.NorthEast);

            Assert.AreEqual(LineVerdict.Clear, LineOfSight.Verdict(grid, C(Hex.Zero), C(ne)));

            map.SetEdge(Hex.Zero, HexDirection.NorthEast, Curtain);
            Assert.AreEqual(LineVerdict.Blocked, LineOfSight.Verdict(grid, C(Hex.Zero), C(ne)));
            Assert.AreEqual(LineVerdict.Blocked, LineOfSight.Verdict(grid, C(ne), C(Hex.Zero)), "from either side");

            map.SetEdge(Hex.Zero, HexDirection.NorthEast, Low);
            Assert.AreEqual(LineVerdict.Obstructed, LineOfSight.Verdict(grid, C(Hex.Zero), C(ne)));

            map.SetEdge(Hex.Zero, HexDirection.NorthEast, Wall.None);
            map.SetRay(Hex.Zero, 0, Solid);
            map.SetRay(Hex.Zero, 6, Solid);
            Assert.AreEqual(LineVerdict.Blocked,
                LineOfSight.Verdict(grid, C(Hex.Zero, 0), C(Hex.Zero, 1)),
                "the halves of a split tile cannot see each other through a high wall");
        }

        [Test]
        public void TheTwinOfAHalfEdgeIsItsMirrorAcrossTheEdge()
        {
            // The east half of a South edge is the east half of the neighbour's North edge.
            Assert.AreEqual(5, TileGeometry.Twin(0));
            Assert.AreEqual(6, TileGeometry.Twin(11));

            for (var h = 0; h < TileGeometry.HalfEdges; h++)
            {
                Assert.AreEqual(h, TileGeometry.Twin(TileGeometry.Twin(h)), "a twin's twin is itself");
                Assert.AreEqual(TileGeometry.EdgeOf(h).Opposite(), TileGeometry.EdgeOf(TileGeometry.Twin(h)),
                    "the twin lies on the opposite edge");
            }
        }

        [Test]
        public void AQuarterCutCornerStepsOntoTheRightHalfOfTheTileBelow()
        {
            // The check that found the mirror bug: a south-east quarter of one tile and the east
            // half of the tile below share ground, and the west half is on the other side of a wall.
            var map = Map(2);
            var grid = new GridRules(map);
            var below = Hex.Zero.Neighbor(HexDirection.South);

            map.SetRay(Hex.Zero, 3, Solid);
            map.SetRay(Hex.Zero, 6, Solid);
            map.SetRay(below, 0, Solid);
            map.SetRay(below, 6, Solid);

            var quarter = new Cell(Hex.Zero, map[Hex.Zero].Areas.AreaOf(4));
            var into = new List<Cell>();
            grid.Neighbours(quarter, into);

            CollectionAssert.Contains(into, new Cell(below, 0));
            CollectionAssert.DoesNotContain(into, new Cell(below, 1));
        }

        [Test]
        public void APointIsPickedIntoTheAreaItLiesIn()
        {
            var map = Map(1);
            map.SetRay(Hex.Zero, 0, Solid);
            map.SetRay(Hex.Zero, 6, Solid);

            Assert.AreEqual(C(Hex.Zero, 0), map.CellAt(new Vector3(0.3f, 0f, 0.1f)), "east of the wall");
            Assert.AreEqual(C(Hex.Zero, 1), map.CellAt(new Vector3(-0.3f, 0f, 0.1f)), "west of it");
            Assert.AreEqual(C(new Hex(1, 0)), map.CellAt(map.Layout.ToWorld(new Hex(1, 0))), "a whole tile is its one cell");
            Assert.IsNull(map.CellAt(new Vector3(9f, 0f, 9f)), "off the map is nowhere");

            // Rays 0 and 2 walled: the sixty degrees between them is a sliver, the rest one area.
            map.SetRay(Hex.Zero, 6, Wall.None);
            map.SetRay(Hex.Zero, 2, Solid);
            Assert.IsNull(map.CellAt(new Vector3(0.1f, 0f, 0.35f)), "a sliver is nowhere to stand");
            Assert.AreEqual(C(Hex.Zero, 0), map.CellAt(new Vector3(0.35f, 0f, -0.1f)), "the rest is");
        }

        [Test]
        public void AnAreaIsCarriedAcrossAChangeOfWalls()
        {
            var whole = AreaTable.Whole;
            var halves = AreaTable.For((1 << 0) | (1 << 6));
            var quarters = AreaTable.For((1 << 0) | (1 << 3) | (1 << 6) | (1 << 9));
            var sliver = AreaTable.For((1 << 0) | (1 << 2));

            Assert.AreEqual(0, halves.Carry(1, whole), "a wall coming down merges the halves");
            Assert.AreEqual(0, whole.Carry(0, halves), "a wall going up keeps the creature on the first wedge's side");
            Assert.AreEqual(quarters.AreaOf(6), halves.Carry(1, quarters), "the west half becomes the south-west quarter");
            Assert.AreEqual(AreaLayout.Dead, whole.Carry(0, sliver), "ground that became a sliver is nowhere to stand");
            Assert.AreEqual(AreaLayout.Dead, halves.Carry(7, whole), "an area that never existed goes nowhere");
        }

        [Test]
        public void AWallChangeReportsTheAreasTheTileHadBefore()
        {
            var map = Map(1);
            AreaLayout reported = null;

            map.WallChanged += (tile, before) => reported = before;
            map.SetRay(Hex.Zero, 0, Solid);
            map.SetRay(Hex.Zero, 6, Solid);

            Assert.AreEqual(1, reported.Count, "the second ray was the one that split it");
            Assert.AreEqual(2, map[Hex.Zero].Areas.Count);
        }

        [Test]
        public void TheTilesBetweenTwoCellsExcludeBothEnds()
        {
            var between = new List<Hex>();

            LineOfSight.TilesBetween(C(Hex.Zero), C(new Hex(3, 0)), between);
            CollectionAssert.AreEqual(new[] { new Hex(1, 0), new Hex(2, 0) }, between);

            LineOfSight.TilesBetween(C(Hex.Zero), C(new Hex(1, 0)), between);
            Assert.IsEmpty(between, "neighbours have nothing between them");

            LineOfSight.TilesBetween(C(Hex.Zero, 0), C(Hex.Zero, 1), between);
            Assert.IsEmpty(between, "nor do two areas of one tile");
        }

        [Test]
        public void NobodySpawnsInASealedPocket()
        {
            var map = Map(2);
            var grid = new GridRules(map);
            var pocket = Hex.Zero.Neighbor(HexDirection.North);

            for (var d = 0; d < 6; d++)
            {
                map.SetEdge(pocket, (HexDirection)d, Solid);
            }

            var playable = HexSpawnPlacement.Playable(grid);
            CollectionAssert.DoesNotContain(playable, C(pocket));

            var spawns = HexSpawnPlacement.PlaceGrouped(grid, new[] { 0, 0, 1, 1 }, 2);
            Assert.IsTrue(spawns.All(playable.Contains));
        }
    }
}
