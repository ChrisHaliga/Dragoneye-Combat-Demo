using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    public class HexTests
    {
        [Test]
        public void CubeAxesAlwaysSumToZero()
        {
            foreach (var hex in Hex.Range(Hex.Zero, 6))
            {
                Assert.AreEqual(0, hex.Q + hex.R + hex.S, $"{hex} breaks the cube invariant");
            }
        }

        [Test]
        public void DistanceToSelfIsZero()
        {
            Assert.AreEqual(0, Hex.Distance(new Hex(3, -2), new Hex(3, -2)));
        }

        [Test]
        public void EveryNeighborIsOneStepAway()
        {
            var center = new Hex(2, -1);

            foreach (var neighbor in center.Neighbors())
            {
                Assert.AreEqual(1, Hex.Distance(center, neighbor), $"{neighbor} should be adjacent");
            }
        }

        [Test]
        public void NeighborsAreReciprocal()
        {
            var center = new Hex(-3, 4);

            foreach (HexDirection direction in System.Enum.GetValues(typeof(HexDirection)))
            {
                var neighbor = center.Neighbor(direction);
                Assert.AreEqual(
                    center,
                    neighbor.Neighbor(direction.Opposite()),
                    $"Going {direction} then back should return to the start");
            }
        }

        [Test]
        public void NeighborsAreSixDistinctHexes()
        {
            var neighbors = new Hex(0, 0).Neighbors().ToList();

            Assert.AreEqual(6, neighbors.Count);
            Assert.AreEqual(6, neighbors.Distinct().Count(), "Direction vectors must be distinct");
        }

        [Test]
        public void OppositeOfOppositeIsIdentity()
        {
            foreach (HexDirection direction in System.Enum.GetValues(typeof(HexDirection)))
            {
                Assert.AreEqual(direction, direction.Opposite().Opposite());
            }
        }

        [Test]
        public void SixClockwiseRotationsReturnToStart()
        {
            foreach (HexDirection direction in System.Enum.GetValues(typeof(HexDirection)))
            {
                Assert.AreEqual(direction, direction.RotateClockwise(6));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(9)]
        public void RingHasSixTilesPerRadius(int radius)
        {
            var ring = Hex.Ring(new Hex(1, 2), radius).ToList();
            var expected = radius == 0 ? 1 : 6 * radius;

            Assert.AreEqual(expected, ring.Count);
            Assert.AreEqual(expected, ring.Distinct().Count(), "Ring should not repeat a hex");
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(7)]
        public void EveryRingTileIsExactlyRadiusAway(int radius)
        {
            // This is the check that catches a wrong starting corner: the count would still be
            // right, but the hexes would not all sit on the ring.
            var center = new Hex(-2, 5);

            foreach (var hex in Hex.Ring(center, radius))
            {
                Assert.AreEqual(radius, Hex.Distance(center, hex), $"{hex} is off the ring");
            }
        }

        [TestCase(0, 1)]
        [TestCase(1, 7)]
        [TestCase(2, 19)]
        [TestCase(3, 37)]
        [TestCase(5, 91)]
        public void RangeMatchesCenteredHexagonalNumber(int radius, int expected)
        {
            // 3r^2 + 3r + 1
            var tiles = Hex.Range(Hex.Zero, radius).ToList();

            Assert.AreEqual(expected, tiles.Count);
            Assert.AreEqual(expected, tiles.Distinct().Count());
        }

        [Test]
        public void RangeIsTheUnionOfItsRings()
        {
            const int radius = 4;

            var range = new HashSet<Hex>(Hex.Range(Hex.Zero, radius));
            var rings = new HashSet<Hex>();
            for (var r = 0; r <= radius; r++)
            {
                rings.UnionWith(Hex.Ring(Hex.Zero, r));
            }

            Assert.IsTrue(range.SetEquals(rings));
        }

        [Test]
        public void LineStartsAndEndsAtItsEndpoints()
        {
            var a = new Hex(-3, 1);
            var b = new Hex(4, -2);

            var line = Hex.Line(a, b).ToList();

            Assert.AreEqual(a, line.First());
            Assert.AreEqual(b, line.Last());
        }

        [Test]
        public void LineIsContiguousAndTheShortestPath()
        {
            var a = new Hex(-4, 2);
            var b = new Hex(3, -5);

            var line = Hex.Line(a, b).ToList();

            Assert.AreEqual(Hex.Distance(a, b) + 1, line.Count, "A line should be the shortest path");

            for (var i = 1; i < line.Count; i++)
            {
                Assert.AreEqual(1, Hex.Distance(line[i - 1], line[i]), "Line has a gap");
            }
        }

        [Test]
        public void EqualityAndHashingAgree()
        {
            var a = new Hex(3, -7);
            var b = new Hex(3, -7);
            var c = new Hex(-7, 3);

            Assert.IsTrue(a == b);
            Assert.IsFalse(a == c);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreNotEqual(a.GetHashCode(), c.GetHashCode(), "q and r must not be symmetric");
        }
    }
}
