using System.Collections.Generic;
using Dragoneye.Game;
using Dragoneye.Game.Combat;
using Dragoneye.Hex;
using Dragoneye.Hex.Rendering;
using Dragoneye.Hex.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    // Declared inside the namespace: out here the bare name Hex would bind to the Dragoneye.Hex
    // namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Authors the arena's map -- a hexagon of grass with ruins on it -- and wires what draws it.
    ///
    /// The map is an <see cref="AuthoredMapDefinition"/>: ground, a few boulders, and walls as
    /// rays and half-edges. The walls here are written by two small helpers that know how a
    /// straight line lies on a hex grid, so a rectangular room comes out as the rays and edges the
    /// rules read. A painting tool would write the same records; until there is one, this is the
    /// authoring surface, and it is a page of numbers with names on them.
    ///
    /// Safe to re-run. The asset is updated in place, the scene's map component is pointed at it,
    /// and the wall renderer, the see-through driver and the reach overlay are ensured.
    /// </summary>
    static class ArenaMapSetup
    {
        const string k_Map = "Assets/Settings/Hex/Ruins.asset";
        const string k_WallMaterial = "Assets/Settings/Hex/Wall.mat";
        const string k_Grass = "Assets/Settings/Hex/Grass.asset";
        const string k_Stone = "Assets/Settings/Hex/Stone.asset";
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";

        static readonly Wall Solid = new Wall(WallFlags.Solid);
        static readonly Wall Low = new Wall(WallFlags.BlocksMovement);
        static readonly Wall Curtain = new Wall(WallFlags.BlocksSight);

        internal static void Run()
        {
            var grass = AssetDatabase.LoadAssetAtPath<TerrainType>(k_Grass);
            var stone = AssetDatabase.LoadAssetAtPath<TerrainType>(k_Stone);

            if (grass == null || stone == null)
            {
                Debug.LogWarning("Grass or Stone terrain is missing; the arena map was not authored.");
                return;
            }

            // A boulder is something a line stops at, not only something feet do.
            var stoneSerialized = new SerializedObject(stone);
            stoneSerialized.FindProperty("m_BlocksSight").boolValue = true;
            stoneSerialized.FindProperty("m_IsWalkable").boolValue = false;
            stoneSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(stone);

            var map = AuthorRuins(grass, stone);
            var material = WallMaterial();

            WireScene(map, material);
        }

        // ---------- the map ----------

        sealed class Draft
        {
            public readonly List<(int q, int r, TerrainType terrain)> Tiles = new List<(int, int, TerrainType)>();
            public readonly List<(int q, int r, int ray, Wall wall)> Rays = new List<(int, int, int, Wall)>();
            public readonly List<(int q, int r, int half, Wall wall)> HalfEdges = new List<(int, int, int, Wall)>();

            public void Ray(Hex hex, int ray, Wall wall) => Rays.Add((hex.Q, hex.R, ray, wall));

            public void Edge(Hex hex, HexDirection edge, Wall wall)
            {
                TileGeometry.HalvesOf(edge, out var first, out var second);
                HalfEdges.Add((hex.Q, hex.R, first, wall));
                HalfEdges.Add((hex.Q, hex.R, second, wall));
            }

            /// <summary>
            /// A vertical wall up a column of tiles, midpoint to midpoint: rays 0 and 6 on each.
            /// The north midpoint of one tile is the south midpoint of the next, so the line is
            /// continuous.
            /// </summary>
            public void Vertical(Hex bottom, int tiles, Wall wall)
            {
                for (var i = 0; i < tiles; i++)
                {
                    var hex = new Hex(bottom.Q, bottom.R + i);
                    Ray(hex, 0, wall);
                    Ray(hex, 6, wall);
                }
            }

            /// <summary>
            /// A horizontal wall eastward from a tile's centre: corner to corner through the tile
            /// (rays 3 and 9), then along the flat north edge of the tile below-right, then through
            /// the next tile on the row, and so on. Each piece of the alternation is one unit of
            /// length, so a run of 2n pieces spans n tiles of the row.
            /// </summary>
            public void Horizontal(Hex start, int pieces, Wall wall)
            {
                for (var i = 0; i < pieces; i++)
                {
                    var hex = new Hex(start.Q + i, start.R - (i + 1) / 2);

                    if (i % 2 == 0)
                    {
                        Ray(hex, 3, wall);
                        Ray(hex, 9, wall);
                    }
                    else
                    {
                        Edge(hex, HexDirection.North, wall);
                    }
                }
            }
        }

        static AuthoredMapDefinition AuthorRuins(TerrainType grass, TerrainType stone)
        {
            var draft = new Draft();

            // Boulders. Something to stand behind, and something the line stops at.
            draft.Tiles.Add((3, -3, stone));
            draft.Tiles.Add((-2, -1, stone));
            draft.Tiles.Add((1, -4, stone));
            draft.Tiles.Add((-4, 3, stone));

            // A roofless room to the north-east, two tiles wide, with its door on the south side.
            //
            // Its corners are quarter-cut tiles and its long sides run through the middles of
            // tiles, so it is the whole of what this feature is for in one place: standing on the
            // inside half of a cut tile, and not being able to reach the outside half of it.
            var a = new Hex(0, 3);   // north-west corner: the room lies to its south-east
            var b = new Hex(2, 2);   // north-east corner
            var c = new Hex(2, 0);   // south-east corner
            var d = new Hex(0, 1);   // south-west corner

            draft.Ray(a, 3, Solid);
            draft.Ray(a, 6, Solid);
            draft.Edge(new Hex(1, 2), HexDirection.North, Solid);   // the top, between the corners
            draft.Ray(b, 9, Solid);
            draft.Ray(b, 6, Solid);
            draft.Vertical(new Hex(2, 1), 1, Solid);                 // the east side
            draft.Ray(c, 0, Solid);
            draft.Ray(c, 9, Solid);
            // The bottom would run along the north edge of (1, 0). Left open: that is the door.
            draft.Ray(d, 3, Solid);
            draft.Ray(d, 0, Solid);
            draft.Vertical(new Hex(0, 2), 1, Solid);                 // the west side

            // A hedge to the west: waist high, three tiles long. Feet stop, eyes and arrows do not.
            draft.Vertical(new Hex(-3, 0), 3, Low);

            // A hanging cloth across one edge to the south: eyes stop, feet do not.
            draft.Edge(new Hex(-1, -2), HexDirection.North, Curtain);

            // A stub of fallen wall, alone. It splits nothing; it is just in the way of a line.
            draft.Ray(new Hex(3, -1), 9, Solid);

            var asset = AssetDatabase.LoadAssetAtPath<AuthoredMapDefinition>(k_Map);

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AuthoredMapDefinition>();
                AssetDatabase.CreateAsset(asset, k_Map);
            }

            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_TileSize").floatValue = 1f;
            serialized.FindProperty("m_Radius").intValue = 5;
            serialized.FindProperty("m_DefaultTerrain").objectReferenceValue = grass;

            var tiles = serialized.FindProperty("m_Tiles");
            tiles.arraySize = draft.Tiles.Count;

            for (var i = 0; i < draft.Tiles.Count; i++)
            {
                var entry = tiles.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Q").intValue = draft.Tiles[i].q;
                entry.FindPropertyRelative("R").intValue = draft.Tiles[i].r;
                entry.FindPropertyRelative("Terrain").objectReferenceValue = draft.Tiles[i].terrain;
            }

            var rays = serialized.FindProperty("m_Rays");
            rays.arraySize = draft.Rays.Count;

            for (var i = 0; i < draft.Rays.Count; i++)
            {
                var entry = rays.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Q").intValue = draft.Rays[i].q;
                entry.FindPropertyRelative("R").intValue = draft.Rays[i].r;
                entry.FindPropertyRelative("Ray").intValue = draft.Rays[i].ray;
                entry.FindPropertyRelative("Flags").intValue = (int)draft.Rays[i].wall.Flags;
                entry.FindPropertyRelative("Integrity").intValue = draft.Rays[i].wall.Integrity;
            }

            var halves = serialized.FindProperty("m_HalfEdges");
            halves.arraySize = draft.HalfEdges.Count;

            for (var i = 0; i < draft.HalfEdges.Count; i++)
            {
                var entry = halves.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Q").intValue = draft.HalfEdges[i].q;
                entry.FindPropertyRelative("R").intValue = draft.HalfEdges[i].r;
                entry.FindPropertyRelative("HalfEdge").intValue = draft.HalfEdges[i].half;
                entry.FindPropertyRelative("Flags").intValue = (int)draft.HalfEdges[i].wall.Flags;
                entry.FindPropertyRelative("Integrity").intValue = draft.HalfEdges[i].wall.Integrity;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            Debug.Log($"Authored {k_Map}: {draft.Tiles.Count} special tiles, {draft.Rays.Count} ray "
                + $"walls, {draft.HalfEdges.Count} half-edge walls.");

            return asset;
        }

        // ---------- the material ----------

        static Material WallMaterial()
        {
            var shader = Shader.Find("Dragoneye/WallCutaway");

            if (shader == null)
            {
                Debug.LogWarning("Dragoneye/WallCutaway did not compile or is not imported; "
                    + "walls fall back to a plain lit stone.");
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(k_WallMaterial);

            if (material == null)
            {
                material = new Material(shader) { name = "Wall" };
                AssetDatabase.CreateAsset(material, k_WallMaterial);
            }
            else
            {
                material.shader = shader;
            }

            var stone = new Color(0.34f, 0.32f, 0.30f, 1f);
            material.SetColor("_BaseColor", stone);
            material.color = stone;

            if (material.HasProperty("_CutRadius"))
            {
                material.SetFloat("_CutRadius", 0.11f);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------- the scene ----------

        static void WireScene(AuthoredMapDefinition map, Material wallMaterial)
        {
            var scene = EditorSceneManager.OpenScene(k_ArenaScene, OpenSceneMode.Single);
            var context = Object.FindAnyObjectByType<ArenaContext>();
            var arena = Object.FindAnyObjectByType<ArenaMap>();

            if (context == null || arena == null)
            {
                Debug.LogError("The arena is missing its context or map; run the earlier setups first.");
                return;
            }

            Assign(arena, ("m_Definition", map));

            var walls = Ensure<WallRenderer>(arena.gameObject);
            Assign(walls, ("m_WallMaterial", wallMaterial));

            var host = context.gameObject;
            Ensure<WallCutaway>(host);

            var input = host.GetComponent<BoardActionInput>();
            var reach = Ensure<ReachPreview>(host);

            if (input != null)
            {
                Assign(reach, ("m_Input", input));
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("Arena map wired: Ruins, walls drawn, see-through and reach overlay ensured.");
        }

        static T Ensure<T>(GameObject target) where T : Component
        {
            var existing = target.GetComponent<T>();
            return existing == null ? target.AddComponent<T>() : existing;
        }

        static void Assign(Object target, params (string Path, Object Value)[] fields)
        {
            var serialized = new SerializedObject(target);

            foreach (var (path, value) in fields)
            {
                var property = serialized.FindProperty(path);

                if (property == null)
                {
                    Debug.LogError($"{target.GetType().Name} has no field '{path}'.", target);
                    continue;
                }

                property.objectReferenceValue = value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
