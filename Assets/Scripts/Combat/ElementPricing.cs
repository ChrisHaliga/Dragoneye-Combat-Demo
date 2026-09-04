namespace Dragoneye.Combat
{
    /// <summary>
    /// What an element costs to hold.
    ///
    /// The four physical elements are a point each, the two opposed ones are two, and Arcana is
    /// three. So a level-three character holds three Geo, or one Geo and one Lux, or nothing but a
    /// single Arcana -- the pool is a spread and a depth at the same time, and the rarer the
    /// element the more of the budget one of it takes.
    ///
    /// Separate from <see cref="ElementInfo"/> because this is a rule and that is a description: the
    /// name and the colour of Pyro are facts about it, what it costs is a decision that will be
    /// tuned. Separate from <see cref="PointBuy"/> because attributes and elements are bought from
    /// different budgets, and folding them into one class would invite spending one on the other.
    /// </summary>
    public static class ElementPricing
    {
        /// <summary>What one of this element costs out of the pool budget.</summary>
        public static int CostOf(Element element)
        {
            switch (element)
            {
                case Element.Geo:
                case Element.Hydro:
                case Element.Pyro:
                case Element.Aero:
                    return 1;

                case Element.Lux:
                case Element.Nyx:
                    return 2;

                case Element.Arcana:
                    return 3;

                default:
                    return 1;
            }
        }

        /// <summary>What a whole pool costs.</summary>
        public static int CostOf(ElementCounts pool)
        {
            var total = 0;

            foreach (var element in ElementInfo.All)
            {
                var held = pool[element];

                if (held > 0)
                {
                    total += held * CostOf(element);
                }
            }

            return total;
        }

        /// <summary>Budget left after paying for what is already held.</summary>
        public static int Remaining(ElementCounts pool, int budget) => budget - CostOf(pool);

        /// <summary>
        /// Whether one more of an element is affordable.
        ///
        /// Asked by the creation screen so a player is never shown a step that would put them over,
        /// and by nothing else -- the validator checks the total, which is the same question asked
        /// once at the end.
        /// </summary>
        public static bool CanAdd(ElementCounts pool, Element element, int budget) =>
            Remaining(pool, budget) >= CostOf(element);
    }
}
