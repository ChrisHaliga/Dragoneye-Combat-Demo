using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>
    /// One portrait: a picture, the species it belongs to, and the number that names it.
    ///
    /// The id is what crosses the network and goes into a save file, not the picture. That is the
    /// whole point of the library existing -- a character's face is a small integer everybody can
    /// resolve, rather than image bytes somebody has to send.
    /// </summary>
    [System.Serializable]
    public struct PortraitEntry
    {
        [Tooltip("Stable and permanent. Derived from the file name, so renaming a portrait orphans "
             + "the characters wearing it -- they fall back to their initial.")]
        public int Id;

        public string Name;
        public Sprite Image;

        [Tooltip("Which species this face belongs to. Zero means any.")]
        public int SpeciesId;
    }

    /// <summary>
    /// Every portrait the game ships with, grouped by species.
    ///
    /// Prepackaged rather than uploaded. A picture a player picked off their own disk cannot be
    /// shown to anybody else without sending it, and a demo does not want an image transfer in the
    /// lobby -- so the pictures ship with the game and a character carries the number of the one it
    /// chose. Everybody resolves the same face from the same id.
    ///
    /// Built by the setup step from whatever is in Assets/Art/Portraits, so adding a face is
    /// dropping a file into a folder rather than editing a list.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Portrait Library", fileName = "PortraitLibrary")]
    public sealed class PortraitLibrary : ScriptableObject
    {
        [SerializeField, Tooltip("Rebuilt by ClaudeCode > Set Up Everything from the art folder.")]
        List<PortraitEntry> m_Portraits = new List<PortraitEntry>();

        readonly Dictionary<int, Sprite> m_ById = new Dictionary<int, Sprite>();
        bool m_Indexed;

        public IReadOnlyList<PortraitEntry> All => m_Portraits;

        /// <summary>The picture for an id, or null when nothing answers to it.</summary>
        public Sprite Get(int id)
        {
            if (id == 0)
            {
                return null;
            }

            Index();
            return m_ById.TryGetValue(id, out var sprite) ? sprite : null;
        }

        /// <summary>
        /// The faces a member of this species may wear.
        ///
        /// Entries with no species belong to everybody, so a set of generic faces can be shared
        /// rather than copied into every folder.
        /// </summary>
        public List<PortraitEntry> ForSpecies(int speciesId)
        {
            var matching = new List<PortraitEntry>();

            foreach (var entry in m_Portraits)
            {
                if (entry.Image != null && (entry.SpeciesId == speciesId || entry.SpeciesId == 0))
                {
                    matching.Add(entry);
                }
            }

            return matching;
        }

        /// <summary>The first face this species has, for a character that has not chosen one.</summary>
        public int DefaultFor(int speciesId)
        {
            var matching = ForSpecies(speciesId);
            return matching.Count > 0 ? matching[0].Id : 0;
        }

        void Index()
        {
            if (m_Indexed)
            {
                return;
            }

            m_ById.Clear();

            foreach (var entry in m_Portraits)
            {
                if (entry.Image != null)
                {
                    m_ById[entry.Id] = entry.Image;
                }
            }

            m_Indexed = true;
        }

        void OnValidate() => m_Indexed = false;
    }

    /// <summary>
    /// Where a portrait id is turned back into a picture.
    ///
    /// A seam rather than a reference, for the same reason the skill catalog is one: creatures live
    /// on a spawned prefab and cannot carry a serialised pointer to a content asset, and a lookup
    /// through the arena on every draw would tie the HUD to a scene being loaded.
    ///
    /// Filled by <see cref="ContentCatalog"/> when it builds, which is the first thing any screen or
    /// match does with content -- so by the time anything wants a face, this knows them.
    /// </summary>
    public static class Portraits
    {
        public static PortraitLibrary Current { get; set; }

        public static Sprite Get(int id) => Current != null ? Current.Get(id) : null;
    }
}
