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
        // Seven more than it was. Attributes now start at zero, so the first point of each is
        // bought rather than given, and the budget covers exactly that -- every spread that fitted
        // before still fits, and dumping one now pays for something.
        const int Budget = 27;
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
                    null, ArmourClass.Heavy));

        /// <summary>
        /// A build that passes, so each case below can break exactly one thing.
        ///
        /// Four attributes at three cost 4 x (1 + 1 + 2) = 16, one at four costs 7, and two at two
        /// cost 2 x (1 + 1) = 4 -- so this spends the budget exactly.
        /// </summary>
        static CharacterBuild Valid(IContentIndex content)
        {
            content.TryGetClass(1, out var guardian);
            var build = CharacterBuild.StartingFrom(content.Species[0], guardian, Level);
            build.Name = "Ansel";

            // Four at 3 is 16; one at 4 is 7 more, making 23; two at 2 bring it to 27.
            build.Attributes = new AttributeBlock(3, 3, 3, 3, 4, 2, 2);
            // Four points of pool at level four. All four of these are a point each.
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
        public void TheFirstPointIsNotFree()
        {
            // The rule is "a step costs the value it leaves", and the value it leaves is now zero.
            // Taken literally that would make the first point of all seven attributes free, which
            // is not a floor at zero -- it is a floor at one wearing a different number.
            Assert.AreEqual(1, PointBuy.CostToRaise(0));
        }

        [Test]
        public void ReachingAValueCostsEveryStepAlongTheWay()
        {
            Assert.AreEqual(0, PointBuy.CostOf(0), "the floor is free");
            Assert.AreEqual(1, PointBuy.CostOf(1), "but standing off it is not");
            Assert.AreEqual(2, PointBuy.CostOf(2), "1 + 1");
            Assert.AreEqual(4, PointBuy.CostOf(3), "1 + 1 + 2");
            Assert.AreEqual(7, PointBuy.CostOf(4), "1 + 1 + 2 + 3");
            Assert.AreEqual(11, PointBuy.CostOf(5), "1 + 1 + 2 + 3 + 4");
        }

        [Test]
        public void TheFloorCostsNothingAcrossEveryAttribute()
        {
            Assert.AreEqual(0, PointBuy.TotalCost(AttributeBlock.Uniform(PointBuy.Floor)));
        }

        [Test]
        public void OneHighAttributeCostsFarMoreThanSeveralMiddling()
        {
            // The whole point of the curve: the budget buys breadth or depth, never both.
            var deep = AttributeBlock.Uniform(1).With(Attribute.Strength, 6);
            var broad = new AttributeBlock(3, 3, 3, 3, 1, 1, 1);

            Assert.AreEqual(22, PointBuy.TotalCost(deep), "six ones, and 1+1+2+3+4+5 for the six");
            Assert.AreEqual(19, PointBuy.TotalCost(broad), "four at 3 apiece, three at 1");
        }

        [Test]
        public void CanRaiseRefusesWhatTheBudgetWillNotCover()
        {
            var spent = new AttributeBlock(3, 3, 3, 3, 4, 2, 2);

            Assert.AreEqual(0, PointBuy.Remaining(spent, Budget), "this spread spends the lot");
            Assert.IsFalse(PointBuy.CanRaise(spent, Attribute.Strength, Budget, 8));
        }

        [Test]
        public void DumpingAnAttributeGivesItsPointsBack()
        {
            // What a floor of zero is for. At a floor of one this was not a decision anybody could
            // make: every character was adequate at all seven whether it wanted to be or not.
            var spread = new AttributeBlock(3, 3, 3, 3, 4, 2, 2);

            Assert.AreEqual(2, PointBuy.TotalCost(spread)
                - PointBuy.TotalCost(spread.With(Attribute.Willpower, 0)),
                "dropping a 2 refunds what reaching 2 cost");
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
        public void ActionPointsAreTheSpeciesBasePlusEndurance()
        {
            // No floor any more. A floor and an authored base are two answers to the same question,
            // and with both in place the authored one did nothing until Endurance had cleared the
            // floor on its own -- which made "AP is species dependent" untrue in every real build.
            // The untouched character is the one that mattered. With attributes starting at one
            // it arrived a point of Endurance ahead, so a species authored with four action points
            // fielded five and the authored number was never the number anybody saw.
            var untouched = Vitals.From(AttributeBlock.Zero, 1, ArmourClass.None);
            Assert.AreEqual(Ap.FromWhole(Vitals.DefaultBaseAp), untouched.MaxAp,
                "exactly what the species says, and nothing on top");

            var weak = Vitals.From(AttributeBlock.Uniform(1), 1, ArmourClass.None);
            Assert.AreEqual(Ap.FromWhole(5), weak.MaxAp, "the default base of four, plus one END");

            var brisk = Vitals.From(AttributeBlock.Uniform(1), 1, ArmourClass.None, baseAp: 6);
            Assert.AreEqual(Ap.FromWhole(7), brisk.MaxAp, "a species that says otherwise is obeyed");

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
            // Zero is a legal choice now, and a cheap one. Below it is still nonsense: the derived
            // stats count attributes, and a negative one would buy health back off the character.
            var content = Content();
            var build = Valid(content);
            build.Attributes = build.Attributes.With(Attribute.Willpower, -1);

            CollectionAssert.Contains(Problems(build, content), BuildProblem.AttributeBelowFloor);
        }

        [Test]
        public void AnAttributeAtZeroIsAllowed()
        {
            var content = Content();
            var build = Valid(content);

            // Willpower dumped, and the two points it refunds spent on Endurance, so the spread
            // still costs the budget exactly and the whole build has to come back clean.
            build.Attributes = build.Attributes
                .With(Attribute.Willpower, 0)
                .With(Attribute.Endurance, 3);

            CollectionAssert.IsEmpty(Problems(build, content));
        }

        [Test]
        public void ThePoolMustCostTheLevelInAnyShape()
        {
            var content = Content();

            // Four of one cheap element is as legal as one each of four.
            var narrow = Valid(content);
            narrow.StartingPool = new ElementCounts(0, 4, 0, 0, 0, 0, 0);
            CollectionAssert.IsEmpty(Problems(narrow, content));

            var broad = Valid(content);
            broad.StartingPool = new ElementCounts(1, 1, 1, 1, 0, 0, 0);
            CollectionAssert.IsEmpty(Problems(broad, content));

            // And so is one Arcana and one Geo: three plus one is the same four points. Depth
            // against rarity is the decision, and the budget is what both are measured in.
            var rare = Valid(content);
            rare.StartingPool = new ElementCounts(1, 0, 0, 0, 0, 0, 1);
            CollectionAssert.IsEmpty(Problems(rare, content));

            // Two Lux costs four even though it is only two gems.
            var opposed = Valid(content);
            opposed.StartingPool = new ElementCounts(0, 0, 0, 0, 2, 0, 0);
            CollectionAssert.IsEmpty(Problems(opposed, content));

            var short_ = Valid(content);
            short_.StartingPool = new ElementCounts(1, 0, 0, 0, 0, 0, 0);
            CollectionAssert.Contains(Problems(short_, content), BuildProblem.PoolWrongSize);

            var over = Valid(content);
            over.StartingPool = new ElementCounts(9, 0, 0, 0, 0, 0, 0);
            CollectionAssert.Contains(Problems(over, content), BuildProblem.PoolWrongSize);

            // Four gems that cost more than four points is over budget, not exactly right.
            var expensive = Valid(content);
            expensive.StartingPool = new ElementCounts(0, 0, 0, 0, 0, 0, 4);
            CollectionAssert.Contains(Problems(expensive, content), BuildProblem.PoolWrongSize);
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
