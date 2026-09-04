using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>
    /// What a creature is, as an authored asset.
    ///
    /// Every creature has a species, premade or built, which makes it the one place to put anything
    /// true of all of them. Take a Breath lives here rather than in the rules for exactly that
    /// reason: it comes free with almost every species, but it is still content -- a designer who
    /// authors something that cannot catch its breath should be able to.
    ///
    /// Named Definition rather than Asset, unlike its neighbours in this file's folder, because the
    /// four species already authored reference this script by id. Renaming the type would orphan
    /// them and the creatures pointing at them.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Species", fileName = "Species")]
    public sealed class SpeciesDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("Stable and permanent. Saved characters and network traffic both "
             + "carry this, so changing it reinterprets every existing character.")]
        int m_Id = 1;

        [SerializeField]
        string m_DisplayName = "Species";

        [SerializeField, TextArea(2, 5)]
        string m_Description = "";

        [SerializeField, Tooltip("Attributes every member of this species has before class, "
             + "points or equipment.")]
        AttributeValues m_Baseline;

        [SerializeField, Tooltip("What being this species lets you do, whatever else you are.")]
        List<SkillAsset> m_Skills = new List<SkillAsset>();

        [SerializeField, Min(1), Tooltip("Action points a turn before Endurance is added. How much "
             + "a thing gets through in a turn is a fact about what it is.")]
        int m_BaseAp = 4;

        public int Id => m_Id;

        public string DisplayName => m_DisplayName;

        public string Description => m_Description;

        public int BaseAp => m_BaseAp;

        public SpeciesSpec ToSpec() =>
            new SpeciesSpec(m_Id, m_DisplayName, m_Baseline.ToBlock(),
                ContentIds.SkillIds(m_Skills), m_Description, m_BaseAp);

        /// <summary>
        /// The skills this species grants, as ids.
        ///
        /// Exposed separately from <see cref="ToSpec"/> for premade creatures, which assemble a
        /// skill list directly rather than going through a build.
        /// </summary>
        public IReadOnlyList<int> SkillIds => ContentIds.SkillIds(m_Skills);

        void OnValidate()
        {
            if (m_Id < 1)
            {
                m_Id = 1;
            }
        }
    }
}
