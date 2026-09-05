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

        /// <summary>
        /// How it came out, from the defender's side.
        ///
        /// Written over the defender because they are the one it happened to: the attacker already
        /// knows they swung, and what is worth saying is whether it got through.
        /// </summary>
        public static string Describe(ClashOutcome outcome)
        {
            switch (outcome)
            {
                case ClashOutcome.AttackerWins: return "OVERCOME";
                case ClashOutcome.Tie: return "HELD HALF";
                default: return "TURNED ASIDE";
            }
        }

        /// <summary>Why a defender is being asked for two elements rather than one.</summary>
        public static string Describe(DefenceRequest request)
        {
            if (request.Flanked && !request.Shielded)
            {
                return "Struck from your flank. Two elements, and the worse of them answers.";
            }

            if (request.Shielded && !request.Flanked)
            {
                return "Your shield. Two elements, and the better of them answers.";
            }

            if (request.Flanked)
            {
                return "Flanked, but shielded. One element, as usual.";
            }

            return "One element. Whichever you spend is gone either way.";
        }

        /// <summary>The word shown where an action's cost would be, when position has changed it.</summary>
        public const string Advantage = "ADVANTAGE";
    }
}
