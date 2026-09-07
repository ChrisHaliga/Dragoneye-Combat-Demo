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
    /// Authors the arena's map -- the Ruins -- and wires what draws it.
    ///
    /// The map is <see cref="Maps.Ruins"/>, a recipe, written into an <see cref="AuthoredMapDefinition"/>
    /// with the terrain names bound to the terrain assets. The recipe is the authoring surface:
    /// the scenarios stand on the same one, and the harness builds it to check the room is
    /// closed. A painting tool would write the same records.
    ///
    /// Safe to re-run. The asset is updated in place, the scene's map component is pointed at it,
    /// and the wall renderer, the see-through driver, the reach overlay and the scenario runner
    /// are ensured.
    /// </summary>
    static class ArenaMapSetup
    {
        const string k_Map = "Assets/Settings/Hex/Ruins.asset";
        const string k_WallMaterial = "Assets/Settings/Hex/Wall.mat";
        const string k_Grass = "Assets/Settings/Hex/Grass.asset";
        const string k_Stone = "Assets/Settings/Hex/Stone.asset";
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";

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

            WireScene(map, material, grass, stone);
        }

        // ---------- the map ----------

        static AuthoredMapDefinition AuthorRuins(TerrainType grass, TerrainType stone)
        {
            var asset = AssetDatabase.LoadAssetAtPath<AuthoredMapDefinition>(k_Map);

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AuthoredMapDefinition>();
                AssetDatabase.CreateAsset(asset, k_Map);
            }

            var recipe = Maps.Ruins();

            asset.Author(recipe, Palette(grass, stone));
            EditorUtility.SetDirty(asset);

            Debug.Log($"Authored {k_Map}: {recipe.Tiles.Count} special tiles, {recipe.Walls.Count} wall segments.");

            return asset;
        }

        /// <summary>What the recipes' terrain names mean, in assets.</summary>
        static List<AuthoredMapDefinition.TerrainEntry> Palette(TerrainType grass, TerrainType stone) =>
            new List<AuthoredMapDefinition.TerrainEntry>
            {
                new AuthoredMapDefinition.TerrainEntry { Name = Ground.Grass, Terrain = grass },
                new AuthoredMapDefinition.TerrainEntry { Name = Ground.Stone, Terrain = stone }
            };

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

        static void WireScene(AuthoredMapDefinition map, Material wallMaterial, TerrainType grass,
            TerrainType stone)
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
            Assign(scenarios, ("m_Grass", grass), ("m_Stone", stone));
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
