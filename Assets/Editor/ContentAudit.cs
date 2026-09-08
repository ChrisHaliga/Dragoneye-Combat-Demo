using System.Collections.Generic;
using System.Text;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Game.Creatures;
using UnityEditor;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Checks every authored asset is where it belongs, named as it should be, and reachable from
    /// the catalog that has to find it. Repairs the two things that can be repaired safely.
    ///
    /// Unity resolves references by the GUID in an asset's <c>.meta</c>, never by path, so moving
    /// content does not break a scene, a prefab or a catalog entry as long as the meta moves with
    /// it. What moving content *does* break is anything that names a path -- an editor step, a
    /// stylesheet -- and anything that quietly relied on a folder scan. This is the check that
    /// says so out loud instead of leaving it to a missing creature at the draft board.
    ///
    /// It repairs two things and no others. A catalog entry whose asset was deleted is dropped,
    /// and an asset the catalog has never heard of is added. Everything else it finds -- an asset
    /// in the wrong folder, a file named against the convention, two things claiming one id -- it
    /// reports and leaves alone, because each of those is a decision and none of them is this
    /// step's to make.
    /// </summary>
    static class ContentAudit
    {
        const string k_Content = "Assets/Content";
        const string k_CatalogPath = k_Content + "/ContentCatalog.asset";
        const string k_CreatureCatalogPath = k_Content + "/Creatures/CreatureCatalog.asset";

        /// <summary>Where each kind of thing lives. The folder an asset is in is what it is.</summary>
        static readonly (string Folder, string Type)[] k_Homes =
        {
            (k_Content + "/Classes", nameof(ClassAsset)),
            (k_Content + "/Species", nameof(SpeciesDefinition)),
            (k_Content + "/Skills", nameof(SkillAsset)),
            (k_Content + "/Equipment", nameof(EquipmentAsset)),
            (k_Content + "/Creatures", nameof(CreatureDefinition))
        };

        static int s_Faults;
        static int s_Repairs;

        [MenuItem("ClaudeCode/Check The Content Is Wired")]
        internal static void Run()
        {
            s_Faults = 0;
            s_Repairs = 0;

            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(k_CatalogPath);

            if (catalog == null)
            {
                Fault($"No content catalog at {k_CatalogPath}. Run Seed Missing Character Content.");
                Report();
                return;
            }

            HomesAreRight();
            NamesFollowTheConvention();
            EveryAssetKnowsItsScript();

            var serialized = new SerializedObject(catalog);
            Reconcile(serialized.FindProperty("m_Species"), Everything<SpeciesDefinition>(), "species");
            Reconcile(serialized.FindProperty("m_Classes"), Everything<ClassAsset>(), "classes");
            Reconcile(serialized.FindProperty("m_Equipment"), Everything<EquipmentAsset>(), "equipment");
            Reconcile(serialized.FindProperty("m_Skills"), Everything<SkillAsset>(), "skills");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            catalog.Invalidate();
            EditorUtility.SetDirty(catalog);

            IdsAreUnique(Everything<SpeciesDefinition>(), a => a.Id, a => a.DisplayName, "species");
            IdsAreUnique(Everything<ClassAsset>(), a => a.Id, a => a.DisplayName, "class");
            IdsAreUnique(Everything<EquipmentAsset>(), a => a.Id, a => a.DisplayName, "equipment");
            IdsAreUnique(Everything<SkillAsset>(), a => a.Id, a => a.DisplayName, "skill");

            EquipmentSitsInItsSlot();
            Creatures();

            AssetDatabase.SaveAssets();
            Report();
        }

        // ---------- where things live ----------

        /// <summary>
        /// Every asset of a kind is under the folder for that kind, and nothing else is.
        ///
        /// The point of the layout: you can tell what an asset is from where it is. An asset that
        /// drifts out of its folder makes that false for every asset, because the reader can no
        /// longer trust the rule.
        /// </summary>
        static void HomesAreRight()
        {
            foreach (var (folder, type) in k_Homes)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    Fault($"{folder} does not exist, so nothing of type {type} has anywhere to live.");
                    continue;
                }

                foreach (var guid in AssetDatabase.FindAssets($"t:{type}"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);

                    if (!path.StartsWith(folder + "/", System.StringComparison.Ordinal))
                    {
                        Fault($"{path} is a {type} but does not live under {folder}.");
                    }
                }
            }
        }

        /// <summary>
        /// A file is named for what it holds: the display name, every word capitalised, run
        /// together. No type prefix -- the folder already said that.
        /// </summary>
        static void NamesFollowTheConvention()
        {
            CheckNames(Everything<SpeciesDefinition>(), a => a.DisplayName);
            CheckNames(Everything<ClassAsset>(), a => a.DisplayName);
            CheckNames(Everything<EquipmentAsset>(), a => a.DisplayName);
            CheckNames(Everything<SkillAsset>(), a => a.DisplayName);
        }

        static void CheckNames<T>(IReadOnlyList<T> assets, System.Func<T, string> nameOf)
            where T : Object
        {
            foreach (var asset in assets)
            {
                var wanted = FileName(nameOf(asset));

                if (!string.IsNullOrEmpty(wanted) && asset.name != wanted)
                {
                    Fault($"{AssetDatabase.GetAssetPath(asset)} holds \"{nameOf(asset)}\", "
                        + $"so the file should be {wanted}.asset.");
                }
            }
        }

        /// <summary>An authored name as a file name. The same rule the seeder writes by.</summary>
        static string FileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var built = new StringBuilder(name.Length);

            foreach (var word in name.Split(new[] { ' ', '-', '_' },
                         System.StringSplitOptions.RemoveEmptyEntries))
            {
                built.Append(char.ToUpperInvariant(word[0]));
                built.Append(word, 1, word.Length - 1);
            }

            return built.ToString();
        }

        /// <summary>
        /// Every asset can name the script it is an instance of.
        ///
        /// **This is the one check here that catches something the editor cannot show you.** Unity
        /// only creates a MonoScript for a type whose file is named after it, and an asset of a
        /// type without one is written with no script reference at all -- just a string naming the
        /// class. The editor resolves that string and everything looks right; a built player has
        /// no such fallback, so the asset deserialises to null. Seven classes and eleven items
        /// once shipped that way, and the only symptom was a character creator that said no
        /// classes were authored, in the build and nowhere else.
        ///
        /// So it is not reported as a naming problem. It is reported as what it is: an asset that
        /// will vanish the next time the game is built.
        /// </summary>
        static void EveryAssetKnowsItsScript()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { k_Content }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

                if (asset != null && MonoScript.FromScriptableObject(asset) == null)
                {
                    Fault($"{path} has no script reference, so it loads in the editor and is null "
                        + $"in a build. {asset.GetType().Name} needs to live in a file named after "
                        + "it, and the asset needs repointing at that script.");
                }
            }
        }

        /// <summary>A piece of equipment is filed under the slot it occupies.</summary>
        static void EquipmentSitsInItsSlot()
        {
            foreach (var item in Everything<EquipmentAsset>())
            {
                var wanted = item.Slot == EquipmentSlot.Armor ? "Armour"
                    : item.Slot == EquipmentSlot.Offhand ? "Offhand"
                    : "Weapons";

                var path = AssetDatabase.GetAssetPath(item);

                if (!path.StartsWith($"{k_Content}/Equipment/{wanted}/", System.StringComparison.Ordinal))
                {
                    Fault($"{path} goes in the {item.Slot} slot, so it belongs under "
                        + $"{k_Content}/Equipment/{wanted}.");
                }
            }
        }

        // ---------- what the catalogs can find ----------

        /// <summary>
        /// Drops entries whose asset is gone and appends assets the catalog has never heard of.
        ///
        /// The one thing here worth repairing rather than reporting: a catalog is an index, its
        /// contents are derivable from the folder, and an index that has fallen behind is a
        /// creature that silently will not spawn.
        /// </summary>
        static void Reconcile<T>(SerializedProperty list, IReadOnlyList<T> onDisk, string what)
            where T : Object
        {
            var kept = new List<Object>();
            var dropped = 0;

            for (var i = 0; i < list.arraySize; i++)
            {
                var entry = list.GetArrayElementAtIndex(i).objectReferenceValue;

                if (entry == null)
                {
                    dropped++;
                }
                else if (!kept.Contains(entry))
                {
                    kept.Add(entry);
                }
            }

            var added = 0;

            foreach (var asset in onDisk)
            {
                if (!kept.Contains(asset))
                {
                    kept.Add(asset);
                    added++;
                }
            }

            if (dropped == 0 && added == 0)
            {
                return;
            }

            list.arraySize = kept.Count;

            for (var i = 0; i < kept.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = kept[i];
            }

            s_Repairs++;
            Debug.Log($"[Content] {what}: added {added}, dropped {dropped} that no longer exist.");
        }

        /// <summary>
        /// Ids cross the network and get written into saved characters, so two assets sharing one
        /// is not a tidiness problem: it decides which of them a client resolves.
        /// </summary>
        static void IdsAreUnique<T>(IReadOnlyList<T> assets, System.Func<T, int> idOf,
            System.Func<T, string> nameOf, string what) where T : Object
        {
            var seen = new Dictionary<int, T>();

            foreach (var asset in assets)
            {
                var id = idOf(asset);

                if (seen.TryGetValue(id, out var other))
                {
                    Fault($"Two {what} assets both claim id {id}: {nameOf(other)} and {nameOf(asset)}.");
                    continue;
                }

                seen[id] = asset;
            }
        }

        static void Creatures()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CreatureCatalog>(k_CreatureCatalogPath);

            if (catalog == null)
            {
                Fault($"No creature catalog at {k_CreatureCatalogPath}.");
                return;
            }

            var onDisk = Everything<CreatureDefinition>();
            var serialized = new SerializedObject(catalog);
            Reconcile(serialized.FindProperty("m_Creatures"), onDisk, "creatures");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);

            // A premade's id is what a scenario, the draft and the network name it by. Blank ones
            // fall back to the file name, which is how two creatures end up as one.
            var seen = new Dictionary<string, CreatureDefinition>();

            foreach (var creature in onDisk)
            {
                if (string.IsNullOrWhiteSpace(creature.Id))
                {
                    Fault($"{AssetDatabase.GetAssetPath(creature)} has no id.");
                    continue;
                }

                if (seen.TryGetValue(creature.Id, out var other))
                {
                    Fault($"Two creatures claim the id \"{creature.Id}\": "
                        + $"{other.name} and {creature.name}.");
                    continue;
                }

                seen[creature.Id] = creature;
            }
        }

        static List<T> Everything<T>() where T : Object
        {
            var found = new List<T>();

            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { k_Content }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));

                if (asset != null)
                {
                    found.Add(asset);
                }
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }

        static void Fault(string message)
        {
            s_Faults++;
            Debug.LogError($"[Content] {message}");
        }

        static void Report()
        {
            if (s_Faults == 0)
            {
                Debug.Log(s_Repairs == 0
                    ? "Content is wired: every asset is where it belongs and every catalog can find it."
                    : $"Content is wired. {s_Repairs} catalog lists were brought up to date.");
                return;
            }

            Debug.LogError($"[Content] {s_Faults} problems above. None of them was repaired here: "
                + "each is a decision about what the content should be.");
        }
    }
}
