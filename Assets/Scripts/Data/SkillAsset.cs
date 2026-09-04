using Dragoneye.Combat;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>
    /// An authored skill: the six fields DE-002 asks for, and nothing else.
    ///
    /// A skill is data, not code. Adding one is a new asset -- element, costs, range, target and
    /// effect -- and nothing in the rules changes. The effect is an enum and an amount for the same
    /// reason: a skill that needed a new method would be a skill only a programmer could add.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Skill", fileName = "Skill")]
    public sealed class SkillAsset : ScriptableObject
    {
        [SerializeField, Tooltip("Stable and permanent. It crosses the network.")]
        int m_Id = 1;

        [SerializeField]
        string m_DisplayName = "Skill";

        [SerializeField, TextArea(2, 4)]
        string m_Description = "";

        [SerializeField, Tooltip("Fixed by the skill. The user does not choose it.")]
        Element m_Element = Element.Fire;

        [SerializeField, Min(0), Tooltip("Whole action points. Stored as half-units internally.")]
        int m_ApCost = 1;

        [SerializeField, Min(0), Tooltip("How much of the skill's element it consumes from the pool. "
             + "Zero means it draws on nothing.")]
        int m_ElementCost = 1;

        [SerializeField, Min(0), Tooltip("Reach in tiles. Zero means the user only.")]
        int m_Range = 1;

        [SerializeField]
        SkillTarget m_Target = SkillTarget.Creature;

        [SerializeField]
        SkillEffectKind m_Effect = SkillEffectKind.Damage;

        [SerializeField, Min(0)]
        int m_Amount = 5;

        public int Id => m_Id;

        public string DisplayName => m_DisplayName;

        public SkillSpec ToSpec() =>
            new SkillSpec(m_Id, m_DisplayName, m_Element, Ap.FromWhole(m_ApCost), m_ElementCost,
                m_Range, m_Target, new SkillEffect(m_Effect, m_Amount), m_Description);

        void OnValidate()
        {
            if (m_Id < 1)
            {
                m_Id = 1;
            }

            // A self-directed skill with reach would offer targets it then refuses, since the check
            // ignores range for them entirely.
            if (m_Target == SkillTarget.Self)
            {
                m_Range = 0;
            }
        }
    }
}
