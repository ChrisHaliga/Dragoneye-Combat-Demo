using System.Collections.Generic;
using System.IO;
using Dragoneye.CameraControl;
using Dragoneye.Game;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// One-off scaffolding: authors the demo creature set, builds the draft and unit prefabs, and
    /// wires the arena HUD. Disposable once it has run.
    /// </summary>
    static class CreatureSetup
    {
        const string k_PrefabFolder = "Assets/NGO_Minimal_Setup";
        const string k_UnitPrefabPath = k_PrefabFolder + "/Unit.prefab";
        const string k_DraftPrefabPath = k_PrefabFolder + "/DraftState.prefab";
        const string k_PrefabsListPath = k_PrefabFolder + "/NetworkPrefabsList.asset";
        const string k_CreatureFolder = "Assets/Settings/Creatures";
        const string k_CatalogPath = k_CreatureFolder + "/CreatureCatalog.asset";
        const string k_RingMaterialPath = "Assets/Settings/Hex/UnitRing.mat";
        const string k_HudUxmlPath = "Assets/UI/ArenaHud.uxml";
        const string k_PanelSettingsPath = "Assets/UI/SessionPanelSettings.asset";
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";
        const string k_BootstrapScene = "Assets/Scenes/Bootstrap.unity";

        [MenuItem("ClaudeCode/Set Up Creatures And HUD")]
        static void SetUp()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var catalog = BuildCreatureAssets();
            var draftPrefab = BuildDraftPrefab(catalog);
            RebuildUnitPrefab();

            AssetDatabase.SaveAssets();

            var unitPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_UnitPrefabPath);
            RegisterNetworkPrefab(draftPrefab);
            RegisterNetworkPrefab(unitPrefab);
            AssetDatabase.SaveAssets();

            WireArenaScene();
            WireBootstrapScene(draftPrefab);

            AssetDatabase.SaveAssets();
            Debug.Log("Creatures and HUD ready. Delete Assets/Editor/CreatureSetup.cs once verified.");
        }

        // ------------------------------------------------------------------ assets

        static CreatureCatalog BuildCreatureAssets()
        {
            Directory.CreateDirectory(k_CreatureFolder);

            var species = new Dictionary<string, SpeciesDefinition>
            {
                ["Human"] = Species("Human", "Adaptable and disciplined; the backbone of every warband."),
                ["Goblinoid"] = Species("Goblinoid", "Small, vicious and far better organised than anyone expects."),
                ["Beast"] = Species("Beast", "Fast, instinctive, and unbothered by tactics."),
                ["Giantkin"] = Species("Giantkin", "Slow to move and slower to fall.")
            };

            var classes = new Dictionary<string, ClassDefinition>
            {
                ["Warrior"] = Class("Warrior"),
                ["Ranger"] = Class("Ranger"),
                ["Cleric"] = Class("Cleric"),
                ["Rogue"] = Class("Rogue")
            };

            var creatures = new List<CreatureDefinition>
            {
                Creature("hero-knight", "Knight", species["Human"], classes["Warrior"], 24, 6, 4),
                Creature("hero-ranger", "Ranger", species["Human"], classes["Ranger"], 18, 6, 7),
                Creature("hero-cleric", "Cleric", species["Human"], classes["Cleric"], 20, 7, 5),

                Creature("monster-goblin", "Goblin", species["Goblinoid"], classes["Rogue"], 12, 5, 8),
                Creature("monster-ogre", "Ogre", species["Giantkin"], classes["Warrior"], 34, 4, 3),
                Creature("monster-wolf", "Dire Wolf", species["Beast"], classes["Ranger"], 16, 6, 9),

                Creature("guard-sergeant", "Sergeant", species["Human"], classes["Warrior"], 26, 6, 4),
                Creature("guard-recruit", "Recruit", species["Human"], classes["Warrior"], 16, 5, 5),
                Creature("guard-archer", "Watch Archer", species["Human"], classes["Ranger"], 17, 6, 6),

                Creature("bandit-cutpurse", "Cutpurse", species["Human"], classes["Rogue"], 14, 7, 8),
                Creature("bandit-brute", "Brute", species["Giantkin"], classes["Warrior"], 30, 4, 3),
                Creature("bandit-scout", "Scout", species["Human"], classes["Ranger"], 15, 6, 7)
            };

            var catalog = AssetDatabase.LoadAssetAtPath<CreatureCatalog>(k_CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<CreatureCatalog>();
                AssetDatabase.CreateAsset(catalog, k_CatalogPath);
            }

            var serialized = new SerializedObject(catalog);
            var array = serialized.FindProperty("m_Creatures");
            array.arraySize = creatures.Count;
            for (var i = 0; i < creatures.Count; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = creatures[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        static SpeciesDefinition Species(string name, string description)
        {
            var path = $"{k_CreatureFolder}/Species_{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<SpeciesDefinition>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<SpeciesDefinition>();
                AssetDatabase.CreateAsset(asset, path);
            }

            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.FindProperty("m_Description").stringValue = description;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        static ClassDefinition Class(string name)
        {
            var path = $"{k_CreatureFolder}/Class_{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<ClassDefinition>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<ClassDefinition>();
                AssetDatabase.CreateAsset(asset, path);
            }

            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        static CreatureDefinition Creature(string id, string displayName, SpeciesDefinition species,
            ClassDefinition creatureClass, int maxHp, int maxAp, int speed)
        {
            var path = $"{k_CreatureFolder}/{id}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<CreatureDefinition>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<CreatureDefinition>();
                AssetDatabase.CreateAsset(asset, path);
            }

            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_Id").stringValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = displayName;
            serialized.FindProperty("m_Species").objectReferenceValue = species;
            serialized.FindProperty("m_Class").objectReferenceValue = creatureClass;
            serialized.FindProperty("m_MaxHp").intValue = maxHp;
            serialized.FindProperty("m_MaxAp").intValue = maxAp;
            serialized.FindProperty("m_Speed").intValue = speed;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }

        // ------------------------------------------------------------------ prefabs

        static GameObject BuildDraftPrefab(CreatureCatalog catalog)
        {
            var root = new GameObject("DraftState", typeof(NetworkObject), typeof(DraftState));

            var serialized = new SerializedObject(root.GetComponent<DraftState>());
            Assign(serialized, "m_Catalog", catalog);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, k_DraftPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Adds creature state, the ownership ring and its geometry to the existing unit.</summary>
        static void RebuildUnitPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_UnitPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"{k_UnitPrefabPath} is missing; run the movement setup first.");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(k_UnitPrefabPath);
            try
            {
                if (root.GetComponent<CreatureState>() == null)
                {
                    root.AddComponent<CreatureState>();
                }

                var ring = root.GetComponent<UnitOwnershipRing>() ?? root.AddComponent<UnitOwnershipRing>();

                var partyRing = FindOrCreateDisc(root.transform, "Party Ring", 1.5f, 0.012f);
                var accent = FindOrCreateDisc(root.transform, "Player Accent", 0.95f, 0.016f);

                var serialized = new SerializedObject(ring);
                Assign(serialized, "m_PartyRing", partyRing);
                Assign(serialized, "m_PlayerAccent", accent);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, k_UnitPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Renderer FindOrCreateDisc(Transform parent, string name, float diameter, float height)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = name;
            disc.transform.SetParent(parent, false);
            disc.transform.localScale = new Vector3(diameter, 0.01f, diameter);
            disc.transform.localPosition = new Vector3(0f, height, 0f);
            Object.DestroyImmediate(disc.GetComponent<Collider>());

            var renderer = disc.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = RingMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        static Material RingMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(k_RingMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(k_RingMaterialPath));
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            // Instancing matters here for the same reason it does on tiles: a property block opts
            // the renderer out of the SRP Batcher.
            var material = new Material(shader) { enableInstancing = true };
            AssetDatabase.CreateAsset(material, k_RingMaterialPath);
            return material;
        }

        static void RegisterNetworkPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(k_PrefabsListPath);
            if (list == null)
            {
                Debug.LogError($"Could not load {k_PrefabsListPath}.");
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

        // ------------------------------------------------------------------ scenes

        static void WireArenaScene()
        {
            var scene = EditorSceneManager.OpenScene(k_ArenaScene, OpenSceneMode.Single);

            var context = Object.FindAnyObjectByType<ArenaContext>();
            var pointer = Object.FindAnyObjectByType<HexPointer>();
            var units = Object.FindAnyObjectByType<UnitIndex>();
            var rigInput = Object.FindAnyObjectByType<CameraRigInput>();

            if (context == null || pointer == null || units == null)
            {
                Debug.LogError("Arena is missing its context, pointer or unit index; run the earlier setups first.");
                return;
            }

            var host = context.gameObject;

            var selection = host.GetComponent<CreatureSelection>() ?? host.AddComponent<CreatureSelection>();
            var boardSelect = host.GetComponent<BoardSelectionInput>() ?? host.AddComponent<BoardSelectionInput>();

            var boardSerialized = new SerializedObject(boardSelect);
            Assign(boardSerialized, "m_Pointer", pointer);
            Assign(boardSerialized, "m_Units", units);
            Assign(boardSerialized, "m_Selection", selection);
            boardSerialized.ApplyModifiedPropertiesWithoutUndo();

            var matchInput = Object.FindAnyObjectByType<MatchInput>();
            if (matchInput != null)
            {
                var inputSerialized = new SerializedObject(matchInput);
                Assign(inputSerialized, "m_Input", rigInput);
                Assign(inputSerialized, "m_Selection", selection);
                inputSerialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // HUD document, sharing the lobby's panel settings so both use one scaling baseline.
            var hudObject = GameObject.Find("Arena HUD")
                ?? new GameObject("Arena HUD", typeof(UIDocument), typeof(ArenaHudView));

            if (hudObject.GetComponent<ArenaHudView>() == null)
            {
                hudObject.AddComponent<ArenaHudView>();
            }

            var document = hudObject.GetComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(k_PanelSettingsPath);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_HudUxmlPath);

            var hudSerialized = new SerializedObject(hudObject.GetComponent<ArenaHudView>());
            Assign(hudSerialized, "m_Selection", selection);
            hudSerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void WireBootstrapScene(GameObject draftPrefab)
        {
            var scene = EditorSceneManager.OpenScene(k_BootstrapScene, OpenSceneMode.Single);

            var spawner = Object.FindAnyObjectByType<MatchSpawner>();
            if (spawner == null)
            {
                Debug.LogError($"No {nameof(MatchSpawner)} in {k_BootstrapScene}.");
                return;
            }

            var host = spawner.gameObject;
            var draftHost = host.GetComponent<DraftHost>() ?? host.AddComponent<DraftHost>();

            var serialized = new SerializedObject(draftHost);
            Assign(serialized, "m_DraftPrefab", draftPrefab);
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
