using Dragoneye.Combat;
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
        static ActionPlan Move(int wholeAp, int steps) =>
            ActionResolver.Resolve(true, true, Ap.FromWhole(wholeAp), false, false, 0, steps);

        static ActionPlan Attack(int wholeAp, int distance, bool enemy = true) =>
            ActionResolver.Resolve(true, true, Ap.FromWhole(wholeAp), true, enemy, distance, -1);

        [Test]
        public void MovingCostsOneApPerStep()
        {
            var plan = Move(wholeAp: 6, steps: 3);

            Assert.AreEqual(BoardAction.Move, plan.Action);
            Assert.AreEqual(CombatRules.MoveCostPerTile * 3, plan.Cost);
            Assert.IsTrue(plan.IsAllowed);
        }

        [Test]
        public void AMoveTooLongToAffordIsPricedAndRefused()
        {
            // Priced, not hidden: "Move -- 5 AP (not enough)" tells the player what to change.
            var plan = Move(wholeAp: 1, steps: 5);

            Assert.AreEqual(BoardAction.Move, plan.Action);
            Assert.AreEqual(CombatRules.MoveCost(5), plan.Cost);
            Assert.IsFalse(plan.IsAllowed);
            Assert.IsTrue(plan.IsUnaffordable);
        }

        [Test]
        public void AnUnreachableHexOffersNothing()
        {
            var plan = Move(wholeAp: 6, steps: -1);

            Assert.AreEqual(BoardAction.None, plan.Action);
            Assert.AreEqual(ActionRefusal.Unreachable, plan.Refusal);
        }

        [Test]
        public void TheHexYouAreStandingOnOffersNothing()
        {
            Assert.AreEqual(BoardAction.None, Move(wholeAp: 6, steps: 0).Action);
        }

        [Test]
        public void AttackingAnAdjacentEnemyCostsTheAttackPrice()
        {
            var plan = Attack(wholeAp: 6, distance: 1);

            Assert.AreEqual(BoardAction.Attack, plan.Action);
            Assert.AreEqual(CombatRules.AttackCost, plan.Cost);
            Assert.IsTrue(plan.IsAllowed);
        }

        [Test]
        public void AnEnemyOutOfReachIsPricedButRefused()
        {
            var plan = Attack(wholeAp: 6, distance: 4);

            Assert.AreEqual(BoardAction.Attack, plan.Action);
            Assert.AreEqual(ActionRefusal.OutOfRange, plan.Refusal);
            Assert.IsFalse(plan.IsAllowed);
        }

        [Test]
        public void AlliesAreNotTargets()
        {
            var plan = Attack(wholeAp: 6, distance: 1, enemy: false);

            Assert.AreEqual(BoardAction.None, plan.Action);
            Assert.AreEqual(ActionRefusal.Friendly, plan.Refusal);
        }

        [Test]
        public void AnAttackYouCannotAffordIsRefusedNotHidden()
        {
            // One whole point, when an attack costs two.
            var plan = Attack(wholeAp: 1, distance: 1);

            Assert.IsTrue(plan.IsUnaffordable);
            Assert.AreEqual(CombatRules.AttackCost, plan.Cost);
        }

        [Test]
        public void NothingIsOfferedWhenItIsNotYourTurn()
        {
            var plan = ActionResolver.Resolve(isActorsTurn: false, controlsActor: true,
                currentAp: Ap.FromWhole(6), targetOccupied: false, targetIsEnemy: false,
                distanceToTarget: 2, moveSteps: 2);

            Assert.AreEqual(ActionRefusal.NotYourTurn, plan.Refusal);
            Assert.IsFalse(plan.IsAllowed);
        }

        [Test]
        public void NothingIsOfferedForACreatureYouDoNotControl()
        {
            // Checked before the turn, so hovering with an enemy active says nothing at all rather
            // than "not your turn" over every hex on the board.
            var plan = ActionResolver.Resolve(isActorsTurn: true, controlsActor: false,
                currentAp: Ap.FromWhole(6), targetOccupied: false, targetIsEnemy: false,
                distanceToTarget: 2, moveSteps: 2);

            Assert.AreEqual(ActionRefusal.NotYours, plan.Refusal);
            Assert.IsEmpty(ActionLabels.Describe(plan));
        }

        [Test]
        public void AllowedActionsAlwaysDescribeTheirPrice()
        {
            // Two tiles at half a point each reads as one whole point.
            StringAssert.Contains("1 AP", ActionLabels.Describe(Move(6, 2)));
            StringAssert.Contains($"{CombatRules.AttackCost} AP",
                ActionLabels.Describe(Attack(6, 1)));
        }

        [Test]
        public void AnUnaffordableActionSaysSo()
        {
            StringAssert.Contains("not enough", ActionLabels.Describe(Move(0, 5)));
        }
    }
}
