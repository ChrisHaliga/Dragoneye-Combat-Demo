using Dragoneye.Combat;
using Dragoneye.Game;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    public class CombatArithmeticTests
    {
        [Test]
        public void DamageNeverDrivesHealthBelowZero()
        {
            // A negative pool would make a corpse look healable and break every X/Y readout.
            Assert.AreEqual(0, CombatRules.Damaged(3, 999));
        }

        [Test]
        public void DamageSubtracts()
        {
            Assert.AreEqual(15, CombatRules.Damaged(20, 5));
        }

        [Test]
        public void NonPositiveDamageLeavesHealthAlone()
        {
            Assert.AreEqual(20, CombatRules.Damaged(20, 0));
            Assert.AreEqual(20, CombatRules.Damaged(20, -5));
        }

        [Test]
        public void ZeroHealthIsDeadAndOneIsNot()
        {
            Assert.IsFalse(CombatRules.IsAlive(0));
            Assert.IsTrue(CombatRules.IsAlive(1));
        }

        static readonly Ap k_Step = CombatRules.BaseStepCost;

        [Test]
        public void MoveCostScalesWithSteps()
        {
            Assert.AreEqual(Ap.Zero, CombatRules.MoveCost(0, k_Step));
            Assert.AreEqual(k_Step * 3, CombatRules.MoveCost(3, k_Step));

            // DE-000: half a point per tile, so two tiles is one whole point.
            Assert.AreEqual(Ap.FromWhole(1), CombatRules.MoveCost(2, k_Step));
        }

        [Test]
        public void TheSuitSetsThePriceOfAStep()
        {
            // Half a point unarmoured, and half a point more per class: the numbers the help
            // page promises, pinned so a retune of one does not quietly drift the others.
            Assert.AreEqual(Ap.Step, ArmourRules.StepCost(ArmourClass.None));
            Assert.AreEqual(Ap.FromWhole(1), ArmourRules.StepCost(ArmourClass.Light));
            Assert.AreEqual(Ap.FromWhole(1) + Ap.Step, ArmourRules.StepCost(ArmourClass.Medium));
            Assert.AreEqual(Ap.FromWhole(2), ArmourRules.StepCost(ArmourClass.Heavy));

            // Three points is six tiles in nothing and one tile in plate.
            Assert.AreEqual(6, CombatRules.StepsAffordable(Ap.FromWhole(3), Ap.Step));
            Assert.AreEqual(1, CombatRules.StepsAffordable(Ap.FromWhole(3),
                ArmourRules.StepCost(ArmourClass.Heavy)));
        }

        [Test]
        public void ZeroDistanceIsNotInRange()
        {
            // Distance zero is the user's own hex. Nothing aimed at somebody else reaches it,
            // however long its reach.
            Assert.IsFalse(CombatRules.InRange(0, 1));
            Assert.IsFalse(CombatRules.InRange(0, 9));

            Assert.IsTrue(CombatRules.InRange(1, 1), "melee is one tile");
            Assert.IsFalse(CombatRules.InRange(2, 1));
            Assert.IsTrue(CombatRules.InRange(4, 4), "and a bow reaches as far as it says");
        }

        [Test]
        public void SpentMeansNoActionIsAffordable()
        {
            Assert.IsFalse(CombatRules.CanAffordAnything(Ap.Zero,
                anyMoveInRange: true, anySkillUsable: false, stepCost: k_Step));
        }

        [Test]
        public void HalfAPointStillBuysAStep()
        {
            Assert.IsTrue(CombatRules.CanAffordAnything(Ap.Step,
                anyMoveInRange: true, anySkillUsable: false, stepCost: k_Step));
            Assert.IsFalse(CombatRules.CanAffordAnything(Ap.Step,
                anyMoveInRange: false, anySkillUsable: false, stepCost: k_Step));

            // But not in plate, where a step is two whole points.
            Assert.IsFalse(CombatRules.CanAffordAnything(Ap.Step,
                anyMoveInRange: true, anySkillUsable: false,
                stepCost: ArmourRules.StepCost(ArmourClass.Heavy)));
        }

        [Test]
        public void AUsableSkillIsSomethingToDoWhateverTheApSays()
        {
            // Whether a skill is affordable is the skill's own question, asked of the same rules
            // the bar and the server ask. By the time it reaches here it has been answered.
            Assert.IsTrue(CombatRules.CanAffordAnything(Ap.Zero,
                anyMoveInRange: false, anySkillUsable: true, stepCost: k_Step));
        }

        [Test]
        public void StepsAffordableCountsTilesNotPoints()
        {
            // Three whole points is six tiles, because a tile is half a point.
            Assert.AreEqual(6, CombatRules.StepsAffordable(Ap.FromWhole(3), k_Step));
            Assert.AreEqual(1, CombatRules.StepsAffordable(Ap.Step, k_Step));
            Assert.AreEqual(0, CombatRules.StepsAffordable(Ap.Zero, k_Step));
        }

        [Test]
        public void BoxedInWithNoTargetIsSpentEvenWithFullAp()
        {
            // The case that makes this a board question rather than an arithmetic one: a creature
            // walled in by its own allies has AP it cannot spend.
            Assert.IsFalse(CombatRules.CanAffordAnything(Ap.FromWhole(99),
                anyMoveInRange: false, anySkillUsable: false, stepCost: k_Step));
        }
    }
}
