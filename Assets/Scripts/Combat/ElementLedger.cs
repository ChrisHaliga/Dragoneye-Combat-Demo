using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>Why a spend was refused. <see cref="None"/> means it went through.</summary>
    public enum SpendRefusal
    {
        None,
        NotHeld,
        NotAnElement,
        NothingToSpend
    }

    /// <summary>
    /// A creature's elements: what it still holds, and what it has been seen to spend.
    ///
    /// The two live in one type because DE-001 requires them to agree. Spending is a single
    /// operation that produces a new ledger with the pool lowered and the record raised by the same
    /// amount -- there is no way to write one without the other, and no window where a crash or an
    /// early return could leave them inconsistent.
    ///
    /// Their visibility is opposite and that is the whole design: the pool is private to the
    /// controller, the record is public to everyone. Keeping them in one value here does not change
    /// that -- the replication layer projects each half to a different audience, and that is the one
    /// place the split needs to exist.
    ///
    /// Immutable, so a refused spend cannot have partially applied.
    /// </summary>
    public readonly struct ElementLedger
    {
        /// <summary>What the creature can still spend. Visible only to its controller.</summary>
        public readonly ElementCounts Pool;

        /// <summary>What the creature has spent, cumulatively. Visible to everyone.</summary>
        public readonly ElementCounts Revealed;

        public ElementLedger(ElementCounts pool, ElementCounts revealed)
        {
            Pool = pool;
            Revealed = revealed;
        }

        /// <summary>A creature at the start of a fight: a full pool, nothing revealed.</summary>
        public static ElementLedger Starting(ElementCounts pool) =>
            new ElementLedger(pool, ElementCounts.Empty);

        public static ElementLedger Starting(IReadOnlyList<Element> picks) =>
            Starting(ElementCounts.From(picks));

        /// <summary>
        /// How much of an element is left, once what has been spent is taken off.
        ///
        /// Pools deplete over a fight and are never restored, so this only ever falls.
        /// </summary>
        public int Remaining(Element element) => Pool[element];

        public bool CanSpend(Element element, int amount) =>
            Check(element, amount) == SpendRefusal.None;

        /// <summary>
        /// Spends from the pool and records the same amount as revealed.
        ///
        /// Returns the new ledger rather than mutating, so a caller that ignores the refusal cannot
        /// accidentally act on a half-applied spend.
        /// </summary>
        public bool TrySpend(Element element, int amount, out ElementLedger result,
            out SpendRefusal refusal)
        {
            refusal = Check(element, amount);

            if (refusal != SpendRefusal.None)
            {
                result = this;
                return false;
            }

            result = new ElementLedger(
                Pool.With(element, Pool[element] - amount),
                Revealed.Plus(element, amount));

            return true;
        }

        SpendRefusal Check(Element element, int amount)
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
