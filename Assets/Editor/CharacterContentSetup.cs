using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Creates a starter set of classes, weapons and armour, collects them into a catalog, and hands
    /// that catalog to the menu.
    ///
    /// Content, not code: everything written here is an ordinary asset a designer can edit
    /// afterwards. It exists so a fresh clone has something to build a character out of, not because
    /// the game needs these particular three classes.
    ///
    /// Safe to re-run. Existing assets are updated in place rather than duplicated, so re-running
    /// after adding a class keeps the ids and the edits already made to the others.
    /// </summary>
    static class CharacterContentSetup
    {
        const string k_Folder = "Assets/Settings/Characters";
        const string k_CatalogPath = k_Folder + "/ContentCatalog.asset";
        const string k_MenuScene = "Assets/Scenes/MainMenu.unity";

        [MenuItem("ClaudeCode/Set Up Character Content")]
        static void SetUpFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Run();
            }
        }

        /// <summary>Runs the whole step. Called directly by the master setup.</summary>
        internal static void Run()
        {
            var catalog = BuildContent();

            if (catalog != null)
            {
                WireMenu(catalog);
            }
        }

        static ContentCatalog BuildContent()
        {
            EnsureFolder();

            // Ids are hand-assigned and permanent: they are written into saved characters and cross
            // the network. Grouped by kind so a new weapon is obviously an 1x.
            var sword = Equipment(10, "Sword", EquipmentSlot.Weapon, 0, 0, 2, 0,
                "A soldier's blade. Reliable, and heavy enough to matter.");
            var greataxe = Equipment(11, "Greataxe", EquipmentSlot.Weapon, 0, -1, 3, 0,
                "Enormous. You will hit first only by accident.");
            var bow = Equipment(12, "Bow", EquipmentSlot.Weapon, 0, 1, 1, 0,
                "Keeps the fight at the distance you choose.");
            var dagger = Equipment(13, "Dagger", EquipmentSlot.Weapon, 0, 2, 1, 0,
                "Short reach, and you will be somewhere else before it is answered.");
            var staff = Equipment(14, "Staff", EquipmentSlot.Weapon, 0, 0, 1, 1,
                "Focuses what you draw from the pool.");

            var light = Equipment(20, "Light armour", EquipmentSlot.Armor, 1, 0, 0, 0,
                "Padding and leather. You will still be quick.");
            var medium = Equipment(21, "Medium armour", EquipmentSlot.Armor, 3, -1, 0, 0,
                "Mail. A fair trade, most days.");
            var heavy = Equipment(22, "Heavy armour", EquipmentSlot.Armor, 5, -2, 0, 0,
                "Plate. Very hard to hurt, and everyone acts before you.");

            var classes = new List<ClassAsset>
            {
                Class(1, "Warrior", 3, 1, 2, 2,
                    "Holds ground. Trades initiative for the health to be wrong once.",
                    new[] { sword, greataxe }),
                Class(2, "Ranger", 2, 3, 2, 1,
                    "Acts first and picks the fight. Cannot afford to be caught.",
                    new[] { bow, dagger }),
                Class(3, "Mystic", 2, 1, 1, 4,
                    "Deepest reserves on the field. What it does with them is up to the pool.",
                    new[] { staff, dagger })
            };

            var equipment = new List<EquipmentAsset>
            {
                sword, greataxe, bow, dagger, staff, light, medium, heavy
            };

            return Catalog(classes, equipment);
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            if (!AssetDatabase.IsValidFolder(k_Folder))
            {
                AssetDatabase.CreateFolder("Assets/Settings", "Characters");
            }
        }

        /// <summary>
        /// Loads the asset at a path or creates it, so re-running updates rather than duplicates.
        /// </summary>
        static T Upsert<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);

            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static EquipmentAsset Equipment(int id, string name, EquipmentSlot slot,
            int vitality, int speed, int power, int focus, string description)
        {
            var asset = Upsert<EquipmentAsset>($"{k_Folder}/{Sanitise(name)}.asset");
            var serialized = new SerializedObject(asset);

            serialized.FindProperty("m_Id").intValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.FindProperty("m_Description").stringValue = description;
            serialized.FindProperty("m_Slot").enumValueIndex = (int)slot;

            WriteStats(serialized.FindProperty("m_Modifiers"), vitality, speed, power, focus);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static ClassAsset Class(int id, string name, int vitality, int speed, int power, int focus,
            string description, IReadOnlyList<EquipmentAsset> weapons)
        {
            var asset = Upsert<ClassAsset>($"{k_Folder}/{Sanitise(name)}.asset");
            var serialized = new SerializedObject(asset);

            serialized.FindProperty("m_Id").intValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.FindProperty("m_Description").stringValue = description;

            WriteStats(serialized.FindProperty("m_Baseline"), vitality, speed, power, focus);
            WriteList(serialized.FindProperty("m_Weapons"), weapons);

            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static ContentCatalog Catalog(IReadOnlyList<ClassAsset> classes,
            IReadOnlyList<EquipmentAsset> equipment)
        {
            var asset = Upsert<ContentCatalog>(k_CatalogPath);
            var serialized = new SerializedObject(asset);

            WriteList(serialized.FindProperty("m_Classes"), classes);
            WriteList(serialized.FindProperty("m_Equipment"), equipment);

            // Four stats at a floor of one, plus eight to spend. Deliberately tight: a budget that
            // covers everything is not a choice.
            serialized.FindProperty("m_PointBudget").intValue = 8;
            serialized.FindProperty("m_MinPerStat").intValue = 1;
            serialized.FindProperty("m_MaxPerStat").intValue = 8;
            serialized.FindProperty("m_Level").intValue = 4;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            asset.Invalidate();
            EditorUtility.SetDirty(asset);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Character content ready: {classes.Count} classes, {equipment.Count} items, "
                + $"catalog at {k_CatalogPath}.");

            return asset;
        }

        static void WriteStats(SerializedProperty stats, int vitality, int speed, int power, int focus)
        {
            stats.FindPropertyRelative("Vitality").intValue = vitality;
            stats.FindPropertyRelative("Speed").intValue = speed;
            stats.FindPropertyRelative("Power").intValue = power;
            stats.FindPropertyRelative("Focus").intValue = focus;
        }

        static void WriteList<T>(SerializedProperty list, IReadOnlyList<T> items)
            where T : ScriptableObject
        {
            list.arraySize = items.Count;

            for (var i = 0; i < items.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
        }

        /// <summary>
        /// Hands the catalog to the menu.
        ///
        /// Selected by the document it drives rather than by "any MainMenuUI", for the same reason
        /// the menu rewire does: the scene holds more than one UIDocument and picking arbitrarily
        /// between them once already put the menu on the draft panel.
        /// </summary>
        static void WireMenu(ContentCatalog catalog)
        {
            var scene = EditorSceneManager.OpenScene(k_MenuScene, OpenSceneMode.Single);

            var menu = Object.FindAnyObjectByType<MainMenuUI>();

            if (menu == null)
            {
                Debug.LogError("No MainMenuUI in the menu scene; run 'Rewire Main Menu' first.");
                return;
            }

            var serialized = new SerializedObject(menu);
            serialized.FindProperty("m_Content").objectReferenceValue = catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("Menu wired to the content catalog.");
        }

        static string Sanitise(string name) => name.Replace(" ", string.Empty);
    }
}
