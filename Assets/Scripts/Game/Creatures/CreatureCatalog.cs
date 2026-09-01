using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Turns creature assets into something the wire can carry, and back again.
    ///
    /// ScriptableObject references cannot be replicated, so every creature travels as a
    /// <see cref="ushort"/>. The id is derived from the definition's authored <c>Id</c> string, not
    /// its position in the array: an index breaks the moment someone reorders or deletes an entry,
    /// and it breaks *silently* -- the wrong creature appears rather than anything erroring.
    ///
    /// Because a 16-bit hash can collide, the mapping is built once and collisions are reported
    /// loudly at that point, where the fix is renaming an asset, rather than at match time.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Creature Catalog", fileName = "CreatureCatalog")]
    public sealed class CreatureCatalog : ScriptableObject
    {
        /// <summary>Reserved. Means "no creature", so a default-initialised id is never valid.</summary>
        public const ushort NoCreature = 0;

        [SerializeField]
        CreatureDefinition[] m_Creatures = new CreatureDefinition[0];

        readonly Dictionary<ushort, CreatureDefinition> m_ById = new Dictionary<ushort, CreatureDefinition>();
        readonly Dictionary<CreatureDefinition, ushort> m_IdOf = new Dictionary<CreatureDefinition, ushort>();

        bool m_Built;

        public IReadOnlyList<CreatureDefinition> Creatures => m_Creatures;

        public int Count => m_Creatures != null ? m_Creatures.Length : 0;

        /// <summary>The definition for a network id, or null if it does not resolve.</summary>
        public CreatureDefinition Resolve(ushort id)
        {
            Build();
            return m_ById.TryGetValue(id, out var definition) ? definition : null;
        }

        /// <summary>The network id for a definition, or <see cref="NoCreature"/> if it is not catalogued.</summary>
        public ushort IdOf(CreatureDefinition definition)
        {
            if (definition == null)
            {
                return NoCreature;
            }

            Build();
            return m_IdOf.TryGetValue(definition, out var id) ? id : NoCreature;
        }

        /// <summary>
        /// Stable 16-bit hash of an id string.
        ///
        /// FNV-1a folded in half rather than <c>string.GetHashCode</c>, which is randomised per
        /// process in modern .NET -- two clients would disagree about every creature.
        /// </summary>
        public static ushort HashId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NoCreature;
            }

            unchecked
            {
                const uint offsetBasis = 2166136261;
                const uint prime = 16777619;

                var hash = offsetBasis;
                foreach (var c in id)
                {
                    hash ^= c;
                    hash *= prime;
                }

                var folded = (ushort)((hash >> 16) ^ (hash & 0xFFFF));

                // Never hand back the reserved value; nudging one id is invisible and keeps
                // "0 means nothing" true.
                return folded == NoCreature ? (ushort)1 : folded;
            }
        }

        void OnEnable() => m_Built = false;

        void OnValidate() => m_Built = false;

        void Build()
        {
            if (m_Built)
            {
                return;
            }

            m_Built = true;
            m_ById.Clear();
            m_IdOf.Clear();

            if (m_Creatures == null)
            {
                return;
            }

            foreach (var creature in m_Creatures)
            {
                if (creature == null)
                {
                    continue;
                }

                var id = HashId(creature.Id);

                if (m_ById.TryGetValue(id, out var existing) && existing != creature)
                {
                    Debug.LogError(
                        $"Creature id collision: '{creature.Id}' and '{existing.Id}' both hash to {id}. "
                        + "Rename one of them.", this);
                    continue;
                }

                m_ById[id] = creature;
                m_IdOf[creature] = id;
            }
        }
    }
}
