using System.Collections.Generic;
using System.Linq;
using Dragoneye.Game;
using NUnit.Framework;
using Hex = Dragoneye.Hex.Hex;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// The opponent is deliberately simple, but it must never stall: a brain that returns an action
    /// it cannot perform, or passes while an enemy stands next to it, hangs or wastes a turn.
    /// </summary>
    public class BasicBrainTests
    {
        /// <summary>
        /// An open board with straight-line routes. Enough for decisions; the real pathfinder is
        /// tested separately, and the brain only cares that a cost comes back.
        /// </summary>
        sealed class OpenBoard : IBoardQuery
        {
            readonly HashSet<Hex> m_Occupied;

            public OpenBoard(params Hex[] occupied) => m_Occupied = new HashSet<Hex>(occupied);

            public int CostTo(Hex from, Hex to) => Hex.Distance(from, to);

            public IReadOnlyList<Hex> PathTo(Hex from, Hex to)
            {
                var line = Hex.Line(from, to).Skip(1).ToList();
                return line;
            }

            public bool IsOccupied(Hex hex) => m_Occupied.Contains(hex);
        }

        static BrainView Actor(Hex cell, int ap = 6, Party party = Party.Monsters) =>
            new BrainView(1, cell, party, ap, 20);

        static BrainView Enemy(uint id, Hex cell, int hp = 20) =>
            new BrainView(id, cell, Party.Heroes, 6, hp);

        [Test]
        public void AttacksAnAdjacentEnemy()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero), new[] { Enemy(2, new Hex(1, 0)) }, new OpenBoard(new Hex(1, 0)));

            Assert.AreEqual(BoardAction.Attack, decision.Action);
            Assert.AreEqual(2u, decision.TargetId);
        }

        [Test]
        public void MovesTowardTheEnemyWhenOutOfReach()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero), new[] { Enemy(2, new Hex(4, 0)) }, new OpenBoard(new Hex(4, 0)));

            Assert.AreEqual(BoardAction.Move, decision.Action);
            Assert.Less(Hex.Distance(decision.Destination, new Hex(4, 0)), 4,
                "Moving should close the gap");
        }

        [Test]
        public void NeverTargetsTheEnemyHexItself()
        {
            // An occupied hex is not a legal destination, so a brain aiming at one wastes its turn.
            var enemyCell = new Hex(4, 0);

            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero), new[] { Enemy(2, enemyCell) }, new OpenBoard(enemyCell));

            Assert.AreNotEqual(enemyCell, decision.Destination);
        }

        [Test]
        public void DoesNotOutrunItsAp()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero, ap: 2), new[] { Enemy(2, new Hex(6, 0)) },
                new OpenBoard(new Hex(6, 0)));

            Assert.AreEqual(BoardAction.Move, decision.Action);
            Assert.LessOrEqual(Hex.Distance(Hex.Zero, decision.Destination), 2,
                "Two AP buys two steps");
        }

        [Test]
        public void PassesWithNoApLeft()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero, ap: 0), new[] { Enemy(2, new Hex(4, 0)) },
                new OpenBoard(new Hex(4, 0)));

            Assert.AreEqual(BoardAction.None, decision.Action);
        }

        [Test]
        public void MovesWhenAdjacentButUnableToAffordTheAttack()
        {
            // One AP: cannot attack, but should still reposition rather than stand and pass.
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero, ap: 1), new[] { Enemy(2, new Hex(1, 0)) },
                new OpenBoard(new Hex(1, 0)));

            Assert.AreNotEqual(BoardAction.Attack, decision.Action);
        }

        [Test]
        public void IgnoresItsOwnParty()
        {
            var ally = new BrainView(2, new Hex(1, 0), Party.Monsters, 6, 20);

            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero), new[] { ally }, new OpenBoard(new Hex(1, 0)));

            Assert.AreEqual(BoardAction.None, decision.Action, "There is no enemy to fight");
        }

        [Test]
        public void IgnoresTheDead()
        {
            var corpse = Enemy(2, new Hex(1, 0), hp: 0);

            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero), new[] { corpse }, new OpenBoard(new Hex(1, 0)));

            Assert.AreEqual(BoardAction.None, decision.Action);
        }

        [Test]
        public void ChoosesTheNearestOfSeveralEnemies()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero),
                new[] { Enemy(2, new Hex(5, 0)), Enemy(3, new Hex(1, 0)) },
                new OpenBoard(new Hex(5, 0), new Hex(1, 0)));

            Assert.AreEqual(BoardAction.Attack, decision.Action);
            Assert.AreEqual(3u, decision.TargetId, "The adjacent one");
        }

        [Test]
        public void PassesWithNothingToFight()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero), new BrainView[0], new OpenBoard());

            Assert.AreEqual(BoardAction.None, decision.Action);
        }

        [Test]
        public void NullInputsPassRatherThanThrow()
        {
            Assert.AreEqual(BoardAction.None,
                new BasicBrain().Decide(Actor(Hex.Zero), null, new OpenBoard()).Action);
            Assert.AreEqual(BoardAction.None,
                new BasicBrain().Decide(Actor(Hex.Zero), new BrainView[0], null).Action);
        }
    }
}
