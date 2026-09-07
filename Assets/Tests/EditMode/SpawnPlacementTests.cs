using System.Collections.Generic;
using System.Linq;
using Dragoneye.Hex.Systems;
using Dragoneye.Multiplayer;
using NUnit.Framework;
using Cell = Dragoneye.Hex.Cell;
using Hex = Dragoneye.Hex.Hex;
using HexLayout = Dragoneye.Hex.HexLayout;
using HexMap = Dragoneye.Hex.HexMap;
using HexTile = Dragoneye.Hex.HexTile;
using TerrainType = Dragoneye.Hex.TerrainType;
using Wall = Dragoneye.Hex.Wall;
using WallFlags = Dragoneye.Hex.WallFlags;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Dragoneye.Hex.Tests
{
    public class FindNearestFreeTests
    {
        static HexMap Map(int radius, TerrainType terrain = null) =>
            new HexMap(new HexLayout(1f, Vector3.zero),
                Hex.Range(Hex.Zero, radius).Select(h => new HexTile(h, terrain)));

        static GridRules Grid(int radius, TerrainType terrain = null) => new GridRules(Map(radius, terrain));

        static Cell C(Hex hex, int area = 0) => new Cell(hex, (byte)area);

        [Test]
        public void AFreeAnchorIsUsedAsIs()
        {
            Assert.AreEqual(C(Hex.Zero),
                HexSpawnPlacement.FindNearestFree(Grid(3), C(Hex.Zero), new HashSet<Cell>()));
        }

        [Test]
        public void ATakenAnchorSpillsToAnAdjacentCell()
        {
            var taken = new HashSet<Cell> { C(Hex.Zero) };

            var cell = HexSpawnPlacement.FindNearestFree(Grid(3), C(Hex.Zero), taken);

            Assert.AreEqual(1, Cell.Distance(C(Hex.Zero), cell), "Should land on the first ring");
        }

        [Test]
        public void ItFillsInwardRingsBeforeOuterOnes()
        {
            // Rings rather than a line, so a party clusters around its anchor.
            var grid = Grid(4);
            var taken = new HashSet<Cell>();

            for (var i = 0; i < 7; i++)
            {
                var cell = HexSpawnPlacement.FindNearestFree(grid, C(Hex.Zero), taken);
                Assert.LessOrEqual(Cell.Distance(C(Hex.Zero), cell), 1,
                    "The centre plus its six neighbours should fill before ring 2");
                taken.Add(cell);
            }

            Assert.AreEqual(2, Cell.Distance(C(Hex.Zero), HexSpawnPlacement.FindNearestFree(grid, C(Hex.Zero), taken)));
        }

        [Test]
        public void ASplitTileOffersEachOfItsAreas()
        {
            var map = Map(3);
            var grid = new GridRules(map);
            map.SetRay(Hex.Zero, 0, new Wall(WallFlags.Solid));
            map.SetRay(Hex.Zero, 6, new Wall(WallFlags.Solid));

            var taken = new HashSet<Cell> { C(Hex.Zero, 0) };

            Assert.AreEqual(C(Hex.Zero, 1), HexSpawnPlacement.FindNearestFree(grid, C(Hex.Zero), taken),
                "the other half of the anchor's tile comes before the next ring");
        }

        [Test]
        public void EveryPlacementIsDistinct()
        {
            var grid = Grid(3);
            var taken = new HashSet<Cell>();

            for (var i = 0; i < 12; i++)
            {
                var cell = HexSpawnPlacement.FindNearestFree(grid, C(Hex.Zero), taken);
                Assert.IsTrue(taken.Add(cell), $"{cell} was handed out twice");
            }
        }

        [Test]
        public void UnwalkableTilesAreSkipped()
        {
            var stone = ScriptableObject.CreateInstance<TerrainType>();
            try
            {
                typeof(TerrainType)
                    .GetField("m_IsWalkable", System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(stone, false);

                // Whole map unwalkable: nothing to find, so it falls back to the anchor rather than
                // looping or throwing.
                Assert.AreEqual(C(Hex.Zero),
                    HexSpawnPlacement.FindNearestFree(Grid(2, stone), C(Hex.Zero), new HashSet<Cell>()));
            }
            finally
            {
                Object.DestroyImmediate(stone);
            }
        }

        [Test]
        public void AnAnchorOffTheMapStillResolvesOntoIt()
        {
            var grid = Grid(2);
            var cell = HexSpawnPlacement.FindNearestFree(grid, C(new Hex(20, -20)), new HashSet<Cell>());

            Assert.AreNotEqual(C(new Hex(20, -20)), cell, "Should have walked back onto the map");
            Assert.IsTrue(grid.Contains(cell));
        }

        [Test]
        public void DegenerateInputsFallBackToTheAnchor()
        {
            Assert.AreEqual(C(Hex.Zero), HexSpawnPlacement.FindNearestFree(null, C(Hex.Zero), null));
            Assert.AreEqual(C(Hex.Zero), HexSpawnPlacement.FindNearestFree(Grid(2), C(Hex.Zero), null));
        }

        [Test]
        public void TheTakenSetIsNotMutated()
        {
            var taken = new HashSet<Cell> { C(Hex.Zero) };

            HexSpawnPlacement.FindNearestFree(Grid(3), C(Hex.Zero), taken);

            Assert.AreEqual(1, taken.Count, "Callers own the set; the search must not add to it");
        }
    }

    public class SessionFaultTests
    {
        static SessionFault Classify(SessionError error) =>
            LobbyProjection.Classify(new SessionException("test", error, null));

        [TestCase(SessionError.SessionNotFound, SessionFault.NotFound)]
        [TestCase(SessionError.SessionDeleted, SessionFault.Deleted)]
        [TestCase(SessionError.Forbidden, SessionFault.Forbidden)]
        [TestCase(SessionError.NotAuthorized, SessionFault.NotAuthorized)]
        [TestCase(SessionError.RateLimitExceeded, SessionFault.RateLimited)]
        [TestCase(SessionError.SessionTypeAlreadyExists, SessionFault.AlreadyInSession)]
        public void SdkErrorsMapToOwnedFaults(SessionError error, SessionFault expected)
        {
            Assert.AreEqual(expected, Classify(error));
        }

        [TestCase(SessionError.NetworkManagerNotInitialized)]
        [TestCase(SessionError.NetworkManagerStartFailed)]
        [TestCase(SessionError.NetworkSetupFailed)]
        public void EveryNetcodeFailureCollapsesToOneFault(SessionError error)
        {
            // The player cannot act differently on these three, so the UI should not offer three
            // different sentences.
            Assert.AreEqual(SessionFault.NetcodeFailed, Classify(error));
        }

        [Test]
        public void UnmappedErrorsAndPlainExceptionsAreUnknown()
        {
            Assert.AreEqual(SessionFault.Unknown, Classify(SessionError.Unknown));
            Assert.AreEqual(SessionFault.Unknown, LobbyProjection.Classify(new System.Exception("boom")));
        }

        [Test]
        public void ClassificationNeverReportsSuccess()
        {
            // None means "nothing went wrong"; returning it from a failure path would clear the
            // fault the caller is trying to report.
            foreach (SessionError error in System.Enum.GetValues(typeof(SessionError)))
            {
                Assert.AreNotEqual(SessionFault.None, Classify(error), error.ToString());
            }
        }
    }

    public class PlaceGroupedTests
    {
        static GridRules Grid(int radius) =>
            new GridRules(new HexMap(new HexLayout(1f, Vector3.zero),
                Hex.Range(Hex.Zero, radius).Select(h => new HexTile(h, null))));

        [Test]
        public void OneCellComesBackPerItemInOrder()
        {
            var cells = HexSpawnPlacement.PlaceGrouped(Grid(5), new[] { 0, 0, 1, 1, 1 }, 2);

            Assert.AreEqual(5, cells.Count);
        }

        [Test]
        public void NoTwoItemsShareACell()
        {
            var cells = HexSpawnPlacement.PlaceGrouped(Grid(5), Enumerable.Repeat(0, 12).ToList(), 1);

            CollectionAssert.AllItemsAreUnique(cells);
        }

        [Test]
        public void AGroupLandsTogetherAndAwayFromTheOthers()
        {
            // The point of grouping: a party should be able to see itself, and should not open the
            // match already mixed into the enemy.
            var cells = HexSpawnPlacement.PlaceGrouped(Grid(6), new[] { 0, 0, 0, 1, 1, 1 }, 2);

            var spread = Widest(cells.Take(3).ToList());
            var apart = Cell.Distance(cells[0], cells[3]);

            Assert.Less(spread, apart, $"Group spread {spread} should be tighter than the gap {apart}");
        }

        [Test]
        public void GroupIndicesOutsideTheGroupCountStillPlace()
        {
            // A caller passing a stale index should not throw or drop a creature on the floor.
            var cells = HexSpawnPlacement.PlaceGrouped(Grid(4), new[] { 9, -3 }, 2);

            Assert.AreEqual(2, cells.Count);
            Assert.AreNotEqual(cells[0], cells[1]);
        }

        [Test]
        public void DegenerateInputsReturnEmptyRatherThanThrowing()
        {
            Assert.IsEmpty(HexSpawnPlacement.PlaceGrouped(Grid(3), null, 2));
            Assert.IsEmpty(HexSpawnPlacement.PlaceGrouped(Grid(3), new int[0], 2));
            Assert.IsEmpty(HexSpawnPlacement.PlaceGrouped(null, new[] { 0 }, 1));
        }

        static int Widest(List<Cell> cells)
        {
            var widest = 0;
            for (var i = 0; i < cells.Count; i++)
            {
                for (var j = i + 1; j < cells.Count; j++)
                {
                    widest = Mathf.Max(widest, Cell.Distance(cells[i], cells[j]));
                }
            }

            return widest;
        }
    }
}
