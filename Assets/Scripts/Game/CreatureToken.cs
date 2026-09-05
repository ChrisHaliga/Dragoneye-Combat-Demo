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

        /// <summary>How far past the token's edge the facing mark reaches.</summary>
        public const float PointerReach = 0.34f;

        /// <summary>How wide the facing mark is at its base, as a fraction of the token.</summary>
        public const float PointerWidth = 0.42f;

        static Mesh s_Disc;
        static Mesh s_Cylinder;
        static Mesh s_Pointer;

        /// <summary>
        /// A flat triangle lying on the ground, pointing along positive Z.
        ///
        /// Which way a creature is turned has to be readable from across the board and from any
        /// camera angle, and half of what facing is worth is opponents seeing it in time to walk
        /// round the back. A wedge on the ground reads at a glance from directly above, which is
        /// where this camera mostly is; anything drawn on the token's face would not.
        ///
        /// Built here rather than authored so it cannot go missing from a prefab, which is the
        /// lesson the tokens themselves already taught this project.
        /// </summary>
        public static Mesh Pointer
        {
            get
            {
                if (s_Pointer != null)
                {
                    return s_Pointer;
                }

                // Outside the token, not under it. Built from the origin the first time round,
                // which put the whole triangle inside a disc of larger radius -- so the mark was
                // there, correct, and completely hidden.
                var half = PointerWidth * 0.5f;
                var start = Radius + 0.03f;
                var tip = start + PointerReach;

                var vertices = new[]
                {
                    new Vector3(-half, 0f, start),
                    new Vector3(half, 0f, start),
                    new Vector3(0f, 0f, tip)
                };

                s_Pointer = new Mesh
                {
                    name = "Token Pointer",
                    vertices = vertices,

                    // Wound both ways, so it is visible from under the board as well as over it --
                    // a camera that can orbit will find the back of it otherwise.
                    triangles = new[] { 0, 2, 1, 0, 1, 2 },
                    uv = new[] { Vector2.zero, Vector2.right, new Vector2(0.5f, 1f) }
                };

                s_Pointer.RecalculateNormals();
                s_Pointer.RecalculateBounds();
                return s_Pointer;
            }
        }

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
        /// A closed cylinder of radius one half and height one, centred on its own middle.
        ///
        /// Built here rather than borrowed from <c>GameObject.CreatePrimitive</c>. That call makes
        /// a whole GameObject with a collider on it, in whatever scene happens to be active, purely
        /// to read one mesh off it -- and it was doing that from inside the Awake of an object
        /// netcode was in the middle of spawning. Fifty lines of vertices is a smaller thing to
        /// reason about than that.
        /// </summary>
        public static Mesh Cylinder
        {
            get
            {
                if (s_Cylinder != null)
                {
                    return s_Cylinder;
                }

                const int segments = 48;

                // Two rings for the wall, plus a centre and a ring for each cap. The wall gets its
                // own vertices because its normals point outward and the caps' point up and down;
                // shared vertices would smear the two into a bevel.
                var vertices = new Vector3[(segments * 2) + ((segments + 1) * 2)];
                var normals = new Vector3[vertices.Length];
                var triangles = new int[(segments * 6) + (segments * 6)];

                var topCentre = segments * 2;
                var bottomCentre = topCentre + segments + 1;

                vertices[topCentre] = new Vector3(0f, 0.5f, 0f);
                normals[topCentre] = Vector3.up;
                vertices[bottomCentre] = new Vector3(0f, -0.5f, 0f);
                normals[bottomCentre] = Vector3.down;

                for (var i = 0; i < segments; i++)
                {
                    var angle = (i / (float)segments) * Mathf.PI * 2f;
                    var x = Mathf.Cos(angle) * 0.5f;
                    var z = Mathf.Sin(angle) * 0.5f;
                    var outward = new Vector3(x, 0f, z).normalized;

                    vertices[i] = new Vector3(x, 0.5f, z);
                    normals[i] = outward;
                    vertices[segments + i] = new Vector3(x, -0.5f, z);
                    normals[segments + i] = outward;

                    vertices[topCentre + 1 + i] = new Vector3(x, 0.5f, z);
                    normals[topCentre + 1 + i] = Vector3.up;
                    vertices[bottomCentre + 1 + i] = new Vector3(x, -0.5f, z);
                    normals[bottomCentre + 1 + i] = Vector3.down;
                }

                var t = 0;

                for (var i = 0; i < segments; i++)
                {
                    var next = (i + 1) % segments;

                    // Wall.
                    triangles[t++] = i;
                    triangles[t++] = segments + i;
                    triangles[t++] = segments + next;

                    triangles[t++] = i;
                    triangles[t++] = segments + next;
                    triangles[t++] = next;

                    // Top, wound to face up.
                    triangles[t++] = topCentre;
                    triangles[t++] = topCentre + 1 + next;
                    triangles[t++] = topCentre + 1 + i;

                    // Bottom, wound the other way.
                    triangles[t++] = bottomCentre;
                    triangles[t++] = bottomCentre + 1 + i;
                    triangles[t++] = bottomCentre + 1 + next;
                }

                s_Cylinder = new Mesh
                {
                    name = "Token Body",
                    vertices = vertices,
                    normals = normals,
                    triangles = triangles
                };

                s_Cylinder.RecalculateBounds();
                return s_Cylinder;
            }
        }
    }
}
