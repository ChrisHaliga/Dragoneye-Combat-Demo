using Dragoneye.Game;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// The cursor label and the command both come from here. A disagreement between them is a UI
    /// that offers a move the server refuses, so these cases are about the price being one number.
    /// </summary>
    public class ActionResolverTests
    {
        static ActionPlan Move(int ap, int cost) =>
            ActionResolver.Resolve(true, true, ap, false, false, 0, cost);

        static ActionPlan Attack(int ap, int distance, bool enemy = true) =>
            ActionResolver.Resolve(true, true, ap, true, enemy, distance, -1);

        [Test]
        public void MovingCostsOneApPerStep()
        {
            var plan = Move(ap: 6, cost: 3);

            Assert.AreEqual(BoardAction.Move, plan.Action);
            Assert.AreEqual(3 * CombatRules.MoveApPerTile, plan.Cost);
            Assert.IsTrue(plan.IsAllowed);
        }

        [Test]
        public void AMoveTooLongToAffordIsPricedAndRefused()
        {
            // Priced, not hidden: "Move -- 5 AP (not enough)" tells the player what to change.
            var plan = Move(ap: 2, cost: 5);

            Assert.AreEqual(BoardAction.Move, plan.Action);
            Assert.AreEqual(5, plan.Cost);
            Assert.IsFalse(plan.IsAllowed);
            Assert.IsTrue(plan.IsUnaffordable);
        }

        [Test]
        public void AnUnreachableHexOffersNothing()
        {
            var plan = Move(ap: 6, cost: -1);

            Assert.AreEqual(BoardAction.None, plan.Action);
            Assert.AreEqual(ActionRefusal.Unreachable, plan.Refusal);
        }

        [Test]
        public void TheHexYouAreStandingOnOffersNothing()
        {
            Assert.AreEqual(BoardAction.None, Move(ap: 6, cost: 0).Action);
        }

        [Test]
        public void AttackingAnAdjacentEnemyCostsTheAttackPrice()
        {
            var plan = Attack(ap: 6, distance: 1);

            Assert.AreEqual(BoardAction.Attack, plan.Action);
            Assert.AreEqual(CombatRules.AttackApCost, plan.Cost);
            Assert.IsTrue(plan.IsAllowed);
        }

        [Test]
        public void AnEnemyOutOfReachIsPricedButRefused()
        {
            var plan = Attack(ap: 6, distance: 4);

            Assert.AreEqual(BoardAction.Attack, plan.Action);
            Assert.AreEqual(ActionRefusal.OutOfRange, plan.Refusal);
            Assert.IsFalse(plan.IsAllowed);
        }

        [Test]
        public void AlliesAreNotTargets()
        {
            var plan = Attack(ap: 6, distance: 1, enemy: false);

            Assert.AreEqual(BoardAction.None, plan.Action);
            Assert.AreEqual(ActionRefusal.Friendly, plan.Refusal);
        }

        [Test]
        public void AnAttackYouCannotAffordIsRefusedNotHidden()
        {
            var plan = Attack(ap: CombatRules.AttackApCost - 1, distance: 1);

            Assert.IsTrue(plan.IsUnaffordable);
            Assert.AreEqual(CombatRules.AttackApCost, plan.Cost);
        }

        [Test]
        public void NothingIsOfferedWhenItIsNotYourTurn()
        {
            var plan = ActionResolver.Resolve(isActorsTurn: false, controlsActor: true,
                currentAp: 6, targetOccupied: false, targetIsEnemy: false,
                distanceToTarget: 2, moveCost: 2);

            Assert.AreEqual(ActionRefusal.NotYourTurn, plan.Refusal);
            Assert.IsFalse(plan.IsAllowed);
        }

        [Test]
        public void NothingIsOfferedForACreatureYouDoNotControl()
        {
            // Checked before the turn, so hovering with an enemy active says nothing at all rather
            // than "not your turn" over every hex on the board.
            var plan = ActionResolver.Resolve(isActorsTurn: true, controlsActor: false,
                currentAp: 6, targetOccupied: false, targetIsEnemy: false,
                distanceToTarget: 2, moveCost: 2);

            Assert.AreEqual(ActionRefusal.NotYours, plan.Refusal);
            Assert.IsEmpty(ActionResolver.Describe(plan));
        }

        [Test]
        public void AllowedActionsAlwaysDescribeTheirPrice()
        {
            StringAssert.Contains("2 AP", ActionResolver.Describe(Move(6, 2)));
            StringAssert.Contains($"{CombatRules.AttackApCost} AP",
                ActionResolver.Describe(Attack(6, 1)));
        }

        [Test]
        public void AnUnaffordableActionSaysSo()
        {
            StringAssert.Contains("not enough", ActionResolver.Describe(Move(1, 5)));
        }
    }
}
