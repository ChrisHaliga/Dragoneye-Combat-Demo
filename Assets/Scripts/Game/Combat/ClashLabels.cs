using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
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
        /// A hotter red than an outcome, for something about to go wrong rather than something
        /// that already has. Warnings have to out-shout a cursor label that is already three lines
        /// long, and the muted red of a lost clash reads as one more fact on it.
        /// </summary>
        public const string DangerColour = "#F05A3C";

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
        /// <summary>
        /// The one line above the answers: what is being asked, and the key to the three numbers
        /// on each of them.
        ///
        /// One line, and short. This panel goes up in the middle of somebody else's turn, on top
        /// of the result of the last attack, and every sentence it spends explaining the stakes is
        /// a sentence in front of the board. What win, tie and lose are worth is on the options
        /// themselves; this only says which is which.
        /// </summary>
        public static string Describe(DefenceRequest request)
        {
            var ask = request.Flanked && !request.Shielded
                ? "Struck from behind: put up two, the worse counts."
                : request.Shielded && !request.Flanked
                    ? "Put up two; the better counts."
                    : "Answer with an element.";

            return ask + "   " + OddsKey;
        }

        /// <summary>The word shown where an action's cost would be, when position has changed it.</summary>
        public const string Advantage = "ADVANTAGE";

        /// <summary>
        /// What an attacker is playing for, under the odds.
        ///
        /// The defender's prompt spells its stakes out and the attacker's cursor never did, which
        /// left one side of every clash reading three percentages with nothing attached to them.
        /// Both halves matter and neither is obvious: a tie stops the attack as thoroughly as a
        /// loss does, and the element is gone whichever of the three comes up -- an attack is not
        /// refunded for having been answered well.
        /// </summary>
        public const string AttackerStakes =
            "Only a win lands it; your element is spent either way.";

        /// <summary>
        /// Said before a move that somebody is watching.
        ///
        /// The warning is the feature. An opportunity attack that arrived unannounced would be a
        /// punishment for not having memorised six facing arcs; announced, it is the reason to
        /// spend a turn getting round somebody the long way, or to want a skill that does not
        /// provoke.
        /// </summary>
        /// <summary>"72% to hit", for a shot.</summary>
        public static string Chance(int percent) => $"{percent}% to hit";

        /// <summary>Who the shot flies over, and what that costs. In the danger colour.</summary>
        public static string Cover(string names, int penalty) =>
            Tint(DangerColour, $"! Firing past {names}: -{penalty} to hit");

        public static string Provokes =>
            Tint(DangerColour, "! Moving draws an opportunity attack");

        /// <summary>What the three numbers on a prompt are, in the order they are written.</summary>
        public static string OddsKey =>
            Tint(WinColour, "WIN") + " / " + Tint(TieColour, "TIE") + " / "
            + Tint(LoseColour, "LOSE");

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
