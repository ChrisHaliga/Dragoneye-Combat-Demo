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

            // ---------- weapon skills ----------
            //
            // These scale: the number is the base, and the fighter's Strength or Dexterity is
            // added on top. Heavy things scale with Strength, quick ones with Dexterity, so a bow
            // in a strongman's hands is still a bow. Anything with reach past a tile rolls to hit,
            // with a chance that falls with distance -- see the accuracy and falloff on each.
            var strike = Skill(100, "Strike", Element.Pyro, ap: 1, elementCost: 1, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 4,
                "A committed swing. Cheap, and it asks its question in Pyro.",
                scaling: Attribute.Strength);
            var cleave = Skill(101, "Cleave", Element.Geo, ap: 2, elementCost: 2, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 8,
                "Slower and dearer, and it ends arguments.", level: 3,
                scaling: Attribute.Strength);
            var loose = Skill(102, "Loose", Element.Aero, ap: 1, elementCost: 1, range: 4,
                SkillTarget.Creature, SkillEffectKind.Damage, 3,
                "An arrow, from wherever you are standing. Surer the closer they are.",
                scaling: Attribute.Dexterity, accuracy: 90, falloff: 8);
            var jab = Skill(103, "Jab", Element.Aero, ap: 1, elementCost: 1, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 2,
                "Quick, and it asks its question in Aero. What you throw when Aero is what you "
                + "are holding.", scaling: Attribute.Dexterity);
            var ember = Skill(104, "Ember", Element.Pyro, ap: 1, elementCost: 1, range: 2,
                SkillTarget.Creature, SkillEffectKind.Damage, 4,
                "A coal flicked off the end of the staff. Reaches, a little.",
                accuracy: 95, falloff: 10);
            var smite = Skill(105, "Smite", Element.Lux, ap: 2, elementCost: 1, range: 2,
                SkillTarget.Creature, SkillEffectKind.Damage, 8,
                "Light, at a distance, for a price only the devout tend to be holding.", level: 2);
            var drain = Skill(106, "Drain", Element.Nyx, ap: 2, elementCost: 1, range: 2,
                SkillTarget.Creature, SkillEffectKind.Damage, 7,
                "Nyx answers questions nobody wanted asked.", level: 2);

            // ---------- what everybody can learn ----------
            var recover = Skill(110, "Recover", Element.Hydro, ap: 1, elementCost: 1, range: 0,
                SkillTarget.Self, SkillEffectKind.Heal, 6,
                "Spend a turn staying alive. Hydro, so it competes with nothing you attack with.");
            var meditate = Skill(111, "Meditate", Element.Aero, ap: 1, elementCost: 0, range: 0,
                SkillTarget.Self, SkillEffectKind.RestoreAp, 2,
                "Trade a point now for two later. Only worth it if you have somewhere to spend them.");
            var focus = Skill(112, "Focus", Element.Arcana, ap: 2, elementCost: 0, range: 0,
                SkillTarget.Self, SkillEffectKind.ReturnElement, 2,
                "A longer breath. Two elements back, at twice the price of one.", level: 4);

            // ---------- class skills, one at each of levels two, four and six ----------
            //
            // The weapon is what a character does at level one. The class is what it grows into,
            // and each of the seven grows in its own direction: the Guardian towards staying up,
            // the Rogue towards the one big hit, the Hunter towards the long shot.
            var holdTheLine = Skill(150, "Hold the Line", Element.Hydro, ap: 1, elementCost: 1,
                range: 0, SkillTarget.Self, SkillEffectKind.Heal, 3,
                "Set your feet and take a breath. Heals with your Toughness.", level: 2,
                scaling: Attribute.Toughness);
            var shieldBash = Skill(151, "Shield Bash", Element.Geo, ap: 2, elementCost: 1, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 5,
                "The rim, not the face. Geo, because it is mostly weight.", level: 4,
                scaling: Attribute.Strength);
            var rally = Skill(152, "Rally", Element.Lux, ap: 2, elementCost: 1, range: 0,
                SkillTarget.Self, SkillEffectKind.RestoreAp, 4,
                "Two points spent, four back. A second wind that costs Lux to catch.", level: 6);

            var backstab = Skill(160, "Backstab", Element.Nyx, ap: 1, elementCost: 1, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 5,
                "Somewhere they were not covering. Nyx, and quick.", level: 2,
                scaling: Attribute.Dexterity);
            var slipAway = Skill(161, "Slip Away", Element.Aero, ap: 1, elementCost: 1, range: 0,
                SkillTarget.Self, SkillEffectKind.ReturnElement, 2,
                "Gone for a moment, and back with two of what you spent.", level: 4);
            var assassinate = Skill(162, "Assassinate", Element.Nyx, ap: 2, elementCost: 2, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 9,
                "The one that was planned. Two Nyx, and it is usually over.", level: 6,
                scaling: Attribute.Dexterity);

            var heavyBlow = Skill(170, "Heavy Blow", Element.Geo, ap: 2, elementCost: 1, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 6,
                "Both hands, all the way through.", level: 2, scaling: Attribute.Strength);
            var secondWind = Skill(171, "Second Wind", Element.Hydro, ap: 1, elementCost: 1,
                range: 0, SkillTarget.Self, SkillEffectKind.Heal, 4,
                "Shake it off. Heals with your Toughness.", level: 4,
                scaling: Attribute.Toughness);
            var whirlwind = Skill(172, "Whirlwind", Element.Pyro, ap: 2, elementCost: 2, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 8,
                "Everything you have, in one turn of the body.", level: 6,
                scaling: Attribute.Strength);

            var snapShot = Skill(130, "Snap Shot", Element.Aero, ap: 1, elementCost: 1, range: 3,
                SkillTarget.Creature, SkillEffectKind.Damage, 2,
                "Loosed before it was aimed. Cheap, and sure up close.", level: 2,
                scaling: Attribute.Dexterity, accuracy: 95, falloff: 6);
            var aimedShot = Skill(131, "Aimed Shot", Element.Aero, ap: 2, elementCost: 1, range: 5,
                SkillTarget.Creature, SkillEffectKind.Damage, 5,
                "A breath held, and the arrow goes where it was looked at.", level: 4,
                scaling: Attribute.Dexterity, accuracy: 100, falloff: 4);
            var piercingShot = Skill(132, "Piercing Shot", Element.Geo, ap: 2, elementCost: 2,
                range: 4, SkillTarget.Creature, SkillEffectKind.Damage, 8,
                "A heavy shaft. Geo, because it arrives like a thrown stone.", level: 6,
                scaling: Attribute.Dexterity, accuracy: 90, falloff: 8);

            var sanctuary = Skill(180, "Sanctuary", Element.Lux, ap: 2, elementCost: 1, range: 0,
                SkillTarget.Self, SkillEffectKind.Heal, 6,
                "A moment nothing reaches into. Heals with your Willpower.", level: 4,
                scaling: Attribute.Willpower);
            var judgement = Skill(181, "Judgement", Element.Lux, ap: 2, elementCost: 2, range: 3,
                SkillTarget.Creature, SkillEffectKind.Damage, 10,
                "Light, from further than they expected. Two Lux.", level: 6,
                accuracy: 90, falloff: 6);

            var hex = Skill(190, "Hex", Element.Nyx, ap: 2, elementCost: 1, range: 3,
                SkillTarget.Creature, SkillEffectKind.Damage, 6,
                "A word, and it reaches them. Nyx, at range.", level: 4,
                accuracy: 90, falloff: 10);
            var oblivion = Skill(191, "Oblivion", Element.Nyx, ap: 3, elementCost: 2, range: 2,
                SkillTarget.Creature, SkillEffectKind.Damage, 12,
                "Everything the Apostate learned, and everything they gave up.", level: 6);

            // ---------- what the premades swing with ----------
            //
            // Built characters get their attack from a weapon; a premade has no weapon slot, so
            // its attacks are authored here. Each one is a different element from the last, and
            // every premade carries at least two, because a creature that asks one question is a
            // creature with one answer to learn.
            var club = Skill(200, "Club", Element.Geo, ap: 1, elementCost: 1, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 4,
                "Something heavy on the end of something long.", scaling: Attribute.Strength);
            var bite = Skill(201, "Bite", Element.Aero, ap: 1, elementCost: 1, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 3,
                "Fast, and closer than anybody wanted.", scaling: Attribute.Dexterity);
            var maul = Skill(202, "Maul", Element.Geo, ap: 2, elementCost: 1, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 6,
                "The whole weight behind it.", scaling: Attribute.Strength);
            var sling = Skill(203, "Sling", Element.Geo, ap: 1, elementCost: 1, range: 3,
                SkillTarget.Creature, SkillEffectKind.Damage, 2,
                "A stone, from further than you would like.", scaling: Attribute.Dexterity,
                accuracy: 85, falloff: 10);
            var torch = Skill(204, "Torch", Element.Pyro, ap: 1, elementCost: 1, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 3,
                "A burning brand, swung. It asks in Pyro and it does not care who answers.");

            var fireball = Skill(140, "Fireball", Element.Pyro, ap: 2, elementCost: 2, range: 3,
                SkillTarget.Creature, SkillEffectKind.Damage, 9,
                "The reason the staff is held at arm's length. Two Pyro, thrown.", level: 2,
                accuracy: 85, falloff: 10);
            var storm = Skill(141, "Storm", Element.Aero, ap: 3, elementCost: 2, range: 3,
                SkillTarget.Creature, SkillEffectKind.Damage, 11,
                "The air itself, and all of it at once.", level: 6,
                accuracy: 85, falloff: 8);

            // Ids are hand-assigned and permanent: they are written into saved characters and cross
            // the network. Grouped by kind so a new weapon is obviously an 1x.
            //
            // A weapon is its skills and nothing else. It used to nudge an attribute as well,
            // and every weapon in the list was two things to compare instead of one.
            var sword = Equipment(10, "Sword", EquipmentSlot.Weapon,
                "A soldier's blade. Reliable, and heavy enough to matter.", strike);
            var greataxe = Equipment(11, "Greataxe", EquipmentSlot.Weapon,
                "Enormous. You will hit first only by accident.", strike, cleave);
            var bow = Equipment(12, "Bow", EquipmentSlot.Weapon,
                "Keeps the fight at the distance you choose.", loose);
            var dagger = Equipment(13, "Dagger", EquipmentSlot.Weapon,
                "Short reach, and you will be somewhere else before it is answered.", jab);
            var staff = Equipment(14, "Staff", EquipmentSlot.Weapon,
                "Focuses what you draw from the pool.", ember);
            var mace = Equipment(15, "Mace", EquipmentSlot.Weapon,
                "Blunt and devout. The two go together more often than anyone admits.", smite);

            // Four armour, in the offhand, so carrying one costs no speed and no step. What it
            // costs is the hand.
            //
            // DE-006 names a shield as its example of something granting advantage, and it was
            // authored that way and then taken out again. Advantage costs two elements instead of
            // one, so it would have made every defence twice as expensive for as long as the shield
            // was equipped -- a drain the player never chose and cannot switch off mid-fight. An
            // item that quietly doubles your burn rate is not a defensive item.
            var shield = Equipment(30, "Shield", EquipmentSlot.Offhand,
                "Between you and anything that reaches you, and no speed for it. It costs you "
                + "the other hand.",
                new SkillAsset[0], ArmourClass.None, armourPoints: 4);

            // The numbers are not in the words: the creator prints "+4 ARM  -2 SPD" beside the
            // name from the rules, so a retune does not leave a description telling a lie.
            var light = Armour(20, "Light armour", ArmourClass.Light,
                "Padding and leather. You will still be quick.");
            var medium = Armour(21, "Medium armour", ArmourClass.Medium,
                "Mail. A fair trade, most days.");
            var heavy = Armour(22, "Heavy armour", ArmourClass.Heavy,
                "Plate. Everyone else has already acted, and the walk is long.");

            // One per species, all four conditioned on having nothing in the weapon slot. The
            // numbers are deliberately identical: what differs today is the name and the flavour,
            // which is the hook to differentiate them on later without inventing balance now.
            var fists = Unarmed(120, 1, "Fists",
                "No weapon, and no excuses. Whatever you throw, you throw with.");
            var claws = Unarmed(121, 2, "Claws",
                "What you were born holding. It has never needed sharpening.");
            var slam = Unarmed(122, 3, "Slam",
                "The whole of you, arriving at once.");
            var shiv = Unarmed(123, 4, "Shiv",
                "Not a weapon. Nobody has ever successfully argued otherwise.");

            var species = new List<SpeciesDefinition>
            {
                // Each baseline is a nudge, not a build: one attribute up and one down, so a
                // species reads as a leaning rather than a decision the player did not make.
                // Humans lean nowhere, which is their whole thing.
                Species(1, "Human", Attr(),
                    "Adaptable, and the only species with nothing to apologise for.", 4,
                    breath, fists),
                Species(2, "Beast", Attr(dexterity: 1, willpower: -1),
                    "Quick, and disinclined to argue about it.", 4, breath, claws),
                Species(3, "Giantkin", Attr(strength: 1, toughness: 1, dexterity: -1),
                    "Slow to arrive and hard to remove.", 4, breath, slam),
                Species(4, "Goblinoid", Attr(dexterity: 1, toughness: -1),
                    "Small, fast, and entirely aware of both.", 4, breath, shiv)
            };

            // The seven. Baselines are deliberately flat: a class is what it may carry and what it
            // knows, and giving each one a stat bonus as well would decide the point buy for the
            // player before they had spent anything. Each knows one thing at level one and grows
            // one skill at each of levels two, four and six.
            var classes = new List<ClassAsset>
            {
                Class(1, "Guardian", "Stands where the line would otherwise break.",
                    new[] { sword, mace }, new[] { recover, holdTheLine, shieldBash, rally }),
                Class(2, "Rogue", "Picks the moment, and is elsewhere by the time it lands.",
                    new[] { dagger, bow }, new[] { meditate, backstab, slipAway, assassinate }),
                Class(3, "Fighter", "No tricks. Enough of that becomes its own trick.",
                    new[] { sword, greataxe }, new[] { meditate, heavyBlow, secondWind, whirlwind }),
                Class(4, "Hunter", "Decides the range the fight happens at.",
                    new[] { bow, dagger }, new[] { meditate, snapShot, aimedShot, piercingShot }),
                Class(5, "Priest", "Keeps others standing, and answers in Lux when it must.",
                    new[] { mace, staff }, new[] { recover, smite, sanctuary, judgement }),
                Class(6, "Apostate", "Trained devout, and no longer.",
                    new[] { staff, dagger }, new[] { recover, drain, hex, oblivion }),
                Class(7, "Elementalist",
                    "Deepest reserves on the field. What it does with them is up to the pool.",
                    new[] { staff }, new[] { fireball, focus, storm })
            };

            var equipment = new List<EquipmentAsset>
            {
                sword, greataxe, bow, dagger, staff, mace, light, medium, heavy, shield
            };

            var skills = new List<SkillAsset>
            {
                breath, strike, cleave, loose, jab, ember, smite, drain, recover, meditate, focus,
                holdTheLine, shieldBash, rally, backstab, slipAway, assassinate,
                heavyBlow, secondWind, whirlwind, snapShot, aimedShot, piercingShot,
                sanctuary, judgement, hex, oblivion, fireball, storm,
                club, bite, maul, sling, torch
            };

            Creatures(new PremadeKit(strike, cleave, loose, jab, smite, recover, heavyBlow,
                backstab, snapShot, holdTheLine, club, bite, maul, sling, torch));

            return Catalog(species, classes, equipment, skills);
        }

        /// <summary>The skills the premades are authored from, by name, so a line reads.</summary>
        readonly struct PremadeKit
        {
            public readonly SkillAsset Strike, Cleave, Loose, Jab, Smite, Recover, HeavyBlow,
                Backstab, SnapShot, HoldTheLine, Club, Bite, Maul, Sling, Torch;

            public PremadeKit(SkillAsset strike, SkillAsset cleave, SkillAsset loose, SkillAsset jab,
                SkillAsset smite, SkillAsset recover, SkillAsset heavyBlow, SkillAsset backstab,
                SkillAsset snapShot, SkillAsset holdTheLine, SkillAsset club, SkillAsset bite,
                SkillAsset maul, SkillAsset sling, SkillAsset torch)
            {
                Strike = strike;
                Cleave = cleave;
                Loose = loose;
                Jab = jab;
                Smite = smite;
                Recover = recover;
                HeavyBlow = heavyBlow;
                Backstab = backstab;
                SnapShot = snapShot;
                HoldTheLine = holdTheLine;
                Club = club;
                Bite = bite;
                Maul = maul;
                Sling = sling;
                Torch = torch;
            }
        }

        const string k_PortraitFolder = "Assets/Art/Portraits";

        /// <summary>
        /// Authors the twelve premade creatures in full: portrait, level, the four stats, the pool
        /// and the skills.
        ///
        /// Stats follow the same shape a built character's would -- a base speed of eight, less
        /// what the armour costs, and a pool that costs exactly the creature's budget -- so a
        /// premade and a character of the same level stand on the same footing. Speeds are on the
        /// new scale, where a tile costs by speed: eight is half a point a tile, five is a whole
        /// one, and the ogre at five is a creature that arrives late and stays.
        ///
        /// Every pool is a spread rather than a stack of one element, because a pool is what a
        /// creature answers an attack with as well as what it attacks from. A creature holding
        /// only Aero has exactly one answer to everything, which is not a decision.
        ///
        /// Portraits come from the art already in the project. There are four human faces for
        /// eight human premades, so some share one; the alternative was a lettered tile.
        /// </summary>
        static void Creatures(PremadeKit kit)
        {
            // Every premade below carries at least two attacks in two different elements, and
            // holds those elements. Skills above the authored level are left off the list, since
            // a premade only ever has what its level allows -- so a level-one recruit is authored
            // with level-one things.

            // Rank and file. Padded, a little slow, a sword and a club.
            Creature("guard-recruit", "Human/Finn", level: 1, hp: 14, ap: 4, speed: 6, armour: 4,
                shielded: false, Pool(pyro: 2, geo: 2), Element.Pyro, kit.Strike, kit.Club);

            // Small and quick: a jab up close and a stone from further off.
            Creature("monster-goblin", "Goblinoid/Brawler", level: 1, hp: 10, ap: 5, speed: 10,
                armour: 0, shielded: false, Pool(aero: 2, geo: 2), Element.Aero, kit.Jab, kit.Sling);

            // All teeth and weight. The fastest thing on the board.
            Creature("monster-wolf", "Beast/Direwolf", level: 1, hp: 14, ap: 6, speed: 12,
                armour: 0, shielded: false, Pool(aero: 2, geo: 2), Element.Aero, kit.Bite, kit.Maul);

            // Skirmishers: an arrow, a stone, and a snap shot for close work.
            Creature("bandit-scout", "Human/henry-jester", level: 2, hp: 15, ap: 5, speed: 9,
                armour: 0, shielded: false, Pool(aero: 3, geo: 2), Element.Aero,
                kit.Loose, kit.Sling, kit.SnapShot);
            Creature("guard-archer", "Human/Finn", level: 2, hp: 15, ap: 5, speed: 8,
                armour: 0, shielded: false, Pool(aero: 3, pyro: 2), Element.Aero,
                kit.Loose, kit.Strike, kit.SnapShot);
            Creature("hero-ranger", "Human/henry-jester", level: 3, hp: 18, ap: 5, speed: 9,
                armour: 0, shielded: false, Pool(aero: 3, geo: 2, hydro: 1), Element.Aero,
                kit.Loose, kit.Club, kit.SnapShot, kit.Recover);

            // Quick and underhanded: Aero to open, Nyx when the back is turned.
            Creature("bandit-cutpurse", "Human/Finn", level: 2, hp: 14, ap: 6, speed: 10,
                armour: 0, shielded: false, Pool(aero: 3, nyx: 2), Element.Aero,
                kit.Jab, kit.Backstab);

            // The heavies. Cleave wants level three, so this is the first rank that has it.
            Creature("bandit-brute", "Giantkin/Barbarian", level: 3, hp: 26, ap: 4, speed: 6,
                armour: 4, shielded: false, Pool(geo: 3, pyro: 2, hydro: 1), Element.Geo,
                kit.Club, kit.Strike, kit.Cleave, kit.HeavyBlow);
            Creature("guard-sergeant", "Giantkin/Crusader", level: 3, hp: 24, ap: 5, speed: 5,
                armour: 8, shielded: false, Pool(pyro: 3, geo: 2, hydro: 1), Element.Pyro,
                kit.Strike, kit.Club, kit.Cleave, kit.HoldTheLine);
            Creature("monster-ogre", "Goblinoid/Ogre", level: 3, hp: 34, ap: 4, speed: 5,
                armour: 6, shielded: false, Pool(geo: 3, pyro: 2, aero: 1), Element.Geo,
                kit.Maul, kit.Torch, kit.Cleave);

            // Plate and a shield: sixteen and four, and a speed of four for it. Arrives last and
            // takes a while to put down.
            Creature("hero-knight", "Human/Knight", level: 3, hp: 24, ap: 4, speed: 4, armour: 20,
                shielded: true, Pool(pyro: 2, geo: 2, hydro: 2), Element.Pyro,
                kit.Strike, kit.Club, kit.HeavyBlow, kit.Recover);

            // Two Lux is four of the six. Smite, a mace in Pyro, and something to stay up with.
            Creature("hero-cleric", "Human/Scholar", level: 3, hp: 20, ap: 5, speed: 6, armour: 4,
                shielded: false, Pool(lux: 2, pyro: 2, hydro: 2), Element.Lux,
                kit.Smite, kit.Strike, kit.Recover);

            CreatureCatalogAll();
        }

        /// <summary>Writes a premade in full. Species and class references stay as authored.</summary>
        static void Creature(string id, string portrait, int level, int hp, int ap, int speed,
            int armour, bool shielded, ElementValues pool, Element buys, params SkillAsset[] skills)
        {
            var path = $"{k_SpeciesFolder}/{id}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<CreatureDefinition>(path);

            if (asset == null)
            {
                Debug.LogWarning($"No creature at {path}; it was not authored.");
                return;
            }

            var serialized = new SerializedObject(asset);

            var sprite = LoadPortrait($"{k_PortraitFolder}/{portrait}.jpg");

            if (sprite == null)
            {
                Debug.LogWarning($"No portrait at {k_PortraitFolder}/{portrait}.jpg for {id}. "
                    + "Is the file imported as a sprite? The portrait step does that.");
            }

            serialized.FindProperty("m_Portrait").objectReferenceValue = sprite;
            serialized.FindProperty("m_Level").intValue = level;
            serialized.FindProperty("m_MaxHp").intValue = hp;
            serialized.FindProperty("m_MaxAp").intValue = ap;
            serialized.FindProperty("m_Speed").intValue = speed;
            serialized.FindProperty("m_Armour").intValue = armour;
            serialized.FindProperty("m_Shielded").boolValue = shielded;
            WriteElements(serialized.FindProperty("m_StartingPool"), pool);
            WriteList(serialized.FindProperty("m_Skills"), skills);

            // What it spends the budget on if the host fields it above its authored level. Deep
            // enough to cover any level a host is plausibly going to drag a bandit up to; a pick
            // that never gets reached costs nothing.
            const int depth = 8;

            var picks = serialized.FindProperty("m_LevelUpPicks");
            picks.arraySize = depth;

            for (var i = 0; i < depth; i++)
            {
                picks.GetArrayElementAtIndex(i).intValue = (int)buys;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// The sprite for a portrait file, however the importer chose to expose it.
        ///
        /// A texture imported as a single sprite answers the typed load directly; one that was
        /// re-imported this run may only answer through its sub-assets. Both are tried.
        /// </summary>
        static Sprite LoadPortrait(string path)
        {
            var direct = AssetDatabase.LoadAssetAtPath<Sprite>(path);

            if (direct != null)
            {
                return direct;
            }

            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                if (asset is Sprite sprite)
                {
                    return sprite;
                }
            }

            return null;
        }

        /// <summary>
        /// Every premade in the catalog, so the roster offers all twelve.
        ///
        /// The catalog on disk listed nine. Three were authored after it and never added, which is
        /// the kind of thing that happens when a list is maintained by hand; it is written from
        /// the folder now.
        /// </summary>
        static void CreatureCatalogAll()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CreatureCatalog>(
                $"{k_SpeciesFolder}/CreatureCatalog.asset");

            if (catalog == null)
            {
                return;
            }

            var creatures = new List<CreatureDefinition>();

            foreach (var guid in AssetDatabase.FindAssets("t:CreatureDefinition",
                         new[] { k_SpeciesFolder }))
            {
                var definition = AssetDatabase.LoadAssetAtPath<CreatureDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (definition != null)
                {
                    creatures.Add(definition);
                }
            }

            creatures.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            var serialized = new SerializedObject(catalog);
            WriteList(serialized.FindProperty("m_Creatures"), creatures);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
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

        /// <summary>
        /// An unarmed strike: what a species does with nothing in its hands.
        ///
        /// Conditioned on the weapon slot being empty rather than removed from the list by hand,
        /// which is the whole point of conditions -- the same asset is a skill or is not one
        /// depending on what the character is holding, and nothing has to remember to take it away.
        ///
        /// It offers the four physical elements. A sword asks its question in one element because a
        /// sword is a particular thing; a fist is not, so what it arrives as is the fighter own
        /// decision -- which makes going unarmed a real trade rather than a penalty: less damage,
        /// and the only attack in the game that can answer whatever it needs to.
        /// </summary>
        static SkillAsset Unarmed(int id, int speciesId, string name, string description) =>
            Skill(id, name, Element.Geo, ap: 1, elementCost: 1, range: 1,
                SkillTarget.Creature, SkillEffectKind.Damage, 2, description,
                level: 1,
                conditions: new[] { SkillCondition.NoWeapon, SkillCondition.Species(speciesId) },
                options: k_Physical, scaling: Attribute.Strength);

        /// <summary>
        /// The four an unarmed strike may be made of.
        ///
        /// The commons, and not Lux, Nyx or Arcana. Those three are the tiers a pool is built to
        /// reach for, and a fist that could be any of them would answer everything -- the point of
        /// paying for Arcana is that not everybody has it.
        /// </summary>
        static readonly Element[] k_Physical =
            { Element.Geo, Element.Hydro, Element.Pyro, Element.Aero };

        static SkillAsset Skill(int id, string name, Element element, int ap, int elementCost,
            int range, SkillTarget target, SkillEffectKind effect, int amount, string description,
            int level = 1, IReadOnlyList<SkillCondition> conditions = null,
            IReadOnlyList<Element> options = null, Attribute? scaling = null,
            int accuracy = 0, int falloff = 0)
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
            serialized.FindProperty("m_Scales").boolValue = scaling.HasValue;
            serialized.FindProperty("m_ScalesWith").intValue =
                (int)(scaling ?? Attribute.Strength);
            serialized.FindProperty("m_Accuracy").intValue = accuracy;
            serialized.FindProperty("m_Falloff").intValue = falloff;
            serialized.FindProperty("m_LevelRequired").intValue = level;

            WriteConditions(serialized.FindProperty("m_Conditions"), conditions);
            WriteElements(serialized.FindProperty("m_ElementOptions"), options);

            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(asset);
            return asset;
        }

        static void WriteConditions(SerializedProperty list,
            IReadOnlyList<SkillCondition> conditions)
        {
            list.ClearArray();

            if (conditions == null)
            {
                return;
            }

            for (var i = 0; i < conditions.Count; i++)
            {
                list.InsertArrayElementAtIndex(i);

                var entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Kind").intValue = (int)conditions[i].Kind;
                entry.FindPropertyRelative("Value").intValue = conditions[i].Value;
            }
        }

        static void WriteElements(SerializedProperty list, IReadOnlyList<Element> elements)
        {
            list.ClearArray();

            if (elements == null)
            {
                return;
            }

            for (var i = 0; i < elements.Count; i++)
            {
                list.InsertArrayElementAtIndex(i);
                list.GetArrayElementAtIndex(i).intValue = (int)elements[i];
            }
        }

        static EquipmentAsset Equipment(int id, string name, EquipmentSlot slot,
            string description, params SkillAsset[] skills) =>
            Equipment(id, name, slot, description, skills, ArmourClass.None);

        /// <summary>
        /// Armour carries no pool of its own: what a suit stops comes from its class, so that
        /// "medium stops eight" is one rule rather than a number repeated on every suit.
        /// </summary>
        static EquipmentAsset Armour(int id, string name, ArmourClass armour, string description) =>
            Equipment(id, name, EquipmentSlot.Armor, description, new SkillAsset[0], armour);

        static EquipmentAsset Equipment(int id, string name, EquipmentSlot slot,
            string description, IReadOnlyList<SkillAsset> skills,
            ArmourClass armour = ArmourClass.None, int armourPoints = 0,
            bool grantsAdvantage = false)
        {
            var asset = Upsert<EquipmentAsset>($"{k_Folder}/{Sanitise(name)}.asset");
            var serialized = new SerializedObject(asset);

            serialized.FindProperty("m_Id").intValue = id;
            serialized.FindProperty("m_DisplayName").stringValue = name;
            serialized.FindProperty("m_Description").stringValue = description;
            serialized.FindProperty("m_Slot").intValue = (int)slot;
            serialized.FindProperty("m_Armour").intValue = (int)armour;

            serialized.FindProperty("m_ArmourPoints").intValue = armourPoints;
            serialized.FindProperty("m_GrantsAdvantage").boolValue = grantsAdvantage;

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
