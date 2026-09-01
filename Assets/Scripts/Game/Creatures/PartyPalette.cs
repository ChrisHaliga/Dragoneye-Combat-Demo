using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Party colours for the board.
    ///
    /// Kept apart from <see cref="PlayerPalette"/> on purpose: the two answer different questions and
    /// appear together on the same ring, so they must stay visually distinct. Sharing one table would
    /// eventually put the same colour on "the Heroes" and "player 1".
    /// </summary>
    public static class PartyPalette
    {
        static readonly Color[] k_Colors =
        {
            new Color(0.30f, 0.65f, 0.35f), // Heroes  - green
            new Color(0.72f, 0.25f, 0.30f), // Monsters - crimson
            new Color(0.30f, 0.45f, 0.75f), // Guards  - steel blue
            new Color(0.62f, 0.45f, 0.20f)  // Bandits - ochre
        };

        public static Color ForParty(Party party) =>
            k_Colors[(int)Mathf.Repeat((int)party, k_Colors.Length)];

        public static string NameOf(Party party) => party.ToString();
    }
}
