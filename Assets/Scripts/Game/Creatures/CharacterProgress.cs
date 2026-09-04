using Dragoneye.Data;

namespace Dragoneye.Game
{
    /// <summary>
    /// Writes experience earned in a match onto the character it was earned by.
    ///
    /// Banked as it arrives rather than at the end of the match. A player who is disconnected, or
    /// who leaves after the last kill and before the outcome banner, has still earned what they
    /// earned -- and there is no end-of-match hook that survives every way a match can stop.
    ///
    /// Idempotent, which is what makes that safe: the server replicates a running total per slot,
    /// and this banks the difference between that total and what it has already written. Replaying
    /// the same total changes nothing, so a late join, a re-send or a duplicate event cannot pay
    /// twice.
    ///
    /// Only the local player's own character is touched. Everyone else's save folder is on another
    /// machine, and their totals are theirs to bank.
    /// </summary>
    public static class CharacterProgress
    {
        static string s_BankedFor;
        static int s_Banked;

        /// <summary>
        /// Records that this player's character has earned <paramref name="total"/> so far.
        /// </summary>
        /// <param name="slot">The local player's slot, or negative when they have none.</param>
        public static void Bank(int slot, int total)
        {
            var character = SelectedCharacter.Current;

            if (slot < 0 || character == null || total <= 0)
            {
                return;
            }

            // A different character means a different tally. Playing as somebody else must not
            // inherit the last one's banked total and swallow their first few kills.
            if (s_BankedFor != character.Id)
            {
                s_BankedFor = character.Id;
                s_Banked = 0;
            }

            var earned = total - s_Banked;

            if (earned <= 0)
            {
                return;
            }

            character.Build.Xp += earned;
            s_Banked = total;

            CharacterStore.Save(character);
        }

        /// <summary>
        /// Forgets what has been banked, so the next match starts its tally from nothing.
        ///
        /// Called when a match ends. Without it, a second match whose server total starts again at
        /// one would look like a total that had gone backwards, and nothing would ever be banked.
        /// </summary>
        public static void Reset()
        {
            s_BankedFor = null;
            s_Banked = 0;
        }
    }
}
