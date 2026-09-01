using System.Collections.Generic;
using System.Linq;
using Dragoneye.Game;
using NUnit.Framework;
using UnityEngine;
using Hex = Dragoneye.Hex.Hex;
using HexLayout = Dragoneye.Hex.HexLayout;
using HexMap = Dragoneye.Hex.HexMap;
using HexTile = Dragoneye.Hex.HexTile;
using TerrainType = Dragoneye.Hex.TerrainType;

namespace Dragoneye.Hex.Tests
{
    public class MoveRulesTests
    {
        static HexMap Map(int radius, TerrainType terrain = null) =>
            new HexMap(new HexLayout(1f, Vector3.zero),
                Hex.Range(Hex.Zero, radius).Select(h => new HexTile(h, terrain)));

        [Test]
        public void ValidMoveIsAccepted()
        {
            Assert.IsTrue(MoveRules.CanEnter(Map(3), Hex.Zero, new Hex(1, 0), false, out var why));
            Assert.AreEqual(MoveRejection.None, why);
        }

        [Test]
        public void OffMapIsRejected()
        {
            Assert.IsFalse(MoveRules.CanEnter(Map(2), Hex.Zero, new Hex(99, -99), false, out var why));
            Assert.AreEqual(MoveRejection.OffMap, why);
        }

        [Test]
        public void OccupiedIsRejected()
        {
            Assert.IsFalse(MoveRules.CanEnter(Map(3), Hex.Zero, new Hex(1, 0), true, out var why));
            Assert.AreEqual(MoveRejection.Occupied, why);
        }

        [Test]
        public void MovingNowhereIsRejected()
        {
            Assert.IsFalse(MoveRules.CanEnter(Map(3), Hex.Zero, Hex.Zero, false, out var why));
            Assert.AreEqual(MoveRejection.AlreadyThere, why);
        }

        [Test]
        public void NonWalkableTerrainIsRejected()
        {
            var stone = ScriptableObject.CreateInstance<TerrainType>();
            try
            {
                // A default TerrainType is walkable, so drive the flag through the serialised field.
                var field = typeof(TerrainType).GetField("m_IsWalkable",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                field.SetValue(stone, false);

                Assert.IsFalse(MoveRules.CanEnter(Map(3, stone), Hex.Zero, new Hex(1, 0), false, out var why));
                Assert.AreEqual(MoveRejection.NotWalkable, why);
            }
            finally
            {
                Object.DestroyImmediate(stone);
            }
        }

        [Test]
        public void ANullMapRejectsRatherThanThrowing()
        {
            Assert.IsFalse(MoveRules.CanEnter(null, Hex.Zero, Hex.Zero, false, out var why));
            Assert.AreEqual(MoveRejection.OffMap, why);
        }

        [Test]
        public void OffMapIsCheckedBeforeOccupancy()
        {
            // Order matters for the message the player would eventually see: "there is no tile
            // there" is more useful than "something is standing there".
            MoveRules.CanEnter(Map(2), Hex.Zero, new Hex(50, 0), true, out var why);
            Assert.AreEqual(MoveRejection.OffMap, why);
        }
    }

    public class HexPointerMathTests
    {
        static readonly Plane k_Ground = new Plane(Vector3.up, Vector3.zero);

        [Test]
        public void RayPointingDownHitsThePlane()
        {
            var ray = new Ray(new Vector3(3f, 10f, -4f), Vector3.down);

            Assert.IsTrue(HexPointerMath.TryGroundPoint(ray, k_Ground, out var point));
            Assert.AreEqual(3f, point.x, 1e-4f);
            Assert.AreEqual(0f, point.y, 1e-4f);
            Assert.AreEqual(-4f, point.z, 1e-4f);
        }

        [Test]
        public void RayParallelToThePlaneMisses()
        {
            var ray = new Ray(new Vector3(0f, 5f, 0f), Vector3.forward);

            Assert.IsFalse(HexPointerMath.TryGroundPoint(ray, k_Ground, out _));
        }

        [Test]
        public void RayPointingAwayFromThePlaneMisses()
        {
            // Behind the camera must not resolve to a tile, which a raw distance would allow.
            var ray = new Ray(new Vector3(0f, 5f, 0f), Vector3.up);

            Assert.IsFalse(HexPointerMath.TryGroundPoint(ray, k_Ground, out _));
        }

        [Test]
        public void RayOriginatingOnThePlaneMisses()
        {
            // Zero distance is degenerate rather than a hit.
            var ray = new Ray(Vector3.zero, Vector3.down);

            Assert.IsFalse(HexPointerMath.TryGroundPoint(ray, k_Ground, out _));
        }

        [Test]
        public void AngledRayResolvesToTheExpectedCell()
        {
            var layout = new HexLayout(1f, Vector3.zero);
            var target = new Hex(2, -1);
            var world = layout.ToWorld(target);

            // Aim at the tile centre from above and off to one side.
            var origin = world + new Vector3(4f, 9f, -3f);
            var ray = new Ray(origin, (world - origin).normalized);

            Assert.IsTrue(HexPointerMath.TryGroundPoint(ray, k_Ground, out var point));
            Assert.AreEqual(target, layout.FromWorld(point));
        }

        [Test]
        public void PointsAcrossATileAllResolveToThatTile()
        {
            var layout = new HexLayout(1f, Vector3.zero);

            foreach (var hex in Hex.Range(Hex.Zero, 4))
            {
                var centre = layout.ToWorld(hex);

                for (var degrees = 0; degrees < 360; degrees += 30)
                {
                    var radians = Mathf.Deg2Rad * degrees;
                    var offset = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * 0.3f;
                    var origin = centre + offset + Vector3.up * 12f;
                    var ray = new Ray(origin, Vector3.down);

                    Assert.IsTrue(HexPointerMath.TryGroundPoint(ray, k_Ground, out var point));
                    Assert.AreEqual(hex, layout.FromWorld(point), $"{hex} at {degrees} degrees");
                }
            }
        }
    }

    public class UnitViewStepTests
    {
        [Test]
        public void StepMovesTowardTheTargetAtConstantSpeed()
        {
            var moved = UnitView.Step(Vector3.zero, new Vector3(10f, 0f, 0f), 4f, 0.5f);

            Assert.AreEqual(2f, moved.x, 1e-4f);
        }

        [Test]
        public void StepNeverOvershoots()
        {
            // A huge frame must land exactly on the target, not past it. Overshoot is what makes a
            // unit visibly jitter around its destination.
            var target = new Vector3(1f, 0f, 0f);
            var moved = UnitView.Step(Vector3.zero, target, 100f, 10f);

            Assert.AreEqual(target, moved);
        }

        [Test]
        public void StepArrivesEventuallyAndStays()
        {
            var target = new Vector3(3f, 0f, 4f);
            var current = Vector3.zero;

            for (var i = 0; i < 1000; i++)
            {
                current = UnitView.Step(current, target, 6f, 1f / 60f);
            }

            Assert.AreEqual(0f, Vector3.Distance(current, target), 1e-4f);
            Assert.AreEqual(target, UnitView.Step(current, target, 6f, 1f / 60f), "Should rest on arrival");
        }

        [Test]
        public void StepDoesNotSnap()
        {
            // The contract is "the view may be arbitrarily behind". A step that jumped straight to
            // the target would quietly turn animation into teleportation.
            var target = new Vector3(100f, 0f, 0f);
            var moved = UnitView.Step(Vector3.zero, target, 5f, 1f / 60f);

            Assert.Less(moved.x, 1f);
            Assert.Greater(moved.x, 0f);
        }

        [Test]
        public void RetargetingMidWalkIsContinuous()
        {
            // Rapid clicks retarget rather than queueing. The unit must carry on from where it is.
            var current = Vector3.zero;
            for (var i = 0; i < 10; i++)
            {
                current = UnitView.Step(current, new Vector3(10f, 0f, 0f), 6f, 1f / 60f);
            }

            var before = current;
            var after = UnitView.Step(current, new Vector3(-10f, 0f, 0f), 6f, 1f / 60f);

            Assert.Less(Vector3.Distance(before, after), 0.2f, "Retarget should not teleport");
        }

        [TestCase(0f)]
        [TestCase(-5f)]
        public void NonPositiveSpeedOrDeltaDoesNotMoveBackwards(float speed)
        {
            var current = new Vector3(1f, 0f, 0f);

            Assert.AreEqual(current, UnitView.Step(current, Vector3.zero, speed, 1f / 60f));
            Assert.AreEqual(current, UnitView.Step(current, Vector3.zero, 6f, -1f));
        }
    }

    public class UnitIndexTests
    {
        [Test]
        public void OccupancyIgnoresTheMoverItself()
        {
            // Without this a unit could never be asked to stay put, and more importantly a move
            // validated against its own current cell would always report "occupied".
            var go = new GameObject("index");
            try
            {
                var index = go.AddComponent<UnitIndex>();
                Assert.IsFalse(index.IsOccupied(Hex.Zero));
                Assert.IsFalse(index.IsOccupiedByOther(Hex.Zero, null));
                Assert.IsFalse(index.TryGet(Hex.Zero, out _));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
