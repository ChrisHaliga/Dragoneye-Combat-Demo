using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>One element and the rune that stands for it.</summary>
    [System.Serializable]
    public struct ElementIcon
    {
        public Element Element;
        public Sprite Image;
    }

    /// <summary>
    /// The rune for each element.
    ///
    /// A picture rather than a colour. Seven coloured discs are seven things a player has to learn
    /// a legend for and then keep straight at a glance mid-fight; seven distinct shapes are seven
    /// things they already recognise by the second match. Colour still does its half of the work --
    /// the runes are coloured art -- but shape is what survives being drawn at eighteen pixels next
    /// to six others.
    ///
    /// Built by the setup step from whatever is in <c>Assets/Art/Elements</c>, matched by file
    /// name, so replacing a rune is replacing a file.
    ///
    /// <see cref="ElementPalette"/> is still the answer for text: a skill's cost is written in its
    /// element's colour, and a rune cannot colour a word.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Element Icons", fileName = "ElementIcons")]
    public sealed class ElementIconLibrary : ScriptableObject
    {
        [SerializeField, Tooltip("Rebuilt by ClaudeCode > Set Up Everything from the art folder.")]
        List<ElementIcon> m_Icons = new List<ElementIcon>();

        /// <summary>The rune for an element, or null when the art is missing.</summary>
        public Sprite Get(Element element)
        {
            foreach (var icon in m_Icons)
            {
                if (icon.Element == element)
                {
                    return icon.Image;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Where an element is turned into the picture of one.
    ///
    /// A seam rather than a reference, for the same reason <see cref="Portraits"/> is one: the
    /// arena HUD draws these from components on spawned prefabs, which cannot carry a serialised
    /// pointer to a content asset.
    ///
    /// Filled by <see cref="ContentCatalog"/> when it builds.
    /// </summary>
    public static class ElementIcons
    {
        public static ElementIconLibrary Current { get; set; }

        public static Sprite Get(Element element) => Current != null ? Current.Get(element) : null;
    }
}
