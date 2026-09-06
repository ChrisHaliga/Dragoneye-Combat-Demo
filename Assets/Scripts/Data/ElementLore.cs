using System.Collections.Generic;
using Dragoneye.Combat;

namespace Dragoneye.Data
{
    /// <summary>
    /// What an element answers well, and what answers it.
    ///
    /// The matchup table was, until this existed, readable by exactly two things: the resolver and
    /// the forecaster. A player could be told a defence had a seventy per cent chance and never
    /// find out why, and the only way to learn that Hydro answers Pyro was to lose to it. That is a
    /// game whose central mechanic is hidden from the person playing it.
    ///
    /// Read out of the shipped table rather than written down here, so retuning the asset retunes
    /// the explanation with it. A hand-written list of matchups in the UI would be a second copy of
    /// the rules, and it would be wrong the first time somebody edited the first copy.
    ///
    /// Every method takes the table it should read, with an overload that reaches for the shipped
    /// one. That is what lets the checks hold this against <see cref="ClashRules"/> directly: a
    /// tooltip claiming an element beats another one, when the resolver disagrees, is the same
    /// class of bug as a forecast pointing the wrong way.
    /// </summary>
    public static class ElementLore
    {
        /// <summary>Everything this element answers well.</summary>
        public static List<Element> Beats(Element element, IElementMatchup table) =>
            Where(element, ClashOutcome.AttackerWins, table);

        /// <summary>Everything that answers this element well.</summary>
        public static List<Element> LosesTo(Element element, IElementMatchup table) =>
            Where(element, ClashOutcome.DefenderWins, table);

        /// <summary>Everything this element is even against, itself included.</summary>
        public static List<Element> Even(Element element, IElementMatchup table) =>
            Where(element, ClashOutcome.Tie, table);

        public static List<Element> Beats(Element element) =>
            Beats(element, ElementMatchups.Table);

        public static List<Element> LosesTo(Element element) =>
            LosesTo(element, ElementMatchups.Table);

        public static List<Element> Even(Element element) =>
            Even(element, ElementMatchups.Table);

        static List<Element> Where(Element element, ClashOutcome outcome, IElementMatchup table)
        {
            var found = new List<Element>();

            if (table == null)
            {
                return found;
            }

            foreach (var other in ElementInfo.All)
            {
                if (table.Compare(element, other) == outcome)
                {
                    found.Add(other);
                }
            }

            return found;
        }

        /// <summary>
        /// The whole matchup, as a tooltip.
        ///
        /// Three short lines rather than a sentence, because it is looked at to answer one question
        /// -- "does this beat that" -- and a list is scanned where a sentence has to be read.
        /// </summary>
        public static string Describe(Element element, IElementMatchup table)
        {
            var text = ElementInfo.NameOf(element);

            var beats = Names(Beats(element, table));
            var loses = Names(LosesTo(element, table));

            if (beats.Length == 0 && loses.Length == 0)
            {
                return text + "\nEven against everything.";
            }

            if (beats.Length > 0)
            {
                text += "\nBeats " + beats;
            }

            if (loses.Length > 0)
            {
                text += "\nLoses to " + loses;
            }

            return text;
        }

        public static string Describe(Element element) =>
            Describe(element, ElementMatchups.Table);

        static string Names(IReadOnlyList<Element> elements)
        {
            var text = string.Empty;

            foreach (var element in elements)
            {
                text += text.Length == 0
                    ? ElementInfo.NameOf(element)
                    : ", " + ElementInfo.NameOf(element);
            }

            return text;
        }
    }
}
