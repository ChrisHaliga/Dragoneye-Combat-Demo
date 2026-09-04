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
        List<ClassAsset> m_Classes = new List<ClassAsset>();

        [SerializeField]
        List<EquipmentAsset> m_Equipment = new List<EquipmentAsset>();

        [Header("Build rules")]
        [SerializeField, Min(0), Tooltip("Points to spend across all stats, beyond the minimum.")]
        int m_PointBudget = 8;

        [SerializeField, Min(0)]
        int m_MinPerStat = 1;

        [SerializeField, Min(1)]
        int m_MaxPerStat = 8;

        [SerializeField, Min(1), Tooltip("Element resources in a starting pool -- one per level.")]
        int m_Level = 4;

        readonly List<ClassSpec> m_ClassSpecs = new List<ClassSpec>();
        readonly List<EquipmentSpec> m_EquipmentSpecs = new List<EquipmentSpec>();
        readonly Dictionary<int, ClassSpec> m_ClassById = new Dictionary<int, ClassSpec>();
        readonly Dictionary<int, EquipmentSpec> m_EquipmentById = new Dictionary<int, EquipmentSpec>();

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
            if (m_MaxPerStat < m_MinPerStat)
            {
                m_MaxPerStat = m_MinPerStat;
            }

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

            m_ClassSpecs.Clear();
            m_EquipmentSpecs.Clear();
            m_ClassById.Clear();
            m_EquipmentById.Clear();

            m_Rules = new CharacterRules(m_PointBudget, m_MinPerStat, m_MaxPerStat, m_Level);

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
