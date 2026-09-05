using System.Collections.Generic;
using System.IO;
using Dragoneye.Data;
using UnityEditor;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Turns a folder of pictures into the portrait library.
    ///
    /// Adding a face is dropping a file into <c>Assets/Art/Portraits/&lt;Species&gt;/</c>. The
    /// subfolder names a species by its display name; anything loose in the root, or in a folder
    /// that matches no species, belongs to everybody. Nothing has to be wired up by hand, which is
    /// the point -- a list a designer has to remember to edit is a list that goes stale.
    ///
    /// Ids are derived from the species and the file name, so they are the same on every machine
    /// and survive a rebuild. Renaming a picture changes its id and orphans the characters wearing
    /// it; they fall back to their initial rather than breaking, and that is the trade for not
    /// keeping a hand-maintained id column.
    ///
    /// Import settings are set here too. A picture dropped into a Unity project is a texture until
    /// somebody says otherwise, and a texture is not a sprite.
    /// </summary>
    static class PortraitSetup
    {
        const string k_Root = "Assets/Art/Portraits";
        const string k_Library = "Assets/Settings/Characters/PortraitLibrary.asset";
        const string k_Catalog = "Assets/Settings/Characters/ContentCatalog.asset";

        static readonly string[] k_Extensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>
        /// True while this is writing assets of its own.
        ///
        /// Fixing the import settings of a picture reimports it, which is a change to the folder,
        /// which is what <see cref="ArtImporter"/> watches for -- so without this the two would
        /// call each other until the editor gave up.
        /// </summary>
        internal static bool IsRebuilding { get; private set; }

        /// <summary>
        /// Runs the whole step.
        ///
        /// Called by <see cref="SetUpEverything"/>, and by the importer whenever the folder changes
        /// -- which is the path that matters, because it means adding a portrait is dropping a file
        /// in and nothing else.
        /// </summary>
        internal static void Run()
        {
            if (IsRebuilding)
            {
                return;
            }

            IsRebuilding = true;

            try
            {
                Rebuild();
            }
            finally
            {
                IsRebuilding = false;
            }
        }

        static void Rebuild()
        {
            EnsureFolders();

            var species = SpeciesByName();
            var entries = new List<PortraitEntry>();

            Collect(k_Root, 0, entries);

            foreach (var directory in Directory.GetDirectories(k_Root))
            {
                var folder = Path.GetFileName(directory);
                var id = species.TryGetValue(folder, out var match) ? match : 0;

                Collect(directory.Replace('\\', '/'), id, entries);
            }

            var library = Upsert<PortraitLibrary>(k_Library);
            var serialized = new SerializedObject(library);
            var list = serialized.FindProperty("m_Portraits");

            list.arraySize = entries.Count;

            for (var i = 0; i < entries.Count; i++)
            {
                var element = list.GetArrayElementAtIndex(i);

                element.FindPropertyRelative("Id").intValue = entries[i].Id;
                element.FindPropertyRelative("Name").stringValue = entries[i].Name;
                element.FindPropertyRelative("Image").objectReferenceValue = entries[i].Image;
                element.FindPropertyRelative("SpeciesId").intValue = entries[i].SpeciesId;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(library);

            Attach(library);

            AssetDatabase.SaveAssets();

            if (entries.Count == 0)
            {
                Debug.LogWarning($"No portraits found. Drop images into {k_Root}/<Species>/.");
            }
        }

        /// <summary>Every image in one folder, as entries belonging to one species.</summary>
        static void Collect(string folder, int speciesId, List<PortraitEntry> into)
        {
            foreach (var path in Directory.GetFiles(folder))
            {
                var asset = path.Replace('\\', '/');

                if (System.Array.IndexOf(k_Extensions,
                        Path.GetExtension(asset).ToLowerInvariant()) < 0)
                {
                    continue;
                }

                AsSprite(asset);

                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(asset);

                if (sprite == null)
                {
                    Debug.LogWarning($"{asset} could not be read as a sprite; skipping it.");
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(asset);

                into.Add(new PortraitEntry
                {
                    Id = StableId(speciesId, name),
                    Name = name,
                    Image = sprite,
                    SpeciesId = speciesId
                });
            }
        }

        /// <summary>
        /// Makes sure a picture is imported as a sprite.
        ///
        /// A file dropped into a Unity project is a texture until somebody says otherwise, and a
        /// texture cannot be drawn as a portrait. Only touched when it is wrong, so re-running does
        /// not reimport the whole folder.
        /// </summary>
        static void AsSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null || importer.textureType == TextureImporterType.Sprite)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;

            importer.SaveAndReimport();
        }

        /// <summary>
        /// A number for a portrait, the same on every machine.
        ///
        /// FNV-1a over the species and the file name. Hand-assigning these would mean a designer
        /// keeping a column of numbers in step with a folder of files, which is exactly the kind of
        /// bookkeeping that is wrong the first time somebody is in a hurry.
        /// </summary>
        static int StableId(int speciesId, string name)
        {
            unchecked
            {
                var hash = 2166136261;

                foreach (var c in $"{speciesId}/{name}")
                {
                    hash = (hash ^ c) * 16777619;
                }

                // Positive and never zero, which means "no portrait".
                var id = (int)(hash & 0x7FFFFFFF);
                return id == 0 ? 1 : id;
            }
        }

        /// <summary>Species by display name, which is what the folders are named after.</summary>
        static Dictionary<string, int> SpeciesByName()
        {
            var byName = new Dictionary<string, int>();

            foreach (var guid in AssetDatabase.FindAssets("t:SpeciesDefinition"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<SpeciesDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (asset != null && !byName.ContainsKey(asset.DisplayName))
                {
                    byName[asset.DisplayName] = asset.Id;
                }
            }

            return byName;
        }

        /// <summary>Hands the library to the catalog, which is where everything else reads it from.</summary>
        static void Attach(PortraitLibrary library)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(k_Catalog);

            if (catalog == null)
            {
                Debug.LogWarning($"No content catalog at {k_Catalog}; the library was not attached.");
                return;
            }

            var serialized = new SerializedObject(catalog);
            serialized.FindProperty("m_Portraits").objectReferenceValue = library;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(catalog);
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Art"))
            {
                AssetDatabase.CreateFolder("Assets", "Art");
            }

            if (!AssetDatabase.IsValidFolder(k_Root))
            {
                AssetDatabase.CreateFolder("Assets/Art", "Portraits");
            }
        }

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
    }
}
