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

        /// <summary>For a fight the reader has no side in. Neither good news nor bad.</summary>
        public const string NeutralColour = "#C8CCD6";

        /// <summary>
        /// <summary>Whether the attack got through. A tie stops it as surely as a win does.</summary>
        public static bool Landed(ClashOutcome outcome) => outcome == ClashOutcome.AttackerWins;

        /// <summary>
        /// How it came out, said about the attack rather than about either creature.
        ///
        /// It used to read WIN, TIE or LOSE from the reader's own side, which works while the
        /// reader is in the fight and falls apart the moment they are not: two creatures neither
        /// of which is yours trade blows and the screen says LOSE, and there is no way to tell
        /// what lost. What happened to the attack is the same fact for everybody watching, and
        /// whose good news it is belongs in the colour rather than in the words.
        /// </summary>
        public static string Describe(ClashOutcome outcome) =>
            Landed(outcome) ? "ATTACK SUCCEEDED" : "ATTACK FAILED";

        /// <summary>
        /// The colour it is written in: green where it went the local player's side's way, red
        /// where it did not, and neutral for a fight they are not in.
        ///
        /// By side rather than by whose creature it is. An ally's creature taking a hit is bad
        /// news whether or not the player is the one moving it.
        /// </summary>
        public static string ColourOf(ClashOutcome outcome, LogSide side)
        {
            if (side == LogSide.Neither)
            {
                return NeutralColour;
            }

            // A tie is its own news and gets its own colour, on either side of the attack. Green
            // and red are for a clash somebody won: reading a tie as a win because no damage
            // landed hides that both elements are gone, which is the part a player has to notice
            // before the hand they were counting on is empty.
            if (outcome == ClashOutcome.Tie)
            {
                return TieColour;
            }

            // Otherwise the defender's side reads it backwards from the attacker's: an attack that
            // lands is the attacker's good day and the defender's bad one.
            var good = side == LogSide.Attacker ? Landed(outcome) : !Landed(outcome);

            return good ? WinColour : LoseColour;
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
        public static string Describe(DefenceRequest request) =>
            request.Flanked && !request.Shielded
                ? "Struck from behind: put up two, the worse counts."
                : request.Shielded && !request.Flanked
                    ? "Put up two; the better counts."

                    // Nothing. A panel of eight elements with a percentage under each does not
                    // need to be captioned "answer with an element": the only two asks worth a
                    // line are the two that are not the ordinary one.
                    : string.Empty;

        /// <summary>
        /// What each of the three outcomes is worth, in three short sentences.
        ///
        /// Kept, and kept short. A defender who does not know what a tie costs is not making a
        /// decision, they are picking a colour -- and this panel is often the first thing a new
        /// player is asked to answer.
        /// </summary>
        public static string Stakes =>
            Tint(WinColour, "Win") + " and the attack does nothing and you keep your element.   "
            + Tint(TieColour, "Tie") + " and it still does nothing, but the element is spent.   "
            + Tint(LoseColour, "Lose") + " and it lands, and the element is spent.";

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
