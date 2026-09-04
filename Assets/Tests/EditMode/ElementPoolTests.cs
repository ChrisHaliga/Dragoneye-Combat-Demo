using System.Collections.Generic;
using Dragoneye.Combat;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    // System.Attribute would otherwise win the name; the alias must be inside the namespace.
    using Attribute = Dragoneye.Combat.Attribute;

    /// <summary>
    /// DE-001. A pool depletes and is never restored, an element that is not held cannot be spent,
    /// and the reveal record rises by exactly what the pool lost.
    /// </summary>
    public class ElementPoolTests
    {
        [Test]
        public void CountsAreKeptPerElement()
        {
            var counts = new ElementCounts(2, 1, 0, 3, 0, 0, 0);

            Assert.AreEqual(2, counts[Element.Pyro]);
            Assert.AreEqual(0, counts[Element.Geo]);
            Assert.AreEqual(6, counts.Total);
        }

        [Test]
        public void WithReplacesOneElementAndLeavesTheRest()
        {
            var counts = new ElementCounts(2, 1, 0, 3, 0, 0, 0).With(Element.Hydro, 9);

            Assert.AreEqual(9, counts[Element.Hydro]);
            Assert.AreEqual(2, counts[Element.Pyro]);
        }

        [Test]
        public void CountsNeverGoNegative()
        {
            Assert.AreEqual(0, new ElementCounts(2, 0, 0, 0, 0, 0, 0).With(Element.Pyro, -5)[Element.Pyro]);
            Assert.AreEqual(0, ElementCounts.Empty.Plus(Element.Pyro, -3)[Element.Pyro]);
        }

        [Test]
        public void APoolIsASpreadRatherThanAListOfPicks()
        {
            // Any combination totalling the level is legal, so the shape carries as much meaning as
            // the size: three of one element is a different character from one each of three.
            var narrow = new ElementCounts(0, 3, 0, 0, 0, 0, 0);
            var broad = new ElementCounts(1, 1, 1, 0, 0, 0, 0);

            Assert.AreEqual(3, narrow.Total);
            Assert.AreEqual(3, broad.Total);
            Assert.AreNotEqual(narrow, broad);
        }

        [Test]
        public void SpendingLowersThePoolAndRaisesTheRecordTogether()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(2, 0, 0, 0, 0, 0, 0));

            Assert.IsTrue(ledger.TrySpend(Element.Pyro, 1, out var after, out var refusal));
            Assert.AreEqual(SpendRefusal.None, refusal);
            Assert.AreEqual(1, after.Pool[Element.Pyro]);
            Assert.AreEqual(1, after.Revealed[Element.Pyro]);
        }

        [Test]
        public void PoolPlusRevealedIsConserved()
        {
            // The invariant behind DE-001's "write them in one operation": whatever leaves the pool
            // appears in the record, so the two can never disagree about what was spent.
            var ledger = ElementLedger.Starting(new ElementCounts(3, 0, 0, 0, 0, 0, 0));

            ledger.TrySpend(Element.Pyro, 2, out var after, out _);

            Assert.AreEqual(3, after.Pool[Element.Pyro] + after.Revealed[Element.Pyro]);
        }

        [Test]
        public void SpendingWhatIsNotHeldIsRefusedAndChangesNothing()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(1, 0, 0, 0, 0, 0, 0));

            Assert.IsFalse(ledger.TrySpend(Element.Pyro, 2, out var after, out var refusal));
            Assert.AreEqual(SpendRefusal.NotHeld, refusal);
            Assert.AreEqual(ledger.Pool, after.Pool, "a refused spend must not partially apply");
            Assert.AreEqual(ledger.Revealed, after.Revealed);
        }

        [Test]
        public void AnElementNeverHeldCannotBeSpent()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(1, 0, 0, 0, 0, 0, 0));

            Assert.IsFalse(ledger.TrySpend(Element.Hydro, 1, out _, out var refusal));
            Assert.AreEqual(SpendRefusal.NotHeld, refusal);
        }

        [Test]
        public void AnUndefinedElementCannotBeSpent()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(9, 9, 9, 9, 0, 0, 0));

            Assert.IsFalse(ledger.TrySpend((Element)99, 1, out _, out var refusal));
            Assert.AreEqual(SpendRefusal.NotAnElement, refusal);
        }

        [Test]
        public void SpendingNothingIsRefused()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(2, 0, 0, 0, 0, 0, 0));

            Assert.IsFalse(ledger.TrySpend(Element.Pyro, 0, out _, out var refusal));
            Assert.AreEqual(SpendRefusal.NothingToSpend, refusal);
            Assert.IsFalse(ledger.TrySpend(Element.Pyro, -3, out _, out _));
        }

        [Test]
        public void APoolBottomsOutAndNeverRefills()
        {
            // Pools deplete over a fight and are not restored. Nothing on this type can raise one.
            var ledger = ElementLedger.Starting(new ElementCounts(2, 0, 0, 0, 0, 0, 0));

            for (var i = 0; i < 10; i++)
            {
                ledger.TrySpend(Element.Pyro, 1, out ledger, out _);
            }

            Assert.AreEqual(0, ledger.Pool[Element.Pyro]);
            Assert.AreEqual(2, ledger.Revealed[Element.Pyro], "never reveals more than it held");
        }

        [Test]
        public void CanSpendAgreesWithTrySpend()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(2, 0, 0, 0, 0, 0, 0));

            Assert.IsTrue(ledger.CanSpend(Element.Pyro, 2));
            Assert.IsFalse(ledger.CanSpend(Element.Pyro, 3));
            Assert.IsFalse(ledger.CanSpend(Element.Hydro, 1));
        }

        [Test]
        public void AFreshLedgerHasRevealedNothing()
        {
            Assert.IsTrue(ElementLedger.Starting(new ElementCounts(4, 4, 4, 4, 0, 0, 0)).Revealed.IsEmpty);
        }
    }
}
