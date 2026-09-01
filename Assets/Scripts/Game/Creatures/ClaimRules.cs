using System.Collections.Generic;

namespace Dragoneye.Game
{
    /// <summary>
    /// How many creatures each player in a party may claim, and what to release when that changes.
    ///
    /// Pure and static so the arithmetic can be checked without a session, and -- more importantly --
    /// so it produces the same answer on every machine. Every client renders caps; if two of them
    /// disagreed, players would see different claim buttons enabled.
    /// </summary>
    public static class ClaimRules
    {
        /// <summary>
        /// The claim cap for one player.
        /// </summary>
        /// <param name="playerOrdinal">
        /// Position in ascending slot order *within the party*. Using the raw slot would make the cap
        /// depend on who else is in the match rather than on who is in this party.
        /// </param>
        /// <remarks>
        /// The remainder goes to the lowest ordinals, so the caps always sum to the creature count
        /// exactly -- no creature is left unclaimable by everyone.
        /// </remarks>
        public static int CapFor(int creatureCount, int playerCount, int playerOrdinal)
        {
            if (playerCount <= 0 || creatureCount <= 0 || playerOrdinal < 0 || playerOrdinal >= playerCount)
            {
                return 0;
            }

            var baseCap = creatureCount / playerCount;
            var remainder = creatureCount % playerCount;
            return baseCap + (playerOrdinal < remainder ? 1 : 0);
        }

        /// <summary>
        /// Which of a player's claims must be given up to fit a new cap.
        ///
        /// Newest first, by claim sequence. Releasing the oldest would take away the creatures a
        /// player has had longest and most likely built a plan around; releasing the newest undoes
        /// the most recent decision, which is the one they are still holding in their head.
        /// </summary>
        /// <param name="claims">
        /// The player's claims as (entry index, claim sequence). Order does not matter.
        /// </param>
        /// <returns>Entry indices to release, newest claim first. Empty when already within cap.</returns>
        public static List<int> ClaimsToRelease(IReadOnlyList<(int EntryIndex, uint Sequence)> claims, int cap)
        {
            var release = new List<int>();
            if (claims == null)
            {
                return release;
            }

            var excess = claims.Count - (cap < 0 ? 0 : cap);
            if (excess <= 0)
            {
                return release;
            }

            var ordered = new List<(int EntryIndex, uint Sequence)>(claims);

            // Descending sequence, then descending index so the result is deterministic even if two
            // claims somehow share a sequence number.
            ordered.Sort((a, b) => a.Sequence != b.Sequence
                ? b.Sequence.CompareTo(a.Sequence)
                : b.EntryIndex.CompareTo(a.EntryIndex));

            for (var i = 0; i < excess; i++)
            {
                release.Add(ordered[i].EntryIndex);
            }

            return release;
        }
    }
}
