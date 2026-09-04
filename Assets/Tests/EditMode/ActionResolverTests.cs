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
            ActionResolver.Resolve(true, true, Ap.FromWhole(wholeAp), false, steps);

        static ActionPlan OnACreature(int wholeAp) =>
            ActionResolver.Resolve(true, true, Ap.FromWhole(wholeAp), true, -1);

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
        public void AnOccupiedHexOffersNothingToPrice()
        {
            // There is no bare attack any more: reaching somebody is a skill, chosen on the bar
            // before the board is clicked. A click here reads the creature's card and costs nothing,
            // so there is nothing for the cursor to price or to explain.
            var plan = OnACreature(wholeAp: 6);

            Assert.AreEqual(BoardAction.None, plan.Action);
            Assert.AreEqual(ActionRefusal.Occupied, plan.Refusal);
            Assert.IsFalse(plan.IsAllowed);
            Assert.IsEmpty(ActionLabels.Describe(plan));
        }

        [Test]
        public void NothingIsOfferedWhenItIsNotYourTurn()
        {
            var plan = ActionResolver.Resolve(isActorsTurn: false, controlsActor: true,
                currentAp: Ap.FromWhole(6), targetOccupied: false, moveSteps: 2);

            Assert.AreEqual(ActionRefusal.NotYourTurn, plan.Refusal);
            Assert.IsFalse(plan.IsAllowed);
        }

        [Test]
        public void NothingIsOfferedForACreatureYouDoNotControl()
        {
            // Checked before the turn, so hovering with an enemy active says nothing at all rather
            // than "not your turn" over every hex on the board.
            var plan = ActionResolver.Resolve(isActorsTurn: true, controlsActor: false,
                currentAp: Ap.FromWhole(6), targetOccupied: false, moveSteps: 2);

            Assert.AreEqual(ActionRefusal.NotYours, plan.Refusal);
            Assert.IsEmpty(ActionLabels.Describe(plan));
        }

        [Test]
        public void AllowedActionsAlwaysDescribeTheirPrice()
        {
            // Two tiles at half a point each reads as one whole point.
            StringAssert.Contains("1 AP", ActionLabels.Describe(Move(6, 2)));
            StringAssert.Contains("0.5 AP", ActionLabels.Describe(Move(6, 1)));
        }

        // ---------- an armed skill ----------

        static readonly SkillSpec k_Strike = new SkillSpec(1, "Strike", Element.Pyro,
            Ap.FromWhole(1), 0, 1, SkillTarget.Creature,
            new SkillEffect(SkillEffectKind.Damage, 6));

        static ActionPlan Aim(int wholeAp, int steps, bool enemy = true, bool creature = true) =>
            ActionResolver.ResolveSkill(true, true, Ap.FromWhole(wholeAp), k_Strike,
                targetIsCreature: creature, targetIsEnemy: enemy, stepsToReach: steps);

        [Test]
        public void AnEnemyAlreadyInReachCostsTheSkillAlone()
        {
            var plan = Aim(wholeAp: 6, steps: 0);

            Assert.AreEqual(BoardAction.UseSkill, plan.Action);
            Assert.AreEqual(k_Strike.ApCost, plan.Cost);
            Assert.IsTrue(plan.MoveCost.IsZero);
            Assert.IsTrue(plan.IsAllowed);
        }

        [Test]
        public void WalkingIntoReachIsPartOfThePrice()
        {
            // Three tiles at half a point each, then the skill. The halves are kept apart because
            // one of them is avoidable by standing somewhere else first.
            var plan = Aim(wholeAp: 6, steps: 3);

            Assert.AreEqual(CombatRules.MoveCost(3) + k_Strike.ApCost, plan.Cost);
            Assert.AreEqual(CombatRules.MoveCost(3), plan.MoveCost);
            Assert.IsTrue(plan.IsAllowed);

            var label = ActionLabels.Describe(plan);
            StringAssert.Contains("Strike", label);
            StringAssert.Contains($"{CombatRules.MoveCost(3)} + {k_Strike.ApCost} AP", label);
        }

        [Test]
        public void AnApproachItCannotAffordIsPricedAndRefused()
        {
            // Enough for the walk or the swing, but not for both. Priced anyway, so the player can
            // see they are one point short rather than being told nothing.
            var plan = Aim(wholeAp: 1, steps: 3);

            Assert.AreEqual(BoardAction.UseSkill, plan.Action);
            Assert.IsTrue(plan.IsUnaffordable);
            StringAssert.Contains("not enough", ActionLabels.Describe(plan));
        }

        [Test]
        public void BareGroundIsNotATargetForSomethingAimedAtACreature()
        {
            // The misclick this exists to stop: with a skill armed, clicking the tile beside an
            // enemy must not quietly spend the turn walking there.
            var plan = Aim(wholeAp: 6, steps: 0, creature: false);

            Assert.AreEqual(BoardAction.None, plan.Action);
            Assert.AreEqual(ActionRefusal.NoTarget, plan.Refusal);
        }

        [Test]
        public void AlliesAreNotTargetsEither()
        {
            var plan = Aim(wholeAp: 6, steps: 0, enemy: false);

            Assert.AreEqual(BoardAction.None, plan.Action);
        }

        [Test]
        public void AnEnemyWithNowhereToStandIsRefused()
        {
            var plan = Aim(wholeAp: 6, steps: -1);

            Assert.AreEqual(ActionRefusal.Unreachable, plan.Refusal);
            Assert.IsFalse(plan.IsAllowed);
        }

        [Test]
        public void AnUnaffordableActionSaysSo()
        {
            StringAssert.Contains("not enough", ActionLabels.Describe(Move(0, 5)));
        }
    }
}
