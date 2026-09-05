using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;

namespace Dragoneye.Data
{
    /// <summary>
    /// Every authored class and item, and the constraints a build is checked against.
    ///
    /// The one implementation of <see cref="IContentIndex"/> that reads assets. Combat asks "what is
    /// class 3" and this answers; Combat never learns a ScriptableObject was involved, which is what
    /// lets the same validator run on the host, in the creation screen and in a test.
    ///
    /// Specs are built once and cached. A `ToSpec` per lookup would allocate on every keystroke in
    /// the creator, and would hand out a different object each time for what is a fixed answer.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Content Catalog", fileName = "ContentCatalog")]
    public sealed class ContentCatalog : ScriptableObject, IContentIndex
    {
        [SerializeField]
        List<SpeciesDefinition> m_Species = new List<SpeciesDefinition>();

        [SerializeField]
        List<ClassAsset> m_Classes = new List<ClassAsset>();

        [SerializeField]
        List<EquipmentAsset> m_Equipment = new List<EquipmentAsset>();

        [SerializeField]
        List<SkillAsset> m_Skills = new List<SkillAsset>();

        [SerializeField, Tooltip("Every portrait the game ships with. Rebuilt by the setup step "
             + "from Assets/Art/Portraits.")]
        PortraitLibrary m_Portraits;

        [SerializeField, Tooltip("The rune drawn for each element. Rebuilt by the setup step from "
             + "Assets/Art/Elements.")]
        ElementIconLibrary m_ElementIcons;

        [Header("Build rules")]
        [SerializeField, Min(0), Tooltip("Points to spend raising attributes. Each step costs the "
             + "attribute's current value, so the first is cheap and the last is dear. Seven of "
             + "these buy the first point of each attribute, which used to be free.")]
        int m_PointBudget = 27;

        [SerializeField, Min(1), Tooltip("The highest any one attribute may be bought to.")]
        int m_MaxPerAttribute = 8;

        [SerializeField, Min(1), Tooltip("The level a newly created character starts at. Only the "
             + "starting point -- a character's level lives on the character after that.")]
        int m_StartingLevel = 1;

        readonly List<SpeciesSpec> m_SpeciesSpecs = new List<SpeciesSpec>();
        readonly Dictionary<int, SpeciesSpec> m_SpeciesById = new Dictionary<int, SpeciesSpec>();
        readonly List<ClassSpec> m_ClassSpecs = new List<ClassSpec>();
        readonly List<EquipmentSpec> m_EquipmentSpecs = new List<EquipmentSpec>();
        readonly Dictionary<int, ClassSpec> m_ClassById = new Dictionary<int, ClassSpec>();
        readonly Dictionary<int, EquipmentSpec> m_EquipmentById = new Dictionary<int, EquipmentSpec>();
        readonly List<SkillSpec> m_SkillSpecs = new List<SkillSpec>();
        readonly Dictionary<int, SkillSpec> m_SkillById = new Dictionary<int, SkillSpec>();

        CharacterRules m_Rules;
        bool m_Built;

        public CharacterRules Rules
        {
            get
            {
                Build();
                return m_Rules;
            }
        }

        public IReadOnlyList<SpeciesSpec> Species
        {
            get
            {
                Build();
                return m_SpeciesSpecs;
            }
        }

        public IReadOnlyList<ClassSpec> Classes
        {
            get
            {
                Build();
                return m_ClassSpecs;
            }
        }

        public IReadOnlyList<EquipmentSpec> Equipment
        {
            get
            {
                Build();
                return m_EquipmentSpecs;
            }
        }

        public IReadOnlyList<SkillSpec> Skills
        {
            get
            {
                Build();
                return m_SkillSpecs;
            }
        }

        public bool TryGetSkill(int id, out SkillSpec spec)
        {
            Build();
            return m_SkillById.TryGetValue(id, out spec);
        }

        public bool TryGetSpecies(int id, out SpeciesSpec spec)
        {
            Build();
            return m_SpeciesById.TryGetValue(id, out spec);
        }

        public bool TryGetClass(int id, out ClassSpec spec)
        {
            Build();
            return m_ClassById.TryGetValue(id, out spec);
        }

        public bool TryGetEquipment(int id, out EquipmentSpec spec)
        {
            Build();

            if (id == CharacterBuild.NoEquipment)
            {
                // Not a missing item -- an empty slot. Callers distinguish the two, so this must not
                // be reported as a lookup failure for a real id.
                spec = null;
                return false;
            }

            return m_EquipmentById.TryGetValue(id, out spec);
        }

        /// <summary>Everything that fits a slot, for the creation screen to offer.</summary>
        public List<EquipmentSpec> InSlot(EquipmentSlot slot)
        {
            Build();

            var result = new List<EquipmentSpec>();

            foreach (var spec in m_EquipmentSpecs)
            {
                if (spec.Slot == slot)
                {
                    result.Add(spec);
                }
            }

            return result;
        }

        /// <summary>Rebuilds the caches. Called by the editor when the assets change.</summary>
        public void Invalidate() => m_Built = false;

        void OnDisable() => m_Built = false;

        void OnValidate()
        {
            m_Built = false;
        }

        void Build()
        {
            if (m_Built)
            {
                return;
            }

            // Set first: the loop below reports duplicates through Debug, and a logger that re-enters
            // this property would recurse.
            m_Built = true;

            m_SpeciesSpecs.Clear();
            m_SpeciesById.Clear();
            m_ClassSpecs.Clear();
            m_EquipmentSpecs.Clear();
            m_ClassById.Clear();
            m_EquipmentById.Clear();
            m_SkillSpecs.Clear();
            m_SkillById.Clear();

            m_Rules = new CharacterRules(m_PointBudget, m_MaxPerAttribute, m_StartingLevel);

            // Filled here because building content is the first thing any screen or match does, so
            // by the time anything wants to draw a face this is already answered.
            Portraits.Current = m_Portraits;
            ElementIcons.Current = m_ElementIcons;

            foreach (var asset in m_Skills)
            {
                if (asset == null)
                {
                    continue;
                }

                var spec = asset.ToSpec();

                if (m_SkillById.ContainsKey(spec.Id))
                {
                    Debug.LogError($"Duplicate skill id {spec.Id} on '{asset.name}'.", asset);
                    continue;
                }

                m_SkillById.Add(spec.Id, spec);
                m_SkillSpecs.Add(spec);
            }

            foreach (var asset in m_Species)
            {
                if (asset == null)
                {
                    continue;
                }

                var spec = asset.ToSpec();

                if (m_SpeciesById.ContainsKey(spec.Id))
                {
                    Debug.LogError($"Duplicate species id {spec.Id} on '{asset.name}'.", asset);
                    continue;
                }

                m_SpeciesById.Add(spec.Id, spec);
                m_SpeciesSpecs.Add(spec);
            }

            foreach (var asset in m_Classes)
            {
                if (asset == null)
                {
                    continue;
                }

                var spec = asset.ToSpec();

                if (m_ClassById.ContainsKey(spec.Id))
                {
                    // Two classes with one id means saved characters resolve to whichever was listed
                    // first, which looks like corruption rather than an authoring mistake.
                    Debug.LogError($"Duplicate class id {spec.Id} on '{asset.name}'.", asset);
                    continue;
                }

                m_ClassById.Add(spec.Id, spec);
                m_ClassSpecs.Add(spec);
            }

            foreach (var asset in m_Equipment)
            {
                if (asset == null)
                {
                    continue;
                }

                var spec = asset.ToSpec();

                if (spec.Id == CharacterBuild.NoEquipment)
                {
                    Debug.LogError($"Equipment '{asset.name}' uses the reserved id 0.", asset);
                    continue;
                }

                if (m_EquipmentById.ContainsKey(spec.Id))
                {
                    Debug.LogError($"Duplicate equipment id {spec.Id} on '{asset.name}'.", asset);
                    continue;
                }

                m_EquipmentById.Add(spec.Id, spec);
                m_EquipmentSpecs.Add(spec);
            }
        }
    }
}
