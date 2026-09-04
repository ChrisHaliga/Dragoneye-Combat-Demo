using System.Collections.Generic;
using Dragoneye.Combat;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// DE-001. A pool depletes and is never restored, an element that is not held cannot be spent,
    /// and the reveal record rises by exactly what the pool lost.
    /// </summary>
    public class ElementPoolTests
    {
        [Test]
        public void CountsAreKeptPerElement()
        {
            var counts = new ElementCounts(2, 1, 0, 3);

            Assert.AreEqual(2, counts[Element.Fire]);
            Assert.AreEqual(0, counts[Element.Earth]);
            Assert.AreEqual(6, counts.Total);
        }

        [Test]
        public void WithReplacesOneElementAndLeavesTheRest()
        {
            var counts = new ElementCounts(2, 1, 0, 3).With(Element.Water, 9);

            Assert.AreEqual(9, counts[Element.Water]);
            Assert.AreEqual(2, counts[Element.Fire]);
        }

        [Test]
        public void CountsNeverGoNegative()
        {
            Assert.AreEqual(0, new ElementCounts(2, 0, 0, 0).With(Element.Fire, -5)[Element.Fire]);
            Assert.AreEqual(0, ElementCounts.Empty.Plus(Element.Fire, -3)[Element.Fire]);
        }

        [Test]
        public void APoolIsBuiltFromPicksWithRepeatsCounted()
        {
            var counts = ElementCounts.From(new List<Element>
            {
                Element.Fire, Element.Fire, Element.Air
            });

            Assert.AreEqual(2, counts[Element.Fire]);
            Assert.AreEqual(1, counts[Element.Air]);
            Assert.AreEqual(3, counts.Total);
        }

        [Test]
        public void AnUndefinedElementInThePicksIsDropped()
        {
            // Picks arrive from a save file or a client, and casting an int to an enum is unchecked.
            var counts = ElementCounts.From(new List<Element> { Element.Fire, (Element)77 });

            Assert.AreEqual(1, counts.Total);
        }

        [Test]
        public void SpendingLowersThePoolAndRaisesTheRecordTogether()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(2, 0, 0, 0));

            Assert.IsTrue(ledger.TrySpend(Element.Fire, 1, out var after, out var refusal));
            Assert.AreEqual(SpendRefusal.None, refusal);
            Assert.AreEqual(1, after.Pool[Element.Fire]);
            Assert.AreEqual(1, after.Revealed[Element.Fire]);
        }

        [Test]
        public void PoolPlusRevealedIsConserved()
        {
            // The invariant behind DE-001's "write them in one operation": whatever leaves the pool
            // appears in the record, so the two can never disagree about what was spent.
            var ledger = ElementLedger.Starting(new ElementCounts(3, 0, 0, 0));

            ledger.TrySpend(Element.Fire, 2, out var after, out _);

            Assert.AreEqual(3, after.Pool[Element.Fire] + after.Revealed[Element.Fire]);
        }

        [Test]
        public void SpendingWhatIsNotHeldIsRefusedAndChangesNothing()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(1, 0, 0, 0));

            Assert.IsFalse(ledger.TrySpend(Element.Fire, 2, out var after, out var refusal));
            Assert.AreEqual(SpendRefusal.NotHeld, refusal);
            Assert.AreEqual(ledger.Pool, after.Pool, "a refused spend must not partially apply");
            Assert.AreEqual(ledger.Revealed, after.Revealed);
        }

        [Test]
        public void AnElementNeverHeldCannotBeSpent()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(1, 0, 0, 0));

            Assert.IsFalse(ledger.TrySpend(Element.Water, 1, out _, out var refusal));
            Assert.AreEqual(SpendRefusal.NotHeld, refusal);
        }

        [Test]
        public void AnUndefinedElementCannotBeSpent()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(9, 9, 9, 9));

            Assert.IsFalse(ledger.TrySpend((Element)99, 1, out _, out var refusal));
            Assert.AreEqual(SpendRefusal.NotAnElement, refusal);
        }

        [Test]
        public void SpendingNothingIsRefused()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(2, 0, 0, 0));

            Assert.IsFalse(ledger.TrySpend(Element.Fire, 0, out _, out var refusal));
            Assert.AreEqual(SpendRefusal.NothingToSpend, refusal);
            Assert.IsFalse(ledger.TrySpend(Element.Fire, -3, out _, out _));
        }

        [Test]
        public void APoolBottomsOutAndNeverRefills()
        {
            // Pools deplete over a fight and are not restored. Nothing on this type can raise one.
            var ledger = ElementLedger.Starting(new ElementCounts(2, 0, 0, 0));

            for (var i = 0; i < 10; i++)
            {
                ledger.TrySpend(Element.Fire, 1, out ledger, out _);
            }

            Assert.AreEqual(0, ledger.Pool[Element.Fire]);
            Assert.AreEqual(2, ledger.Revealed[Element.Fire], "never reveals more than it held");
        }

        [Test]
        public void CanSpendAgreesWithTrySpend()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(2, 0, 0, 0));

            Assert.IsTrue(ledger.CanSpend(Element.Fire, 2));
            Assert.IsFalse(ledger.CanSpend(Element.Fire, 3));
            Assert.IsFalse(ledger.CanSpend(Element.Water, 1));
        }

        [Test]
        public void AFreshLedgerHasRevealedNothing()
        {
            Assert.IsTrue(ElementLedger.Starting(new ElementCounts(4, 4, 4, 4)).Revealed.IsEmpty);
        }
    }
}
