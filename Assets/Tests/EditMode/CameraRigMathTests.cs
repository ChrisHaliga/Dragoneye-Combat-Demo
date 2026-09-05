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
        public void VerticalDragDoesNothing()
        {
            // A right-drag turns the camera and does not move it. It used to zoom on the vertical
            // as well, so every attempt to turn also changed how far away the camera was.
            Assert.AreEqual(0f, CameraRigMath.OrbitDragYaw(new Vector2(0f, 100f), 0.25f));
        }

        [Test]
        public void DragDisplacementIsLinearInDistance()
        {
            // Halving the drag must halve the movement, including below one pixel. Routing pixel
            // deltas through PanDirection made displacement proportional to |delta| squared under
            // 1px, so slow drags on a high-DPI pointer were silently damped.
            var far = CameraRigMath.PanDirectionFromDelta(new Vector2(8f, 0f), 0f) * 8f;
            var half = CameraRigMath.PanDirectionFromDelta(new Vector2(4f, 0f), 0f) * 4f;

            Assert.AreEqual(far.magnitude * 0.5f, half.magnitude, 1e-4f);
        }

        [TestCase(2f)]
        [TestCase(1f)]
        [TestCase(0.5f)]
        [TestCase(0.1f)]
        [TestCase(0.01f)]
        public void SubPixelDragStaysProportional(float distance)
        {
            var move = CameraRigMath.PanDirectionFromDelta(new Vector2(distance, 0f), 0f) * distance;

            Assert.AreEqual(distance, move.magnitude, 1e-5f,
                $"A {distance}px drag should move {distance} units, not {move.magnitude}");
        }

        [Test]
        public void DragDirectionIsIndependentOfDistance()
        {
            var big = CameraRigMath.PanDirectionFromDelta(new Vector2(50f, 20f), 30f);
            var small = CameraRigMath.PanDirectionFromDelta(new Vector2(0.05f, 0.02f), 30f);

            Assert.AreEqual(1f, big.magnitude, 1e-4f);
            Assert.AreEqual(1f, small.magnitude, 1e-4f);
            Assert.AreEqual(0f, Vector3.Distance(big, small), 1e-4f);
        }

        [Test]
        public void ZeroDragProducesNoDirection()
        {
            Assert.AreEqual(Vector3.zero, CameraRigMath.PanDirectionFromDelta(Vector2.zero, 0f));
        }

        [Test]
        public void ScrollingUpZoomsIn()
        {
            // Positive scroll (wheel away from you) should reduce zoom, i.e. move closer.
            Assert.Less(CameraRigMath.ZoomDelta(120f, Notch), 0f);
            Assert.Greater(CameraRigMath.ZoomDelta(-120f, Notch), 0f);
        }

        [Test]
        public void NoScrollProducesNoZoom()
        {
            Assert.AreEqual(0f, CameraRigMath.ZoomDelta(0f, Notch));
        }

        [Test]
        public void AWildTrackpadFlingCannotCrossTheWholeZoomRange()
        {
            // Trackpads can emit huge bursts; one frame must never jump the entire range.
            var delta = CameraRigMath.ZoomDelta(100000f, Notch);

            Assert.AreEqual(-0.25f, delta, 1e-4f);
        }

        [Test]
        public void ANotchIsANotchWhateverThePlatformCallsIt()
        {
            // A Windows wheel reports 120 per detent and other setups report 1. Scaling the raw
            // number directly made the same sensitivity mean two things a hundred-odd times apart,
            // which is why zoom felt unusably slow on some machines and fine on others.
            Assert.AreEqual(CameraRigMath.ZoomDelta(1f, Notch), CameraRigMath.ZoomDelta(120f, Notch),
                1e-5f);

            // And a trackpad's fractions of a notch stay fractions rather than rounding up to one.
            Assert.Less(
                Mathf.Abs(CameraRigMath.ZoomDelta(0.1f, Notch)),
                Mathf.Abs(CameraRigMath.ZoomDelta(1f, Notch)));
        }

        [Test]
        public void OneWheelNotchIsAMeaningfulButSmallStep()
        {
            var delta = Mathf.Abs(CameraRigMath.ZoomDelta(120f, Notch));

            Assert.Greater(delta, 0.05f, "A notch should do something visible");

            // Strictly below the per-frame clamp: if a normal notch saturates the trackpad-fling
            // guard, then wheel and trackpad feel identical and sensitivity has stopped mattering.
            Assert.Less(delta, 0.25f, "A notch should not saturate the fling clamp");
        }

        /// <summary>The shipped default: five notches from closest to furthest out.</summary>
        const float Notch = 0.2f;
    }
}
