using UnityEngine;

namespace Dragoneye.CameraControl
{
    /// <summary>
    /// The pure geometry behind the camera rig, kept separate from the MonoBehaviour so it can be
    /// tested without a scene.
    /// </summary>
    public static class CameraRigMath
    {
        /// <summary>
        /// Converts a screen-relative pan input into a world-space delta, rotated so that "forward"
        /// means whichever way the rig is currently facing.
        /// </summary>
        /// <param name="input">x = right, y = forward. Expected in the -1..1 range.</param>
        /// <param name="yawDegrees">Current rig yaw.</param>
        public static Vector3 PanDirection(Vector2 input, float yawDegrees)
        {
            var local = new Vector3(input.x, 0f, input.y);
            if (local.sqrMagnitude > 1f)
            {
                local.Normalize();
            }

            return Quaternion.Euler(0f, yawDegrees, 0f) * local;
        }

        /// <summary>
        /// Direction for a pointer movement measured in pixels. Always normalises.
        ///
        /// <see cref="PanDirection"/> deliberately preserves magnitude below 1 so a half-deflected
        /// stick moves at half speed. Feeding pixel deltas through that rule makes displacement
        /// proportional to |delta| above one pixel but |delta| squared below it, so slow drags on a
        /// high-DPI pointer are damped non-linearly. Callers scale by |delta| themselves.
        /// </summary>
        public static Vector3 PanDirectionFromDelta(Vector2 pixelDelta, float yawDegrees)
        {
            var local = new Vector3(pixelDelta.x, 0f, pixelDelta.y);
            if (local.sqrMagnitude < 1e-12f)
            {
                return Vector3.zero;
            }

            local.Normalize();
            return Quaternion.Euler(0f, yawDegrees, 0f) * local;
        }

        /// <summary>Keeps the focus point inside <paramref name="bounds"/>, ignoring height.</summary>
        public static Vector3 ClampToBounds(Vector3 focus, Bounds bounds) =>
            new Vector3(
                Mathf.Clamp(focus.x, bounds.min.x, bounds.max.x),
                focus.y,
                Mathf.Clamp(focus.z, bounds.min.z, bounds.max.z));

        /// <summary>
        /// The camera's offset from the focus point, given a normalised zoom level.
        ///
        /// Zoom 0 is fully zoomed in: close and low, for a cinematic angle. Zoom 1 is fully out:
        /// far and steep, closer to a readable top-down view. Pitch and distance are interpolated
        /// together so the two never disagree.
        /// </summary>
        /// <param name="zoom">0 (closest) to 1 (furthest).</param>
        public static Vector3 ArmOffset(float zoom, float minDistance, float maxDistance,
            float minPitchDegrees, float maxPitchDegrees)
        {
            zoom = Mathf.Clamp01(zoom);

            var distance = Mathf.Lerp(minDistance, maxDistance, zoom);
            var pitch = Mathf.Lerp(minPitchDegrees, maxPitchDegrees, zoom) * Mathf.Deg2Rad;

            // Behind and above the focus point. Negative Z so the camera looks forward (+Z) at it.
            return new Vector3(
                0f,
                distance * Mathf.Sin(pitch),
                -distance * Mathf.Cos(pitch));
        }

        /// <summary>
        /// Yaw change from a horizontal drag, in degrees.
        ///
        /// Not time-scaled: the input is already a distance moved since the last frame, so scaling
        /// it again would make the same physical drag orbit further at low framerates.
        /// </summary>
        public static float OrbitDragYaw(Vector2 pixelDelta, float degreesPerPixel) =>
            pixelDelta.x * degreesPerPixel;

        /// <summary>
        /// Zoom change from a vertical drag. Dragging down pulls the camera back, matching the
        /// sense of dragging the world toward you.
        /// </summary>
        public static float OrbitDragZoom(Vector2 pixelDelta, float zoomPerPixel,
            float maxPerFrame = 0.25f) =>
            Mathf.Clamp(-pixelDelta.y * zoomPerPixel, -maxPerFrame, maxPerFrame);

        /// <summary>
        /// Normalises a scroll-wheel reading into a zoom delta.
        ///
        /// Raw scroll values are wildly inconsistent: a Windows mouse wheel reports 120 per notch
        /// while a trackpad emits a stream of small values. Scaling and then clamping keeps one
        /// notch meaningful without letting a trackpad fling cross the whole zoom range in a frame.
        /// </summary>
        public static float ZoomDelta(float rawScroll, float sensitivity, float maxPerFrame = 0.25f)
        {
            if (Mathf.Approximately(rawScroll, 0f))
            {
                return 0f;
            }

            return Mathf.Clamp(-rawScroll * sensitivity, -maxPerFrame, maxPerFrame);
        }
    }
}
