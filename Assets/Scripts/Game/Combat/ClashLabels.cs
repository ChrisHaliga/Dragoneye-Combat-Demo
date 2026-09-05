using System.Collections.Generic;
using Dragoneye.Combat;

namespace Dragoneye.Game
{
    /// <summary>
    /// The words a clash is announced in.
    ///
    /// Separate from the rules and in a different assembly, for the same reason
    /// <see cref="ActionLabels"/> is: <c>Dragoneye.Combat</c> decides outcomes and has no business
    /// holding English. One place for the wording all the same, so a new outcome cannot be added
    /// without somebody deciding what the player is told about it.
    /// </summary>
    public static class ClashLabels
    {
        /// <summary>What a side put up, as it rises off their head.</summary>
        public static string Committed(IReadOnlyList<Element> elements)
        {
            if (elements == null || elements.Count == 0)
            {
                return "no answer";
            }

            var text = string.Empty;

            foreach (var element in elements)
            {
                text += text.Length == 0
                    ? ElementInfo.ShortNameOf(element)
                    : " + " + ElementInfo.ShortNameOf(element);
            }

            return text;
        }

        /// <summary>Green. Won: no damage, and the element stays in the hand.</summary>
        public const string WinColour = "#7FBF6A";

        /// <summary>Gold. Tied: no damage, but the element is gone.</summary>
        public const string TieColour = "#E2BF7A";

        /// <summary>Red. Lost: damage, and the element is gone.</summary>
        public const string LoseColour = "#D9705E";

        /// <summary>
        /// How it came out, for whoever is reading it.
        ///
        /// Win, tie and lose, and always from the reader's own side -- an attacker reading "LOSE"
        /// means their attack lost, and a defender reading it means they took the hit. Naming the
        /// outcome after what happened to the attack instead ("through", "stopped") meant every
        /// player had to work out which end of it they were on first.
        /// </summary>
        public static string Describe(ClashOutcome outcome, bool asAttacker)
        {
            var mine = asAttacker ? (int)outcome : -(int)outcome;

            return mine > 0 ? "WIN" : mine < 0 ? "LOSE" : "TIE";
        }

        /// <summary>The colour that outcome is written in.</summary>
        public static string ColourOf(ClashOutcome outcome, bool asAttacker)
        {
            var mine = asAttacker ? (int)outcome : -(int)outcome;

            return mine > 0 ? WinColour : mine < 0 ? LoseColour : TieColour;
        }

        /// <summary>Why a defender is being asked for two elements rather than one.</summary>
        public static string Describe(DefenceRequest request)
        {
            const string stakes = "Win and you take nothing and keep it; tie and you take "
                + "nothing and lose it; lose and you take the hit and lose it.";

            if (request.Flanked && !request.Shielded)
            {
                return "Struck from behind. Two elements, and the worse of them answers. " + stakes;
            }

            if (request.Shielded && !request.Flanked)
            {
                return "Two elements, and the better of them answers. " + stakes;
            }

            return "One element. " + stakes;
        }

        /// <summary>The word shown where an action's cost would be, when position has changed it.</summary>
        public const string Advantage = "ADVANTAGE";

        /// <summary>
        /// How a clash is expected to go, in three coloured numbers.
        ///
        /// Always from the reader's own side, and always in the same order and the same colours --
        /// green for a win, gold for a tie, red for a loss -- so the shape of the row is readable
        /// before any of the digits are.
        ///
        /// Rounded to whole percent and forced to sum to a hundred, so three numbers on a screen
        /// never add up to ninety-nine and make somebody wonder what the missing one was.
        /// </summary>
        public static string Forecast(ClashOdds odds)
        {
            odds.AsPercent(out var win, out var tie, out var loss);

            return Tint(WinColour, $"{win}% WIN") + "  \u00b7  "
                + Tint(TieColour, $"{tie}% TIE") + "  \u00b7  "
                + Tint(LoseColour, $"{loss}% LOSE");
        }

        /// <summary>The same three, short enough to sit under an element on the prompt.</summary>
        public static string Chances(ClashOdds odds)
        {
            odds.AsPercent(out var win, out var tie, out var loss);

            return Tint(WinColour, $"{win}") + Tint(TieColour, $" / {tie}")
                + Tint(LoseColour, $" / {loss}");
        }

        /// <summary>Rich text, which UI Toolkit labels understand out of the box.</summary>
        static string Tint(string colour, string text) => $"<color={colour}>{text}</color>";
    }
}
