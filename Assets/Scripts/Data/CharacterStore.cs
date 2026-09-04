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
        public SavedCharacter(string id, CharacterBuild build, Texture2D portrait = null)
        {
            Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id;
            Build = build ?? new CharacterBuild();
            Portrait = portrait;
        }

        /// <summary>Local only. Identifies the file, not the character in a match.</summary>
        public string Id { get; }

        public CharacterBuild Build { get; }

        /// <summary>Null when none was chosen. Local to this machine.</summary>
        public Texture2D Portrait { get; set; }
    }

    /// <summary>
    /// Characters saved on this machine.
    ///
    /// Plain JSON under <c>persistentDataPath</c>, one file per character, portraits alongside as
    /// PNGs. A file each rather than one index means a corrupt character costs that character rather
    /// than the whole roster, and the folder can be inspected and edited by hand during a playtest.
    ///
    /// Static because there is exactly one save location per install, and threading a store instance
    /// through the menu to answer "what has this player made" would be ceremony around a fact about
    /// the filesystem.
    /// </summary>
    public static class CharacterStore
    {
        // Portraits are cached by character id and owned here. LoadAll runs every time the roster
        // screen opens, and decoding a fresh 256-square texture per character per open leaked a
        // quarter of a megabyte each time with nothing destroying the old ones.
        static readonly Dictionary<string, Texture2D> s_Portraits = new Dictionary<string, Texture2D>();

        const string k_Folder = "Characters";
        const string k_Extension = ".json";
        const string k_PortraitExtension = ".png";

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

        /// <summary>Writes the character and its portrait. Returns false if the write failed.</summary>
        public static bool Save(SavedCharacter character)
        {
            if (character == null)
            {
                return false;
            }

            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                var record = Record.From(character);
                record.PortraitFile = SavePortrait(character) ? character.Id + k_PortraitExtension : "";

                File.WriteAllText(PathFor(character.Id), JsonUtility.ToJson(record, true));

                // The store now owns this texture, and whatever it was showing before is dead.
                Adopt(character.Id, character.Portrait);
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

                var portrait = PortraitPathFor(id);

                if (File.Exists(portrait))
                {
                    File.Delete(portrait);
                }

                Adopt(id, null);
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

                return new SavedCharacter(record.Id, record.ToBuild(), LoadPortrait(record));
            }
            catch (Exception e)
            {
                // One unreadable file must not take the rest of the roster with it.
                Debug.LogError($"Could not read character file '{path}': {e.Message}");
                return null;
            }
        }

        static bool SavePortrait(SavedCharacter character)
        {
            if (character.Portrait == null)
            {
                return false;
            }

            try
            {
                File.WriteAllBytes(PortraitPathFor(character.Id), character.Portrait.EncodeToPNG());
                return true;
            }
            catch (Exception e)
            {
                // A character without its picture is still playable, so this is not fatal.
                Debug.LogError($"Could not save portrait for '{character.Id}': {e.Message}");
                return false;
            }
        }

        static Texture2D LoadPortrait(Record record)
        {
            if (string.IsNullOrEmpty(record.PortraitFile))
            {
                return null;
            }

            // The null check matters as well as the lookup: a cached texture can have been destroyed
            // by a scene change, and Unity reports that as a null-equal object rather than absence.
            if (s_Portraits.TryGetValue(record.Id, out var cached) && cached != null)
            {
                return cached;
            }

            var path = Path.Combine(Directory, record.PortraitFile);
            var loaded = File.Exists(path) ? PortraitLoader.FromFile(path) : null;

            if (loaded != null)
            {
                s_Portraits[record.Id] = loaded;
            }

            return loaded;
        }

        /// <summary>
        /// Takes ownership of a portrait, destroying whatever was cached for that character.
        /// </summary>
        static void Adopt(string id, Texture2D portrait)
        {
            if (s_Portraits.TryGetValue(id, out var previous) && previous != null && previous != portrait)
            {
                UnityEngine.Object.Destroy(previous);
            }

            if (portrait == null)
            {
                s_Portraits.Remove(id);
            }
            else
            {
                s_Portraits[id] = portrait;
            }
        }

        static string PathFor(string id) => Path.Combine(Directory, id + k_Extension);

        static string PortraitPathFor(string id) => Path.Combine(Directory, id + k_PortraitExtension);

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
            public int ClassId;
            public int Vitality;
            public int Speed;
            public int Power;
            public int Focus;
            public int[] ElementPicks;
            public int WeaponId;
            public int ArmorId;
            public int OffhandId;
            public string PortraitFile;

            public static Record From(SavedCharacter character)
            {
                var build = character.Build;
                var picks = new int[build.ElementPicks.Count];

                for (var i = 0; i < picks.Length; i++)
                {
                    picks[i] = (int)build.ElementPicks[i];
                }

                return new Record
                {
                    Id = character.Id,
                    Name = build.Name,
                    ClassId = build.ClassId,
                    Vitality = build.Allocation.Vitality,
                    Speed = build.Allocation.Speed,
                    Power = build.Allocation.Power,
                    Focus = build.Allocation.Focus,
                    ElementPicks = picks,
                    WeaponId = build.WeaponId,
                    ArmorId = build.ArmorId,
                    OffhandId = build.OffhandId,
                    PortraitFile = ""
                };
            }

            public CharacterBuild ToBuild()
            {
                var build = new CharacterBuild
                {
                    Name = Name ?? string.Empty,
                    ClassId = ClassId,
                    Allocation = new StatBlock(Vitality, Speed, Power, Focus),
                    WeaponId = WeaponId,
                    ArmorId = ArmorId,
                    OffhandId = OffhandId
                };

                if (ElementPicks != null)
                {
                    foreach (var pick in ElementPicks)
                    {
                        build.ElementPicks.Add((Element)pick);
                    }
                }

                return build;
            }
        }
    }
}
