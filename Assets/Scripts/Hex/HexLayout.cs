using UnityEngine;

namespace Dragoneye.Hex
{
    /// <summary>
    /// Converts between hex coordinates and world positions for a flat grid in the XZ plane.
    ///
    /// This is the only place that knows about world space, and the conversion is exactly
    /// invertible: <c>FromWorld(ToWorld(h)) == h</c> for every hex, which is what makes mouse
    /// picking a two-line operation instead of a pile of special cases.
    /// </summary>
    public readonly struct HexLayout
    {
        static readonly float k_Sqrt3 = Mathf.Sqrt(3f);

        /// <summary>Distance from a tile's centre to any of its corners.</summary>
        public readonly float Size;

        /// <summary>World position of <see cref="Hex.Zero"/>.</summary>
        public readonly Vector3 Origin;

        public HexLayout(float size, Vector3 origin)
        {
            Size = size;
            Origin = origin;
        }

        /// <summary>Centre-to-centre distance between two neighbours.</summary>
        public float Spacing => Size * k_Sqrt3;

        public Vector3 ToWorld(Hex hex)
        {
            var x = Size * 1.5f * hex.Q;
            var z = Size * (k_Sqrt3 * 0.5f * hex.Q + k_Sqrt3 * hex.R);
            return new Vector3(Origin.x + x, Origin.y, Origin.z + z);
        }

        public Hex FromWorld(Vector3 world)
        {
            var x = (world.x - Origin.x) / Size;
            var z = (world.z - Origin.z) / Size;

            // Inverse of ToWorld, then snapped back onto the grid.
            var q = 2f / 3f * x;
            var r = -x / 3f + k_Sqrt3 / 3f * z;

            return Hex.Round(q, r);
        }

        /// <summary>
        /// Corner <paramref name="index"/> (0-5) of a tile, relative to its centre. Flat-top corners
        /// sit at 0, 60, 120, 180, 240 and 300 degrees, so corner 0 is due east.
        /// </summary>
        public Vector3 CornerOffset(int index)
        {
            var angle = Mathf.Deg2Rad * 60f * index;
            return new Vector3(Size * Mathf.Cos(angle), 0f, Size * Mathf.Sin(angle));
        }

        public void GetCorners(Hex hex, Vector3[] into)
        {
            var center = ToWorld(hex);
            for (var i = 0; i < 6; i++)
            {
                into[i] = center + CornerOffset(i);
            }
        }
    }
}
