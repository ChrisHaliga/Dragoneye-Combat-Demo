using System;
using System.Collections.Generic;
using System.IO;
using Dragoneye.Combat;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>
    /// A character as it sits on disk: the build, plus the parts that never cross the network.
    ///
    /// The portrait is deliberately outside <see cref="CharacterBuild"/>. A build is rules-relevant
    /// state that every peer needs; a portrait is a picture on one machine. Keeping them apart is
    /// what stops someone replicating a megabyte of PNG per player because it happened to be in the
    /// same object.
    /// </summary>
    public sealed class SavedCharacter
    {
        public SavedCharacter(string id, CharacterBuild build)
        {
            Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id;
            Build = build ?? new CharacterBuild();
        }

        /// <summary>Local only. Identifies the file, not the character in a match.</summary>
        public string Id { get; }

        public CharacterBuild Build { get; }

        /// <summary>
        /// The face this character wears, resolved from the shipped library.
        ///
        /// Looked up rather than stored, so a character's picture is the same on every machine and
        /// a save file stays a page of numbers.
        /// </summary>
        public Sprite Portrait => Portraits.Get(Build.PortraitId);
    }

    /// <summary>
    /// Characters saved on this machine.
    ///
    /// Plain JSON under <c>persistentDataPath</c>, one file per character. A file each rather than
    /// one index means a corrupt character costs that character rather than the whole roster, and
    /// the folder can be inspected and edited by hand during a playtest.
    ///
    /// No images. A face is the id of one of the game's own portraits, so a save file stays a page
    /// of numbers and a character looks the same on every machine that resolves it.
    ///
    /// Static because there is exactly one save location per install, and threading a store instance
    /// through the menu to answer "what has this player made" would be ceremony around a fact about
    /// the filesystem.
    /// </summary>
    public static class CharacterStore
    {
        const string k_Folder = "Characters";
        const string k_Extension = ".json";

        public static string Directory =>
            Path.Combine(Application.persistentDataPath, k_Folder);

        /// <summary>
        /// Every saved character, newest first.
        ///
        /// The folder is re-read on each call. The list is short, it is only asked for when a menu
        /// opens, and caching it would go stale the moment someone edited a file by hand -- which is
        /// a thing this format exists to allow. Portraits are the exception: those are cached,
        /// because decoding them is the expensive part and they are far more likely to be reused.
        /// </summary>
        public static List<SavedCharacter> LoadAll()
        {
            var characters = new List<SavedCharacter>();

            if (!System.IO.Directory.Exists(Directory))
            {
                return characters;
            }

            var files = System.IO.Directory.GetFiles(Directory, "*" + k_Extension);
            Array.Sort(files, (a, b) =>
                File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));

            foreach (var file in files)
            {
                var character = LoadFile(file);

                if (character != null)
                {
                    characters.Add(character);
                }
            }

            return characters;
        }

        public static SavedCharacter Load(string id)
        {
            var path = PathFor(id);
            return File.Exists(path) ? LoadFile(path) : null;
        }

        /// <summary>Writes the character. Returns false if the write failed.</summary>
        public static bool Save(SavedCharacter character)
        {
            if (character == null)
            {
                return false;
            }

            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                File.WriteAllText(PathFor(character.Id),
                    JsonUtility.ToJson(Record.From(character), true));

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not save character '{character.Id}': {e.Message}");
                return false;
            }
        }

        public static bool Delete(string id)
        {
            try
            {
                var path = PathFor(id);

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not delete character '{id}': {e.Message}");
                return false;
            }
        }

        static SavedCharacter LoadFile(string path)
        {
            try
            {
                var record = JsonUtility.FromJson<Record>(File.ReadAllText(path));

                if (record == null || string.IsNullOrEmpty(record.Id))
                {
                    Debug.LogError($"Character file '{path}' is not readable; skipping it.");
                    return null;
                }

                return new SavedCharacter(record.Id, record.ToBuild());
            }
            catch (Exception e)
            {
                // One unreadable file must not take the rest of the roster with it.
                Debug.LogError($"Could not read character file '{path}': {e.Message}");
                return null;
            }
        }

        static string PathFor(string id) => Path.Combine(Directory, id + k_Extension);

        /// <summary>
        /// The on-disk shape.
        ///
        /// Flat public fields because that is all JsonUtility handles -- it cannot serialise the
        /// readonly struct or the enum list a <see cref="CharacterBuild"/> is made of. Converting
        /// here keeps that limitation out of the rules.
        /// </summary>
        [Serializable]
        sealed class Record
        {
            public string Id;
            public string Name;
            public int SpeciesId;
            public int ClassId;
            public int PortraitId;
            public int Level;
            public int Xp;

            // One field per attribute and per element, because JsonUtility cannot serialise the
            // readonly structs the rules use. Flat and explicit beats clever here: the file is meant
            // to be readable and hand-editable during a playtest.
            public int Toughness;
            public int Dexterity;
            public int Strength;
            public int Skill;
            public int Vitality;
            public int Willpower;
            public int Endurance;

            public int Geo;
            public int Hydro;
            public int Pyro;
            public int Aero;
            public int Lux;
            public int Nyx;
            public int Arcana;

            public int WeaponId;
            public int ArmorId;
            public int OffhandId;
            public int[] LearnedSkillIds;


            public static Record From(SavedCharacter character)
            {
                var build = character.Build;
                var a = build.Attributes;
                var p = build.StartingPool;

                return new Record
                {
                    Id = character.Id,
                    Name = build.Name,
                    SpeciesId = build.SpeciesId,
                    ClassId = build.ClassId,
                    PortraitId = build.PortraitId,
                    Level = build.Level,
                    Xp = build.Xp,
                    Toughness = a.Toughness,
                    Dexterity = a.Dexterity,
                    Strength = a.Strength,
                    Skill = a.Skill,
                    Vitality = a.Vitality,
                    Willpower = a.Willpower,
                    Endurance = a.Endurance,
                    Geo = p[Element.Geo],
                    Hydro = p[Element.Hydro],
                    Pyro = p[Element.Pyro],
                    Aero = p[Element.Aero],
                    Lux = p[Element.Lux],
                    Nyx = p[Element.Nyx],
                    Arcana = p[Element.Arcana],
                    WeaponId = build.WeaponId,
                    ArmorId = build.ArmorId,
                    OffhandId = build.OffhandId,
                    LearnedSkillIds = build.LearnedSkillIds.ToArray()
                };
            }

            public CharacterBuild ToBuild()
            {
                var build = new CharacterBuild
                {
                    Name = Name ?? string.Empty,
                    SpeciesId = SpeciesId,
                    ClassId = ClassId,
                    PortraitId = PortraitId,

                    // Characters saved before levels existed have none, and a level of zero would
                    // give them no pool budget and no skills at all.
                    Level = Level < Progression.FirstLevel ? Progression.FirstLevel : Level,
                    Xp = Xp,
                    Attributes = new AttributeBlock(Toughness, Dexterity, Strength, Skill,
                        Vitality, Willpower, Endurance),
                    StartingPool = new ElementCounts(Geo, Hydro, Pyro, Aero, Lux, Nyx, Arcana),
                    WeaponId = WeaponId,
                    ArmorId = ArmorId,
                    OffhandId = OffhandId
                };

                if (LearnedSkillIds != null)
                {
                    build.LearnedSkillIds.AddRange(LearnedSkillIds);
                }

                return build;
            }
        }
    }
}
