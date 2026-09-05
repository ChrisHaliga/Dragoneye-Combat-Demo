using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Game;
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
                "Slower and dearer, and it ends arguments.", level: 3);
            var loose = Skill(102, "Loose", Element.Aero, ap: 1, elementCost: 1, range: 4,
                SkillTarget.Creature, SkillEffectKind.Damage, 5,
                "From wherever you are standing, which is the whole idea.");
            var jab = Skill(103, "Jab", Element.Aero, ap: 1, elementCost: 0, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 3,
                "Costs nothing from the pool. What you use when the pool is what you are short of.");
            var ember = Skill(104, "Ember", Element.Pyro, ap: 2, elementCost: 2, range: 3,
                SkillTarget.Creature, SkillEffectKind.Damage, 9,
                "Reaches, and it is expensive in exactly the element it is made of.", level: 2);
            var smite = Skill(105, "Smite", Element.Lux, ap: 2, elementCost: 1, range: 2,
                SkillTarget.Creature, SkillEffectKind.Damage, 8,
                "Light, at a distance, for a price only the devout tend to be holding.", level: 2);
            var drain = Skill(106, "Drain", Element.Nyx, ap: 2, elementCost: 1, range: 2,
                SkillTarget.Creature, SkillEffectKind.Damage, 7,
                "Nyx answers questions nobody wanted asked.", level: 2);

            var recover = Skill(110, "Recover", Element.Hydro, ap: 1, elementCost: 1, range: 0,
                SkillTarget.Self, SkillEffectKind.Heal, 6,
                "Spend a turn staying alive. Hydro, so it competes with nothing you attack with.");
            var meditate = Skill(111, "Meditate", Element.Aero, ap: 1, elementCost: 0, range: 0,
                SkillTarget.Self, SkillEffectKind.RestoreAp, 2,
                "Trade a point now for two later. Only worth it if you have somewhere to spend them.");
            var focus = Skill(112, "Focus", Element.Arcana, ap: 2, elementCost: 0, range: 0,
                SkillTarget.Self, SkillEffectKind.ReturnElement, 2,
                "A longer breath. Two elements back, at twice the price of one.", level: 3);

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

            // Three off every blow, in the offhand, so carrying one costs no speed. That is the
            // whole of what a shield does now -- it used to grant "advantage when defending", which
            // was a flag no rule ever read.
            var shield = Equipment(30, "Shield", EquipmentSlot.Offhand, Attr(toughness: 1),
                "Three damage off anything that reaches you, and it costs you no speed.",
                new SkillAsset[0], ArmourClass.None, damageReduction: 3);

            var light = Armour(20, "Light armour", Attr(toughness: 1), ArmourClass.Light,
                "Padding and leather. One damage off every blow, and you will still be quick.");
            var medium = Armour(21, "Medium armour", Attr(toughness: 2), ArmourClass.Medium,
                "Mail. Two damage off every blow, at two speed. A fair trade, most days.");
            var heavy = Armour(22, "Heavy armour", Attr(toughness: 3), ArmourClass.Heavy,
                "Plate. Four damage off every blow, and everyone else has already acted.");

            var species = new List<SpeciesDefinition>
            {
                // Four action points each. The field exists so that something quick or something
                // ponderous does not need a rule of its own; nothing authored today differs yet.
                Species(1, "Human", Attr(),
                    "Adaptable, and the only species with nothing to apologise for.", 4, breath),
                Species(2, "Beast", Attr(dexterity: 1, willpower: -1),
                    "Quick, and disinclined to argue about it.", 4, breath),
                Species(3, "Giantkin", Attr(strength: 1, toughness: 1, dexterity: -1),
                    "Slow to arrive and hard to remove.", 4, breath),
                Species(4, "Goblinoid", Attr(dexterity: 1, toughness: -1),
                    "Small, fast, and entirely aware of both.", 4, breath)
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

            // The premades were authored before skills existed, back when every creature had a
            // generic attack. They have carried empty skill lists and empty pools ever since, which
            // is why they walked up to a hero and stood there: the only thing they knew was Take a
            // Breath, and with nothing spent there was nothing to take back.
            Creatures(strike, cleave, loose, jab, smite, recover, breath);

            return Catalog(species, classes, equipment, skills);
        }

        /// <summary>
        /// Gives the premade creatures something to do, and a pool to do it with.
        ///
        /// Only the four fields that make a creature act are written -- level, pool, skills and what
        /// it buys as it levels. Health, speed, species and class stay as they were authored, so
        /// re-running this does not quietly undo somebody's tuning.
        ///
        /// Pools cost exactly the creature's level, the same budget a player spends. Levels are what
        /// the creature reads as: a recruit is a level one, a sergeant is a level three, and the
        /// host can move either on the board.
        /// </summary>
        static void Creatures(SkillAsset strike, SkillAsset cleave, SkillAsset loose, SkillAsset jab,
            SkillAsset smite, SkillAsset recover, SkillAsset breath)
        {
            // Rank and file: one cheap skill, one element, nothing clever.
            Creature("guard-recruit", 1, Pool(pyro: 1), Element.Pyro, strike, cleave);
            Creature("monster-goblin", 1, Pool(aero: 1), Element.Aero, jab, cleave);
            Creature("monster-wolf", 1, Pool(aero: 1), Element.Aero, jab);

            // Skirmishers: reach, and enough pool to use it twice.
            Creature("bandit-scout", 2, Pool(aero: 2), Element.Aero, loose);
            Creature("guard-archer", 2, Pool(aero: 2), Element.Aero, loose);
            Creature("hero-ranger", 2, Pool(aero: 2), Element.Aero, loose, jab);
            Creature("bandit-cutpurse", 2, Pool(aero: 2), Element.Aero, jab, loose);

            // The heavies. Cleave wants level three, so this is the first rank that has it.
            Creature("bandit-brute", 3, Pool(geo: 1, pyro: 2), Element.Geo, strike, cleave);
            Creature("guard-sergeant", 3, Pool(geo: 1, pyro: 2), Element.Pyro, strike, cleave);
            Creature("monster-ogre", 3, Pool(geo: 2, pyro: 1), Element.Geo, strike, cleave);
            Creature("hero-knight", 3, Pool(hydro: 1, pyro: 2), Element.Pyro, strike, cleave, recover);

            // One Lux is two points, which is most of a level-three budget. That is the trade.
            Creature("hero-cleric", 3, Pool(hydro: 1, lux: 1), Element.Lux, smite, recover);
        }

        /// <summary>
        /// Writes the four fields that decide what a creature does.
        ///
        /// Take a Breath is not in these lists: every species already grants it, and authoring it
        /// again here would be the same skill from two sources pretending to be two skills.
        /// </summary>
        static void Creature(string id, int level, ElementValues pool, Element buys,
            params SkillAsset[] skills)
        {
            var path = $"{k_SpeciesFolder}/{id}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<CreatureDefinition>(path);

            if (asset == null)
            {
                Debug.LogWarning($"No creature at {path}; it was not given skills.");
                return;
            }

            var serialized = new SerializedObject(asset);

            serialized.FindProperty("m_Level").intValue = level;
            WriteElements(serialized.FindProperty("m_StartingPool"), pool);
            WriteList(serialized.FindProperty("m_Skills"), skills);

            // What it spends the budget on if the host fields it above its authored level. Three
            // deep, which is further than anybody is likely to push a bandit.
            var picks = serialized.FindProperty("m_LevelUpPicks");
            picks.arraySize = 3;

            for (var i = 0; i < 3; i++)
            {
                picks.GetArrayElementAtIndex(i).intValue = (int)buys;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>An element spread by name, for the same reason <see cref="Attr"/> exists.</summary>
        static ElementValues Pool(int geo = 0, int hydro = 0, int pyro = 0, int aero = 0,
            int lux = 0, int nyx = 0, int arcana = 0) =>
            new ElementValues
            {
                Geo = geo,
                Hydro = hydro,
                Pyro = pyro,
                Aero = aero,
                Lux = lux,
                Nyx = nyx,
                Arcana = arcana
            };

        static void WriteElements(SerializedProperty block, ElementValues values)
        {
            block.FindPropertyRelative("Geo").intValue = values.Geo;
            block.FindPropertyRelative("Hydro").intValue = values.Hydro;
            block.FindPropertyRelative("Pyro").intValue = values.Pyro;
            block.FindPropertyRelative("Aero").intValue = values.Aero;
            block.FindPropertyRelative("Lux").intValue = values.Lux;
            block.FindPropertyRelative("Nyx").intValue = values.Nyx;
            block.FindPropertyRelative("Arcana").intValue = values.Arcana;
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
            int range, SkillTarget target, SkillEffectKind effect, int amount, string description,
            int level = 1)
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
            serialized.FindProperty("m_LevelRequired").intValue = level;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static EquipmentAsset Equipment(int id, string name, EquipmentSlot slot,
            AttributeValues modifiers, string description, params SkillAsset[] skills) =>
            Equipment(id, name, slot, modifiers, description, skills, ArmourClass.None);

        /// <summary>
        /// Armour carries no reduction of its own: what a suit stops comes from its class, so that
        /// "medium stops two" is one rule rather than a number repeated on every suit.
        /// </summary>
        static EquipmentAsset Armour(int id, string name, AttributeValues modifiers,
            ArmourClass armour, string description) =>
            Equipment(id, name, EquipmentSlot.Armor, modifiers, description, new SkillAsset[0],
                armour);

        static EquipmentAsset Equipment(int id, string name, EquipmentSlot slot,
            AttributeValues modifiers, string description, IReadOnlyList<SkillAsset> skills,
            ArmourClass armour = ArmourClass.None, int damageReduction = 0)
        {
            var asset = Upsert<EquipmentAsset>($"{k_Folder}/{Sanitise(name)}.asset");
            var serialized = new SerializedObject(asset);

            serialized.FindProperty("m_Id").intValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.FindProperty("m_Description").stringValue = description;
            serialized.FindProperty("m_Slot").intValue = (int)slot;
            serialized.FindProperty("m_Armour").intValue = (int)armour;

            serialized.FindProperty("m_DamageReduction").intValue = damageReduction;

            WriteAttributes(serialized.FindProperty("m_Modifiers"), modifiers);
            WriteList(serialized.FindProperty("m_Skills"), skills);
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
            string description, int baseAp, params SkillAsset[] skills)
        {
            var asset = Upsert<SpeciesDefinition>($"{k_SpeciesFolder}/Species_{Sanitise(name)}.asset");
            var serialized = new SerializedObject(asset);

            serialized.FindProperty("m_Id").intValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.FindProperty("m_Description").stringValue = description;
            serialized.FindProperty("m_BaseAp").intValue = baseAp;

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
            serialized.FindProperty("m_PointBudget").intValue = 27;
            serialized.FindProperty("m_MaxPerAttribute").intValue = 8;
            serialized.FindProperty("m_StartingLevel").intValue = 1;

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
