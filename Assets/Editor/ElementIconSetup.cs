using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEditor;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Turns the folder of element art into the icon library.
    ///
    /// Matched by file name -- <c>Geo.png</c> is Geo -- so replacing a rune is replacing a file and
    /// nothing has to be rewired. There are exactly seven and the enum is permanent, so unlike the
    /// portraits there is no id to derive and nothing to orphan: a file whose name matches no
    /// element is simply not used, and an element with no file draws nothing rather than breaking.
    ///
    /// Import settings are fixed here as well as in <see cref="ArtImporter"/>. The importer
    /// covers anything dropped in from now on; this covers what was already sitting in the folder
    /// before it existed, which a project cloned today has plenty of.
    /// </summary>
    static class ElementIconSetup
    {
        const string k_Root = "Assets/Art/Elements";
        const string k_Library = "Assets/Settings/Characters/ElementIcons.asset";
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
        /// Called by <see cref="SetUpEverything"/>, and by the importer whenever the folder
        /// changes -- which is the path that matters, because it means replacing a rune is
        /// replacing a file and nothing else.
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

            var library = Upsert<ElementIconLibrary>(k_Library);
            var serialized = new SerializedObject(library);
            var list = serialized.FindProperty("m_Icons");
            var missing = string.Empty;

            list.arraySize = ElementInfo.All.Length;

            for (var i = 0; i < ElementInfo.All.Length; i++)
            {
                var element = ElementInfo.All[i];
                var sprite = Load(element);

                if (sprite == null)
                {
                    missing += missing.Length == 0 ? element.ToString() : ", " + element;
                }

                var entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Element").enumValueIndex = (int)element;
                entry.FindPropertyRelative("Image").objectReferenceValue = sprite;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(library);

            Attach(library);
            AssetDatabase.SaveAssets();

            if (missing.Length > 0)
            {
                Debug.LogWarning($"No element art for {missing}. Drop a file named after each "
                    + $"into {k_Root}/ -- they will draw nothing until then.");
            }
        }

        /// <summary>The sprite named after an element, whichever extension it was saved as.</summary>
        static Sprite Load(Element element)
        {
            foreach (var extension in k_Extensions)
            {
                var path = $"{k_Root}/{element}{extension}";

                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
                {
                    continue;
                }

                AsSprite(path);
                return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }

            return null;
        }

        /// <summary>
        /// Makes sure a picture is imported as a sprite.
        ///
        /// Only touched when it is wrong, so re-running the setup does not reimport the folder.
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

        /// <summary>Hands the library to the catalog, which is where everything else reads it from.</summary>
        static void Attach(ElementIconLibrary library)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalog>(k_Catalog);

            if (catalog == null)
            {
                Debug.LogWarning($"No content catalog at {k_Catalog}; the icons were not attached.");
                return;
            }

            var serialized = new SerializedObject(catalog);
            serialized.FindProperty("m_ElementIcons").objectReferenceValue = library;
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
                AssetDatabase.CreateFolder("Assets/Art", "Elements");
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
