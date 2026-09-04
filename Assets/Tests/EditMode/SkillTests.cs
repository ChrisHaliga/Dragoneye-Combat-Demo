using System.Collections.Generic;
using System.Linq;
using Dragoneye.Combat;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    // System.Attribute would otherwise win the name; the alias must be inside the namespace.
    using Attribute = Dragoneye.Combat.Attribute;

    /// <summary>
    /// DE-002. Six authored fields, both costs enforced, a reason for anything unavailable, and
    /// equipment as the only source of skills.
    /// </summary>
    public class SkillTests
    {
        const int StrikeId = 100;
        const int RecoverId = 110;

        static SkillSpec Strike() => new SkillSpec(StrikeId, "Strike", Element.Pyro,
            Ap.FromWhole(1), 1, 1, SkillTarget.Creature, new SkillEffect(SkillEffectKind.Damage, 6));

        static SkillSpec Recover() => new SkillSpec(RecoverId, "Recover", Element.Hydro,
            Ap.FromWhole(1), 1, 0, SkillTarget.Self, new SkillEffect(SkillEffectKind.Heal, 5));

        static ElementLedger Full() => ElementLedger.Starting(new ElementCounts(3, 3, 3, 3, 0, 0, 0));

        static SkillTargetInfo Enemy(int distance = 1) =>
            SkillTargetInfo.Creature(distance, isSelf: false, isAlly: false);

        [Test]
        public void AllSixFieldsAreAuthored()
        {
            var skill = Strike();

            Assert.AreEqual(Element.Pyro, skill.Element);
            Assert.AreEqual(Ap.FromWhole(1), skill.ApCost);
            Assert.AreEqual(1, skill.ElementCost);
            Assert.AreEqual(1, skill.Range);
            Assert.AreEqual(SkillTarget.Creature, skill.Target);
            Assert.AreEqual(SkillEffectKind.Damage, skill.Effect.Kind);
        }

        [Test]
        public void OnlyCreatureTargetedSkillsAreContested()
        {
            Assert.IsTrue(Strike().IsContested);
            Assert.IsFalse(Recover().IsContested);
        }

        [Test]
        public void EitherCostAloneIsEnoughToRefuse()
        {
            var skill = Strike();

            Assert.AreEqual(SkillRefusal.NotEnoughAp,
                SkillRules.CheckAffordable(skill, true, Ap.Zero, Full()));

            Assert.AreEqual(SkillRefusal.NotEnoughElement,
                SkillRules.CheckAffordable(skill, true, Ap.FromWhole(2),
                    ElementLedger.Starting(ElementCounts.Empty)));
        }

        [Test]
        public void ApIsReportedBeforeElements()
        {
            // A creature that can afford neither is sent to fix one thing, not two.
            Assert.AreEqual(SkillRefusal.NotEnoughAp,
                SkillRules.CheckAffordable(Strike(), true, Ap.Zero,
                    ElementLedger.Starting(ElementCounts.Empty)));
        }

        [Test]
        public void ASkillCostingNothingIsAlwaysAffordable()
        {
            var free = new SkillSpec(1, "Free", Element.Pyro, Ap.Zero, 0, 1,
                SkillTarget.Self, default);

            Assert.AreEqual(SkillRefusal.None, SkillRules.CheckAffordable(free, true, Ap.Zero,
                ElementLedger.Starting(ElementCounts.Empty)));
        }

        [Test]
        public void CostIsCheckedBeforeTarget()
        {
            // Reporting "out of range" for something unaffordable sends the player to move when
            // moving would not have helped.
            Assert.AreEqual(SkillRefusal.NotEnoughAp,
                SkillRules.Check(Strike(), true, Ap.Zero, Full(), Enemy(distance: 9)));
        }

        [Test]
        public void TargetsAreCheckedAgainstKindRangeAndSide()
        {
            var skill = Strike();
            var ap = Ap.FromWhole(2);

            Assert.AreEqual(SkillRefusal.None, SkillRules.Check(skill, true, ap, Full(), Enemy()));
            Assert.AreEqual(SkillRefusal.OutOfRange,
                SkillRules.Check(skill, true, ap, Full(), Enemy(distance: 4)));
            Assert.AreEqual(SkillRefusal.TargetIsAlly,
                SkillRules.Check(skill, true, ap, Full(), SkillTargetInfo.Creature(1, false, true)));
            Assert.AreEqual(SkillRefusal.TargetIsSelf,
                SkillRules.Check(skill, true, ap, Full(), SkillTargetInfo.Creature(0, true, false)));
            Assert.AreEqual(SkillRefusal.TargetIsDead,
                SkillRules.Check(skill, true, ap, Full(),
                    SkillTargetInfo.Creature(1, false, false, isAlive: false)));
            Assert.AreEqual(SkillRefusal.NoTarget,
                SkillRules.Check(skill, true, ap, Full(), SkillTargetInfo.None));
            Assert.AreEqual(SkillRefusal.WrongTargetKind,
                SkillRules.Check(skill, true, ap, Full(), SkillTargetInfo.Tile(1)));
        }

        [Test]
        public void SelfDirectedSkillsResolveWithoutAnotherCreature()
        {
            var ap = Ap.FromWhole(2);

            Assert.AreEqual(SkillRefusal.None,
                SkillRules.Check(Recover(), true, ap, Full(), SkillTargetInfo.None));

            // And ignore whatever happens to be under the cursor.
            Assert.AreEqual(SkillRefusal.None,
                SkillRules.Check(Recover(), true, ap, Full(), Enemy(distance: 9)));
        }

        [Test]
        public void EffectsAreClampedAtBothEnds()
        {
            Assert.AreEqual(0, SkillRules.Apply(new SkillEffect(SkillEffectKind.Damage, 99), 20, 20));
            Assert.AreEqual(20, SkillRules.Apply(new SkillEffect(SkillEffectKind.Heal, 99), 10, 20));
            Assert.AreEqual(6, SkillRules.Apply(new SkillEffect(SkillEffectKind.RestoreAp, 99), 1, 6));
        }

        [Test]
        public void SkillsComeFromTheClassAndFromEquipment()
        {
            var content = Content();
            var armed = new CharacterBuild { ClassId = 1, WeaponId = 10 };

            var ids = LoadoutResolver.Resolve(armed, content).Skills.Select(s => s.Id).ToList();

            Assert.Contains(RecoverId, ids, "the class set");
            Assert.Contains(StrikeId, ids, "the weapon set");
            Assert.AreEqual(RecoverId, ids[0], "class skills first, so the order is stable");
        }

        [Test]
        public void RemovingAWeaponRemovesItsSkills()
        {
            var content = Content();
            var bare = new CharacterBuild { ClassId = 1 };

            var ids = LoadoutResolver.Resolve(bare, content).Skills.Select(s => s.Id).ToList();

            CollectionAssert.DoesNotContain(ids, StrikeId);
            Assert.Contains(RecoverId, ids, "the class set survives");
        }

        [Test]
        public void TwoItemsGrantingTheSameSkillGrantOne()
        {
            var content = Content();
            var build = new CharacterBuild { ClassId = 1, WeaponId = 10, ArmorId = 20 };

            var skills = LoadoutResolver.Resolve(build, content).Skills;

            Assert.AreEqual(1, skills.Count(s => s.Id == StrikeId));
        }

        [Test]
        public void AnIdWithNoSkillBehindItIsDropped()
        {
            var content = new SkillContent()
                .With(new ClassSpec(1, "W", AttributeBlock.Zero, new int[0], new[] { 999 }));

            Assert.IsEmpty(LoadoutResolver.Resolve(new CharacterBuild { ClassId = 1 }, content).Skills);
        }

        static SkillContent Content() =>
            new SkillContent()
                .With(Strike()).With(Recover())
                .With(new ClassSpec(1, "Warrior", AttributeBlock.Zero, new[] { 10 }, new[] { RecoverId }))
                .With(new EquipmentSpec(10, "Sword", EquipmentSlot.Weapon, AttributeBlock.Zero,
                    new[] { StrikeId }))
                .With(new EquipmentSpec(20, "Charm", EquipmentSlot.Armor, AttributeBlock.Zero,
                    new[] { StrikeId }));

        sealed class SkillContent : IContentIndex
        {
            readonly List<ClassSpec> m_Classes = new List<ClassSpec>();
            readonly List<EquipmentSpec> m_Equipment = new List<EquipmentSpec>();
            readonly List<SkillSpec> m_Skills = new List<SkillSpec>();

            public CharacterRules Rules { get; } = new CharacterRules(0, 8, 1);
            public IReadOnlyList<ClassSpec> Classes => m_Classes;
            public IReadOnlyList<EquipmentSpec> Equipment => m_Equipment;
            public IReadOnlyList<SkillSpec> Skills => m_Skills;

            public SkillContent With(ClassSpec s) { m_Classes.Add(s); return this; }
            public SkillContent With(EquipmentSpec s) { m_Equipment.Add(s); return this; }
            public SkillContent With(SkillSpec s) { m_Skills.Add(s); return this; }
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
                spec = m_Skills.FirstOrDefault(s => s.Id == id);
                return spec != null;
            }
        }
    }
}
