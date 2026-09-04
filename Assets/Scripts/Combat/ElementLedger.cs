using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>Why a spend was refused. <see cref="None"/> means it went through.</summary>
    public enum SpendRefusal
    {
        None,
        NotHeld,
        NotAnElement,
        NothingToSpend,
        NothingToReturn
    }

    /// <summary>
    /// A creature's elements: what it holds, what it has been seen to spend, and in what order.
    ///
    /// The pool and the reveal record live in one type because DE-001 requires them to agree.
    /// Spending is a single operation that lowers one and raises the other -- there is no way to
    /// write one without the other, and no window where an early return could leave them
    /// inconsistent.
    ///
    /// Their visibility is opposite and that is the design: the pool is private to the controller,
    /// the record is public. Keeping them together here does not change that -- the replication
    /// layer projects each half to a different audience.
    ///
    /// The spend order is kept because elements come back in the order they left. Take a Breath
    /// returns the oldest outstanding spend, so this is a queue rather than a count.
    ///
    /// The reveal record is cumulative and never falls, even when an element is returned: it records
    /// what opponents *saw*, and they did see it spent.
    /// </summary>
    public readonly struct ElementLedger
    {
        /// <summary>What the creature can still spend. Visible only to its controller.</summary>
        public readonly ElementCounts Pool;

        /// <summary>What the creature has been seen to spend, cumulatively. Public.</summary>
        public readonly ElementCounts Revealed;

        /// <summary>
        /// Spends not yet returned, oldest first.
        ///
        /// Public information -- everyone watched them being spent -- and the queue Take a Breath
        /// draws from.
        /// </summary>
        public readonly IReadOnlyList<Element> Outstanding;

        public ElementLedger(ElementCounts pool, ElementCounts revealed,
            IReadOnlyList<Element> outstanding)
        {
            Pool = pool;
            Revealed = revealed;
            Outstanding = outstanding ?? System.Array.Empty<Element>();
        }

        /// <summary>A creature at the start of a fight: a full pool, nothing spent.</summary>
        public static ElementLedger Starting(ElementCounts pool) =>
            new ElementLedger(pool, ElementCounts.Empty, null);

        /// <summary>How much of an element is left to spend.</summary>
        public int Remaining(Element element) => Pool[element];

        public bool CanSpend(Element element, int amount) =>
            CheckSpend(element, amount) == SpendRefusal.None;

        /// <summary>Whether anything is outstanding for Take a Breath to bring back.</summary>
        public bool CanReturn => Outstanding.Count > 0;

        /// <summary>The element the next return would bring back, if any.</summary>
        public bool TryPeekReturn(out Element element)
        {
            if (Outstanding.Count == 0)
            {
                element = default;
                return false;
            }

            element = Outstanding[0];
            return true;
        }

        /// <summary>
        /// Spends from the pool, records it as revealed, and remembers the order.
        ///
        /// Returns the new ledger rather than mutating, so a caller that ignores the refusal cannot
        /// act on a half-applied spend.
        /// </summary>
        public bool TrySpend(Element element, int amount, out ElementLedger result,
            out SpendRefusal refusal)
        {
            refusal = CheckSpend(element, amount);

            if (refusal != SpendRefusal.None)
            {
                result = this;
                return false;
            }

            var outstanding = new List<Element>(Outstanding);

            // One entry per unit spent, because they come back one at a time.
            for (var i = 0; i < amount; i++)
            {
                outstanding.Add(element);
            }

            result = new ElementLedger(
                Pool.Minus(element, amount),
                Revealed.Plus(element, amount),
                outstanding);

            return true;
        }

        /// <summary>
        /// Brings back the oldest outstanding spend, which is what Take a Breath does.
        ///
        /// The reveal record is left alone. It says what was seen to be spent, and returning an
        /// element does not unsee that -- what it changes is how much is left, which is the half
        /// opponents are not entitled to.
        /// </summary>
        public bool TryReturn(out ElementLedger result, out Element returned,
            out SpendRefusal refusal)
        {
            if (!TryPeekReturn(out returned))
            {
                refusal = SpendRefusal.NothingToReturn;
                result = this;
                return false;
            }

            refusal = SpendRefusal.None;

            var outstanding = new List<Element>(Outstanding);
            outstanding.RemoveAt(0);

            result = new ElementLedger(Pool.Plus(returned, 1), Revealed, outstanding);
            return true;
        }

        SpendRefusal CheckSpend(Element element, int amount)
        {
            if (!ElementInfo.IsDefined(element))
            {
                // Reachable: an element arrives as an integer over the network and casting to an
                // enum is not a checked conversion.
                return SpendRefusal.NotAnElement;
            }

            if (amount <= 0)
            {
                return SpendRefusal.NothingToSpend;
            }

            return Pool.Holds(element, amount) ? SpendRefusal.None : SpendRefusal.NotHeld;
        }
    }
}
