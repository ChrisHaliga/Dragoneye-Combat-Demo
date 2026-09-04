using System.Collections.Generic;
using System.Linq;
using Dragoneye.Combat;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// Levels, what they cost, what they buy, and what armour does with a blow.
    ///
    /// The level-up screen and the host both run this arithmetic, so it is checked here rather than
    /// through either of them: a client that awards itself four levels is measured against the same
    /// function that drew the screen.
    /// </summary>
    public class ProgressionTests
    {
        const int SwordId = 10;
        const int ShieldId = 30;
        const int PlateId = 20;

        // ---------- experience ----------

        [Test]
        public void ALevelCostsTwoToThePowerOfIt()
        {
            Assert.AreEqual(2, Progression.XpToLeave(1));
            Assert.AreEqual(4, Progression.XpToLeave(2));
            Assert.AreEqual(8, Progression.XpToLeave(3));
            Assert.AreEqual(16, Progression.XpToLeave(4));
        }

        [Test]
        public void AKillIsWorthTheVictimsLevel()
        {
            Assert.AreEqual(1, Progression.XpForKill(1));
            Assert.AreEqual(5, Progression.XpForKill(5));

            // Nothing is worth nothing: a creature with no level set still pays out.
            Assert.AreEqual(Progression.FirstLevel, Progression.XpForKill(0));
        }

        [Test]
        public void ExperienceShortOfTheCostChangesNothing()
        {
            var gain = Progression.Resolve(1, 1);

            Assert.IsFalse(gain.Any);
            Assert.AreEqual(1, gain.ToLevel);
            Assert.AreEqual(1, gain.RemainingXp, "and it is kept towards the next one");
        }

        [Test]
        public void ExactlyTheCostIsEnough()
        {
            var gain = Progression.Resolve(1, 2);

            Assert.IsTrue(gain.Any);
            Assert.AreEqual(2, gain.ToLevel);
            Assert.AreEqual(0, gain.RemainingXp);
        }

        [Test]
        public void ASingleHaulCanCarryMoreThanOneLevel()
        {
            // Two to leave one, four to leave two, eight to leave three: fourteen buys three levels
            // exactly, and the fifteenth point is change towards the fourth.
            var gain = Progression.Resolve(1, 15);

            Assert.AreEqual(1, gain.FromLevel);
            Assert.AreEqual(4, gain.ToLevel);
            Assert.AreEqual(3, gain.Levels, "asked once, not once per level");
            Assert.AreEqual(1, gain.RemainingXp);
        }

        [Test]
        public void LevellingStopsAtTheCeiling()
        {
            var gain = Progression.Resolve(Progression.MaxLevel, int.MaxValue);

            Assert.AreEqual(Progression.MaxLevel, gain.ToLevel);
            Assert.IsFalse(gain.Any);
        }

        [Test]
        public void EachLevelIsWorthOneElementPoint()
        {
            Assert.AreEqual(1, Progression.PoolBudget(1));
            Assert.AreEqual(6, Progression.PoolBudget(6));
        }

        // ---------- element prices ----------

        [Test]
        public void TheFourPhysicalElementsCostOne()
        {
            Assert.AreEqual(1, ElementPricing.CostOf(Element.Geo));
            Assert.AreEqual(1, ElementPricing.CostOf(Element.Hydro));
            Assert.AreEqual(1, ElementPricing.CostOf(Element.Pyro));
            Assert.AreEqual(1, ElementPricing.CostOf(Element.Aero));
        }

        [Test]
        public void LightAndDarkCostTwoAndArcanaCostsThree()
        {
            Assert.AreEqual(2, ElementPricing.CostOf(Element.Lux));
            Assert.AreEqual(2, ElementPricing.CostOf(Element.Nyx));
            Assert.AreEqual(3, ElementPricing.CostOf(Element.Arcana));
        }

        [Test]
        public void APoolCostsWhatItsContentsCost()
        {
            // Two Geo, one Nyx and one Arcana: 2 + 2 + 3.
            var pool = new ElementCounts(2, 0, 0, 0, 0, 1, 1);

            Assert.AreEqual(7, ElementPricing.CostOf(pool));
            Assert.AreEqual(4, pool.Total, "which is not the same as how many gems it holds");
        }

        [Test]
        public void TheLastPointBuysSomeElementsAndNotOthers()
        {
            var pool = new ElementCounts(2, 0, 0, 0, 0, 0, 0);

            Assert.IsTrue(ElementPricing.CanAdd(pool, Element.Pyro, 3), "one point buys a cheap one");
            Assert.IsFalse(ElementPricing.CanAdd(pool, Element.Lux, 3), "but not a dear one");
            Assert.IsFalse(ElementPricing.CanAdd(pool, Element.Arcana, 3));
        }

        // ---------- armour ----------

        [Test]
        public void ArmourStopsDamageByItsClass()
        {
            Assert.AreEqual(0, ArmourRules.ReductionFor(ArmourClass.None));
            Assert.AreEqual(1, ArmourRules.ReductionFor(ArmourClass.Light));
            Assert.AreEqual(2, ArmourRules.ReductionFor(ArmourClass.Medium));
            Assert.AreEqual(4, ArmourRules.ReductionFor(ArmourClass.Heavy));
        }

        [Test]
        public void AShieldStopsThreeMoreAndCostsNoSpeed()
        {
            var loadout = LoadoutResolver.Resolve(
                new CharacterBuild { SpeciesId = 1, ClassId = 1, OffhandId = ShieldId }, Content());

            Assert.AreEqual(3, loadout.DamageReduction);
            Assert.AreEqual(ArmourClass.None, loadout.Armour, "an offhand is not armour");
        }

        [Test]
        public void ArmourAndAShieldAddUp()
        {
            var loadout = LoadoutResolver.Resolve(
                new CharacterBuild { SpeciesId = 1, ClassId = 1, ArmorId = PlateId, OffhandId = ShieldId },
                Content());

            Assert.AreEqual(4 + 3, loadout.DamageReduction);
        }

        [Test]
        public void ReductionThatOutweighsTheBlowStopsItRatherThanHealing()
        {
            // The whole reason this is a function. Nine off a five-damage hit is a hit that does
            // nothing, not four health handed back to the defender.
            Assert.AreEqual(0, CombatRules.DamageAfter(5, 9));
            Assert.AreEqual(10, CombatRules.Damaged(10, 5, 9));
        }

        [Test]
        public void ReductionComesOffBeforeHealthDoes()
        {
            Assert.AreEqual(3, CombatRules.DamageAfter(5, 2));
            Assert.AreEqual(7, CombatRules.Damaged(10, 5, 2));
        }

        [Test]
        public void AnUnprotectedCreatureTakesTheWholeBlow()
        {
            Assert.AreEqual(5, CombatRules.DamageAfter(5, 0));
            Assert.AreEqual(5, CombatRules.Damaged(10, 5));
        }

        // ---------- skills by level ----------

        [Test]
        public void ASkillAboveTheCharactersLevelIsNotInTheirList()
        {
            var build = new CharacterBuild { SpeciesId = 1, ClassId = 1, WeaponId = SwordId, Level = 1 };
            var loadout = LoadoutResolver.Resolve(build, Content());

            Assert.IsTrue(loadout.Skills.Any(s => s.Id == 100), "the level-one one is theirs");
            Assert.IsFalse(loadout.Skills.Any(s => s.Id == 101),
                "and the level-three one is not shown at all, rather than shown and refused");
        }

        [Test]
        public void ReachingTheLevelGrantsIt()
        {
            var build = new CharacterBuild { SpeciesId = 1, ClassId = 1, WeaponId = SwordId, Level = 3 };
            var loadout = LoadoutResolver.Resolve(build, Content());

            Assert.IsTrue(loadout.Skills.Any(s => s.Id == 101));
        }

        // ---------- species action points ----------

        [Test]
        public void ASpeciesDecidesHowMuchATurnHolds()
        {
            var content = Content();

            var quick = LoadoutResolver.Resolve(
                new CharacterBuild { SpeciesId = 2, ClassId = 1 }, content);
            var ordinary = LoadoutResolver.Resolve(
                new CharacterBuild { SpeciesId = 1, ClassId = 1 }, content);

            Assert.AreEqual(Ap.FromWhole(4 + 0), ordinary.Vitals.MaxAp);
            Assert.AreEqual(Ap.FromWhole(7 + 0), quick.Vitals.MaxAp);
        }

        // ---------- fixtures ----------

        static FakeContent Content() =>
            new FakeContent()
                .With(new ClassSpec(1, "Warrior", AttributeBlock.Zero, new[] { SwordId }))
                .With(new EquipmentSpec(SwordId, "Sword", EquipmentSlot.Weapon, AttributeBlock.Zero,
                    new[] { 100, 101 }))
                .With(new EquipmentSpec(PlateId, "Plate", EquipmentSlot.Armor, AttributeBlock.Zero,
                    null, ArmourClass.Heavy))
                .With(new EquipmentSpec(ShieldId, "Shield", EquipmentSlot.Offhand, AttributeBlock.Zero,
                    null, ArmourClass.None, "", 3))
                .With(new SkillSpec(100, "Strike", Element.Pyro, Ap.FromWhole(1), 1, 1,
                    SkillTarget.Creature, new SkillEffect(SkillEffectKind.Damage, 6)))
                .With(new SkillSpec(101, "Cleave", Element.Geo, Ap.FromWhole(2), 2, 1,
                    SkillTarget.Creature, new SkillEffect(SkillEffectKind.Damage, 11), "",
                    levelRequired: 3));

        sealed class FakeContent : IContentIndex
        {
            readonly List<ClassSpec> m_Classes = new List<ClassSpec>();
            readonly List<EquipmentSpec> m_Equipment = new List<EquipmentSpec>();
            readonly List<SkillSpec> m_Skills = new List<SkillSpec>();

            readonly List<SpeciesSpec> m_Species = new List<SpeciesSpec>
            {
                new SpeciesSpec(1, "Human", AttributeBlock.Zero),
                new SpeciesSpec(2, "Swift", AttributeBlock.Zero, null, "", 7)
            };

            public CharacterRules Rules { get; } = new CharacterRules(20, 8, 1);
            public IReadOnlyList<SpeciesSpec> Species => m_Species;
            public IReadOnlyList<ClassSpec> Classes => m_Classes;
            public IReadOnlyList<EquipmentSpec> Equipment => m_Equipment;
            public IReadOnlyList<SkillSpec> Skills => m_Skills;

            public FakeContent With(ClassSpec s) { m_Classes.Add(s); return this; }
            public FakeContent With(EquipmentSpec s) { m_Equipment.Add(s); return this; }
            public FakeContent With(SkillSpec s) { m_Skills.Add(s); return this; }

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
                    ? null
                    : m_Equipment.FirstOrDefault(e => e.Id == id);
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
