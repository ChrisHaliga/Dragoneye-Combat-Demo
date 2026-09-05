namespace Dragoneye.Combat
{
    /// <summary>
    /// What an attribute spread costs.
    ///
    /// Raising an attribute costs its current value, so the first step off the floor is cheap and
    /// every one after it is dearer. That makes a spread a real decision: twenty points buys one
    /// very high attribute or several middling ones, never both.
    ///
    /// The arithmetic lives here rather than in the validator or the creation screen because all
    /// three ask it -- the screen to price the next step, the validator to check the total, and the
    /// host to check it again.
    /// </summary>
    public static class PointBuy
    {
        /// <summary>
        /// Every attribute starts here, and points are spent raising it.
        ///
        /// Zero, so that a fresh character adds nothing to what its species already gives it. A
        /// floor of one looked harmless and was not: every derived stat counts attributes, so an
        /// untouched character came out a point of Endurance ahead and every species with a base of
        /// four action points fielded five. A floor is not the place to hand out stats.
        ///
        /// It also makes dumping an attribute a real choice rather than a rounding error. Nothing
        /// forces a character to be adequate at all seven any more.
        /// </summary>
        public const int Floor = 0;

        /// <summary>
        /// What it costs to go from <paramref name="current"/> to one higher.
        ///
        /// Its current value, except for the first point, which would otherwise be free -- and a
        /// free step repeated seven times is not a floor, it is a different budget.
        /// </summary>
        public static int CostToRaise(int current) => current < 1 ? 1 : current;

        /// <summary>
        /// What it costs to reach a value from the floor.
        ///
        /// One for the first point, then the triangular number for every step after it, computed
        /// directly rather than looped.
        /// </summary>
        public static int CostOf(int value)
        {
            if (value <= Floor)
            {
                return 0;
            }

            var above = value - 1;
            return 1 + (above * (above + 1)) / 2;
        }

        /// <summary>What a whole spread costs.</summary>
        public static int TotalCost(AttributeBlock attributes)
        {
            var total = 0;

            foreach (var attribute in AttributeInfo.All)
            {
                total += CostOf(attributes[attribute]);
            }

            return total;
        }

        /// <summary>Points left to spend.</summary>
        public static int Remaining(AttributeBlock attributes, int budget) =>
            budget - TotalCost(attributes);

        /// <summary>
        /// Whether one more point in an attribute is affordable.
        ///
        /// Asked by the creation screen to decide whether to offer the step at all, so a player is
        /// never shown a button that would put them over budget.
        /// </summary>
        public static bool CanRaise(AttributeBlock attributes, Attribute attribute, int budget,
            int ceiling)
        {
            var current = attributes[attribute];

            return current < ceiling
                && Remaining(attributes, budget) >= CostToRaise(current);
        }
    }
}
