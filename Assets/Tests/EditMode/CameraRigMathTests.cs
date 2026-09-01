using Dragoneye.CameraControl;
using NUnit.Framework;
using UnityEngine;

namespace Dragoneye.Hex.Tests
{
    public class CameraRigMathTests
    {
        const float k_MinDistance = 8f;
        const float k_MaxDistance = 34f;
        const float k_MinPitch = 35f;
        const float k_MaxPitch = 68f;

        static Vector3 Arm(float zoom) =>
            CameraRigMath.ArmOffset(zoom, k_MinDistance, k_MaxDistance, k_MinPitch, k_MaxPitch);

        [Test]
        public void PanWithNoYawMapsForwardToPositiveZ()
        {
            var direction = CameraRigMath.PanDirection(Vector2.up, 0f);

            Assert.AreEqual(0f, direction.x, 1e-4f);
            Assert.AreEqual(1f, direction.z, 1e-4f);
        }

        [Test]
        public void PanIsRelativeToYaw()
        {
            // Yawed 90 degrees, "forward" should now be world +X.
            var direction = CameraRigMath.PanDirection(Vector2.up, 90f);

            Assert.AreEqual(1f, direction.x, 1e-4f);
            Assert.AreEqual(0f, direction.z, 1e-4f);
        }

        [Test]
        public void DiagonalPanIsNotFasterThanStraightPan()
        {
            var straight = CameraRigMath.PanDirection(Vector2.up, 0f);
            var diagonal = CameraRigMath.PanDirection(new Vector2(1f, 1f), 0f);

            Assert.AreEqual(1f, straight.magnitude, 1e-4f);
            Assert.AreEqual(1f, diagonal.magnitude, 1e-4f, "Diagonal input must be normalised");
        }

        [Test]
        public void PartialPanInputKeepsItsMagnitude()
        {
            // A half-deflected stick should move at half speed, not be normalised up to full.
            var direction = CameraRigMath.PanDirection(new Vector2(0f, 0.5f), 0f);

            Assert.AreEqual(0.5f, direction.magnitude, 1e-4f);
        }

        [Test]
        public void ClampKeepsFocusInsideBoundsAndLeavesHeightAlone()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(20f, 0f, 10f));

            var clamped = CameraRigMath.ClampToBounds(new Vector3(50f, 7f, -50f), bounds);

            Assert.AreEqual(10f, clamped.x, 1e-4f);
            Assert.AreEqual(-5f, clamped.z, 1e-4f);
            Assert.AreEqual(7f, clamped.y, 1e-4f, "Height should not be clamped");
        }

        [Test]
        public void ClampLeavesAPointAlreadyInsideAlone()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(20f, 0f, 20f));
            var inside = new Vector3(3f, 0f, -4f);

            Assert.AreEqual(inside, CameraRigMath.ClampToBounds(inside, bounds));
        }

        [Test]
        public void ArmDistanceMatchesTheZoomRange()
        {
            Assert.AreEqual(k_MinDistance, Arm(0f).magnitude, 1e-3f);
            Assert.AreEqual(k_MaxDistance, Arm(1f).magnitude, 1e-3f);
        }

        [Test]
        public void ArmIsBehindAndAboveTheFocusPoint()
        {
            for (var zoom = 0f; zoom <= 1f; zoom += 0.1f)
            {
                var arm = Arm(zoom);
                Assert.Greater(arm.y, 0f, $"Camera should be above the focus at zoom {zoom}");
                Assert.Less(arm.z, 0f, $"Camera should be behind the focus at zoom {zoom}");
                Assert.AreEqual(0f, arm.x, 1e-4f, "Arm should not drift sideways");
            }
        }

        [Test]
        public void ZoomingOutRaisesThePitch()
        {
            // The chosen behaviour: zoomed in is low and cinematic, zoomed out is steep and readable.
            var closePitch = Mathf.Atan2(Arm(0f).y, -Arm(0f).z) * Mathf.Rad2Deg;
            var farPitch = Mathf.Atan2(Arm(1f).y, -Arm(1f).z) * Mathf.Rad2Deg;

            Assert.AreEqual(k_MinPitch, closePitch, 1e-2f);
            Assert.AreEqual(k_MaxPitch, farPitch, 1e-2f);
            Assert.Greater(farPitch, closePitch);
        }

        [Test]
        public void ArmDistanceIncreasesMonotonicallyWithZoom()
        {
            var previous = Arm(0f).magnitude;

            for (var zoom = 0.05f; zoom <= 1f; zoom += 0.05f)
            {
                var current = Arm(zoom).magnitude;
                Assert.Greater(current, previous, $"Distance went backwards at zoom {zoom}");
                previous = current;
            }
        }

        [Test]
        public void ZoomIsClampedOutsideTheNormalisedRange()
        {
            Assert.AreEqual(Arm(0f), Arm(-5f));
            Assert.AreEqual(Arm(1f), Arm(5f));
        }

        [Test]
        public void HorizontalDragOrbitsAndMatchesTheQeSign()
        {
            Assert.Greater(CameraRigMath.OrbitDragYaw(new Vector2(100f, 0f), 0.25f), 0f);
            Assert.Less(CameraRigMath.OrbitDragYaw(new Vector2(-100f, 0f), 0.25f), 0f);
        }

        [Test]
        public void VerticalDragDoesNotOrbitAndHorizontalDragDoesNotZoom()
        {
            // The two axes of a right-drag must stay independent, or the gesture feels mushy.
            Assert.AreEqual(0f, CameraRigMath.OrbitDragYaw(new Vector2(0f, 100f), 0.25f));
            Assert.AreEqual(0f, CameraRigMath.OrbitDragZoom(new Vector2(100f, 0f), 0.0025f));
        }

        [Test]
        public void DraggingDownZoomsOut()
        {
            Assert.Greater(CameraRigMath.OrbitDragZoom(new Vector2(0f, -100f), 0.0025f), 0f);
            Assert.Less(CameraRigMath.OrbitDragZoom(new Vector2(0f, 100f), 0.0025f), 0f);
        }

        [Test]
        public void DragZoomIsClampedLikeScrollZoom()
        {
            Assert.AreEqual(0.25f, CameraRigMath.OrbitDragZoom(new Vector2(0f, -100000f), 0.0025f), 1e-4f);
        }

        [Test]
        public void ScrollingUpZoomsIn()
        {
            // Positive scroll (wheel away from you) should reduce zoom, i.e. move closer.
            Assert.Less(CameraRigMath.ZoomDelta(120f, 0.0008f), 0f);
            Assert.Greater(CameraRigMath.ZoomDelta(-120f, 0.0008f), 0f);
        }

        [Test]
        public void NoScrollProducesNoZoom()
        {
            Assert.AreEqual(0f, CameraRigMath.ZoomDelta(0f, 0.0008f));
        }

        [Test]
        public void AWildTrackpadFlingCannotCrossTheWholeZoomRange()
        {
            // Trackpads can emit huge bursts; one frame must never jump the entire range.
            var delta = CameraRigMath.ZoomDelta(100000f, 0.0008f);

            Assert.AreEqual(-0.25f, delta, 1e-4f);
        }

        [Test]
        public void OneWheelNotchIsAMeaningfulButSmallStep()
        {
            // A Windows wheel notch reports 120. It should move the camera noticeably without
            // being so coarse that the range is only a few steps wide.
            var delta = Mathf.Abs(CameraRigMath.ZoomDelta(120f, 0.0008f));

            Assert.Greater(delta, 0.02f, "A notch should do something visible");

            // Strictly below the per-frame clamp: if a normal notch saturates the trackpad-fling
            // guard, then wheel and trackpad feel identical and sensitivity has stopped mattering.
            Assert.Less(delta, 0.2f, "A notch should not saturate the fling clamp");
        }
    }
}
