using Dragoneye.Hex.Systems;
using Dragoneye.Scenarios;
using UnityEngine;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game
{
    /// <summary>
    /// The map the host picked, as it reaches the arena.
    ///
    /// The pick is an index on the draft, which every machine has by the time the arena loads.
    /// The arena wakes on the map its scene assigns and is rebuilt onto the chosen one here,
    /// before anything is spawned or drawn for long -- on the host and on every client alike,
    /// from the same recipe, so the boards agree.
    /// </summary>
    public static class ChosenMap
    {
        public static MapChoice Current =>
            MapLibrary.At(DraftState.Current != null ? DraftState.Current.MapIndex : 0);

        /// <summary>Rebuilds the arena onto the chosen map.</summary>
        public static void Apply(ArenaMap arena)
        {
            if (arena == null)
            {
                return;
            }

            // Loud, both ways. A match on the wrong map is a bug that looks like a level design
            // decision, and the last one hid for a whole build because nothing said which map the
            // arena had been built on or that the pick had been lost on the way.
            if (DraftState.Current == null)
            {
                Debug.LogWarning("The arena was built with no draft to read the map from; "
                    + $"playing on {MapLibrary.Default.Title}.");
            }

            var choice = Current;
            arena.Rebuild(choice.Build());
            Debug.Log($"Arena built on {choice.Title}.");
        }
    }
}
