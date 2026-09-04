using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// The shapes a board token is made of: a short cylinder and a flat disc.
    ///
    /// Built in code rather than authored, and shared by every token in the game. Two meshes for the
    /// whole board, made once, so a hundred creatures cost nothing beyond the transforms they stand
    /// on.
    ///
    /// This exists because building them as an editor step did not work and could not be seen to
    /// have not worked: the assets appeared, the prefab was left untouched, and the only symptom was
    /// capsules on the board. A shape a creature is made of is not really content -- it is what a
    /// unit *is* -- so it belongs where the unit is drawn.
    /// </summary>
    public static class CreatureToken
    {
        /// <summary>Radius of the token, in world units. A hex is about 1.7 across.</summary>
        public const float Radius = 0.44f;

        /// <summary>How thick the checker is.</summary>
        public const float Height = 0.22f;

        /// <summary>How much smaller the portrait is than the token, leaving a rim of party colour.</summary>
        public const float PortraitInset = 0.86f;

        static Mesh s_Disc;
        static Mesh s_Cylinder;

        /// <summary>
        /// A flat disc of radius one half, facing up, with the texture mapped across it.
        ///
        /// The UVs are what crop the portrait: a square texture is laid over the circle's bounding
        /// box, so everything outside the circle simply has no geometry to be drawn on. No shader,
        /// no alpha mask, and no requirement that the source texture be readable -- which sprites
        /// imported with default settings are not.
        /// </summary>
        public static Mesh Disc
        {
            get
            {
                if (s_Disc != null)
                {
                    return s_Disc;
                }

                const int segments = 64;

                var vertices = new Vector3[segments + 1];
                var uvs = new Vector2[segments + 1];
                var normals = new Vector3[segments + 1];
                var triangles = new int[segments * 3];

                vertices[0] = Vector3.zero;
                uvs[0] = new Vector2(0.5f, 0.5f);
                normals[0] = Vector3.up;

                for (var i = 0; i < segments; i++)
                {
                    var angle = (i / (float)segments) * Mathf.PI * 2f;
                    var x = Mathf.Cos(angle) * 0.5f;
                    var z = Mathf.Sin(angle) * 0.5f;

                    vertices[i + 1] = new Vector3(x, 0f, z);
                    uvs[i + 1] = new Vector2(0.5f + x, 0.5f + z);
                    normals[i + 1] = Vector3.up;

                    // Wound so the face points up, which is the only side anybody sees.
                    triangles[i * 3] = 0;
                    triangles[(i * 3) + 1] = ((i + 1) % segments) + 1;
                    triangles[(i * 3) + 2] = i + 1;
                }

                s_Disc = new Mesh
                {
                    name = "Token Disc",
                    vertices = vertices,
                    uv = uvs,
                    normals = normals,
                    triangles = triangles
                };

                s_Disc.RecalculateBounds();
                return s_Disc;
            }
        }

        /// <summary>
        /// The engine's cylinder.
        ///
        /// Unity only hands these out by making one, so this makes one, keeps the mesh and throws
        /// the object away. The mesh belongs to the engine and outlives it.
        /// </summary>
        public static Mesh Cylinder
        {
            get
            {
                if (s_Cylinder != null)
                {
                    return s_Cylinder;
                }

                var temporary = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                s_Cylinder = temporary.GetComponent<MeshFilter>().sharedMesh;

                if (Application.isPlaying)
                {
                    Object.Destroy(temporary);
                }
                else
                {
                    Object.DestroyImmediate(temporary);
                }

                return s_Cylinder;
            }
        }
    }
}
