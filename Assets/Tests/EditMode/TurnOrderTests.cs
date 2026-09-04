using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Game;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// Every peer builds and walks this order independently. Any disagreement puts two clients in
    /// different turns, which is close to impossible to reproduce from a bug report.
    /// </summary>
    public class TurnOrderTests
    {
        static List<Combatant> Combatants(params (uint Id, int Speed)[] entries)
        {
            var list = new List<Combatant>();
            foreach (var entry in entries)
            {
                list.Add(new Combatant(entry.Id, entry.Speed));
            }

            return list;
        }

        [Test]
        public void FastestActsFirst()
        {
            CollectionAssert.AreEqual(new uint[] { 2, 3, 1 },
                TurnOrder.Build(Combatants((1, 2), (2, 9), (3, 5))));
        }

        [Test]
        public void EqualSpeedsBreakOnIdAscending()
        {
            // List.Sort is unstable, so without an explicit tiebreak equal speeds come out in
            // whatever order the partitioning left them -- and that can differ between peers.
            CollectionAssert.AreEqual(new uint[] { 4, 7, 9 },
                TurnOrder.Build(Combatants((9, 5), (4, 5), (7, 5))));
        }

        [Test]
        public void OrderDoesNotDependOnInputOrder()
        {
            var forwards = TurnOrder.Build(Combatants((1, 5), (2, 5), (3, 9)));
            var backwards = TurnOrder.Build(Combatants((3, 9), (2, 5), (1, 5)));

            CollectionAssert.AreEqual(forwards, backwards);
        }

        [Test]
        public void BuildDoesNotMutateItsInput()
        {
            var input = Combatants((1, 1), (2, 9));

            TurnOrder.Build(input);

            Assert.AreEqual(1u, input[0].Id, "The caller's list must come back untouched");
        }

        [Test]
        public void AdvanceMovesToTheNextCombatant()
        {
            var order = new uint[] { 1, 2, 3 };

            Assert.IsTrue(TurnOrder.TryAdvance(order, 0, _ => true, out var next, out var ended));
            Assert.AreEqual(1, next);
            Assert.IsFalse(ended);
        }

        [Test]
        public void WrappingPastTheEndEndsTheRound()
        {
            var order = new uint[] { 1, 2, 3 };

            Assert.IsTrue(TurnOrder.TryAdvance(order, 2, _ => true, out var next, out var ended));
            Assert.AreEqual(0, next);
            Assert.IsTrue(ended);
        }

        [Test]
        public void TheDeadAreSkipped()
        {
            var order = new uint[] { 1, 2, 3 };

            Assert.IsTrue(TurnOrder.TryAdvance(order, 0, id => id == 3, out var next, out _));
            Assert.AreEqual(2, next);
        }

        [Test]
        public void ARoundStillEndsWhenTheLastCombatantsAreDead()
        {
            // Wrapping is detected on the step that crosses the end, not on landing there, so a
            // round boundary is not lost when the tail of the order has been wiped out.
            var order = new uint[] { 1, 2, 3 };

            Assert.IsTrue(TurnOrder.TryAdvance(order, 0, id => id == 1, out var next, out var ended));
            Assert.AreEqual(0, next);
            Assert.IsTrue(ended, "Passing the end of the order is what a round is");
        }

        [Test]
        public void NobodyLeftReportsFailureRatherThanLooping()
        {
            var order = new uint[] { 1, 2 };

            Assert.IsFalse(TurnOrder.TryAdvance(order, 0, _ => false, out _, out _));
        }

        [Test]
        public void AnEmptyOrderIsHandled()
        {
            Assert.IsFalse(TurnOrder.TryAdvance(new uint[0], 0, _ => true, out _, out _));
            Assert.IsFalse(TurnOrder.TryAdvance(null, 0, _ => true, out _, out _));
        }

        [Test]
        public void FirstSkipsAnyoneWhoCannotAct()
        {
            Assert.IsTrue(TurnOrder.TryFirst(new uint[] { 1, 2, 3 }, id => id == 2, out var index));
            Assert.AreEqual(1, index);
        }

        [Test]
        public void FirstFailsWhenNobodyCanAct()
        {
            Assert.IsFalse(TurnOrder.TryFirst(new uint[] { 1, 2 }, _ => false, out _));
        }
    }
}
