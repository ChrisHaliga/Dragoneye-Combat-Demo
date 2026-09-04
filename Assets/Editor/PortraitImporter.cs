using UnityEditor;
using UnityEngine;

namespace Dragoneye.MultiplayerEditor
{
    /// <summary>
    /// Keeps the portrait library in step with the folder, without anybody having to remember.
    ///
    /// Dropping a picture into <c>Assets/Art/Portraits</c> should be the whole of adding a portrait.
    /// Requiring a menu item after it is the kind of step that gets forgotten -- and when it is
    /// forgotten the symptom is "no portraits are installed for this species", which points at the
    /// art rather than at the step nobody ran.
    ///
    /// Two hooks, because they answer different questions. The preprocessor decides what a file in
    /// that folder *is* -- a sprite, not a texture -- and does it before the import rather than
    /// reimporting afterwards. The postprocessor notices that the folder changed and rebuilds the
    /// library from it.
    /// </summary>
    sealed class PortraitImporter : AssetPostprocessor
    {
        const string k_Root = "Assets/Art/Portraits";

        /// <summary>
        /// A picture in the portrait folder is a sprite.
        ///
        /// Done here rather than by reimporting later: a file dropped into a Unity project is a
        /// texture until somebody says otherwise, and saying so during the import is one pass
        /// instead of two.
        /// </summary>
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(k_Root, System.StringComparison.Ordinal))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;

            // A portrait is drawn on a 56-pixel card and a token a few pixels across. Anything
            // larger than this is memory spent on detail nobody can see.
            importer.maxTextureSize = 256;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved,
            string[] movedFrom)
        {
            if (PortraitSetup.IsRebuilding)
            {
                return;
            }

            if (Touched(imported) || Touched(deleted) || Touched(moved) || Touched(movedFrom))
            {
                PortraitSetup.Run();
            }
        }

        static bool Touched(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.Replace('\\', '/').StartsWith(k_Root, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
