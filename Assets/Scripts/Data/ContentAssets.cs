using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>
    /// The seven numbers a designer edits, in a shape Unity can serialise.
    ///
    /// <see cref="AttributeBlock"/> is immutable with readonly fields, which is right for a value
    /// the rules pass around and wrong for something an inspector writes into. This is the
    /// authoring form; it converts once, at load.
    /// </summary>
    [System.Serializable]
    public struct AttributeValues
    {
        public int Toughness;
        public int Dexterity;
        public int Strength;
        public int Skill;
        public int Vitality;
        public int Willpower;
        public int Endurance;

        public AttributeBlock ToBlock() =>
            new AttributeBlock(Toughness, Dexterity, Strength, Skill, Vitality, Willpower, Endurance);
    }

    /// <summary>An authored class. Baseline stats and the weapons it may carry.</summary>
    /// <summary>
    /// Turns a list of assets into the list of ids the rules take.
    ///
    /// Nulls are dropped rather than passed through as zero, which is the reserved "nothing" id and
    /// would silently read as an empty slot.
    /// </summary>
    public static class ContentIds
    {
        public static List<int> SkillIds(List<SkillAsset> assets)
        {
            var ids = new List<int>();

            foreach (var asset in assets)
            {
                if (asset != null)
                {
                    ids.Add(asset.Id);
                }
            }

            return ids;
        }
    }

    [CreateAssetMenu(menuName = "Dragoneye/Class", fileName = "Class")]
    public sealed class ClassAsset : ScriptableObject
    {
        [SerializeField, Tooltip("Stable and permanent. Saved characters and network traffic both "
             + "carry this, so changing it reinterprets every existing character.")]
        int m_Id = 1;

        [SerializeField]
        string m_DisplayName = "Class";

        [SerializeField, TextArea(2, 4)]
        string m_Description = "";

        [SerializeField, Tooltip("Stats before allocation or equipment.")]
        AttributeValues m_Baseline;

        [SerializeField, Tooltip("Weapons this class may carry. Anything else fails validation.")]
        List<EquipmentAsset> m_Weapons = new List<EquipmentAsset>();

        [SerializeField, Tooltip("The core skill set. Everything else has to come from equipment.")]
        List<SkillAsset> m_Skills = new List<SkillAsset>();

        public int Id => m_Id;

        public string DisplayName => m_DisplayName;

        public ClassSpec ToSpec()
        {
            var weaponIds = new List<int>();

            foreach (var weapon in m_Weapons)
            {
                if (weapon != null)
                {
                    weaponIds.Add(weapon.Id);
                }
            }

            return new ClassSpec(m_Id, m_DisplayName, m_Baseline.ToBlock(), weaponIds,
                ContentIds.SkillIds(m_Skills), m_Description);
        }

        void OnValidate()
        {
            // Zero is reserved for "nothing equipped" and negatives have no meaning; either would
            // resolve to the wrong asset rather than to an error.
            if (m_Id < 1)
            {
                m_Id = 1;
            }
        }
    }

    /// <summary>
    /// An authored item.
    ///
    /// Described by what it grants, not by a kind. "Heavy armour" is not a category the rules know
    /// about -- it is an item in the armour slot whose modifiers trade Speed for Vitality.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Equipment", fileName = "Equipment")]
    public sealed class EquipmentAsset : ScriptableObject
    {
        [SerializeField, Tooltip("Stable and permanent. Must not be zero -- that means unequipped.")]
        int m_Id = 1;

        [SerializeField]
        string m_DisplayName = "Item";

        [SerializeField, TextArea(2, 4)]
        string m_Description = "";

        [SerializeField]
        EquipmentSlot m_Slot = EquipmentSlot.Weapon;

        [SerializeField, Tooltip("Added to resolved stats. May be negative.")]
        AttributeValues m_Modifiers;

        [SerializeField, Tooltip("Skills this item grants while equipped. Unequipping removes them.")]
        List<SkillAsset> m_Skills = new List<SkillAsset>();

        [SerializeField, Tooltip("How much this slows its wearer, and how much damage it stops. "
             + "Only armour should be anything but None.")]
        ArmourClass m_Armour = ArmourClass.None;

        [SerializeField, Min(0), Tooltip("Damage this stops on top of its armour class. For things "
             + "that protect without being armour -- a shield. Leave armour itself at zero; its "
             + "reduction comes from its class.")]
        int m_DamageReduction;

        public int Id => m_Id;

        public string DisplayName => m_DisplayName;

        public EquipmentSlot Slot => m_Slot;

        public EquipmentSpec ToSpec() =>
            new EquipmentSpec(m_Id, m_DisplayName, m_Slot, m_Modifiers.ToBlock(),
                ContentIds.SkillIds(m_Skills), m_Armour, m_Description, m_DamageReduction);

        void OnValidate()
        {
            if (m_Id < 1)
            {
                m_Id = 1;
            }
        }
    }
}
