using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Where a ray meets the arena's ground plane. Pure, so the awkward cases are testable without
    /// a camera or a scene.
    /// </summary>
    public static class HexPointerMath
    {
        /// <summary>
        /// Intersects a ray with a plane.
        ///
        /// Fails for a ray parallel to the plane and for one pointing away from it. Unity's
        /// <c>Plane.Raycast</c> already reports both as false, but it also returns a *negative*
        /// distance in the second case rather than failing outright in every version, so the sign is
        /// checked here rather than trusted.
        /// </summary>
        public static bool TryGroundPoint(Ray ray, Plane ground, out Vector3 point)
        {
            if (!ground.Raycast(ray, out var distance) || distance <= 0f)
            {
                point = default;
                return false;
            }

            point = ray.GetPoint(distance);
            return true;
        }
    }
}
