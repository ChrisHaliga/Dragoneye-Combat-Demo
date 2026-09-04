using System.Collections.Generic;
using System.Linq;
using Dragoneye.Combat;
using NUnit.Framework;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// DE-003. A passive is data an item grants and a rule reads. The clash asks the loadout a
    /// question it can already answer, so a shield never needs special-casing inside the resolution.
    /// </summary>
    public class PassiveTests
    {
        const int ShieldId = 30;
        const int PlateId = 20;

        [Test]
        public void AnEquippedItemGrantsItsPassive()
        {
            var loadout = LoadoutResolver.Resolve(
                new CharacterBuild { ClassId = 1, OffhandId = ShieldId }, Content());

            Assert.IsTrue(loadout.Passives.Has(Passive.DefendAdvantage));
        }

        [Test]
        public void UnequippingRemovesThePassive()
        {
            var loadout = LoadoutResolver.Resolve(new CharacterBuild { ClassId = 1 }, Content());

            Assert.IsFalse(loadout.Passives.Has(Passive.DefendAdvantage));
        }

        [Test]
        public void ItemsWithoutPassivesGrantNone()
        {
            var loadout = LoadoutResolver.Resolve(
                new CharacterBuild { ClassId = 1, ArmorId = PlateId }, Content());

            Assert.AreEqual(0, loadout.Passives.Count);
        }

        [Test]
        public void HoldingTwoOfTheSamePassiveIsStillOnePassive()
        {
            // A set, not a list: two shields do not defend twice, and every rule that reads a
            // passive asks whether it is present rather than how many.
            var set = new PassiveSet(new[]
            {
                Passive.DefendAdvantage, Passive.DefendAdvantage
            });

            Assert.AreEqual(1, set.Count);
            Assert.IsTrue(set.Has(Passive.DefendAdvantage));
        }

        [Test]
        public void AnEmptySetAnswersNoToEverything()
        {
            Assert.IsFalse(PassiveSet.Empty.Has(Passive.DefendAdvantage));
            Assert.IsFalse(new PassiveSet(null).Has(Passive.DefendAdvantage));
        }

        [Test]
        public void AShieldOccupiesTheOffhandSoItDoesNotCostArmour()
        {
            var content = Content();
            var build = new CharacterBuild { ClassId = 1, ArmorId = PlateId, OffhandId = ShieldId };
            var loadout = LoadoutResolver.Resolve(build, content);

            Assert.AreEqual(2, loadout.Items.Count, "both are worn at once");
            Assert.IsTrue(loadout.Passives.Has(Passive.DefendAdvantage));
        }

        [Test]
        public void AnItemInTheWrongSlotIsRefused()
        {
            var content = Content();
            var faults = new List<BuildFault>();

            BuildValidator.Validate(
                new CharacterBuild { ClassId = 1, Name = "x", OffhandId = PlateId }, content, faults);

            Assert.IsTrue(faults.Any(f => f.Problem == BuildProblem.ItemInWrongSlot));
        }

        static PassiveContent Content() =>
            new PassiveContent()
                .With(new ClassSpec(1, "Warrior", StatBlock.Zero, new int[0]))
                .With(new EquipmentSpec(PlateId, "Plate", EquipmentSlot.Armor, StatBlock.Zero))
                .With(new EquipmentSpec(ShieldId, "Shield", EquipmentSlot.Offhand, StatBlock.Zero,
                    null, new[] { Passive.DefendAdvantage }));

        sealed class PassiveContent : IContentIndex
        {
            readonly List<ClassSpec> m_Classes = new List<ClassSpec>();
            readonly List<EquipmentSpec> m_Equipment = new List<EquipmentSpec>();

            public CharacterRules Rules { get; } = new CharacterRules(0, 0, 8, 0);
            public IReadOnlyList<ClassSpec> Classes => m_Classes;
            public IReadOnlyList<EquipmentSpec> Equipment => m_Equipment;
            public IReadOnlyList<SkillSpec> Skills => System.Array.Empty<SkillSpec>();

            public PassiveContent With(ClassSpec s) { m_Classes.Add(s); return this; }
            public PassiveContent With(EquipmentSpec s) { m_Equipment.Add(s); return this; }

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
    }
}
