using System.IO;
using Dragoneye.CameraControl;
using Dragoneye.Game;
using Dragoneye.Hex.Systems;
using Dragoneye.Multiplayer;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// One-off scaffolding: rebuilds the focus prefab and rewires both scenes for the refactored
    /// components. Disposable once it has run.
    /// </summary>
    static class ArenaWiringSetup
    {
        const string k_PrefabFolder = "Assets/NGO_Minimal_Setup";
        const string k_FocusPrefabPath = k_PrefabFolder + "/PlayerFocus.prefab";
        const string k_OldPrefabPath = k_PrefabFolder + "/PlayerCursor.prefab";
        const string k_PrefabsListPath = k_PrefabFolder + "/NetworkPrefabsList.asset";
        const string k_MaterialPath = "Assets/Settings/Hex/CursorMarker.mat";
        const string k_ActionsPath = "Assets/Settings/Input/CameraControls.inputactions";
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";
        const string k_BootstrapScene = "Assets/Scenes/Bootstrap.unity";

        [MenuItem("ClaudeCode/Rewire Arena After Refactor")]
        static void SetUp()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var prefab = BuildFocusPrefab();
            if (prefab == null)
            {
                return;
            }

            RegisterNetworkPrefab(prefab);
            AssetDatabase.SaveAssets();

            WireArenaScene();
            WireBootstrapScene(prefab);

            AssetDatabase.SaveAssets();
            Debug.Log("Arena rewired. Delete Assets/Editor/ArenaWiringSetup.cs once you have verified it.");
        }

        static GameObject BuildFocusPrefab()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
            if (material == null)
            {
                Debug.LogError($"Could not load {k_MaterialPath}.");
                return null;
            }

            var root = new GameObject("PlayerFocus",
                typeof(NetworkObject),
                typeof(OwnerAuthoritativeTransform),
                typeof(FocusPoint),
                typeof(FocusState),
                typeof(FocusView));

            // The root never rotates or scales -- yaw lives on the camera rig and the label
            // billboards itself -- so replicating those channels is pure bandwidth.
            var transform = root.GetComponent<OwnerAuthoritativeTransform>();
            transform.SyncRotAngleX = false;
            transform.SyncRotAngleY = false;
            transform.SyncRotAngleZ = false;
            transform.SyncScaleX = false;
            transform.SyncScaleY = false;
            transform.SyncScaleZ = false;

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Marker";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localScale = new Vector3(1.15f, 0.02f, 1.15f);
            marker.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            Object.DestroyImmediate(marker.GetComponent<Collider>());

            var markerRenderer = marker.GetComponent<MeshRenderer>();
            markerRenderer.sharedMaterial = material;
            markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var labelObject = new GameObject("Label", typeof(TextMesh), typeof(Billboard));
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 2.1f, 0f);

            var label = labelObject.GetComponent<TextMesh>();
            label.text = "Player";
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.12f;
            label.fontSize = 64;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (label.font != null)
            {
                labelObject.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            }

            var state = new SerializedObject(root.GetComponent<FocusState>());
            Assign(state, "m_Focus", root.GetComponent<FocusPoint>());
            state.ApplyModifiedPropertiesWithoutUndo();

            var view = new SerializedObject(root.GetComponent<FocusView>());
            Assign(view, "m_Marker", markerRenderer);
            Assign(view, "m_Label", label);
            view.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(k_PrefabFolder);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_FocusPrefabPath);
            Object.DestroyImmediate(root);

            if (prefab == null)
            {
                Debug.LogError($"Could not save {k_FocusPrefabPath}.");
            }

            return prefab;
        }

        /// <summary>Swaps the old cursor prefab entry for the new focus prefab.</summary>
        static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(k_PrefabsListPath);
            if (list == null)
            {
                Debug.LogError($"Could not load {k_PrefabsListPath}; the focus will fail to spawn.");
                return;
            }

            var old = AssetDatabase.LoadAssetAtPath<GameObject>(k_OldPrefabPath);

            var serialized = new SerializedObject(list);
            var entries = serialized.FindProperty("List");

            for (var i = entries.arraySize - 1; i >= 0; i--)
            {
                var entry = entries.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab");
                var value = entry != null ? entry.objectReferenceValue : null;

                if (value == prefab)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    return;
                }

                if (value == null || (old != null && value == old))
                {
                    entries.DeleteArrayElementAtIndex(i);
                }
            }

            entries.InsertArrayElementAtIndex(entries.arraySize);
            var added = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            added.FindPropertyRelative("Override").enumValueIndex = 0;
            added.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(list);

            if (old != null)
            {
                AssetDatabase.DeleteAsset(k_OldPrefabPath);
            }
        }

        static void WireArenaScene()
        {
            var scene = EditorSceneManager.OpenScene(k_ArenaScene, OpenSceneMode.Single);

            var actions = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(k_ActionsPath);
            var arenaMap = Object.FindAnyObjectByType<ArenaMap>();
            var rig = Object.FindAnyObjectByType<CameraRig>();
            var rigInput = Object.FindAnyObjectByType<CameraRigInput>();

            if (arenaMap == null || rig == null || rigInput == null)
            {
                Debug.LogError("Arena is missing its map or camera rig; run the earlier setup first.");
                return;
            }

            // Bounds were never in the scene, so the focus point could pan off the map forever.
            var bounds = arenaMap.GetComponent<HexArenaCameraBounds>()
                ?? arenaMap.gameObject.AddComponent<HexArenaCameraBounds>();

            var boundsSerialized = new SerializedObject(bounds);
            Assign(boundsSerialized, "m_Arena", arenaMap);
            boundsSerialized.ApplyModifiedPropertiesWithoutUndo();

            // The roster is an in-scene network object so NGO spawns it with the arena.
            var rosterObject = GameObject.Find("Player Roster")
                ?? new GameObject("Player Roster", typeof(NetworkObject), typeof(PlayerRoster));
            if (rosterObject.GetComponent<PlayerRoster>() == null)
            {
                rosterObject.AddComponent<PlayerRoster>();
            }

            var contextObject = GameObject.Find("Arena Context") ?? new GameObject("Arena Context");
            var context = contextObject.GetComponent<ArenaContext>()
                ?? contextObject.AddComponent<ArenaContext>();

            var contextSerialized = new SerializedObject(context);
            Assign(contextSerialized, "m_Map", arenaMap);
            Assign(contextSerialized, "m_Rig", rig);
            Assign(contextSerialized, "m_RigInput", rigInput);
            Assign(contextSerialized, "m_CameraBounds", bounds);
            Assign(contextSerialized, "m_OutputCamera", Object.FindAnyObjectByType<CinemachineBrain>()?.GetComponent<Camera>());
            contextSerialized.ApplyModifiedPropertiesWithoutUndo();

            var matchInput = contextObject.GetComponent<MatchInput>() ?? contextObject.AddComponent<MatchInput>();
            var matchInputSerialized = new SerializedObject(matchInput);
            Assign(matchInputSerialized, "m_Input", rigInput);
            matchInputSerialized.ApplyModifiedPropertiesWithoutUndo();

            var inputSerialized = new SerializedObject(rigInput);
            Assign(inputSerialized, "m_Actions", actions);
            inputSerialized.ApplyModifiedPropertiesWithoutUndo();

            // SmartUpdate guesses between FixedUpdate and LateUpdate per target, which is wrong for
            // a rig moved by a script in LateUpdate.
            var brain = Object.FindAnyObjectByType<CinemachineBrain>();
            if (brain != null)
            {
                brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
                EditorUtility.SetDirty(brain);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void WireBootstrapScene(GameObject focusPrefab)
        {
            var scene = EditorSceneManager.OpenScene(k_BootstrapScene, OpenSceneMode.Single);

            var runner = Object.FindAnyObjectByType<SessionRunner>();
            if (runner == null)
            {
                Debug.LogError($"No {nameof(SessionRunner)} in {k_BootstrapScene}.");
                return;
            }

            var spawner = runner.GetComponent<MatchSpawner>() ?? runner.gameObject.AddComponent<MatchSpawner>();

            var serialized = new SerializedObject(spawner);
            Assign(serialized, "m_FocusPrefab", focusPrefab);

            // Focus-only play. Drop a prefab in here when there are real units to place.
            Assign(serialized, "m_PlayerPrefab", null);
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
