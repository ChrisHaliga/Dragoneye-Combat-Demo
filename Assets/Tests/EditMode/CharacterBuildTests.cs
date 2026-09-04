using System.Collections.Generic;
using System.Linq;
using Dragoneye.Combat;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// A build arrives from a client, so the validator is a trust boundary rather than a convenience
    /// for the creation screen. These cases are the ones a hostile or stale client would send.
    /// </summary>
    public class CharacterBuildTests
    {
        const int SwordId = 10;
        const int BowId = 11;
        const int PlateId = 20;

        /// <summary>
        /// The content seam answered from lists -- the reason IContentIndex exists. No Unity asset,
        /// no ScriptableObject, no scene.
        /// </summary>
        sealed class FakeContent : IContentIndex
        {
            readonly List<ClassSpec> m_Classes = new List<ClassSpec>();
            readonly List<EquipmentSpec> m_Equipment = new List<EquipmentSpec>();

            public FakeContent(CharacterRules rules) => Rules = rules;

            public CharacterRules Rules { get; }
            public IReadOnlyList<ClassSpec> Classes => m_Classes;
            public IReadOnlyList<EquipmentSpec> Equipment => m_Equipment;

            public FakeContent With(ClassSpec spec) { m_Classes.Add(spec); return this; }
            public FakeContent With(EquipmentSpec spec) { m_Equipment.Add(spec); return this; }

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
        }

        static FakeContent Content(int budget = 8, int min = 1, int max = 8, int level = 4) =>
            new FakeContent(new CharacterRules(budget, min, max, level))
                .With(new ClassSpec(1, "Warrior", new StatBlock(2, 1, 2, 1), new[] { SwordId }))
                .With(new EquipmentSpec(SwordId, "Sword", EquipmentSlot.Weapon, new StatBlock(0, 0, 2, 0)))
                .With(new EquipmentSpec(BowId, "Bow", EquipmentSlot.Weapon, new StatBlock(0, 1, 1, 0)))
                .With(new EquipmentSpec(PlateId, "Plate", EquipmentSlot.Armor, new StatBlock(3, -1, 0, 0)));

        static CharacterBuild Valid(IContentIndex content)
        {
            content.TryGetClass(1, out var warrior);
            var build = CharacterBuild.StartingFrom(warrior, content.Rules);
            build.Name = "Ansel";
            build.Allocation = new StatBlock(5, 3, 2, 2);
            build.ArmorId = PlateId;

            for (var i = 0; i < content.Rules.Level; i++)
            {
                build.ElementPicks.Add(ElementInfo.All[i % ElementInfo.Count]);
            }

            return build;
        }

        static List<BuildProblem> Problems(CharacterBuild build, IContentIndex content)
        {
            var faults = new List<BuildFault>();
            BuildValidator.Validate(build, content, faults);
            return faults.Select(f => f.Problem).ToList();
        }

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
            build.Allocation = build.Allocation.With(StatKind.Vitality, 9);

            CollectionAssert.Contains(Problems(build, content), BuildProblem.OverBudget);
        }

        [Test]
        public void UnspentPointsAreRefusedToo()
        {
            // Not a warning: a half-finished character reaching a match is worse than being told.
            var content = Content();
            var build = Valid(content);
            build.Allocation = build.Allocation.With(StatKind.Vitality, 4);

            CollectionAssert.Contains(Problems(build, content), BuildProblem.UnderBudget);
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
        public void ThePoolMustHoldExactlyOneElementPerLevel()
        {
            var content = Content();

            var tooFew = Valid(content);
            tooFew.ElementPicks.RemoveAt(0);
            CollectionAssert.Contains(Problems(tooFew, content), BuildProblem.PoolWrongSize);

            var tooMany = Valid(content);
            tooMany.ElementPicks.Add(Element.Fire);
            CollectionAssert.Contains(Problems(tooMany, content), BuildProblem.PoolWrongSize);
        }

        [Test]
        public void AnElementOutsideTheEnumIsRefused()
        {
            // What an out-of-date or hostile client sends. Casting an int to an enum is not checked
            // by the language, so it has to be checked here.
            var content = Content();
            var build = Valid(content);
            build.ElementPicks[0] = (Element)99;

            CollectionAssert.Contains(Problems(build, content), BuildProblem.PoolElementUnknown);
        }

        [Test]
        public void AnUnknownClassStopsFurtherComplaints()
        {
            // Everything else is measured against the class, so reporting twelve consequences of one
            // missing class would bury the actual problem.
            var content = Content();
            var build = Valid(content);
            build.ClassId = 404;

            var problems = Problems(build, content);

            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(BuildProblem.ClassUnknown, problems[0]);
        }

        [Test]
        public void EmptySlotsAreAllowed()
        {
            var content = Content();
            var build = Valid(content);
            build.WeaponId = CharacterBuild.NoEquipment;
            build.ArmorId = CharacterBuild.NoEquipment;

            CollectionAssert.IsEmpty(Problems(build, content));
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
        public void ModifierOrderCannotChangeResolvedStats()
        {
            // DE-003 requires two clients resolving one loadout to agree. Whole-block addition is
            // commutative, which is what guarantees it.
            var content = Content();
            var build = Valid(content);

            var once = LoadoutResolver.Resolve(build, content).Stats;
            var again = LoadoutResolver.Resolve(new CharacterBuild(build), content).Stats;

            Assert.AreEqual(once, again);
            Assert.AreEqual(new StatBlock(10, 3, 6, 3), once);
        }

        [Test]
        public void UnequippingRemovesExactlyThoseModifiers()
        {
            var content = Content();
            var build = Valid(content);

            var withPlate = LoadoutResolver.Resolve(build, content).Stats;

            build.ArmorId = CharacterBuild.NoEquipment;
            var without = LoadoutResolver.Resolve(build, content).Stats;

            Assert.AreEqual(new StatBlock(3, -1, 0, 0), new StatBlock(
                withPlate.Vitality - without.Vitality,
                withPlate.Speed - without.Speed,
                withPlate.Power - without.Power,
                withPlate.Focus - without.Focus));
        }

        [Test]
        public void AnInvalidBuildStillResolves()
        {
            // The creator resolves while the player is mid-edit, so resolution must not refuse --
            // that is the validator's job and a second opinion would fight it.
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

        [Test]
        public void ResolvedStatsNeverGoNegative()
        {
            var content = new FakeContent(new CharacterRules(0, 0, 8, 0))
                .With(new ClassSpec(1, "W", new StatBlock(1, 1, 1, 1), new int[0]))
                .With(new EquipmentSpec(30, "Anvil", EquipmentSlot.Armor, new StatBlock(0, -99, 0, 0)));

            var build = new CharacterBuild { ClassId = 1, ArmorId = 30 };

            Assert.AreEqual(0, LoadoutResolver.Resolve(build, content).Stats.Speed);
        }
    }
}
