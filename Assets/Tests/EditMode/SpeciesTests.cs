using System.Collections.Generic;
using System.Linq;
using Dragoneye.Combat;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    // System.Attribute would otherwise win the name; the alias must be inside the namespace.
    using Attribute = Dragoneye.Combat.Attribute;

    /// <summary>
    /// Species as content: every creature has one, it contributes a baseline, and it is where Take a
    /// Breath comes from.
    ///
    /// Written against the effect rather than the id, because the rules know nothing about Take a
    /// Breath by name -- only that some authored skill somewhere returns elements. That is the point
    /// of it being content: a species that cannot catch its breath is something a designer may
    /// author, and these tests would still pass.
    /// </summary>
    public class SpeciesTests
    {
        const int BreathId = 90;
        const int StrikeId = 100;
        const int RecoverId = 110;
        const int SwordId = 10;

        sealed class Content : IContentIndex
        {
            readonly List<SpeciesSpec> m_Species = new List<SpeciesSpec>();
            readonly List<ClassSpec> m_Classes = new List<ClassSpec>();
            readonly List<EquipmentSpec> m_Equipment = new List<EquipmentSpec>();
            readonly List<SkillSpec> m_Skills = new List<SkillSpec>();

            public CharacterRules Rules { get; } = new CharacterRules(20, 8, 4);
            public IReadOnlyList<SpeciesSpec> Species => m_Species;
            public IReadOnlyList<ClassSpec> Classes => m_Classes;
            public IReadOnlyList<EquipmentSpec> Equipment => m_Equipment;
            public IReadOnlyList<SkillSpec> Skills => m_Skills;

            public Content With(SpeciesSpec s) { m_Species.Add(s); return this; }
            public Content With(ClassSpec s) { m_Classes.Add(s); return this; }
            public Content With(EquipmentSpec s) { m_Equipment.Add(s); return this; }
            public Content With(SkillSpec s) { m_Skills.Add(s); return this; }

            public bool TryGetSpecies(int id, out SpeciesSpec spec)
            {
                spec = m_Species.FirstOrDefault(s => s.Id == id);
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
                spec = m_Skills.FirstOrDefault(s => s.Id == id);
                return spec != null;
            }
        }

        static SkillSpec Breath() => new SkillSpec(BreathId, "Take a Breath", Element.Arcana,
            Ap.FromWhole(1), 0, 0, SkillTarget.Self,
            new SkillEffect(SkillEffectKind.ReturnElement, 1));

        static Content Authored() =>
            new Content()
                .With(Breath())
                .With(new SkillSpec(StrikeId, "Strike", Element.Pyro, Ap.FromWhole(1), 1, 1,
                    SkillTarget.Creature, new SkillEffect(SkillEffectKind.Damage, 6)))
                .With(new SkillSpec(RecoverId, "Recover", Element.Hydro, Ap.FromWhole(1), 1, 0,
                    SkillTarget.Self, new SkillEffect(SkillEffectKind.Heal, 6)))
                .With(new SpeciesSpec(1, "Human", AttributeBlock.Zero, new[] { BreathId }))
                .With(new SpeciesSpec(3, "Giantkin",
                    AttributeBlock.Zero.With(Attribute.Strength, 1).With(Attribute.Dexterity, -1),
                    new[] { BreathId }))
                .With(new SpeciesSpec(9, "Construct", AttributeBlock.Zero))
                .With(new ClassSpec(1, "Guardian", AttributeBlock.Zero, new[] { SwordId },
                    new[] { RecoverId }))
                .With(new EquipmentSpec(SwordId, "Sword", EquipmentSlot.Weapon, AttributeBlock.Zero,
                    new[] { StrikeId }));

        static CharacterBuild Human(Content content)
        {
            content.TryGetSpecies(1, out var human);
            content.TryGetClass(1, out var guardian);

            var build = CharacterBuild.StartingFrom(human, guardian);
            build.Name = "Ansel";
            build.Attributes = new AttributeBlock(3, 3, 3, 3, 4, 2, 2);
            build.StartingPool = new ElementCounts(2, 1, 1, 0, 0, 0, 0);
            return build;
        }

        static List<int> SkillIds(CharacterBuild build, IContentIndex content) =>
            LoadoutResolver.Resolve(build, content).Skills.Select(s => s.Id).ToList();

        [Test]
        public void EverySpeciesThatAuthorsItGrantsTakeABreath()
        {
            var content = Authored();
            var ids = SkillIds(Human(content), content);

            Assert.Contains(BreathId, ids);
            Assert.AreEqual(BreathId, ids[0], "species skills come first, so the order is stable");
        }

        [Test]
        public void ASpeciesWithoutItDoesNotGetItForFree()
        {
            // The whole reason this is content and not a rule.
            var content = Authored();
            var construct = Human(content);
            construct.SpeciesId = 9;

            CollectionAssert.DoesNotContain(SkillIds(construct, content), BreathId);
        }

        [Test]
        public void SpeciesClassKitAndLearningAllContribute()
        {
            var content = Authored();
            var build = Human(content);
            build.WeaponId = CharacterBuild.NoEquipment;
            build.LearnedSkillIds.Add(StrikeId);

            var ids = SkillIds(build, content);

            Assert.Contains(BreathId, ids, "species");
            Assert.Contains(RecoverId, ids, "class");
            Assert.Contains(StrikeId, ids, "learned, with no weapon granting it");
        }

        [Test]
        public void GrantedTwiceIsListedOnce()
        {
            // A species and a class both teaching Recover would otherwise put two identical buttons
            // on the skill bar.
            var content = Authored()
                .With(new SpeciesSpec(5, "Sylvan", AttributeBlock.Zero,
                    new[] { BreathId, RecoverId }));

            var build = Human(content);
            build.SpeciesId = 5;

            Assert.AreEqual(1, SkillIds(build, content).Count(id => id == RecoverId));
        }

        [Test]
        public void TheSpeciesBaselineReachesTheDerivedStats()
        {
            var content = Authored();
            var human = LoadoutResolver.Resolve(Human(content), content);

            var giant = Human(content);
            giant.SpeciesId = 3;
            var resolved = LoadoutResolver.Resolve(giant, content);

            Assert.AreEqual(human.Attributes.Strength + 1, resolved.Attributes.Strength);
            Assert.AreEqual(human.Attributes.Dexterity - 1, resolved.Attributes.Dexterity);
            Assert.AreEqual(human.Vitals.Speed - 1, resolved.Vitals.Speed);
        }

        [Test]
        public void ABaselineIsNotBought()
        {
            var content = Authored();
            var giant = Human(content);
            giant.SpeciesId = 3;

            Assert.AreEqual(20, giant.PointsSpent(), "a baseline costs the player nothing");
            CollectionAssert.IsEmpty(Faults(giant, content));
        }

        [Test]
        public void AnUnknownSpeciesStopsFurtherComplaints()
        {
            var content = Authored();
            var stranger = Human(content);
            stranger.SpeciesId = 404;

            var faults = Faults(stranger, content);

            Assert.AreEqual(1, faults.Count);
            Assert.AreEqual(BuildProblem.SpeciesUnknown, faults[0].Problem);
        }

        // ---------- taking a breath ----------

        [Test]
        public void TakingABreathIsRefusedWithNothingSpent()
        {
            // Charging a point for a no-op is the sort of thing a player only notices after it has
            // cost them the turn.
            var fresh = ElementLedger.Starting(new ElementCounts(2, 0, 0, 0, 0, 0, 0));

            Assert.AreEqual(SkillRefusal.NothingToReturn,
                SkillRules.CheckAffordable(Breath(), true, Ap.FromWhole(4), fresh));
        }

        [Test]
        public void TakingABreathBecomesAvailableOnceSomethingIsSpent()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(2, 0, 0, 0, 0, 0, 0));
            ledger.TrySpend(Element.Geo, 1, out ledger, out _);

            Assert.AreEqual(SkillRefusal.None,
                SkillRules.CheckAffordable(Breath(), true, Ap.FromWhole(4), ledger));
            Assert.AreEqual(SkillRefusal.None,
                SkillRules.Check(Breath(), true, Ap.FromWhole(4), ledger, SkillTargetInfo.None),
                "self-directed, so it needs no target");
        }

        [Test]
        public void APointIsStillAPoint()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(2, 0, 0, 0, 0, 0, 0));
            ledger.TrySpend(Element.Geo, 1, out ledger, out _);

            Assert.AreEqual(SkillRefusal.NotEnoughAp,
                SkillRules.CheckAffordable(Breath(), true, Ap.Zero, ledger));
        }

        [Test]
        public void TheElementSpentLongestAgoComesBackFirst()
        {
            var ledger = ElementLedger.Starting(new ElementCounts(1, 1, 0, 0, 0, 0, 0));
            ledger.TrySpend(Element.Hydro, 1, out ledger, out _);
            ledger.TrySpend(Element.Geo, 1, out ledger, out _);

            Assert.IsTrue(ledger.TryReturn(out ledger, out var first, out _));
            Assert.AreEqual(Element.Hydro, first);
            Assert.AreEqual(2, ledger.Revealed.Total, "the opponent still knows both were seen");
        }

        static List<BuildFault> Faults(CharacterBuild build, IContentIndex content)
        {
            var faults = new List<BuildFault>();
            BuildValidator.Validate(build, content, faults);
            return faults;
        }
    }
}
