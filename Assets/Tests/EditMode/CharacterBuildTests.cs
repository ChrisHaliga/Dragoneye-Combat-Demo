using System.Collections.Generic;
using System.Linq;
using Dragoneye.Combat;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    // System.Attribute would otherwise win the name; the alias must be inside the namespace.
    using Attribute = Dragoneye.Combat.Attribute;

    /// <summary>
    /// A build arrives from a client, so the validator is a trust boundary rather than a convenience
    /// for the creation screen. These cases are the ones a hostile or stale client would send, plus
    /// the arithmetic a player is promised on screen.
    /// </summary>
    public class CharacterBuildTests
    {
        const int SwordId = 10;
        const int BowId = 11;
        const int PlateId = 20;
        const int Budget = 20;
        const int Level = 4;

        sealed class FakeContent : IContentIndex
        {
            readonly List<ClassSpec> m_Classes = new List<ClassSpec>();
            readonly List<EquipmentSpec> m_Equipment = new List<EquipmentSpec>();

            public FakeContent(CharacterRules rules) => Rules = rules;

            public CharacterRules Rules { get; }
            public IReadOnlyList<ClassSpec> Classes => m_Classes;
            public IReadOnlyList<EquipmentSpec> Equipment => m_Equipment;
            public IReadOnlyList<SkillSpec> Skills => System.Array.Empty<SkillSpec>();

            public FakeContent With(ClassSpec spec) { m_Classes.Add(spec); return this; }
            public FakeContent With(EquipmentSpec spec) { m_Equipment.Add(spec); return this; }
        readonly List<SpeciesSpec> m_SpeciesList =
            new List<SpeciesSpec> { new SpeciesSpec(1, "Human", AttributeBlock.Zero) };

        public IReadOnlyList<SpeciesSpec> Species => m_SpeciesList;

        public bool TryGetSpecies(int id, out SpeciesSpec spec)
        {
            spec = m_SpeciesList.FirstOrDefault(s => s.Id == id);
            return spec != null;
        }


            public bool TryGetClass(int id, out ClassSpec spec)
            {
                spec = m_Classes.FirstOrDefault(c => c.Id == id);
                return spec != null;
            }

            public bool TryGetEquipment(int id, out EquipmentSpec spec)
            {
                spec = id == CharacterBuild.NoEquipment
                    ? null : m_Equipment.FirstOrDefault(e => e.Id == id);
                return spec != null;
            }

            public bool TryGetSkill(int id, out SkillSpec spec)
            {
                spec = null;
                return false;
            }
        }

        static FakeContent Content() =>
            new FakeContent(new CharacterRules(Budget, 8, Level))
                .With(new ClassSpec(1, "Guardian", AttributeBlock.Zero, new[] { SwordId }))
                .With(new ClassSpec(2, "Hunter", AttributeBlock.Zero, new[] { BowId }))
                .With(new EquipmentSpec(SwordId, "Sword", EquipmentSlot.Weapon, AttributeBlock.Zero))
                .With(new EquipmentSpec(BowId, "Bow", EquipmentSlot.Weapon, AttributeBlock.Zero))
                .With(new EquipmentSpec(PlateId, "Plate", EquipmentSlot.Armor, AttributeBlock.Zero,
                    null, null, ArmourClass.Heavy));

        /// <summary>
        /// A build that passes, so each case below can break exactly one thing.
        ///
        /// Four attributes at three costs 4 x (1 + 2) = 12, and two at five costs 2 x (1 + 2 + 3 + 4)
        /// = 20 -- so this spends the budget exactly.
        /// </summary>
        static CharacterBuild Valid(IContentIndex content)
        {
            content.TryGetClass(1, out var guardian);
            var build = CharacterBuild.StartingFrom(content.Species[0], guardian);
            build.Name = "Ansel";

            // 1 + 2 = 3 each on four attributes is 12; 1 + 2 + 3 = 6 more on one is 18; two more
            // single steps bring it to 20.
            build.Attributes = new AttributeBlock(3, 3, 3, 3, 4, 2, 2);
            build.StartingPool = new ElementCounts(2, 1, 1, 0, 0, 0, 0);

            return build;
        }

        static List<BuildProblem> Problems(CharacterBuild build, IContentIndex content)
        {
            var faults = new List<BuildFault>();
            BuildValidator.Validate(build, content, faults);
            return faults.Select(f => f.Problem).ToList();
        }

        // ---------- point buy ----------

        [Test]
        public void RaisingAnAttributeCostsItsCurrentValue()
        {
            Assert.AreEqual(1, PointBuy.CostToRaise(1), "the first step off the floor is cheap");
            Assert.AreEqual(4, PointBuy.CostToRaise(4), "and every one after it is dearer");
        }

        [Test]
        public void ReachingAValueCostsEveryStepAlongTheWay()
        {
            Assert.AreEqual(0, PointBuy.CostOf(1), "the floor is free");
            Assert.AreEqual(1, PointBuy.CostOf(2));
            Assert.AreEqual(3, PointBuy.CostOf(3), "1 + 2");
            Assert.AreEqual(6, PointBuy.CostOf(4), "1 + 2 + 3");
            Assert.AreEqual(10, PointBuy.CostOf(5), "1 + 2 + 3 + 4");
        }

        [Test]
        public void TheFloorCostsNothingAcrossEveryAttribute()
        {
            Assert.AreEqual(0, PointBuy.TotalCost(AttributeBlock.Uniform(PointBuy.Floor)));
        }

        [Test]
        public void OneHighAttributeCostsFarMoreThanSeveralMiddling()
        {
            // The whole point of the curve: twenty points buys breadth or depth, never both.
            var deep = AttributeBlock.Uniform(1).With(Attribute.Strength, 6);
            var broad = new AttributeBlock(3, 3, 3, 3, 1, 1, 1);

            Assert.AreEqual(15, PointBuy.TotalCost(deep), "1+2+3+4+5");
            Assert.AreEqual(12, PointBuy.TotalCost(broad), "four attributes at 3 apiece");
        }

        [Test]
        public void CanRaiseRefusesWhatTheBudgetWillNotCover()
        {
            var spent = new AttributeBlock(3, 3, 3, 3, 4, 2, 2);

            Assert.AreEqual(0, PointBuy.Remaining(spent, Budget), "this spread spends exactly 20");
            Assert.IsFalse(PointBuy.CanRaise(spent, Attribute.Strength, Budget, 8));
        }

        [Test]
        public void CanRaiseRefusesAtTheCeiling()
        {
            var maxed = AttributeBlock.Uniform(1).With(Attribute.Skill, 8);

            Assert.IsFalse(PointBuy.CanRaise(maxed, Attribute.Skill, 999, 8));
        }

        // ---------- derived stats ----------

        [Test]
        public void HealthIsThreePlusLevelPlusVitalityPlusToughness()
        {
            var vitals = Vitals.From(
                AttributeBlock.Uniform(1).With(Attribute.Vitality, 4).With(Attribute.Toughness, 2),
                level: 3, armour: ArmourClass.None);

            Assert.AreEqual(3 + 3 + 4 + 2, vitals.MaxHealth);
        }

        [Test]
        public void ActionPointsNeverFallBelowTheFloor()
        {
            // 4 + END, floored at 8 -- so Endurance only starts buying points once it clears four.
            var weak = Vitals.From(AttributeBlock.Uniform(1), 1, ArmourClass.None);
            Assert.AreEqual(Ap.FromWhole(8), weak.MaxAp);

            var strong = Vitals.From(
                AttributeBlock.Uniform(1).With(Attribute.Endurance, 6), 1, ArmourClass.None);
            Assert.AreEqual(Ap.FromWhole(10), strong.MaxAp);
        }

        [Test]
        public void SpeedIsDexterityPlusEnduranceLessArmour()
        {
            var attributes = AttributeBlock.Uniform(1)
                .With(Attribute.Dexterity, 5)
                .With(Attribute.Endurance, 3);

            Assert.AreEqual(8, Vitals.From(attributes, 1, ArmourClass.None).Speed);
            Assert.AreEqual(5, Vitals.From(attributes, 1, ArmourClass.Heavy).Speed, "heavy costs 3");
        }

        [Test]
        public void TheHeaviestArmourWornDecidesTheSpeedPenalty()
        {
            // Summing two armour classes would stack a penalty the rules never intended.
            var content = Content();
            var build = Valid(content);
            build.ArmorId = PlateId;

            Assert.AreEqual(ArmourClass.Heavy, LoadoutResolver.Resolve(build, content).Armour);
        }

        // ---------- validation ----------

        [Test]
        public void ACompleteBuildIsAccepted()
        {
            var content = Content();
            CollectionAssert.IsEmpty(Problems(Valid(content), content));
        }

        [Test]
        public void PointsBeyondTheBudgetAreRefused()
        {
            var content = Content();
            var build = Valid(content);
            build.Attributes = build.Attributes.With(Attribute.Strength, 6);

            CollectionAssert.Contains(Problems(build, content), BuildProblem.OverBudget);
        }

        [Test]
        public void UnspentPointsAreRefusedToo()
        {
            var content = Content();
            var build = Valid(content);
            build.Attributes = AttributeBlock.Uniform(PointBuy.Floor);

            CollectionAssert.Contains(Problems(build, content), BuildProblem.UnderBudget);
        }

        [Test]
        public void AnAttributeBelowTheFloorIsRefused()
        {
            var content = Content();
            var build = Valid(content);
            build.Attributes = build.Attributes.With(Attribute.Willpower, 0);

            CollectionAssert.Contains(Problems(build, content), BuildProblem.AttributeBelowFloor);
        }

        [Test]
        public void ThePoolMustTotalTheLevelInAnyShape()
        {
            var content = Content();

            // Four of one element is as legal as one each of four.
            var narrow = Valid(content);
            narrow.StartingPool = new ElementCounts(0, 4, 0, 0, 0, 0, 0);
            CollectionAssert.IsEmpty(Problems(narrow, content));

            var broad = Valid(content);
            broad.StartingPool = new ElementCounts(1, 1, 1, 1, 0, 0, 0);
            CollectionAssert.IsEmpty(Problems(broad, content));

            var short_ = Valid(content);
            short_.StartingPool = new ElementCounts(1, 0, 0, 0, 0, 0, 0);
            CollectionAssert.Contains(Problems(short_, content), BuildProblem.PoolWrongSize);

            var over = Valid(content);
            over.StartingPool = new ElementCounts(9, 0, 0, 0, 0, 0, 0);
            CollectionAssert.Contains(Problems(over, content), BuildProblem.PoolWrongSize);
        }

        [Test]
        public void AWeaponTheClassCannotCarryIsRefused()
        {
            var content = Content();
            var build = Valid(content);
            build.WeaponId = BowId;

            CollectionAssert.Contains(Problems(build, content), BuildProblem.WeaponNotForClass);
        }

        [Test]
        public void ArmourInTheWeaponSlotIsRefused()
        {
            var content = Content();
            var build = Valid(content);
            build.WeaponId = PlateId;

            CollectionAssert.Contains(Problems(build, content), BuildProblem.ItemInWrongSlot);
        }

        [Test]
        public void AnUnknownClassStopsFurtherComplaints()
        {
            var content = Content();
            var build = Valid(content);
            build.ClassId = 404;

            var problems = Problems(build, content);

            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(BuildProblem.ClassUnknown, problems[0]);
        }

        [Test]
        public void NullsAreRefusedRatherThanThrowing()
        {
            var content = Content();

            Assert.IsFalse(BuildValidator.IsValid(null, content));
            Assert.IsFalse(BuildValidator.IsValid(Valid(content), null));
            Assert.IsFalse(BuildValidator.IsValid(null, null));
        }

        [Test]
        public void ModifierOrderCannotChangeResolvedAttributes()
        {
            // DE-003 requires two clients resolving one loadout to agree. Whole-block addition is
            // commutative, which is what guarantees it.
            var content = Content();
            var build = Valid(content);

            Assert.AreEqual(
                LoadoutResolver.Resolve(build, content).Attributes,
                LoadoutResolver.Resolve(new CharacterBuild(build), content).Attributes);
        }

        [Test]
        public void AnInvalidBuildStillResolves()
        {
            // The creator resolves while the player is mid-edit, so resolution must not refuse.
            Assert.IsNotNull(LoadoutResolver.Resolve(new CharacterBuild { ClassId = 404 }, Content()));
        }

        [Test]
        public void ApIsStoredInHalfUnits()
        {
            Assert.AreEqual(6, Ap.FromWhole(3).Units);
            Assert.AreEqual(1, Ap.Step.Units);
            Assert.AreEqual("2.5", (Ap.FromWhole(3) - Ap.Step).ToString());
            Assert.IsTrue((Ap.FromWhole(1) - Ap.FromWhole(5)).IsZero, "spending past zero floors");
        }
    }
}
