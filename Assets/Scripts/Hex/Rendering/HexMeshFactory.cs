using UnityEngine;

namespace Dragoneye.Hex.Rendering
{
    /// <summary>
    /// Builds the single flat hex mesh every tile shares.
    ///
    /// One mesh for the whole map rather than one per tile: tiles differ only in position and
    /// colour, and colour rides on a <see cref="MaterialPropertyBlock"/>.
    /// </summary>
    public static class HexMeshFactory
    {
        /// <summary>
        /// A flat-top hexagon lying in the XZ plane, centred on the origin and facing up.
        /// </summary>
        /// <param name="size">Centre-to-corner distance.</param>
        /// <param name="fill">
        /// Scales the corners inward, leaving a visible gutter between neighbouring tiles.
        /// 1 makes the tiles meet exactly.
        /// </param>
        public static Mesh Create(float size, float fill = 1f)
        {
            var radius = size * Mathf.Clamp(fill, 0.01f, 1f);

            var vertices = new Vector3[7];
            var normals = new Vector3[7];
            var uvs = new Vector2[7];

            vertices[0] = Vector3.zero;
            normals[0] = Vector3.up;
            uvs[0] = new Vector2(0.5f, 0.5f);

            for (var i = 0; i < 6; i++)
            {
                var angle = Mathf.Deg2Rad * 60f * i;
                var x = Mathf.Cos(angle);
                var z = Mathf.Sin(angle);

                vertices[i + 1] = new Vector3(x * radius, 0f, z * radius);
                normals[i + 1] = Vector3.up;
                uvs[i + 1] = new Vector2(0.5f + x * 0.5f, 0.5f + z * 0.5f);
            }

            // Corners run counter-clockwise when viewed from above, so each triangle is wound
            // (centre, next, current) to face +Y. Getting this backwards makes the whole map
            // invisible to a camera looking down at it.
            var triangles = new int[18];
            for (var i = 0; i < 6; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % 6 + 1;
                triangles[i * 3 + 2] = i + 1;
            }

            var mesh = new Mesh
            {
                name = "Hex",
                vertices = vertices,
                normals = normals,
                uv = uvs,
                triangles = triangles
            };

            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
