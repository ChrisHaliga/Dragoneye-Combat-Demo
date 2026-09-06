using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>
    /// One authored condition, in a shape Unity can serialise.
    ///
    /// <see cref="SkillCondition"/> is a readonly struct because the rules have no business being
    /// mutable; the inspector needs fields it can write. This is the seam between the two, and it
    /// is the only place that knows both.
    /// </summary>
    [System.Serializable]
    public struct SkillConditionEntry
    {
        [Tooltip("What has to be true.")]
        public SkillConditionKind Kind;

        [Tooltip("What it is about, where it is about something. A species or class id, usually.")]
        public int Value;
    }

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

        [SerializeField, Tooltip("What this skill is made of, or the first of the elements it "
             + "may be made of.")]
        Element m_Element = Element.Pyro;

        [SerializeField, Tooltip("Leave empty for a skill made of one thing. Fill it in to let "
             + "the user choose, and the element above is the default.")]
        List<Element> m_ElementOptions = new List<Element>();

        [SerializeField, Tooltip("What has to be true of a character before this is one of their "
             + "skills. Empty means always.")]
        List<SkillConditionEntry> m_Conditions = new List<SkillConditionEntry>();

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

        [SerializeField, Min(1), Tooltip("The level a creature reaches before this is theirs. A "
             + "skill above a creature's level is left out of its list entirely, not greyed out.")]
        int m_LevelRequired = 1;

        public int Id => m_Id;

        public string DisplayName => m_DisplayName;

        /// <summary>Exposed so premade creatures can filter their authored list by level.</summary>
        public int LevelRequired => m_LevelRequired;

        public SkillSpec ToSpec() =>
            new SkillSpec(m_Id, m_DisplayName, m_Element, Ap.FromWhole(m_ApCost), m_ElementCost,
                m_Range, m_Target, new SkillEffect(m_Effect, m_Amount), m_Description,
                m_LevelRequired, Conditions(), Options());

        /// <summary>
        /// The authored conditions, as the rules see them.
        ///
        /// Converted rather than stored in the rules type, because a readonly struct is not
        /// something Unity can serialise -- and making the rules type mutable so an inspector could
        /// fill it in would be letting the editor decide the shape of the rules.
        /// </summary>
        List<SkillCondition> Conditions()
        {
            var conditions = new List<SkillCondition>(m_Conditions.Count);

            foreach (var entry in m_Conditions)
            {
                conditions.Add(new SkillCondition(entry.Kind, entry.Value));
            }

            return conditions;
        }

        /// <summary>Null where nothing was authored, which the spec reads as "just this one".</summary>
        List<Element> Options() =>
            m_ElementOptions.Count > 0 ? new List<Element>(m_ElementOptions) : null;

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
