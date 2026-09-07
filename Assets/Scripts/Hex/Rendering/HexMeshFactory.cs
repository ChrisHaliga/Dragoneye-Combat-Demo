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

        /// <summary>
        /// The top face of one area of a tile: a fan of its wedges, apex at the tile's centre,
        /// in tile-local space.
        ///
        /// What the hover marker and the reach overlay draw once a tile has more than one area. A
        /// whole tile is the fan of all twelve, which is the same hexagon <see cref="Create"/>
        /// draws, so the two cannot disagree about where a tile's edge is.
        /// </summary>
        public static Mesh CreateArea(AreaLayout layout, byte area, float size, float fill = 1f)
        {
            var radius = size * Mathf.Clamp(fill, 0.01f, 1f);

            var vertices = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();

            vertices.Add(Vector3.zero);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (var wedge = 0; wedge < TileGeometry.Wedges; wedge++)
            {
                if (layout != null && layout.AreaOf(wedge) != area)
                {
                    continue;
                }

                var a = RayEnd(wedge) * radius;
                var b = RayEnd(wedge + 1) * radius;

                var start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                normals.Add(Vector3.up);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0.5f + a.x * 0.5f / size, 0.5f + a.z * 0.5f / size));
                uvs.Add(new Vector2(0.5f + b.x * 0.5f / size, 0.5f + b.z * 0.5f / size));

                // Rays run clockwise from North, so (centre, this ray, next ray) is clockwise
                // seen from above, which is the winding that faces up.
                triangles.Add(0);
                triangles.Add(start);
                triangles.Add(start + 1);
            }

            var mesh = Finish(vertices, normals, uvs, triangles);
            mesh.name = "Area";
            return mesh;
        }

        /// <summary>
        /// A wall: a box along a segment between two tile-local points, standing on the tile.
        ///
        /// Extended by half its thickness at both ends, so two segments meeting at a corner or
        /// at a ray's root close up rather than leaving a notch.
        /// </summary>
        public static Mesh CreateWall(Vector3 a, Vector3 b, float height, float thickness)
        {
            var along = b - a;
            var length = along.magnitude;

            if (length < 1e-4f)
            {
                return Finish(new System.Collections.Generic.List<Vector3>(),
                    new System.Collections.Generic.List<Vector3>(),
                    new System.Collections.Generic.List<Vector2>(),
                    new System.Collections.Generic.List<int>());
            }

            along /= length;
            var half = thickness * 0.5f;
            var side = Vector3.Cross(Vector3.up, along) * half;
            var start = a - along * half;
            var end = b + along * half;
            var up = Vector3.up * height;

            var vertices = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();

            // Bottom corners: left/right of the start, left/right of the end.
            var sl = start - side;
            var sr = start + side;
            var el = end - side;
            var er = end + side;

            // Each face wound clockwise seen from outside, the way Quad expects.
            Quad(vertices, normals, uvs, triangles, sl + up, el + up, el, sl, -side.normalized);
            Quad(vertices, normals, uvs, triangles, er + up, sr + up, sr, er, side.normalized);
            Quad(vertices, normals, uvs, triangles, sr + up, sl + up, sl, sr, -along);
            Quad(vertices, normals, uvs, triangles, el + up, er + up, er, el, along);
            Quad(vertices, normals, uvs, triangles, sl + up, sr + up, er + up, el + up, Vector3.up);

            var mesh = Finish(vertices, normals, uvs, triangles);
            mesh.name = "Wall";
            return mesh;
        }

        /// <summary>Where a ray ends, as a unit-radius tile-local vector, from the integer table.</summary>
        public static Vector3 RayEnd(int ray)
        {
            TileGeometry.RayEnd(ray, out var x, out var z);
            return new Vector3(x / (float)TileGeometry.Scale, 0f, z / (float)TileGeometry.Scale);
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

            // Clockwise seen from the side the normal points at, which is what Unity calls the
            // front. Wound the other way round, every face of every wall and every tile skirt was
            // a back face: you saw straight through the outside of a wall and onto its inside.
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);

            triangles.Add(start);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
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
