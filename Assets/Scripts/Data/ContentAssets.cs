using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>
    /// The four numbers a designer edits, in a shape Unity can serialise.
    ///
    /// <see cref="StatBlock"/> is immutable with readonly fields, which is right for a value the
    /// rules pass around and wrong for something an inspector writes into. This is the authoring
    /// form; it converts once, at load.
    /// </summary>
    [System.Serializable]
    public struct StatValues
    {
        public int Vitality;
        public int Speed;
        public int Power;
        public int Focus;

        public StatBlock ToBlock() => new StatBlock(Vitality, Speed, Power, Focus);
    }

    /// <summary>An authored class. Baseline stats and the weapons it may carry.</summary>
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
        StatValues m_Baseline;

        [SerializeField, Tooltip("Weapons this class may carry. Anything else fails validation.")]
        List<EquipmentAsset> m_Weapons = new List<EquipmentAsset>();

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

            return new ClassSpec(m_Id, m_DisplayName, m_Baseline.ToBlock(), weaponIds, m_Description);
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
        StatValues m_Modifiers;

        public int Id => m_Id;

        public string DisplayName => m_DisplayName;

        public EquipmentSlot Slot => m_Slot;

        public EquipmentSpec ToSpec() =>
            new EquipmentSpec(m_Id, m_DisplayName, m_Slot, m_Modifiers.ToBlock(), m_Description);

        void OnValidate()
        {
            if (m_Id < 1)
            {
                m_Id = 1;
            }
        }
    }
}
