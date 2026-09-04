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
        const string k_ArenaScene = "Assets/Scenes/Arena.unity";

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

            if (catalog == null)
            {
                return;
            }

            // The arena first, then the menu: WireMenu leaves the menu scene open, and reopening it
            // to do the arena afterwards would discard that.
            WireArena(catalog);
            WireMenu(catalog);
        }

        static ContentCatalog BuildContent()
        {
            EnsureFolder();

            // Skills first: classes and equipment grant them by reference.
            //
            // The two self-directed ones are the point of the set. Recover and Meditate are what
            // make AP a choice rather than a countdown to attacking -- without something worth
            // spending a turn on that is not an attack, every turn is the same turn.
            var strike = Skill(100, "Strike", Element.Fire, ap: 1, elementCost: 1, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 6,
                "A committed swing. Cheap, and it asks a question in Fire.");
            var cleave = Skill(101, "Cleave", Element.Earth, ap: 2, elementCost: 2, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 11,
                "Slower and dearer, and it ends arguments.");
            var loose = Skill(102, "Loose", Element.Air, ap: 1, elementCost: 1, range: 4,
                SkillTarget.Creature, SkillEffectKind.Damage, 5,
                "From wherever you are standing, which is the whole idea.");
            var jab = Skill(103, "Jab", Element.Air, ap: 1, elementCost: 0, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 3,
                "Costs nothing from the pool. What you use when the pool is what you are short of.");
            var ember = Skill(104, "Ember", Element.Fire, ap: 2, elementCost: 2, range: 3,
                SkillTarget.Creature, SkillEffectKind.Damage, 9,
                "Reaches, and it is expensive in exactly the element it is made of.");

            var recover = Skill(110, "Recover", Element.Water, ap: 1, elementCost: 1, range: 0,
                SkillTarget.Self, SkillEffectKind.Heal, 6,
                "Spend a turn staying alive. Water, so it competes with nothing you attack with.");
            var meditate = Skill(111, "Meditate", Element.Air, ap: 1, elementCost: 0, range: 0,
                SkillTarget.Self, SkillEffectKind.RestoreAp, 2,
                "Trade a point now for two later. Only worth it if you have somewhere to spend them.");

            // Ids are hand-assigned and permanent: they are written into saved characters and cross
            // the network. Grouped by kind so a new weapon is obviously an 1x.
            var sword = Equipment(10, "Sword", EquipmentSlot.Weapon, 0, 0, 2, 0,
                "A soldier's blade. Reliable, and heavy enough to matter.", strike);
            var greataxe = Equipment(11, "Greataxe", EquipmentSlot.Weapon, 0, -1, 3, 0,
                "Enormous. You will hit first only by accident.", strike, cleave);
            var bow = Equipment(12, "Bow", EquipmentSlot.Weapon, 0, 1, 1, 0,
                "Keeps the fight at the distance you choose.", loose);
            var dagger = Equipment(13, "Dagger", EquipmentSlot.Weapon, 0, 2, 1, 0,
                "Short reach, and you will be somewhere else before it is answered.", jab);
            var staff = Equipment(14, "Staff", EquipmentSlot.Weapon, 0, 0, 1, 1,
                "Focuses what you draw from the pool.", ember);

            var shield = Equipment(30, "Shield", EquipmentSlot.Offhand, 1, 0, 0, 0,
                "Advantage when you are the one being asked a question.",
                new SkillAsset[0], Passive.DefendAdvantage);

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
                    new[] { sword, greataxe }, new[] { recover }),
                Class(2, "Ranger", 2, 3, 2, 1,
                    "Acts first and picks the fight. Cannot afford to be caught.",
                    new[] { bow, dagger }, new[] { meditate }),
                Class(3, "Mystic", 2, 1, 1, 4,
                    "Deepest reserves on the field. What it does with them is up to the pool.",
                    new[] { staff, dagger }, new[] { recover, meditate })
            };

            var equipment = new List<EquipmentAsset>
            {
                sword, greataxe, bow, dagger, staff, light, medium, heavy, shield
            };

            var skills = new List<SkillAsset>
            {
                strike, cleave, loose, jab, ember, recover, meditate
            };

            return Catalog(classes, equipment, skills);
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

        static SkillAsset Skill(int id, string name, Element element, int ap, int elementCost,
            int range, SkillTarget target, SkillEffectKind effect, int amount, string description)
        {
            var asset = Upsert<SkillAsset>($"{k_Folder}/Skill{Sanitise(name)}.asset");
            var serialized = new SerializedObject(asset);

            serialized.FindProperty("m_Id").intValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.FindProperty("m_Description").stringValue = description;
            serialized.FindProperty("m_Element").enumValueIndex = (int)element;
            serialized.FindProperty("m_ApCost").intValue = ap;
            serialized.FindProperty("m_ElementCost").intValue = elementCost;
            serialized.FindProperty("m_Range").intValue = range;
            serialized.FindProperty("m_Target").enumValueIndex = (int)target;
            serialized.FindProperty("m_Effect").enumValueIndex = (int)effect;
            serialized.FindProperty("m_Amount").intValue = amount;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static EquipmentAsset Equipment(int id, string name, EquipmentSlot slot,
            int vitality, int speed, int power, int focus, string description,
            params SkillAsset[] skills) =>
            Equipment(id, name, slot, vitality, speed, power, focus, description, skills,
                new Passive[0]);

        static EquipmentAsset Equipment(int id, string name, EquipmentSlot slot,
            int vitality, int speed, int power, int focus, string description,
            IReadOnlyList<SkillAsset> skills, params Passive[] passives)
        {
            var asset = Upsert<EquipmentAsset>($"{k_Folder}/{Sanitise(name)}.asset");
            var serialized = new SerializedObject(asset);

            serialized.FindProperty("m_Id").intValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.FindProperty("m_Description").stringValue = description;
            serialized.FindProperty("m_Slot").enumValueIndex = (int)slot;

            WriteStats(serialized.FindProperty("m_Modifiers"), vitality, speed, power, focus);
            WriteList(serialized.FindProperty("m_Skills"), skills);
            WriteEnums(serialized.FindProperty("m_Passives"), passives);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static ClassAsset Class(int id, string name, int vitality, int speed, int power, int focus,
            string description, IReadOnlyList<EquipmentAsset> weapons, IReadOnlyList<SkillAsset> skills)
        {
            var asset = Upsert<ClassAsset>($"{k_Folder}/{Sanitise(name)}.asset");
            var serialized = new SerializedObject(asset);

            serialized.FindProperty("m_Id").intValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.FindProperty("m_Description").stringValue = description;

            WriteStats(serialized.FindProperty("m_Baseline"), vitality, speed, power, focus);
            WriteList(serialized.FindProperty("m_Weapons"), weapons);
            WriteList(serialized.FindProperty("m_Skills"), skills);

            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static ContentCatalog Catalog(IReadOnlyList<ClassAsset> classes,
            IReadOnlyList<EquipmentAsset> equipment, IReadOnlyList<SkillAsset> skills)
        {
            var asset = Upsert<ContentCatalog>(k_CatalogPath);
            var serialized = new SerializedObject(asset);

            WriteList(serialized.FindProperty("m_Classes"), classes);
            WriteList(serialized.FindProperty("m_Equipment"), equipment);
            WriteList(serialized.FindProperty("m_Skills"), skills);

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
                + $"{skills.Count} skills, catalog at {k_CatalogPath}.");

            return asset;
        }

        static void WriteStats(SerializedProperty stats, int vitality, int speed, int power, int focus)
        {
            stats.FindPropertyRelative("Vitality").intValue = vitality;
            stats.FindPropertyRelative("Speed").intValue = speed;
            stats.FindPropertyRelative("Power").intValue = power;
            stats.FindPropertyRelative("Focus").intValue = focus;
        }

        /// <summary>
        /// Writes enum values by their underlying number.
        ///
        /// Not <c>enumValueIndex</c>, which is the position in the declaration rather than the
        /// value. They agree only for enums numbered from zero with no gaps -- <see cref="Passive"/>
        /// starts at one, so writing the index there would store the wrong passive or none at all.
        /// </summary>
        static void WriteEnums(SerializedProperty list, IReadOnlyList<Passive> values)
        {
            list.arraySize = values.Count;

            for (var i = 0; i < values.Count; i++)
            {
                list.GetArrayElementAtIndex(i).intValue = (int)values[i];
            }
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

        /// <summary>
        /// Hands the catalog to the arena, which owns the skill seam while a match is loaded.
        /// </summary>
        static void WireArena(ContentCatalog catalog)
        {
            var scene = EditorSceneManager.OpenScene(k_ArenaScene, OpenSceneMode.Single);
            var context = Object.FindAnyObjectByType<Dragoneye.Game.ArenaContext>();

            if (context == null)
            {
                Debug.LogError("No ArenaContext in the arena; run the arena rewire first.");
                return;
            }

            var serialized = new SerializedObject(context);
            serialized.FindProperty("m_Content").objectReferenceValue = catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("Arena wired to the content catalog.");
        }

        static string Sanitise(string name) => name.Replace(" ", string.Empty);
    }
}
