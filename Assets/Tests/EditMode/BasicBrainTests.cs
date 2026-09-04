using System.Collections.Generic;
using System.Linq;
using Dragoneye.Combat;
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

        /// <summary>
        /// A melee skill and a ranged one, so "which does it pick" and "how close does it get" are
        /// both askable. Neither costs an element: the brain's choice is being tested here, not the
        /// pool, and a creature that cannot pay would only ever walk.
        /// </summary>
        static readonly SkillSpec k_Jab = new SkillSpec(1, "Jab", Element.Aero, Ap.FromWhole(1), 0,
            1, SkillTarget.Creature, new SkillEffect(SkillEffectKind.Damage, 3));

        static readonly SkillSpec k_Cleave = new SkillSpec(2, "Cleave", Element.Geo, Ap.FromWhole(2),
            0, 1, SkillTarget.Creature, new SkillEffect(SkillEffectKind.Damage, 11));

        static readonly SkillSpec k_Loose = new SkillSpec(3, "Loose", Element.Aero, Ap.FromWhole(1),
            0, 4, SkillTarget.Creature, new SkillEffect(SkillEffectKind.Damage, 5));

        /// <summary>Costs an element, so a creature holding none cannot use it.</summary>
        static readonly SkillSpec k_Strike = new SkillSpec(4, "Strike", Element.Pyro,
            Ap.FromWhole(1), 1, 1, SkillTarget.Creature,
            new SkillEffect(SkillEffectKind.Damage, 6));

        static readonly SkillSpec k_Breath = new SkillSpec(5, "Take a Breath", Element.Arcana,
            Ap.FromWhole(1), 0, 0, SkillTarget.Self,
            new SkillEffect(SkillEffectKind.ReturnElement, 1));

        static BrainView Actor(Hex cell, int wholeAp = 6, Party party = Party.Monsters,
            params SkillSpec[] skills) =>
            new BrainView(1, cell, party, Ap.FromWhole(wholeAp), 20,
                skills.Length > 0 ? skills : new[] { k_Jab });

        static BrainView Enemy(uint id, Hex cell, int hp = 20) =>
            new BrainView(id, cell, Party.Heroes, Ap.FromWhole(6), hp);

        [Test]
        public void UsesASkillOnAnAdjacentEnemy()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero), new[] { Enemy(2, new Hex(1, 0)) }, new OpenBoard(new Hex(1, 0)));

            Assert.AreEqual(BrainAction.UseSkill, decision.Action);
            Assert.AreEqual(2u, decision.TargetId);
            Assert.AreEqual(k_Jab.Id, decision.SkillId);
        }

        [Test]
        public void PicksTheHardestHittingSkillItCanAfford()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero, 6, Party.Monsters, k_Jab, k_Cleave),
                new[] { Enemy(2, new Hex(1, 0)) }, new OpenBoard(new Hex(1, 0)));

            Assert.AreEqual(k_Cleave.Id, decision.SkillId);
        }

        [Test]
        public void FallsBackToWhatItCanStillPayFor()
        {
            // One point left: Cleave wants two, so the cheap one is the only thing on offer.
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero, 1, Party.Monsters, k_Jab, k_Cleave),
                new[] { Enemy(2, new Hex(1, 0)) }, new OpenBoard(new Hex(1, 0)));

            Assert.AreEqual(BrainAction.UseSkill, decision.Action);
            Assert.AreEqual(k_Jab.Id, decision.SkillId);
        }

        [Test]
        public void ReachesFromWhereItStandsWhenItCan()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero, 6, Party.Monsters, k_Loose),
                new[] { Enemy(2, new Hex(3, 0)) }, new OpenBoard(new Hex(3, 0)));

            Assert.AreEqual(BrainAction.UseSkill, decision.Action, "three tiles is inside four");
        }

        [Test]
        public void StopsWalkingAsSoonAsSomethingReaches()
        {
            // A bow at range four. Closing to melee would arrive with nothing left to loose, which
            // is the whole reason the walk stops early.
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero, 6, Party.Monsters, k_Loose),
                new[] { Enemy(2, new Hex(8, 0)) }, new OpenBoard(new Hex(8, 0)));

            Assert.AreEqual(BrainAction.Move, decision.Action);
            Assert.AreEqual(4, Hex.Distance(decision.Destination, new Hex(8, 0)),
                "it stops at the edge of its reach rather than walking into melee");
        }

        // ---------- the state machine ----------

        [Test]
        public void StrikesWhenSomethingItHoldsReaches()
        {
            var plan = BasicBrain.Assess(
                Actor(Hex.Zero, 6, Party.Monsters, k_Jab), Enemy(2, new Hex(1, 0)));

            Assert.AreEqual(BrainState.Striking, plan.State);
            Assert.AreEqual(k_Jab.Id, plan.Skill.Id);
        }

        [Test]
        public void ClosesWhenItCouldActButNotFromHere()
        {
            var plan = BasicBrain.Assess(
                Actor(Hex.Zero, 6, Party.Monsters, k_Jab), Enemy(2, new Hex(5, 0)));

            Assert.AreEqual(BrainState.Closing, plan.State);
        }

        [Test]
        public void RecoversRatherThanWalkingWhenItCanPayForNothing()
        {
            // A skill it cannot afford and a breath it can. Walking closer would put it next to
            // somebody it still could not hit; getting the element back is the only thing that
            // changes the situation.
            ElementLedger.Starting(new ElementCounts(0, 0, 1, 0, 0, 0, 0))
                .TrySpend(Element.Pyro, 1, out var spent, out _);

            var actor = new BrainView(1, Hex.Zero, Party.Monsters, Ap.FromWhole(6), 20,
                new[] { k_Strike, k_Breath }, spent);

            var plan = BasicBrain.Assess(actor, Enemy(2, new Hex(5, 0)));

            Assert.AreEqual(BrainState.Recovering, plan.State);
            Assert.AreEqual(k_Breath.Id, plan.Skill.Id);
            Assert.AreEqual(1u, plan.TargetId, "aimed at itself, because that is what it acts on");
        }

        [Test]
        public void DoesNotStandAroundBreathingWhileItCanStillFight()
        {
            var actor = new BrainView(1, Hex.Zero, Party.Monsters, Ap.FromWhole(6), 20,
                new[] { k_Jab, k_Breath });

            Assert.AreEqual(BrainState.Striking, BasicBrain.Assess(actor, Enemy(2, new Hex(1, 0))).State);
        }

        [Test]
        public void IdlesWithNobodyToFight()
        {
            Assert.AreEqual(BrainState.Idle,
                BasicBrain.Assess(Actor(Hex.Zero), null).State);
        }

        [Test]
        public void IdlesWithNothingLeftToSpend()
        {
            var actor = new BrainView(1, Hex.Zero, Party.Monsters, Ap.Zero, 20, new[] { k_Jab });

            Assert.AreEqual(BrainState.Idle, BasicBrain.Assess(actor, Enemy(2, new Hex(5, 0))).State);
        }

        [Test]
        public void ACreatureWithNothingToFightWithWalksUpAndStandsThere()
        {
            // Correct, not a gap: there is no generic punch any more, so a creature authored
            // without an offensive skill has nothing to do once it arrives.
            var breath = new SkillSpec(9, "Take a Breath", Element.Arcana, Ap.FromWhole(1), 0, 0,
                SkillTarget.Self, new SkillEffect(SkillEffectKind.ReturnElement, 1));

            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero, 6, Party.Monsters, breath),
                new[] { Enemy(2, new Hex(1, 0)) }, new OpenBoard(new Hex(1, 0)));

            Assert.AreNotEqual(BrainAction.UseSkill, decision.Action);
        }

        [Test]
        public void MovesTowardTheEnemyWhenOutOfReach()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero), new[] { Enemy(2, new Hex(4, 0)) }, new OpenBoard(new Hex(4, 0)));

            Assert.AreEqual(BrainAction.Move, decision.Action);
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
                Actor(Hex.Zero, wholeAp: 2), new[] { Enemy(2, new Hex(6, 0)) },
                new OpenBoard(new Hex(6, 0)));

            Assert.AreEqual(BrainAction.Move, decision.Action);
            // Half a point per tile, so two whole points buys four tiles -- asserted through the
            // rule rather than a literal, so changing the cost does not silently pass a stale test.
            Assert.LessOrEqual(Hex.Distance(Hex.Zero, decision.Destination),
                CombatRules.StepsAffordable(Ap.FromWhole(2)));
        }

        [Test]
        public void PassesWithNoApLeft()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero, wholeAp: 0), new[] { Enemy(2, new Hex(4, 0)) },
                new OpenBoard(new Hex(4, 0)));

            Assert.AreEqual(BrainAction.None, decision.Action);
        }

        [Test]
        public void StandsStillRatherThanPacingWhenItCannotAffordAnything()
        {
            // Half a point: adjacent, and cannot use even the cheap skill. Every tile around the
            // enemy is a legal destination and none of them is an improvement, so a brain that
            // moved here would shuffle back and forth until its AP ran out.
            var decision = new BasicBrain().Decide(
                new BrainView(1, Hex.Zero, Party.Monsters, Ap.Step, 20, new[] { k_Jab }),
                new[] { Enemy(2, new Hex(1, 0)) }, new OpenBoard(new Hex(1, 0)));

            Assert.AreEqual(BrainAction.None, decision.Action);
        }

        [Test]
        public void NeverStepsSomewhereNoCloserThanWhereItStands()
        {
            // The same guard from further out: a creature with nothing it can pay for is adjacent
            // and staying there, rather than circling the enemy for the rest of the turn.
            var decision = new BasicBrain().Decide(
                new BrainView(1, Hex.Zero, Party.Monsters, Ap.FromWhole(6), 20, new SkillSpec[0]),
                new[] { Enemy(2, new Hex(1, 0)) }, new OpenBoard(new Hex(1, 0)));

            Assert.AreEqual(BrainAction.None, decision.Action);
        }

        [Test]
        public void IgnoresItsOwnParty()
        {
            var ally = new BrainView(2, new Hex(1, 0), Party.Monsters, Ap.FromWhole(6), 20);

            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero), new[] { ally }, new OpenBoard(new Hex(1, 0)));

            Assert.AreEqual(BrainAction.None, decision.Action, "There is no enemy to fight");
        }

        [Test]
        public void IgnoresTheDead()
        {
            var corpse = Enemy(2, new Hex(1, 0), hp: 0);

            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero), new[] { corpse }, new OpenBoard(new Hex(1, 0)));

            Assert.AreEqual(BrainAction.None, decision.Action);
        }

        [Test]
        public void ChoosesTheNearestOfSeveralEnemies()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero),
                new[] { Enemy(2, new Hex(5, 0)), Enemy(3, new Hex(1, 0)) },
                new OpenBoard(new Hex(5, 0), new Hex(1, 0)));

            Assert.AreEqual(BrainAction.UseSkill, decision.Action);
            Assert.AreEqual(3u, decision.TargetId, "The adjacent one");
        }

        [Test]
        public void PassesWithNothingToFight()
        {
            var decision = new BasicBrain().Decide(
                Actor(Hex.Zero), new BrainView[0], new OpenBoard());

            Assert.AreEqual(BrainAction.None, decision.Action);
        }

        [Test]
        public void NullInputsPassRatherThanThrow()
        {
            Assert.AreEqual(BrainAction.None,
                new BasicBrain().Decide(Actor(Hex.Zero), null, new OpenBoard()).Action);
            Assert.AreEqual(BrainAction.None,
                new BasicBrain().Decide(Actor(Hex.Zero), new BrainView[0], null).Action);
        }
    }
}
