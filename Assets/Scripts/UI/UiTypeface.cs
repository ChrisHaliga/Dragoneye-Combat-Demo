using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.UI
{
    /// <summary>
    /// The face every screen is set in, taken from the machine rather than shipped.
    ///
    /// Half of what made the interface read as a web page was the type: Unity's default is an
    /// Arial, and an Arial in tracked capitals over a dark panel is a dashboard whatever the panel
    /// is made of. A game of this kind is set in a serif -- Pillars, Divinity, Total War, every
    /// tabletop rulebook -- and the register changes the moment it is.
    ///
    /// No font file is added. Windows ships several suitable faces and this asks the operating
    /// system for the first it can find, in order of preference; a machine with none of them keeps
    /// the default and loses nothing but the look. The face is created once and applied to the
    /// root of each document, from which every label inherits it, so a screen opts in with one
    /// call and no stylesheet has to name a font it cannot reference.
    /// </summary>
    public static class UiTypeface
    {
        /// <summary>
        /// Candidates, best first. All of them ship with Windows; the last two also ship with
        /// macOS, so a tester on either gets a serif.
        /// </summary>
        static readonly string[] k_Faces =
        {
            "Palatino Linotype",
            "Book Antiqua",
            "Georgia",
            "Cambria"
        };

        static Font s_Face;
        static bool s_Tried;

        /// <summary>The face, or null when the machine has none of the candidates.</summary>
        public static Font Face
        {
            get
            {
                if (s_Tried)
                {
                    return s_Face;
                }

                s_Tried = true;

                var installed = Font.GetOSInstalledFontNames();

                foreach (var name in k_Faces)
                {
                    if (System.Array.IndexOf(installed, name) < 0)
                    {
                        continue;
                    }

                    // The size is the source rasterisation size for the dynamic atlas; the text
                    // is scaled from it, so it is chosen large enough that headings stay crisp.
                    s_Face = Font.CreateDynamicFontFromOSFont(name, 64);

                    if (s_Face != null)
                    {
                        break;
                    }
                }

                return s_Face;
            }
        }

        /// <summary>Sets the face on a document root. Everything below inherits it.</summary>
        public static void Apply(VisualElement root)
        {
            var face = Face;

            if (root == null || face == null)
            {
                return;
            }

            root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(face));
        }
    }
}
