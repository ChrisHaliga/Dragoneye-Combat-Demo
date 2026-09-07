using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Which side of a clash the person reading the log is on.
    ///
    /// Not a detail of the clash -- the same exchange is read from three positions at once, and the
    /// only thing that changes between them is the colour.
    /// </summary>
    public enum LogSide
    {
        Neither,
        Attacker,
        Defender
    }

    /// <summary>
    /// The wording of a combat log line, without any of the looking-up.
    ///
    /// Split from <see cref="CombatLogView"/> for the reason <see cref="ClashLabels"/> is split
    /// from the rules: everything here is a pure function of what it is handed, so the sentences
    /// can be reasoned about without a scene, and the view is left doing nothing but resolving
    /// names and appending labels.
    ///
    /// Every colour comes from a palette something else already uses -- elements from
    /// <see cref="ElementPalette"/>, outcomes from <see cref="ClashLabels"/> -- so a rune in the
    /// log is the colour it is everywhere else on the screen.
    /// </summary>
    public static class CombatLogLines
    {
        /// <summary>Rich text, which UI Toolkit labels understand out of the box.</summary>
        public static string Tint(string colour, string text) =>
            $"<color={colour}>{text}</color>";

        /// <summary>The same, from a palette colour rather than a literal.</summary>
        public static string Tint(Color colour, string text) =>
            Tint("#" + ColorUtility.ToHtmlStringRGB(colour), text);

        /// <summary>An element's short name, in its own colour.</summary>
        public static string Rune(Element element) =>
            Tint(ElementPalette.ForElement(element), ElementInfo.ShortNameOf(element));

        /// <summary>
        /// A handful of elements, counted rather than listed.
        ///
        /// "2 PYR" and not "PYR + PYR": a defence can put up two of the same thing, and a line that
        /// repeats itself reads as two separate events.
        /// </summary>
        public static string Runes(IReadOnlyList<Element> elements)
        {
            if (elements == null || elements.Count == 0)
            {
                return string.Empty;
            }

            var text = string.Empty;

            foreach (var element in ElementInfo.All)
            {
                var count = 0;

                foreach (var held in elements)
                {
                    if (held == element)
                    {
                        count++;
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                var one = Tint(ElementPalette.ForElement(element),
                    $"{count} {ElementInfo.ShortNameOf(element)}");

                text = text.Length == 0 ? one : text + " + " + one;
            }

            return text;
        }

        /// <summary>
        /// What a skill cost, elements first.
        ///
        /// The element is the interesting half -- action points come back every turn and elements
        /// do not -- so it leads, and it is the half that gets a colour.
        /// </summary>
        /// <summary>What happened to a wall, as a line: it fell, it rose, or it changed.</summary>
        public static string Wall(Dragoneye.Hex.Wall before, Dragoneye.Hex.Wall after)
        {
            if (!after.IsSet)
            {
                return before.BlocksSight ? "A wall comes down." : "A low wall comes down.";
            }

            if (!before.IsSet)
            {
                return after.BlocksSight ? "A wall goes up." : "A low wall goes up.";
            }

            return after.BlocksSight ? "A wall is made whole." : "A wall is broken down to waist height.";
        }

        /// <summary>"3 damage", or "3 damage (2 on armour)", or "nothing: 5 on armour".</summary>
        public static string Blow(int landed, int absorbed)
        {
            if (absorbed <= 0)
            {
                return $"{landed} damage";
            }

            return landed > 0
                ? $"{landed} damage ({absorbed} on armour)"
                : $"nothing: {absorbed} on armour";
        }

        public static string Cost(SkillSpec skill)
        {
            if (skill == null)
            {
                return string.Empty;
            }

            var ap = $"{skill.ApCost} AP";

            if (skill.ElementCost <= 0)
            {
                return ap;
            }

            var element = Tint(ElementPalette.ForElement(skill.Element),
                $"{skill.ElementCost} {ElementInfo.ShortNameOf(skill.Element)}");

            return element + ", " + ap;
        }

        /// <summary>
        /// How the clash came out, naming whoever won it.
        ///
        /// The name is in the sentence rather than left to the colour, because a bare "WIN" is only
        /// unambiguous to whoever it belongs to -- three people read the same line and two of them
        /// have to work out first which end of it they are on. Naming the winner makes the words
        /// true from every seat, and leaves the colour free to say what a reader wants to know
        /// about it: green when their side took it, red when it went the other way.
        /// </summary>
        public static string Verdict(ClashOutcome outcome, string attacker, string defender,
            LogSide reader)
        {
            if (outcome == ClashOutcome.Tie)
            {
                return Tint(ClashLabels.TieColour, "TIE");
            }

            var attackerWon = outcome == ClashOutcome.AttackerWins;
            var winner = attackerWon ? attacker : defender;

            var lost = reader != LogSide.Neither
                && (reader == LogSide.Attacker) != attackerWon;

            return winner + " "
                + Tint(lost ? ClashLabels.LoseColour : ClashLabels.WinColour, "WINS");
        }
    }
}
