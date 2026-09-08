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
        /// <summary>
        /// One of each physical element. Every creature has these, and cannot not have them.
        ///
        /// A floor rather than an allowance, and the distinction is the whole of it: nobody is
        /// given four points to spend on whatever they like, they are given four elements. The
        /// budget buys depth and the rarer three on top. A creator that let a player take one
        /// away would be offering them a trade that does not exist -- the point does not come
        /// back, because it was never charged.
        ///
        /// Why these four: a fight where somebody holds nothing an attack can be made of is not a
        /// fight, and a budget spent entirely on depth in one element left exactly that. Lux, Nyx
        /// and Arcana are what a build is characterised by, so none of them is standard issue.
        /// </summary>
        public static readonly ElementCounts Minimum = new ElementCounts(1, 1, 1, 1, 0, 0, 0);

        /// <summary>
        /// A pool brought up to the floor, whoever wrote the pool.
        ///
        /// Per element the larger of the two, not the sum, so it can be applied twice without
        /// handing anybody eight physical elements -- a premade authored with two Pyro keeps two,
        /// and gains one each of the rest.
        /// </summary>
        public static ElementCounts AtLeastMinimum(ElementCounts pool)
        {
            var whole = pool;

            foreach (var element in ElementInfo.All)
            {
                if (Minimum[element] > pool[element])
                {
                    whole = whole.With(element, Minimum[element]);
                }
            }

            return whole;
        }

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

        /// <summary>
        /// What a whole pool costs, with the floor taken off first.
        ///
        /// So a pool of exactly <see cref="Minimum"/> costs nothing, and a budget buys depth and
        /// the rarer elements rather than the first of each common one.
        /// </summary>
        public static int CostOf(ElementCounts pool)
        {
            var total = 0;

            foreach (var element in ElementInfo.All)
            {
                var paid = pool[element] - Minimum[element];

                if (paid > 0)
                {
                    total += paid * CostOf(element);
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
