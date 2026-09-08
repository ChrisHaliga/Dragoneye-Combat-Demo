using System.Collections.Generic;
using Dragoneye.Game;
using Dragoneye.Game.Combat;
using Dragoneye.Hex;
using Dragoneye.Hex.Rendering;
using Dragoneye.Hex.Systems;
using Dragoneye.Scenarios;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Authors the arena's terrain and map, and wires what draws it.
    ///
    /// The terrains are written from <see cref="ShippedTerrain"/>, so the assets and the harness
    /// agree on what grass, stone and water are. The map is <see cref="Maps.Ruins"/>, a recipe,
    /// written into an <see cref="AuthoredMapDefinition"/> with every shipped terrain bound --
    /// which is the palette the arena uses for whichever map the host picks.
    ///
    /// Safe to re-run. The assets are updated in place, the scene's map component is pointed at
    /// the authored map, and the wall renderer, the see-through driver, the reach overlay and
    /// the scenario runner are ensured.
    /// </summary>
    static class ArenaMapSetup
    {
        const string k_Map = "Assets/Settings/Hex/Ruins.asset";
        const string k_WallMaterial = "Assets/Settings/Hex/Wall.mat";
        const string k_TerrainFolder = "Assets/Settings/Hex";
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";

        [MenuItem("ClaudeCode/Build The Arena Map")]
        internal static void Run()
        {
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var map = AuthorTerrainAndMap();

            if (map == null)
            {
                return;
            }

            WireScene(map, WallMaterial());
        }

        // ---------- the terrain and the map ----------

        /// <summary>Every shipped terrain as an asset, and the arena's map bound to all of them.</summary>
        internal static AuthoredMapDefinition AuthorTerrainAndMap()
        {
            var palette = new List<AuthoredMapDefinition.TerrainEntry>();

            foreach (var spec in ShippedTerrain.All)
            {
                palette.Add(new AuthoredMapDefinition.TerrainEntry { Name = spec.Name, Terrain = Terrain(spec) });
            }

            var asset = AssetDatabase.LoadAssetAtPath<AuthoredMapDefinition>(k_Map);

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AuthoredMapDefinition>();
                AssetDatabase.CreateAsset(asset, k_Map);
            }

            var recipe = Maps.Ruins();

            asset.Author(recipe, palette);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Debug.Log($"Authored {k_Map}: {recipe.Tiles.Count} special tiles, {recipe.Walls.Count} wall "
                + $"segments, {palette.Count} terrains bound.");

            return asset;
        }

        /// <summary>The asset for a terrain, written from its spec whether or not it existed.</summary>
        static TerrainType Terrain(TerrainSpec spec)
        {
            var path = $"{k_TerrainFolder}/{spec.DisplayName}.asset";
            var terrain = AssetDatabase.LoadAssetAtPath<TerrainType>(path);

            if (terrain == null)
            {
                terrain = ScriptableObject.CreateInstance<TerrainType>();
                AssetDatabase.CreateAsset(terrain, path);
            }

            terrain.Apply(spec);
            EditorUtility.SetDirty(terrain);
            return terrain;
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

            // The scenario runner brings its own maps, written against the same terrain names.
            var scenarios = Ensure<ScenarioRunner>(host);
            Assign(context, ("m_Scenarios", scenarios));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("Arena map wired: Ruins, walls drawn, see-through, reach overlay and scenario runner ensured.");
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
