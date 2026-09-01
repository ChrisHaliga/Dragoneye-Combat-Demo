using System.IO;
using Dragoneye.Game;
using Dragoneye.Hex.Rendering;
using Dragoneye.Hex.Systems;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// One-off scaffolding: builds the unit prefab and the hover marker, and wires the pointer into
    /// the arena. Disposable once it has run.
    /// </summary>
    static class UnitSetup
    {
        const string k_PrefabFolder = "Assets/NGO_Minimal_Setup";
        const string k_UnitPrefabPath = k_PrefabFolder + "/Unit.prefab";
        const string k_PrefabsListPath = k_PrefabFolder + "/NetworkPrefabsList.asset";
        const string k_MaterialPath = "Assets/Settings/Hex/UnitBody.mat";
        const string k_HighlightMaterialPath = "Assets/Settings/Hex/HexHighlight.mat";
        const string k_ActionsPath = "Assets/Settings/Input/CameraControls.inputactions";
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";
        const string k_BootstrapScene = "Assets/Scenes/Bootstrap.unity";

        [MenuItem("ClaudeCode/Set Up Units And Pointer")]
        static void SetUp()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var prefab = BuildUnitPrefab();
            if (prefab == null)
            {
                return;
            }

            RegisterNetworkPrefab(prefab);
            AssetDatabase.SaveAssets();

            WireArenaScene();
            WireBootstrapScene(prefab);

            AssetDatabase.SaveAssets();
            Debug.Log("Units and pointer ready. Delete Assets/Editor/UnitSetup.cs once verified.");
        }

        static Material Material(string path, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { enableInstancing = true, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static GameObject BuildUnitPrefab()
        {
            var material = Material(k_MaterialPath, Color.white);

            // Deliberately no NetworkTransform: UnitState.Cell is the only replicated position.
            var root = new GameObject("Unit",
                typeof(NetworkObject),
                typeof(UnitState),
                typeof(UnitCommands),
                typeof(UnitView));

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.7f, 0.5f, 0.7f);
            Object.DestroyImmediate(body.GetComponent<Collider>());

            var renderer = body.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            var view = new SerializedObject(root.GetComponent<UnitView>());
            Assign(view, "m_Body", renderer);
            view.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(k_PrefabFolder);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_UnitPrefabPath);
            Object.DestroyImmediate(root);

            if (prefab == null)
            {
                Debug.LogError($"Could not save {k_UnitPrefabPath}.");
            }

            return prefab;
        }

        static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(k_PrefabsListPath);
            if (list == null)
            {
                Debug.LogError($"Could not load {k_PrefabsListPath}; units will fail to spawn.");
                return;
            }

            var serialized = new SerializedObject(list);
            var entries = serialized.FindProperty("List");

            for (var i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab");
                if (entry != null && entry.objectReferenceValue == prefab)
                {
                    return;
                }
            }

            entries.InsertArrayElementAtIndex(entries.arraySize);
            var added = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            added.FindPropertyRelative("Override").enumValueIndex = 0;
            added.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(list);
        }

        static void WireArenaScene()
        {
            var scene = EditorSceneManager.OpenScene(k_ArenaScene, OpenSceneMode.Single);

            var context = Object.FindAnyObjectByType<ArenaContext>();
            var arenaMap = Object.FindAnyObjectByType<ArenaMap>();
            if (context == null || arenaMap == null)
            {
                Debug.LogError("Arena is missing its context or map; run the earlier setup first.");
                return;
            }

            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(k_ActionsPath);
            var host = context.gameObject;

            var units = host.GetComponent<UnitIndex>() ?? host.AddComponent<UnitIndex>();
            var pointer = host.GetComponent<HexPointer>() ?? host.AddComponent<HexPointer>();
            var orders = host.GetComponent<UnitOrderInput>() ?? host.AddComponent<UnitOrderInput>();
            var highlight = host.GetComponent<HexHoverHighlight>() ?? host.AddComponent<HexHoverHighlight>();

            var pointerSerialized = new SerializedObject(pointer);
            Assign(pointerSerialized, "m_Actions", actions);
            pointerSerialized.ApplyModifiedPropertiesWithoutUndo();

            var contextSerialized = new SerializedObject(context);
            Assign(contextSerialized, "m_Units", units);
            contextSerialized.ApplyModifiedPropertiesWithoutUndo();

            var ordersSerialized = new SerializedObject(orders);
            Assign(ordersSerialized, "m_Pointer", pointer);
            Assign(ordersSerialized, "m_Units", units);
            ordersSerialized.ApplyModifiedPropertiesWithoutUndo();

            var marker = BuildHoverMarker(arenaMap);

            var highlightSerialized = new SerializedObject(highlight);
            Assign(highlightSerialized, "m_Pointer", pointer);
            Assign(highlightSerialized, "m_Marker", marker);
            highlightSerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// A hex-shaped marker built from the same factory the tiles use, so it lines up exactly
        /// whatever the tile size is.
        /// </summary>
        static GameObject BuildHoverMarker(ArenaMap arenaMap)
        {
            var existing = GameObject.Find("Hover Marker");
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            var size = arenaMap.Definition != null ? arenaMap.Definition.TileSize : 1f;

            var marker = new GameObject("Hover Marker", typeof(MeshFilter), typeof(MeshRenderer));
            marker.GetComponent<MeshFilter>().sharedMesh = HexMeshFactory.Create(size, 0.98f);

            var renderer = marker.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Material(k_HighlightMaterialPath, new Color(1f, 0.92f, 0.55f, 1f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            marker.SetActive(false);
            return marker;
        }

        static void WireBootstrapScene(GameObject unitPrefab)
        {
            var scene = EditorSceneManager.OpenScene(k_BootstrapScene, OpenSceneMode.Single);

            var spawner = Object.FindAnyObjectByType<MatchSpawner>();
            if (spawner == null)
            {
                Debug.LogError($"No {nameof(MatchSpawner)} in {k_BootstrapScene}.");
                return;
            }

            var serialized = new SerializedObject(spawner);
            Assign(serialized, "m_UnitPrefab", unitPrefab);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void Assign(SerializedObject serialized, string propertyPath, Object value)
        {
            var property = serialized.FindProperty(propertyPath);
            if (property == null)
            {
                Debug.LogError($"{serialized.targetObject.GetType().Name} has no field '{propertyPath}'.");
                return;
            }

            property.objectReferenceValue = value;

            if (property.objectReferenceValue == null && value != null)
            {
                Debug.LogError($"Failed to assign '{propertyPath}'. Assign it by hand in the inspector.");
            }
        }
    }
}
