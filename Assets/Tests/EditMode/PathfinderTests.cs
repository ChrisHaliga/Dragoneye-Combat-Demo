using System.Collections.Generic;
using System.Linq;
using Dragoneye.Hex.Systems;
using NUnit.Framework;
using UnityEngine;
using Hex = Dragoneye.Hex.Hex;
using HexLayout = Dragoneye.Hex.HexLayout;
using HexMap = Dragoneye.Hex.HexMap;
using HexTile = Dragoneye.Hex.HexTile;

namespace Dragoneye.Hex.Tests
{
    public class HexPathfinderTests
    {
        static HexMap Map(int radius) =>
            new HexMap(new HexLayout(1f, Vector3.zero),
                Hex.Range(Hex.Zero, radius).Select(h => new HexTile(h, null)));

        static List<Hex> Path(HexMap map, Hex from, Hex to, params Hex[] blocked)
        {
            var path = new List<Hex>();
            HexPathfinder.TryFindPath(map, from, to, new HashSet<Hex>(blocked), path);
            return path;
        }

        [Test]
        public void APathExcludesTheStartAndIncludesTheDestination()
        {
            // The count is the cost, so an off-by-one here becomes an off-by-one in AP.
            var path = Path(Map(4), Hex.Zero, new Hex(2, 0));

            Assert.AreEqual(2, path.Count);
            Assert.AreEqual(new Hex(2, 0), path[path.Count - 1]);
            CollectionAssert.DoesNotContain(path, Hex.Zero);
        }

        [Test]
        public void CostMatchesDistanceOnAnEmptyMap()
        {
            var map = Map(5);

            foreach (var target in Hex.Range(Hex.Zero, 4))
            {
                if (target == Hex.Zero)
                {
                    continue;
                }

                Assert.AreEqual(Hex.Distance(Hex.Zero, target),
                    HexPathfinder.CostTo(map, Hex.Zero, target, null),
                    $"{target} should cost its distance when nothing is in the way");
            }
        }

        [Test]
        public void RoutesGoAroundBlockedCells()
        {
            var map = Map(4);
            var wall = Hex.Zero.Neighbors().ToArray();

            // Ring one fully blocked except one gap: the route must find the gap.
            var blocked = new HashSet<Hex>(wall.Skip(1));
            var path = new List<Hex>();

            Assert.IsTrue(HexPathfinder.TryFindPath(map, Hex.Zero, new Hex(2, 0), blocked, path));
            Assert.AreEqual(wall[0], path[0], "The only gap in the ring is the only first step");
            CollectionAssert.IsNotSubsetOf(blocked, path);
        }

        [Test]
        public void ACompletelyWalledInStartHasNoRoute()
        {
            var map = Map(4);
            var blocked = new HashSet<Hex>(Hex.Zero.Neighbors());

            Assert.AreEqual(-1, HexPathfinder.CostTo(map, Hex.Zero, new Hex(2, 0), blocked));
        }

        [Test]
        public void ABlockedDestinationIsNotReachable()
        {
            // Attacking resolves separately; a hex with something standing on it is never a
            // destination, which is what stops a move being priced onto an occupied tile.
            var target = new Hex(2, 0);

            Assert.AreEqual(-1,
                HexPathfinder.CostTo(Map(4), Hex.Zero, target, new HashSet<Hex> { target }));
        }

        [Test]
        public void OffMapDestinationsAreNotReachable()
        {
            Assert.AreEqual(-1, HexPathfinder.CostTo(Map(2), Hex.Zero, new Hex(40, -40), null));
        }

        [Test]
        public void TheStartIsNeverItsOwnDestination()
        {
            Assert.AreEqual(-1, HexPathfinder.CostTo(Map(3), Hex.Zero, Hex.Zero, null));
        }

        [Test]
        public void MaxCostStopsTheSearchShort()
        {
            var map = Map(6);
            var far = new Hex(5, 0);

            Assert.AreEqual(-1, HexPathfinder.CostTo(map, Hex.Zero, far, null, maxCost: 3));
            Assert.AreEqual(5, HexPathfinder.CostTo(map, Hex.Zero, far, null, maxCost: 5));
        }

        [Test]
        public void EveryStepIsAdjacentToTheLast()
        {
            // A path with a jump in it would be walked as a teleport by anything following it.
            var path = Path(Map(5), Hex.Zero, new Hex(3, -2));
            var previous = Hex.Zero;

            foreach (var step in path)
            {
                Assert.AreEqual(1, Hex.Distance(previous, step), $"{previous} to {step} is not a step");
                previous = step;
            }
        }

        [Test]
        public void DegenerateInputsDoNotThrow()
        {
            var path = new List<Hex>();

            Assert.IsFalse(HexPathfinder.TryFindPath(null, Hex.Zero, new Hex(1, 0), null, path));
            Assert.IsFalse(HexPathfinder.TryFindPath(Map(2), Hex.Zero, new Hex(1, 0), null, null));
            Assert.AreEqual(-1, HexPathfinder.CostTo(null, Hex.Zero, new Hex(1, 0), null));
        }

        [Test]
        public void ThePathListIsClearedBeforeUse()
        {
            var path = new List<Hex> { new Hex(9, 9) };

            HexPathfinder.TryFindPath(Map(3), Hex.Zero, new Hex(1, 0), null, path);

            CollectionAssert.DoesNotContain(path, new Hex(9, 9));
        }
    }
}
