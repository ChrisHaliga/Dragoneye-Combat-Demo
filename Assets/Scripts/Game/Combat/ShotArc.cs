using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// The curve a shot draws through the air, shared by the preview and the projectile so the
    /// thing that flies follows the line the player was shown.
    /// </summary>
    public static class ShotArc
    {
        /// <summary>How high the arc peaks, for a shot over this many tiles.</summary>
        public static float Height(int tiles) => 0.28f + 0.1f * Mathf.Max(0, tiles - 1);

        /// <summary>A point along the arc, <paramref name="t"/> from 0 at the shooter to 1 at the target.</summary>
        public static Vector3 Point(Vector3 from, Vector3 to, float t, float height)
        {
            var flat = Vector3.LerpUnclamped(from, to, t);
            var lift = 4f * t * (1f - t);
            return flat + Vector3.up * (height * lift);
        }

        /// <summary>Fills <paramref name="into"/> with evenly spaced points along the arc.</summary>
        public static void Sample(Vector3 from, Vector3 to, float height, Vector3[] into)
        {
            var last = into.Length - 1;

            for (var i = 0; i <= last; i++)
            {
                into[i] = Point(from, to, last == 0 ? 0f : (float)i / last, height);
            }
        }
    }
}
