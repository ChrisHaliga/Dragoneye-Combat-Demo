using UnityEngine;

namespace Dragoneye.Hex.Rendering
{
    /// <summary>
    /// Builds the single hex mesh every tile shares.
    ///
    /// One mesh for the whole map rather than one per tile: tiles differ only in position and
    /// colour, and colour rides on a <see cref="MaterialPropertyBlock"/>.
    ///
    /// A tile with thickness and a bevelled lip, not a flat hexagon. A flat hexagon is a coloured
    /// shape on a plane; a slab is a thing on a table. The sides and the lip catch the directional
    /// light at different angles from the top, which is what makes a board of ninety of them read as
    /// stone rather than as paper, and it costs nothing a board can feel: eighteen more triangles a
    /// tile, one mesh.
    /// </summary>
    public static class HexMeshFactory
    {
        /// <summary>
        /// A flat-top hexagonal slab in the XZ plane, its top face at y = 0, centred on the origin.
        /// </summary>
        /// <param name="size">Centre-to-corner distance.</param>
        /// <param name="fill">
        /// Scales the corners inward, leaving a visible gutter between neighbouring tiles.
        /// 1 makes the tiles meet exactly.
        /// </param>
        /// <param name="depth">How far the sides run down from the top. Zero for a flat hexagon.</param>
        /// <param name="bevel">
        /// How far in from the edge the top face stops and the lip begins, and how far down the lip
        /// runs. Zero for a sharp edge.
        /// </param>
        public static Mesh Create(float size, float fill = 1f, float depth = 0f, float bevel = 0f)
        {
            var radius = size * Mathf.Clamp(fill, 0.01f, 1f);

            depth = Mathf.Max(0f, depth);
            bevel = Mathf.Clamp(bevel, 0f, Mathf.Min(radius * 0.5f, depth));

            var top = radius - bevel;
            var flat = depth <= 0f;

            var vertices = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();

            // The top face, as it always was, at whatever radius the lip leaves it.
            vertices.Add(Vector3.zero);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (var i = 0; i < 6; i++)
            {
                var corner = Corner(i);
                vertices.Add(corner * top);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0.5f + corner.x * 0.5f, 0.5f + corner.z * 0.5f));
            }

            // Corners run counter-clockwise when viewed from above, so each triangle is wound
            // (centre, next, current) to face +Y. Getting this backwards makes the whole map
            // invisible to a camera looking down at it.
            for (var i = 0; i < 6; i++)
            {
                triangles.Add(0);
                triangles.Add((i + 1) % 6 + 1);
                triangles.Add(i + 1);
            }

            if (flat)
            {
                return Finish(vertices, normals, uvs, triangles);
            }

            // The lip and the sides, one quad per edge with its own flat normal. Shared corners
            // would smear the lip into the side and the side into the next side, and the whole
            // point of the geometry is that those are three surfaces lit three ways.
            for (var i = 0; i < 6; i++)
            {
                var a = Corner(i);
                var b = Corner((i + 1) % 6);
                var outward = ((a + b) * 0.5f).normalized;

                if (bevel > 0f)
                {
                    Quad(vertices, normals, uvs, triangles,
                        a * top, b * top,
                        b * radius + Vector3.down * bevel, a * radius + Vector3.down * bevel,
                        (outward + Vector3.up).normalized);
                }

                var lipY = -bevel;

                Quad(vertices, normals, uvs, triangles,
                    a * radius + Vector3.up * lipY, b * radius + Vector3.up * lipY,
                    b * radius + Vector3.down * depth, a * radius + Vector3.down * depth,
                    outward);
            }

            return Finish(vertices, normals, uvs, triangles);
        }

        static Vector3 Corner(int i)
        {
            var angle = Mathf.Deg2Rad * 60f * i;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        /// <summary>
        /// One outward-facing quad, wound so its normal points the way given.
        ///
        /// The four corners go round the quad: near-top, next-top, next-bottom, near-bottom, with
        /// "next" the counter-clockwise neighbour seen from above. Seen from *outside* the slab
        /// that order runs top-left, top-right, bottom-right, bottom-left -- clockwise, which is
        /// the winding Unity draws as a front face. The first cut of this had the two triangles
        /// the other way round and every side of every tile rendered from the inside only.
        /// </summary>
        static void Quad(System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<Vector3> normals,
            System.Collections.Generic.List<Vector2> uvs,
            System.Collections.Generic.List<int> triangles,
            Vector3 topA, Vector3 topB, Vector3 bottomB, Vector3 bottomA, Vector3 normal)
        {
            var start = vertices.Count;

            vertices.Add(topA);
            vertices.Add(topB);
            vertices.Add(bottomB);
            vertices.Add(bottomA);

            for (var i = 0; i < 4; i++)
            {
                normals.Add(normal);
            }

            uvs.Add(Vector2.zero);
            uvs.Add(Vector2.right);
            uvs.Add(Vector2.one);
            uvs.Add(Vector2.up);

            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);

            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        static Mesh Finish(System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<Vector3> normals,
            System.Collections.Generic.List<Vector2> uvs,
            System.Collections.Generic.List<int> triangles)
        {
            var mesh = new Mesh
            {
                name = "Hex",
                vertices = vertices.ToArray(),
                normals = normals.ToArray(),
                uv = uvs.ToArray(),
                triangles = triangles.ToArray()
            };

            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
