using System.Collections.Generic;
using System.Linq;
using Dragoneye.Hex.Systems;
using Dragoneye.Multiplayer;
using NUnit.Framework;
using Unity.Services.Multiplayer;
using UnityEngine;
using Hex = Dragoneye.Hex.Hex;
using HexLayout = Dragoneye.Hex.HexLayout;
using HexMap = Dragoneye.Hex.HexMap;
using HexTile = Dragoneye.Hex.HexTile;
using TerrainType = Dragoneye.Hex.TerrainType;

namespace Dragoneye.Hex.Tests
{
    public class FindNearestFreeTests
    {
        static HexMap Map(int radius, TerrainType terrain = null) =>
            new HexMap(new HexLayout(1f, Vector3.zero),
                Hex.Range(Hex.Zero, radius).Select(h => new HexTile(h, terrain)));

        [Test]
        public void AFreeAnchorIsUsedAsIs()
        {
            Assert.AreEqual(Hex.Zero,
                HexSpawnPlacement.FindNearestFree(Map(3), Hex.Zero, new HashSet<Hex>()));
        }

        [Test]
        public void ATakenAnchorSpillsToAnAdjacentHex()
        {
            var taken = new HashSet<Hex> { Hex.Zero };

            var cell = HexSpawnPlacement.FindNearestFree(Map(3), Hex.Zero, taken);

            Assert.AreEqual(1, Hex.Distance(Hex.Zero, cell), "Should land on the first ring");
        }

        [Test]
        public void ItFillsInwardRingsBeforeOuterOnes()
        {
            // Rings rather than a line, so a party clusters around its anchor.
            var map = Map(4);
            var taken = new HashSet<Hex>();

            for (var i = 0; i < 7; i++)
            {
                var cell = HexSpawnPlacement.FindNearestFree(map, Hex.Zero, taken);
                Assert.LessOrEqual(Hex.Distance(Hex.Zero, cell), 1,
                    "The centre plus its six neighbours should fill before ring 2");
                taken.Add(cell);
            }

            Assert.AreEqual(2, Hex.Distance(Hex.Zero, HexSpawnPlacement.FindNearestFree(map, Hex.Zero, taken)));
        }

        [Test]
        public void EveryPlacementIsDistinct()
        {
            var map = Map(3);
            var taken = new HashSet<Hex>();

            for (var i = 0; i < 12; i++)
            {
                var cell = HexSpawnPlacement.FindNearestFree(map, Hex.Zero, taken);
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
                Assert.AreEqual(Hex.Zero,
                    HexSpawnPlacement.FindNearestFree(Map(2, stone), Hex.Zero, new HashSet<Hex>()));
            }
            finally
            {
                Object.DestroyImmediate(stone);
            }
        }

        [Test]
        public void AnAnchorOffTheMapStillResolvesOntoIt()
        {
            var cell = HexSpawnPlacement.FindNearestFree(Map(2), new Hex(20, -20), new HashSet<Hex>());

            Assert.AreNotEqual(new Hex(20, -20), cell, "Should have walked back onto the map");
            Assert.IsTrue(Map(2).Contains(cell));
        }

        [Test]
        public void DegenerateInputsFallBackToTheAnchor()
        {
            Assert.AreEqual(Hex.Zero, HexSpawnPlacement.FindNearestFree(null, Hex.Zero, null));
            Assert.AreEqual(Hex.Zero, HexSpawnPlacement.FindNearestFree(Map(2), Hex.Zero, null));
        }

        [Test]
        public void TheTakenSetIsNotMutated()
        {
            var taken = new HashSet<Hex> { Hex.Zero };

            HexSpawnPlacement.FindNearestFree(Map(3), Hex.Zero, taken);

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
        static HexMap Map(int radius) =>
            new HexMap(new HexLayout(1f, Vector3.zero),
                Hex.Range(Hex.Zero, radius).Select(h => new HexTile(h, null)));

        [Test]
        public void OneCellComesBackPerItemInOrder()
        {
            var cells = HexSpawnPlacement.PlaceGrouped(Map(5), new[] { 0, 0, 1, 1, 1 }, 2);

            Assert.AreEqual(5, cells.Count);
        }

        [Test]
        public void NoTwoItemsShareACell()
        {
            var cells = HexSpawnPlacement.PlaceGrouped(Map(5), Enumerable.Repeat(0, 12).ToList(), 1);

            CollectionAssert.AllItemsAreUnique(cells);
        }

        [Test]
        public void AGroupLandsTogetherAndAwayFromTheOthers()
        {
            // The point of grouping: a party should be able to see itself, and should not open the
            // match already mixed into the enemy.
            var cells = HexSpawnPlacement.PlaceGrouped(Map(6), new[] { 0, 0, 0, 1, 1, 1 }, 2);

            var spread = Widest(cells.Take(3).ToList());
            var apart = Hex.Distance(cells[0], cells[3]);

            Assert.Less(spread, apart, $"Group spread {spread} should be tighter than the gap {apart}");
        }

        [Test]
        public void GroupIndicesOutsideTheGroupCountStillPlace()
        {
            // A caller passing a stale index should not throw or drop a creature on the floor.
            var cells = HexSpawnPlacement.PlaceGrouped(Map(4), new[] { 9, -3 }, 2);

            Assert.AreEqual(2, cells.Count);
            Assert.AreNotEqual(cells[0], cells[1]);
        }

        [Test]
        public void DegenerateInputsReturnEmptyRatherThanThrowing()
        {
            Assert.IsEmpty(HexSpawnPlacement.PlaceGrouped(Map(3), null, 2));
            Assert.IsEmpty(HexSpawnPlacement.PlaceGrouped(Map(3), new int[0], 2));
            Assert.AreEqual(1, HexSpawnPlacement.PlaceGrouped(null, new[] { 0 }, 1).Count);
        }

        static int Widest(List<Hex> cells)
        {
            var widest = 0;
            for (var i = 0; i < cells.Count; i++)
            {
                for (var j = i + 1; j < cells.Count; j++)
                {
                    widest = Mathf.Max(widest, Hex.Distance(cells[i], cells[j]));
                }
            }

            return widest;
        }
    }
}
