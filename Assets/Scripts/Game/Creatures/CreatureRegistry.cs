using System;
using System.Collections.Generic;

namespace Dragoneye.Game
{
    /// <summary>
    /// Every creature currently on the board.
    ///
    /// The HUD needs "all creatures in my party", which is not a question the hex-keyed
    /// <see cref="UnitIndex"/> answers -- that one exists for "what is standing here". Two different
    /// questions, two lookups, rather than one structure doing both badly.
    ///
    /// Static because creatures are spawned by netcode and the HUD is a scene object, so neither can
    /// hold a serialised reference to the other. Cleared on despawn, and every entry is removed by
    /// its own <c>OnNetworkDespawn</c>, so nothing survives a match.
    /// </summary>
    public static class CreatureRegistry
    {
        static readonly List<CreatureState> s_Creatures = new List<CreatureState>();

        public static IReadOnlyList<CreatureState> All => s_Creatures;

        /// <summary>Raised when a creature appears or disappears.</summary>
        public static event Action Changed;

        public static void Add(CreatureState creature)
        {
            if (creature != null && !s_Creatures.Contains(creature))
            {
                s_Creatures.Add(creature);
                Changed?.Invoke();
            }
        }

        public static void Remove(CreatureState creature)
        {
            if (s_Creatures.Remove(creature))
            {
                Changed?.Invoke();
            }
        }

        /// <summary>Creatures on one side, in spawn order.</summary>
        public static List<CreatureState> InParty(Party party)
        {
            var result = new List<CreatureState>();
            foreach (var creature in s_Creatures)
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
