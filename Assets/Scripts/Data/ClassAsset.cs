using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;
using UnityEngine.Serialization;

namespace Dragoneye.Data
{
    // One ScriptableObject per file, named for it. Unity only makes a MonoScript for a type
    // whose file it can find by name, and an asset of a type with no MonoScript is written
    // with no script reference at all. The editor covers for that; a build does not.
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
}
