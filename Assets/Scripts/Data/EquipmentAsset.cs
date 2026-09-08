using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;
using UnityEngine.Serialization;

namespace Dragoneye.Data
{
    // One ScriptableObject per file, named for it. Unity only makes a MonoScript for a type
    // whose file it can find by name, and an asset of a type with no MonoScript is written
    // with no script reference at all. The editor covers for that; a build does not.
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

        [SerializeField, Tooltip("Skills this item grants while equipped. Unequipping removes them.")]
        List<SkillAsset> m_Skills = new List<SkillAsset>();

        [SerializeField, Tooltip("How much this slows its wearer, and how much damage it stops. "
             + "Only armour should be anything but None.")]
        ArmourClass m_Armour = ArmourClass.None;

        // Renamed from the flat reduction it used to be; the old name is kept on the wire so
        // the shield already on disk keeps its number.
        [SerializeField, FormerlySerializedAs("m_DamageReduction"), Min(0),
         Tooltip("Armour this gives on top of its class: a pool above health, worn down by blows "
             + "and never restored. For things that guard without being armour -- a shield. Leave "
             + "armour itself at zero; its pool comes from its class.")]
        int m_ArmourPoints;

        [SerializeField, Tooltip("Whether holding this answers a clash with the better of two "
             + "elements. It costs two rather than one, so it drains as fast as it protects.")]
        bool m_GrantsAdvantage;

        [SerializeField, Tooltip("Whether this takes both hands. A weapon that does leaves no hand "
             + "for an offhand, and the creator hides the slot while it is carried.")]
        bool m_TwoHanded;

        [SerializeField, Tooltip("What carrying this does to attributes. Usually nothing: an item "
             + "that moves a number is one more thing to weigh against every other item.")]
        AttributeValues m_Modifiers;

        public int Id => m_Id;

        public string DisplayName => m_DisplayName;

        public EquipmentSlot Slot => m_Slot;

        public bool GrantsAdvantage => m_GrantsAdvantage;

        public EquipmentSpec ToSpec() =>
            new EquipmentSpec(m_Id, m_DisplayName, m_Slot,
                ContentIds.SkillIds(m_Skills), m_Armour, m_Description, m_ArmourPoints,
                m_GrantsAdvantage, m_TwoHanded, m_Modifiers.ToBlock());

        void OnValidate()
        {
            if (m_Id < 1)
            {
                m_Id = 1;
            }
        }
    }
}
