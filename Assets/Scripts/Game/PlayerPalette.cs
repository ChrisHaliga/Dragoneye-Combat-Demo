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
        /// The colour for a player slot.
        ///
        /// Keyed on slot rather than client id on purpose: client ids are handed out by the
        /// transport and reused after a disconnect, so using one here let a player change colour
        /// mid-session. Slots are assigned once by the server -- see <see cref="PlayerRoster"/>.
        ///
        /// A negative slot (state not yet replicated) wraps to the first colour rather than
        /// throwing, so a focus point drawn a frame early looks plain rather than breaking.
        /// </summary>
        public static Color ForSlot(int slot) => k_Colors[(int)Mathf.Repeat(slot, k_Colors.Length)];
    }
}
