using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Every creature currently on the board.
    ///
    /// The HUD needs "all creatures in my party", which the hex-keyed <see cref="UnitIndex"/> cannot
    /// answer -- that one exists for "what is standing here". Two different questions, two lookups,
    /// rather than one structure doing both badly.
    ///
    /// A component reached through <see cref="ArenaContext"/> rather than a static, matching
    /// <see cref="UnitIndex"/>. A static list outlives the arena: it survives a domain reload being
    /// disabled, and a match that ends abnormally leaves entries behind for the next one. Scoping it
    /// to the scene makes that impossible instead of merely unlikely.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreatureRegistry : MonoBehaviour
    {
        readonly List<CreatureState> m_Creatures = new List<CreatureState>();

        public IReadOnlyList<CreatureState> All => m_Creatures;

        /// <summary>Raised when a creature appears or disappears.</summary>
        public event Action Changed;

        public void Add(CreatureState creature)
        {
            if (creature != null && !m_Creatures.Contains(creature))
            {
                m_Creatures.Add(creature);
                Changed?.Invoke();
            }
        }

        public void Remove(CreatureState creature)
        {
            if (m_Creatures.Remove(creature))
            {
                Changed?.Invoke();
            }
        }

        /// <summary>Creatures on one side, in spawn order.</summary>
        public List<CreatureState> InParty(Party party)
        {
            var result = new List<CreatureState>();
            foreach (var creature in m_Creatures)
            {
                if (creature != null && creature.Party == party)
                {
                    result.Add(creature);
                }
            }

            return result;
        }
    }
}
