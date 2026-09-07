using System.Linq;
using Dragoneye.Combat;
using Dragoneye.Game.Combat;
using Dragoneye.Hex.Systems;
using Dragoneye.Sim;
using NUnit.Framework;
using UnityEngine;
using Cell = Dragoneye.Hex.Cell;
using Hex = Dragoneye.Hex.Hex;
using HexDirection = Dragoneye.Hex.HexDirection;
using HexLayout = Dragoneye.Hex.HexLayout;
using HexMap = Dragoneye.Hex.HexMap;
using HexTile = Dragoneye.Hex.HexTile;
using Wall = Dragoneye.Hex.Wall;
using WallFlags = Dragoneye.Hex.WallFlags;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// Who is watching whom across the grid: the arc, the reach, and the walls between.
    /// </summary>
    public class ThreatGeometryTests
    {
        static HexMap Map(int radius) =>
            new HexMap(new HexLayout(1f, Vector3.zero),
                Hex.Range(Hex.Zero, radius).Select(h => new HexTile(h, null)));

        static Cell C(Hex hex, int area = 0) => new Cell(hex, (byte)area);

        static readonly Facing North = Facing.Of(0);

        [Test]
        public void TheThreeCellsAheadAreWatchedAndTheRestAreNot()
        {
            var grid = new GridRules(Map(3));

            Assert.IsTrue(ThreatGeometry.Watches(grid, C(Hex.Zero), North, C(Hex.Zero.Neighbor(HexDirection.North))));
            Assert.IsTrue(ThreatGeometry.Watches(grid, C(Hex.Zero), North, C(Hex.Zero.Neighbor(HexDirection.NorthEast))));
            Assert.IsTrue(ThreatGeometry.Watches(grid, C(Hex.Zero), North, C(Hex.Zero.Neighbor(HexDirection.NorthWest))));
            Assert.IsFalse(ThreatGeometry.Watches(grid, C(Hex.Zero), North, C(Hex.Zero.Neighbor(HexDirection.SouthEast))));
            Assert.IsFalse(ThreatGeometry.Watches(grid, C(Hex.Zero), North, C(new Hex(0, 2))), "two tiles out is out of reach");
        }

        [Test]
        public void LeavingAWatchedCellProvokesAndSidesteppingWithinTheArcDoesNot()
        {
            var grid = new GridRules(Map(3));
            var ahead = Hex.Zero.Neighbor(HexDirection.North);
            var aheadRight = Hex.Zero.Neighbor(HexDirection.NorthEast);
            var flank = Hex.Zero.Neighbor(HexDirection.SouthEast);
            var away = new Hex(0, 2);

            Assert.IsTrue(ThreatGeometry.Provokes(grid, C(Hex.Zero), North, C(ahead), C(away)));
            Assert.IsTrue(ThreatGeometry.Provokes(grid, C(Hex.Zero), North, C(aheadRight), C(flank)));
            Assert.IsFalse(ThreatGeometry.Provokes(grid, C(Hex.Zero), North, C(ahead), C(aheadRight)));
            Assert.IsFalse(ThreatGeometry.Provokes(grid, C(Hex.Zero), North, C(away), C(ahead)), "walking in does not");
            Assert.IsFalse(ThreatGeometry.Provokes(grid, C(Hex.Zero), North, C(ahead), C(ahead)), "standing still is not a move");
        }

        [Test]
        public void AWallItCannotSeeThroughIsNotWatchedOver()
        {
            var map = Map(3);
            var grid = new GridRules(map);
            var ahead = Hex.Zero.Neighbor(HexDirection.North);

            map.SetEdge(Hex.Zero, HexDirection.North, new Wall(WallFlags.Solid));
            Assert.IsFalse(ThreatGeometry.Watches(grid, C(Hex.Zero), North, C(ahead)));
            Assert.IsFalse(ThreatGeometry.Provokes(grid, C(Hex.Zero), North, C(ahead), C(new Hex(0, 2))));

            map.SetEdge(Hex.Zero, HexDirection.North, new Wall(WallFlags.BlocksSight));
            Assert.IsFalse(ThreatGeometry.Watches(grid, C(Hex.Zero), North, C(ahead)), "a curtain hides as well as a wall");

            map.SetEdge(Hex.Zero, HexDirection.North, new Wall(WallFlags.BlocksMovement));
            Assert.IsTrue(ThreatGeometry.Watches(grid, C(Hex.Zero), North, C(ahead)), "a low wall is swung over");
        }

        [Test]
        public void TheHalvesOfASplitTileHaveABearingBetweenThem()
        {
            var map = Map(2);
            var grid = new GridRules(map);
            map.SetRay(Hex.Zero, 0, new Wall(WallFlags.BlocksMovement));
            map.SetRay(Hex.Zero, 6, new Wall(WallFlags.BlocksMovement));

            var eastward = ThreatGeometry.Bearing(grid, C(Hex.Zero, 1), C(Hex.Zero, 0));
            var westward = ThreatGeometry.Bearing(grid, C(Hex.Zero, 0), C(Hex.Zero, 1));

            Assert.IsTrue(eastward == Facing.Of(1) || eastward == Facing.Of(2), "the east half lies east");
            Assert.IsTrue(westward == Facing.Of(4) || westward == Facing.Of(5), "the west half lies west");
            Assert.IsTrue(ThreatGeometry.Watches(grid, C(Hex.Zero, 1), eastward, C(Hex.Zero, 0)),
                "facing the other half over a low wall is watching it");
        }
    }
}
