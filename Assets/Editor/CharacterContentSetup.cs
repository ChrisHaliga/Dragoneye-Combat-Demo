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
    /// Creates a starter set of species, classes, weapons and armour, collects them into a catalog,
    /// and hands that catalog to the menu.
    ///
    /// Content, not code: everything written here is an ordinary asset a designer can edit
    /// afterwards. It exists so a fresh clone has something to build a character out of, not because
    /// the game needs these particular seven classes.
    ///
    /// Safe to re-run. Existing assets are updated in place rather than duplicated, so re-running
    /// after adding a class keeps the ids and the edits already made to the others.
    /// </summary>
    static class CharacterContentSetup
    {
        const string k_Folder = "Assets/Settings/Characters";
        const string k_CatalogPath = k_Folder + "/ContentCatalog.asset";

        /// <summary>
        /// Where species already live.
        ///
        /// The twelve premade creatures reference the four species assets in this folder by id.
        /// Authoring them in place is what gives those creatures Take a Breath without re-pointing
        /// twelve assets at somewhere tidier.
        /// </summary>
        const string k_SpeciesFolder = "Assets/Settings/Creatures";

        const string k_MenuScene = "Assets/Scenes/MainMenu.unity";
        const string k_MatchPrefab = "Assets/NGO_Minimal_Setup/DraftState.prefab";

        /// <summary>Runs the whole step. Called directly by the master setup.</summary>
        internal static void Run()
        {
            var catalog = BuildContent();

            if (catalog == null)
            {
                return;
            }

            // The prefab first, then the menu: WireMenu leaves the menu scene open, and opening
            // another scene afterwards would discard that.
            WireMatchPrefab(catalog);
            WireMenu(catalog);
        }

        static ContentCatalog BuildContent()
        {
            EnsureFolder();

            // Skills first: species, classes and equipment all grant them by reference.
            //
            // Take a Breath is the one every species has. It is authored rather than written into
            // the rules because it is content -- a designer authoring something that cannot catch
            // its breath should be able to leave it off.
            var breath = Skill(90, "Take a Breath", Element.Arcana, ap: 1, elementCost: 0, range: 0,
                SkillTarget.Self, SkillEffectKind.ReturnElement, 1,
                "Recover the element you spent longest ago. A point for a breath.");

            // The self-directed ones are the point of the rest of the set. Without something worth
            // spending a turn on that is not an attack, every turn is the same turn.
            var strike = Skill(100, "Strike", Element.Pyro, ap: 1, elementCost: 1, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 6,
                "A committed swing. Cheap, and it asks its question in Pyro.");
            var cleave = Skill(101, "Cleave", Element.Geo, ap: 2, elementCost: 2, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 11,
                "Slower and dearer, and it ends arguments.");
            var loose = Skill(102, "Loose", Element.Aero, ap: 1, elementCost: 1, range: 4,
                SkillTarget.Creature, SkillEffectKind.Damage, 5,
                "From wherever you are standing, which is the whole idea.");
            var jab = Skill(103, "Jab", Element.Aero, ap: 1, elementCost: 0, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 3,
                "Costs nothing from the pool. What you use when the pool is what you are short of.");
            var ember = Skill(104, "Ember", Element.Pyro, ap: 2, elementCost: 2, range: 3,
                SkillTarget.Creature, SkillEffectKind.Damage, 9,
                "Reaches, and it is expensive in exactly the element it is made of.");
            var smite = Skill(105, "Smite", Element.Lux, ap: 2, elementCost: 1, range: 2,
                SkillTarget.Creature, SkillEffectKind.Damage, 8,
                "Light, at a distance, for a price only the devout tend to be holding.");
            var drain = Skill(106, "Drain", Element.Nyx, ap: 2, elementCost: 1, range: 2,
                SkillTarget.Creature, SkillEffectKind.Damage, 7,
                "Nyx answers questions nobody wanted asked.");

            var recover = Skill(110, "Recover", Element.Hydro, ap: 1, elementCost: 1, range: 0,
                SkillTarget.Self, SkillEffectKind.Heal, 6,
                "Spend a turn staying alive. Hydro, so it competes with nothing you attack with.");
            var meditate = Skill(111, "Meditate", Element.Aero, ap: 1, elementCost: 0, range: 0,
                SkillTarget.Self, SkillEffectKind.RestoreAp, 2,
                "Trade a point now for two later. Only worth it if you have somewhere to spend them.");
            var focus = Skill(112, "Focus", Element.Arcana, ap: 2, elementCost: 0, range: 0,
                SkillTarget.Self, SkillEffectKind.ReturnElement, 2,
                "A longer breath. Two elements back, at twice the price of one.");

            // Ids are hand-assigned and permanent: they are written into saved characters and cross
            // the network. Grouped by kind so a new weapon is obviously an 1x.
            var sword = Equipment(10, "Sword", EquipmentSlot.Weapon, Attr(strength: 1),
                "A soldier's blade. Reliable, and heavy enough to matter.", strike);
            var greataxe = Equipment(11, "Greataxe", EquipmentSlot.Weapon,
                Attr(strength: 2, dexterity: -1),
                "Enormous. You will hit first only by accident.", strike, cleave);
            var bow = Equipment(12, "Bow", EquipmentSlot.Weapon, Attr(skill: 1),
                "Keeps the fight at the distance you choose.", loose);
            var dagger = Equipment(13, "Dagger", EquipmentSlot.Weapon, Attr(dexterity: 1),
                "Short reach, and you will be somewhere else before it is answered.", jab);
            var staff = Equipment(14, "Staff", EquipmentSlot.Weapon, Attr(willpower: 1),
                "Focuses what you draw from the pool.", ember);
            var mace = Equipment(15, "Mace", EquipmentSlot.Weapon, Attr(strength: 1, willpower: 1),
                "Blunt and devout. The two go together more often than anyone admits.", smite);

            var shield = Equipment(30, "Shield", EquipmentSlot.Offhand, Attr(toughness: 1),
                "Advantage when you are the one being asked a question.",
                new SkillAsset[0], new[] { Passive.DefendAdvantage });

            var light = Armour(20, "Light armour", Attr(toughness: 1), ArmourClass.Light,
                "Padding and leather. You will still be quick.");
            var medium = Armour(21, "Medium armour", Attr(toughness: 2), ArmourClass.Medium,
                "Mail. A fair trade, most days.");
            var heavy = Armour(22, "Heavy armour", Attr(toughness: 3), ArmourClass.Heavy,
                "Plate. Very hard to hurt, and everyone else has already acted.");

            var species = new List<SpeciesDefinition>
            {
                Species(1, "Human", Attr(),
                    "Adaptable, and the only species with nothing to apologise for.", breath),
                Species(2, "Beast", Attr(dexterity: 1, willpower: -1),
                    "Quick, and disinclined to argue about it.", breath),
                Species(3, "Giantkin", Attr(strength: 1, toughness: 1, dexterity: -1),
                    "Slow to arrive and hard to remove.", breath),
                Species(4, "Goblinoid", Attr(dexterity: 1, toughness: -1),
                    "Small, fast, and entirely aware of both.", breath)
            };

            // The seven. Baselines are deliberately flat: a class is what it may carry and what it
            // knows, and giving each one a stat bonus as well would decide the point buy for the
            // player before they had spent anything.
            var classes = new List<ClassAsset>
            {
                Class(1, "Guardian", "Stands where the line would otherwise break.",
                    new[] { sword, mace }, new[] { recover }),
                Class(2, "Rogue", "Picks the moment, and is elsewhere by the time it lands.",
                    new[] { dagger, bow }, new[] { meditate }),
                Class(3, "Fighter", "No tricks. Enough of that becomes its own trick.",
                    new[] { sword, greataxe }, new[] { meditate }),
                Class(4, "Hunter", "Decides the range the fight happens at.",
                    new[] { bow, dagger }, new[] { meditate }),
                Class(5, "Priest", "Keeps others standing, and answers in Lux when it must.",
                    new[] { mace, staff }, new[] { recover, smite }),
                Class(6, "Apostate", "Trained devout, and no longer.",
                    new[] { staff, dagger }, new[] { drain, recover }),
                Class(7, "Elementalist",
                    "Deepest reserves on the field. What it does with them is up to the pool.",
                    new[] { staff }, new[] { focus, ember })
            };

            var equipment = new List<EquipmentAsset>
            {
                sword, greataxe, bow, dagger, staff, mace, light, medium, heavy, shield
            };

            var skills = new List<SkillAsset>
            {
                breath, strike, cleave, loose, jab, ember, smite, drain, recover, meditate, focus
            };

            return Catalog(species, classes, equipment, skills);
        }

        /// <summary>
        /// An attribute block by name, so an authored line says which attribute it moves.
        ///
        /// Seven positional integers at every call site is how a Strength bonus quietly becomes a
        /// Skill bonus.
        /// </summary>
        static AttributeValues Attr(int toughness = 0, int dexterity = 0, int strength = 0,
            int skill = 0, int vitality = 0, int willpower = 0, int endurance = 0) =>
            new AttributeValues
            {
                Toughness = toughness,
                Dexterity = dexterity,
                Strength = strength,
                Skill = skill,
                Vitality = vitality,
                Willpower = willpower,
                Endurance = endurance
            };

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

            if (!AssetDatabase.IsValidFolder(k_SpeciesFolder))
            {
                AssetDatabase.CreateFolder("Assets/Settings", "Creatures");
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
            serialized.FindProperty("m_Element").intValue = (int)element;
            serialized.FindProperty("m_ApCost").intValue = ap;
            serialized.FindProperty("m_ElementCost").intValue = elementCost;
            serialized.FindProperty("m_Range").intValue = range;
            serialized.FindProperty("m_Target").intValue = (int)target;
            serialized.FindProperty("m_Effect").intValue = (int)effect;
            serialized.FindProperty("m_Amount").intValue = amount;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static EquipmentAsset Equipment(int id, string name, EquipmentSlot slot,
            AttributeValues modifiers, string description, params SkillAsset[] skills) =>
            Equipment(id, name, slot, modifiers, description, skills, new Passive[0]);

        static EquipmentAsset Armour(int id, string name, AttributeValues modifiers,
            ArmourClass armour, string description) =>
            Equipment(id, name, EquipmentSlot.Armor, modifiers, description, new SkillAsset[0],
                new Passive[0], armour);

        static EquipmentAsset Equipment(int id, string name, EquipmentSlot slot,
            AttributeValues modifiers, string description, IReadOnlyList<SkillAsset> skills,
            Passive[] passives, ArmourClass armour = ArmourClass.None)
        {
            var asset = Upsert<EquipmentAsset>($"{k_Folder}/{Sanitise(name)}.asset");
            var serialized = new SerializedObject(asset);

            serialized.FindProperty("m_Id").intValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.FindProperty("m_Description").stringValue = description;
            serialized.FindProperty("m_Slot").intValue = (int)slot;
            serialized.FindProperty("m_Armour").intValue = (int)armour;

            WriteAttributes(serialized.FindProperty("m_Modifiers"), modifiers);
            WriteList(serialized.FindProperty("m_Skills"), skills);
            WriteEnums(serialized.FindProperty("m_Passives"), passives);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>
        /// Authors a species where the premade creatures already look for it.
        ///
        /// Named <c>Species_X</c> rather than <c>X</c> because that is how the four already on disk
        /// are named, and matching the path is what makes this an update rather than a fifth copy.
        /// </summary>
        static SpeciesDefinition Species(int id, string name, AttributeValues baseline,
            string description, params SkillAsset[] skills)
        {
            var asset = Upsert<SpeciesDefinition>($"{k_SpeciesFolder}/Species_{Sanitise(name)}.asset");
            var serialized = new SerializedObject(asset);

            serialized.FindProperty("m_Id").intValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.FindProperty("m_Description").stringValue = description;

            WriteAttributes(serialized.FindProperty("m_Baseline"), baseline);
            WriteList(serialized.FindProperty("m_Skills"), skills);

            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static ClassAsset Class(int id, string name, string description,
            IReadOnlyList<EquipmentAsset> weapons, IReadOnlyList<SkillAsset> skills)
        {
            var asset = Upsert<ClassAsset>($"{k_Folder}/{Sanitise(name)}.asset");
            var serialized = new SerializedObject(asset);

            serialized.FindProperty("m_Id").intValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.FindProperty("m_Description").stringValue = description;

            WriteAttributes(serialized.FindProperty("m_Baseline"), Attr());
            WriteList(serialized.FindProperty("m_Weapons"), weapons);
            WriteList(serialized.FindProperty("m_Skills"), skills);

            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static ContentCatalog Catalog(IReadOnlyList<SpeciesDefinition> species,
            IReadOnlyList<ClassAsset> classes, IReadOnlyList<EquipmentAsset> equipment,
            IReadOnlyList<SkillAsset> skills)
        {
            var asset = Upsert<ContentCatalog>(k_CatalogPath);
            var serialized = new SerializedObject(asset);

            WriteList(serialized.FindProperty("m_Species"), species);
            WriteList(serialized.FindProperty("m_Classes"), classes);
            WriteList(serialized.FindProperty("m_Equipment"), equipment);
            WriteList(serialized.FindProperty("m_Skills"), skills);

            // Twenty points, every attribute starting at one and each step costing the attribute's
            // current value. Deliberately tight: a budget that covers everything is not a choice.
            serialized.FindProperty("m_PointBudget").intValue = 20;
            serialized.FindProperty("m_MaxPerAttribute").intValue = 8;
            serialized.FindProperty("m_Level").intValue = 4;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            asset.Invalidate();
            EditorUtility.SetDirty(asset);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Character content ready: {species.Count} species, {classes.Count} classes, "
                + $"{equipment.Count} items, {skills.Count} skills, catalog at {k_CatalogPath}.");

            return asset;
        }

        static void WriteAttributes(SerializedProperty block, AttributeValues values)
        {
            block.FindPropertyRelative("Toughness").intValue = values.Toughness;
            block.FindPropertyRelative("Dexterity").intValue = values.Dexterity;
            block.FindPropertyRelative("Strength").intValue = values.Strength;
            block.FindPropertyRelative("Skill").intValue = values.Skill;
            block.FindPropertyRelative("Vitality").intValue = values.Vitality;
            block.FindPropertyRelative("Willpower").intValue = values.Willpower;
            block.FindPropertyRelative("Endurance").intValue = values.Endurance;
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

            if (!Assign(menu, "m_Content", catalog))
            {
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("Menu wired to the content catalog.");
        }

        /// <summary>
        /// Hands the catalog to the match object, which owns it for the whole match.
        ///
        /// The prefab rather than the arena: it is spawned when the server starts and lives until
        /// the match ends, so the skill seam is never briefly empty between the lobby and the board.
        /// </summary>
        static void WireMatchPrefab(ContentCatalog catalog)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_MatchPrefab);

            if (prefab == null)
            {
                Debug.LogError($"No prefab at {k_MatchPrefab}; cannot hand over the catalog.");
                return;
            }

            var characters = prefab.GetComponent<Dragoneye.Game.PlayerCharacters>()
                ?? prefab.AddComponent<Dragoneye.Game.PlayerCharacters>();

            if (!Assign(characters, "m_Content", catalog))
            {
                return;
            }

            PrefabUtility.SavePrefabAsset(prefab);
            Debug.Log("Match prefab wired to the content catalog.");
        }

        /// <summary>
        /// Writes a serialised field by name, reporting a missing one rather than throwing.
        ///
        /// FindProperty returns null for a field that has been renamed or removed, and dereferencing
        /// that killed this whole step once -- taking the menu wiring with it, so the only visible
        /// symptom was a menu with no catalog. A setup step should report what it could not do and
        /// let the rest run.
        /// </summary>
        static bool Assign(Object target, string path, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(path);

            if (property == null)
            {
                Debug.LogError($"{target.GetType().Name} has no field '{path}'; "
                    + "it was renamed or removed.", target);
                return false;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
            return true;
        }

        static string Sanitise(string name) => name.Replace(" ", string.Empty);
    }
}
