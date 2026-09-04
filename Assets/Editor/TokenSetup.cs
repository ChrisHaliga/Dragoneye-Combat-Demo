using Dragoneye.Game;
using UnityEditor;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Reshapes the unit prefab into a token: a short cylinder wearing its portrait on top.
    ///
    /// A checker rather than a figure. The board is read from above at a shallow angle, where a
    /// standing capsule is a coloured smudge that hides the tile behind it and tells you nothing
    /// about who it is. A flat disc reads as a piece on a board, and the face is the part a player
    /// actually recognises.
    ///
    /// The crop is the geometry, not the texture. A generated disc mesh with circular UVs shows a
    /// square portrait as a circle without a shader, without an alpha mask, and without needing the
    /// source texture to be readable -- which sprites imported with default settings are not.
    ///
    /// Idempotent, like every other step: it rebuilds the mesh and the material and re-points the
    /// prefab at them, so running it twice leaves one of each.
    /// </summary>
    static class TokenSetup
    {
        const string k_UnitPrefab = "Assets/NGO_Minimal_Setup/Unit.prefab";
        const string k_Folder = "Assets/UI/Generated";
        const string k_DiscMesh = k_Folder + "/TokenDisc.mesh";
        const string k_Material = k_Folder + "/TokenPortrait.mat";

        /// <summary>Radius of the token, in the prefab's local space.</summary>
        const float k_Radius = 0.44f;

        /// <summary>Half the token's height. Unity's cylinder is two units tall, hence the halving.</summary>
        const float k_HalfHeight = 0.11f;

        /// <summary>Runs the whole step. Called by <see cref="SetUpEverything"/>.</summary>
        internal static void Run()
        {
            var disc = BuildDisc();
            var material = BuildMaterial();

            var contents = PrefabUtility.LoadPrefabContents(k_UnitPrefab);

            if (contents == null)
            {
                Debug.LogWarning($"No prefab at {k_UnitPrefab}; tokens were not built.");
                return;
            }

            try
            {
                Reshape(contents, disc, material);
                PrefabUtility.SaveAsPrefabAsset(contents, k_UnitPrefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static void Reshape(GameObject root, Mesh disc, Material material)
        {
            var body = root.transform.Find("Body");

            if (body == null)
            {
                Debug.LogWarning("The unit prefab has no 'Body'; tokens were not built.");
                return;
            }

            // A cylinder standing on the tile rather than a capsule floating in the middle of it.
            var filter = body.GetComponent<MeshFilter>();

            if (filter != null)
            {
                filter.sharedMesh = Primitive(PrimitiveType.Cylinder);
            }

            body.localScale = new Vector3(k_Radius * 2f, k_HalfHeight, k_Radius * 2f);
            body.localPosition = new Vector3(0f, k_HalfHeight, 0f);

            // The rings sit just under the token's lip, where they read as a base rather than as
            // something the token is hovering over.
            Sit(root.transform.Find("Party Ring"), 0.004f);
            Sit(root.transform.Find("Player Accent"), 0.008f);

            var portrait = Portrait(root.transform, disc, material);
            portrait.localScale = new Vector3(k_Radius * 1.72f, 1f, k_Radius * 1.72f);
            portrait.localPosition = new Vector3(0f, (k_HalfHeight * 2f) + 0.004f, 0f);

            // The root no longer needs lifting: the token's base is its own origin now, where the
            // capsule's middle used to be.
            var view = root.GetComponent<UnitView>();

            if (view != null)
            {
                var serialized = new SerializedObject(view);
                serialized.FindProperty("m_GroundOffset").floatValue = 0f;
                serialized.FindProperty("m_Portrait").objectReferenceValue =
                    portrait.GetComponent<MeshRenderer>();
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void Sit(Transform ring, float height)
        {
            if (ring != null)
            {
                ring.localPosition = new Vector3(0f, height, 0f);
            }
        }

        /// <summary>The disc that wears the face. Created once and re-pointed on every run.</summary>
        static Transform Portrait(Transform root, Mesh disc, Material material)
        {
            var existing = root.Find("Portrait");
            var portrait = existing != null
                ? existing.gameObject
                : new GameObject("Portrait");

            portrait.transform.SetParent(root, false);

            var filter = portrait.GetComponent<MeshFilter>() ?? portrait.AddComponent<MeshFilter>();
            var renderer = portrait.GetComponent<MeshRenderer>()
                ?? portrait.AddComponent<MeshRenderer>();

            filter.sharedMesh = disc;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return portrait.transform;
        }

        /// <summary>
        /// A flat disc of radius one half, facing up, with the texture mapped across it.
        ///
        /// The UVs are what do the cropping: the square texture is laid over the circle's bounding
        /// box, so everything outside the circle simply has no geometry to be drawn on.
        /// </summary>
        static Mesh BuildDisc()
        {
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

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(k_DiscMesh);
            var fresh = mesh == null;

            if (fresh)
            {
                mesh = new Mesh();
            }

            mesh.Clear();
            mesh.name = "TokenDisc";
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            if (fresh)
            {
                AssetDatabase.CreateAsset(mesh, k_DiscMesh);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            return mesh;
        }

        /// <summary>
        /// The material the disc wears.
        ///
        /// Unlit, because a portrait is a picture rather than a surface: shading it would darken
        /// whichever faces happen to point away from the light and make half the party look ill.
        /// The texture itself is set per creature at runtime through a property block.
        /// </summary>
        static Material BuildMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(k_Material);

            if (material != null)
            {
                return material;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");

            if (shader == null)
            {
                Debug.LogWarning("No unlit shader found; token portraits will be untextured.");
                return null;
            }

            material = new Material(shader) { name = "TokenPortrait" };
            AssetDatabase.CreateAsset(material, k_Material);
            return material;
        }

        /// <summary>
        /// The built-in mesh for a primitive.
        ///
        /// Unity exposes these only by making one, so this makes one and throws the object away.
        /// The mesh itself belongs to the engine and outlives it.
        /// </summary>
        static Mesh Primitive(PrimitiveType type)
        {
            var temporary = GameObject.CreatePrimitive(type);
            var mesh = temporary.GetComponent<MeshFilter>().sharedMesh;

            Object.DestroyImmediate(temporary);
            return mesh;
        }
    }
}
