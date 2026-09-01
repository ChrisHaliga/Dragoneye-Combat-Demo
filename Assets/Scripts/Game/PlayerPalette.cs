using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Per-player colours.
    ///
    /// A fixed palette rather than the quickstart's <c>Random.InitState(id)</c> plus
    /// <c>Random.ColorHSV()</c>: random hues land on muddy, low-contrast colours that are hard to
    /// tell apart at a glance, which is the entire job of a player colour.
    /// </summary>
    public static class PlayerPalette
    {
        // Chosen to stay distinguishable from each other and from the grid's green, and to survive
        // the most common form of colour blindness by differing in lightness as well as hue.
        static readonly Color[] k_Colors =
        {
            new Color(0.24f, 0.60f, 0.94f), // blue
            new Color(0.93f, 0.36f, 0.29f), // red
            new Color(0.98f, 0.78f, 0.24f), // amber
            new Color(0.66f, 0.42f, 0.90f), // violet
            new Color(0.20f, 0.78f, 0.66f), // teal
            new Color(0.96f, 0.51f, 0.784f) // pink
        };

        public static int Count => k_Colors.Length;

        /// <summary>
        /// The colour for a given netcode client id.
        ///
        /// Client ids are reused after a disconnect, so a player who drops and rejoins can come
        /// back a different colour. Fixing that properly needs a stable mapping from lobby player
        /// to client id, which is a separate piece of work.
        /// </summary>
        public static Color ForClient(ulong clientId) => k_Colors[clientId % (ulong)k_Colors.Length];
    }
}
