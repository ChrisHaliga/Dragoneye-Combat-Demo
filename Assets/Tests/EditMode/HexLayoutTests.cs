using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Dragoneye.Hex.Tests
{
    public class HexLayoutTests
    {
        static readonly HexLayout k_Layout = new HexLayout(1f, Vector3.zero);

        [Test]
        public void OriginHexSitsAtTheLayoutOrigin()
        {
            var layout = new HexLayout(2f, new Vector3(10f, 3f, -5f));

            Assert.AreEqual(layout.Origin, layout.ToWorld(Hex.Zero));
        }

        [Test]
        public void GridIsFlatSoEveryTileSharesTheOriginHeight()
        {
            var layout = new HexLayout(1.5f, new Vector3(0f, 4.25f, 0f));

            foreach (var hex in Hex.Range(Hex.Zero, 4))
            {
                Assert.AreEqual(4.25f, layout.ToWorld(hex).y, 1e-4f);
            }
        }

        [Test]
        public void NeighborsAreAllTheSameDistanceApart()
        {
            var center = k_Layout.ToWorld(Hex.Zero);

            foreach (var neighbor in Hex.Zero.Neighbors())
            {
                var distance = Vector3.Distance(center, k_Layout.ToWorld(neighbor));
                Assert.AreEqual(k_Layout.Spacing, distance, 1e-4f);
            }
        }

        [Test]
        public void FlatTopPutsTheFirstNeighborDueNorth()
        {
            // Flat-top hexes have a flat edge on top, so a neighbour sits due north (+Z) and none
            // sits due east. Pointy-top would be the other way round.
            var north = k_Layout.ToWorld(Hex.Zero.Neighbor(HexDirection.North));

            Assert.AreEqual(0f, north.x, 1e-4f, "The north neighbour should not be offset in X");
            Assert.Greater(north.z, 0f);
        }

        [Test]
        public void CornersAreAllOneSizeFromTheCentre()
        {
            var layout = new HexLayout(2.5f, Vector3.zero);
            var corners = new Vector3[6];
            var hex = new Hex(2, -3);

            layout.GetCorners(hex, corners);
            var center = layout.ToWorld(hex);

            foreach (var corner in corners)
            {
                Assert.AreEqual(2.5f, Vector3.Distance(center, corner), 1e-4f);
            }
        }

        [Test]
        public void WorldRoundTripsBackToTheSameHex()
        {
            foreach (var hex in Hex.Range(Hex.Zero, 12))
            {
                Assert.AreEqual(hex, k_Layout.FromWorld(k_Layout.ToWorld(hex)), $"{hex} did not round-trip");
            }
        }

        [Test]
        public void WorldRoundTripsAtNonUnitSizeAndOffsetOrigin()
        {
            var layout = new HexLayout(3.7f, new Vector3(-12.5f, 2f, 41.25f));

            foreach (var hex in Hex.Range(new Hex(-4, 6), 8))
            {
                Assert.AreEqual(hex, layout.FromWorld(layout.ToWorld(hex)), $"{hex} did not round-trip");
            }
        }

        [Test]
        public void PointsNearTileCentresResolveToThatTile()
        {
            // Sampling well inside each tile: anywhere within half the inradius of the centre must
            // resolve to that tile, whatever direction it is offset in.
            var inradius = k_Layout.Size * Mathf.Sqrt(3f) * 0.5f;
            var probe = inradius * 0.5f;

            foreach (var hex in Hex.Range(Hex.Zero, 6))
            {
                var center = k_Layout.ToWorld(hex);

                for (var degrees = 0; degrees < 360; degrees += 15)
                {
                    var radians = Mathf.Deg2Rad * degrees;
                    var sample = center + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * probe;

                    Assert.AreEqual(hex, k_Layout.FromWorld(sample),
                        $"A point {probe:F2} from the centre of {hex} at {degrees} degrees resolved elsewhere");
                }
            }
        }

        [Test]
        public void EveryPointOnTheMapResolvesToATileOfTheMap()
        {
            // A dense sweep across the bounding box. This is the check that would have caught the
            // off-by-one and swapped-index bugs in an ad-hoc world-to-hex implementation.
            const int radius = 5;
            var map = new HashSet<Hex>(Hex.Range(Hex.Zero, radius));

            var extent = k_Layout.Size * (radius + 0.5f) * 1.5f;
            var resolved = new HashSet<Hex>();

            for (var x = -extent; x <= extent; x += 0.05f)
            {
                for (var z = -extent; z <= extent; z += 0.05f)
                {
                    var hex = k_Layout.FromWorld(new Vector3(x, 0f, z));
                    if (map.Contains(hex))
                    {
                        resolved.Add(hex);
                    }
                }
            }

            Assert.AreEqual(map.Count, resolved.Count,
                "Sweeping the whole map should reach every tile; an unreachable tile means the "
                + "inverse transform disagrees with the forward one");
        }

        [Test]
        public void AdjacentTilesShareABoundarySoThereAreNoGaps()
        {
            // Walk from one centre toward a neighbour's; every sample must land on one of the two.
            var a = new Hex(1, 1);
            var b = a.Neighbor(HexDirection.SouthEast);

            var from = k_Layout.ToWorld(a);
            var to = k_Layout.ToWorld(b);

            for (var t = 0f; t <= 1f; t += 0.01f)
            {
                var hex = k_Layout.FromWorld(Vector3.Lerp(from, to, t));
                Assert.IsTrue(hex == a || hex == b,
                    $"Sample at t={t:F2} between {a} and {b} resolved to {hex}");
            }
        }
    }
}
