using Dragoneye.Game;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// One-off scaffolding: moves the player roster onto the draft prefab, adds the lobby draft
    /// panel, and replaces the two board-click components with the merged one. Disposable.
    /// </summary>
    static class DraftFixSetup
    {
        const string k_DraftPrefabPath = "Assets/NGO_Minimal_Setup/DraftState.prefab";
        const string k_CatalogPath = "Assets/Settings/Creatures/CreatureCatalog.asset";
        const string k_DraftUxmlPath = "Assets/UI/DraftPanel.uxml";
        const string k_PanelSettingsPath = "Assets/UI/SessionPanelSettings.asset";
        const string k_MenuScene = "Assets/Scenes/MainMenu.unity";
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";

        [MenuItem("ClaudeCode/Fix Draft Ownership And Add Lobby Draft")]
        static void SetUp()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            MoveRosterOntoDraftPrefab();
            WireMenuScene();
            WireArenaScene();

            AssetDatabase.SaveAssets();
            Debug.Log("Draft wiring updated. Delete Assets/Editor/DraftFixSetup.cs once verified.");
        }

        /// <summary>
        /// The roster has to exist from the moment the lobby opens, because the draft addresses
        /// players by slot. Living only in the arena made every draft RPC a silent no-op.
        /// </summary>
        static void MoveRosterOntoDraftPrefab()
        {
            var root = PrefabUtility.LoadPrefabContents(k_DraftPrefabPath);
            try
            {
                if (root.GetComponent<PlayerRoster>() == null)
                {
                    root.AddComponent<PlayerRoster>();
                }

                var draft = root.GetComponent<DraftState>();
                var serialized = new SerializedObject(draft);
                Assign(serialized, "m_Catalog", AssetDatabase.LoadAssetAtPath<CreatureCatalog>(k_CatalogPath));
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, k_DraftPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void WireMenuScene()
        {
            var scene = EditorSceneManager.OpenScene(k_MenuScene, OpenSceneMode.Single);

            var existing = GameObject.Find("Draft Panel");
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            var panel = new GameObject("Draft Panel", typeof(UIDocument), typeof(DraftPanelView));
            var document = panel.GetComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(k_PanelSettingsPath);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_DraftUxmlPath);

            // Above the session menu so its own document does not swallow the draft's clicks.
            document.sortingOrder = 1;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void WireArenaScene()
        {
            var scene = EditorSceneManager.OpenScene(k_ArenaScene, OpenSceneMode.Single);

            // The roster now lives on the draft prefab; a second copy here would fight it.
            var sceneRoster = Object.FindAnyObjectByType<PlayerRoster>();
            if (sceneRoster != null)
            {
                Object.DestroyImmediate(sceneRoster.gameObject);
            }

            var context = Object.FindAnyObjectByType<ArenaContext>();
            if (context == null)
            {
                Debug.LogError("No ArenaContext in the arena; run the earlier setups first.");
                return;
            }

            var host = context.gameObject;

            var contextSerialized = new SerializedObject(context);
            Assign(contextSerialized, "m_Catalog", AssetDatabase.LoadAssetAtPath<CreatureCatalog>(k_CatalogPath));
            contextSerialized.ApplyModifiedPropertiesWithoutUndo();

            var pointer = Object.FindAnyObjectByType<HexPointer>();
            var units = Object.FindAnyObjectByType<UnitIndex>();
            var selection = host.GetComponent<CreatureSelection>() ?? host.AddComponent<CreatureSelection>();

            // Selection and orders were two components on one click event, which made the outcome
            // depend on subscription order. One component now owns the whole gesture.
            RemoveByName(host, "BoardSelectionInput");
            RemoveByName(host, "UnitOrderInput");

            var command = host.GetComponent<BoardCommandInput>() ?? host.AddComponent<BoardCommandInput>();
            var commandSerialized = new SerializedObject(command);
            Assign(commandSerialized, "m_Pointer", pointer);
            Assign(commandSerialized, "m_Units", units);
            Assign(commandSerialized, "m_Selection", selection);
            commandSerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void RemoveByName(GameObject host, string typeName)
        {
            foreach (var behaviour in host.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && behaviour.GetType().Name == typeName)
                {
                    Object.DestroyImmediate(behaviour);
                }
            }
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
