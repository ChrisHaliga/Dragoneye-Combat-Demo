using UnityEditor;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Keeps the art libraries in step with the folders that feed them, without anybody having to
    /// remember.
    ///
    /// Dropping a picture into <c>Assets/Art/Portraits</c> or <c>Assets/Art/Elements</c> should be
    /// the whole of adding one. Requiring a menu item afterwards is the kind of step that gets
    /// forgotten, and when it is forgotten the symptom points at the art -- "no portraits are
    /// installed for this species", or an element still drawn as the plain coloured disc it used to
    /// be -- rather than at the step nobody ran. That has cost this project a day more than once.
    ///
    /// Two hooks, because they answer different questions. The preprocessor decides what a file in
    /// one of these folders *is* -- a sprite, not a texture -- and does it during the import rather
    /// than reimporting afterwards. The postprocessor notices the folder changed and rebuilds
    /// whatever library reads from it.
    ///
    /// One importer for both folders rather than one each: they want the same import settings and
    /// differ only in which library they feed, so the second copy would exist to hold one line.
    /// </summary>
    sealed class ArtImporter : AssetPostprocessor
    {
        const string k_Portraits = "Assets/Art/Portraits";
        const string k_Elements = "Assets/Art/Elements";

        /// <summary>
        /// A picture in an art folder is a sprite.
        ///
        /// Done here rather than by reimporting later: a file dropped into a Unity project is a
        /// texture until somebody says otherwise, and saying so during the import is one pass
        /// instead of two.
        /// </summary>
        void OnPreprocessTexture()
        {
            if (!Under(assetPath, k_Portraits) && !Under(assetPath, k_Elements))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved,
            string[] movedFrom)
        {
            if (Touched(k_Portraits, imported, deleted, moved, movedFrom)
                && !PortraitSetup.IsRebuilding)
            {
                PortraitSetup.Run();
            }

            if (Touched(k_Elements, imported, deleted, moved, movedFrom)
                && !ElementIconSetup.IsRebuilding)
            {
                ElementIconSetup.Run();
            }
        }

        static bool Touched(string root, params string[][] batches)
        {
            foreach (var batch in batches)
            {
                foreach (var path in batch)
                {
                    if (Under(path, root))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static bool Under(string path, string root) =>
            path.Replace('\\', '/').StartsWith(root, System.StringComparison.Ordinal);
    }
}
