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

        [Test]
        public void MoveCostScalesWithSteps()
        {
            Assert.AreEqual(0, CombatRules.MoveCost(0));
            Assert.AreEqual(3 * CombatRules.MoveApPerTile, CombatRules.MoveCost(3));
        }

        [Test]
        public void ZeroDistanceIsNotInRange()
        {
            // Distance zero is the attacker's own hex. Melee reach must not include yourself.
            Assert.IsFalse(CombatRules.InRange(0));
            Assert.IsTrue(CombatRules.InRange(1));
            Assert.IsFalse(CombatRules.InRange(CombatRules.AttackRange + 1));
        }

        [Test]
        public void SpentMeansNoActionIsAffordable()
        {
            Assert.IsFalse(CombatRules.CanAffordAnything(0, anyMoveInRange: true, anyTargetInRange: true));
        }

        [Test]
        public void OneApStillBuysAStepButNotAnAttack()
        {
            Assert.IsTrue(CombatRules.CanAffordAnything(1, anyMoveInRange: true, anyTargetInRange: true));
            Assert.IsFalse(CombatRules.CanAffordAnything(1, anyMoveInRange: false, anyTargetInRange: true));
        }

        [Test]
        public void BoxedInWithNoTargetIsSpentEvenWithFullAp()
        {
            // The case that makes this a board question rather than an arithmetic one: a creature
            // walled in by its own allies has AP it cannot spend.
            Assert.IsFalse(CombatRules.CanAffordAnything(99, anyMoveInRange: false, anyTargetInRange: false));
        }
    }
}
