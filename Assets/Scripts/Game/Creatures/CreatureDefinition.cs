using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// An authored creature: who it is and what it starts with.
    ///
    /// Holds no party and no owner. Both are decided in the draft, so putting them here would bake a
    /// single match's arrangement into a reusable asset.
    ///
    /// Stats sit directly on the definition rather than being composed from species and class. There
    /// is nothing to compose yet, and a resolver built before any modifier exists would be a guess at
    /// rules that have not been designed.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Creature", fileName = "Creature")]
    public sealed class CreatureDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("Stable identifier. Hashed into the network id -- changing it "
             + "changes that id, so treat it as permanent once a creature ships.")]
        string m_Id = "";

        [SerializeField]
        string m_DisplayName = "Creature";

        [SerializeField, Tooltip("Optional. The HUD draws a lettered tile when this is empty.")]
        Sprite m_Portrait;

        [SerializeField]
        SpeciesDefinition m_Species;

        [SerializeField]
        ClassDefinition m_Class;

        [SerializeField, Min(1), Tooltip("The level this creature is authored at. The host may "
             + "raise it when setting up a match; everything below scales from here.")]
        int m_Level = 1;

        [SerializeField, Min(1)]
        int m_MaxHp = 20;

        [SerializeField, Min(0)]
        int m_MaxAp = 6;

        [SerializeField, Min(0)]
        int m_Speed = 5;

        [SerializeField, Tooltip("Whether this creature answers a clash with the better of two "
             + "elements -- a shield, or whatever stands in for one. It costs two elements rather "
             + "than one, so it drains as fast as it protects.")]
        bool m_Shielded;

        [SerializeField, Tooltip("Elements this creature starts holding. Any spread; the total is "
             + "this creature's level.")]
        ElementValues m_StartingPool;

        [SerializeField, Tooltip("What this creature spends its element budget on as it levels, "
             + "in order. A premade has nobody to ask, so its level-up choices are authored here.")]
        List<Element> m_LevelUpPicks = new List<Element>();

        [SerializeField, Tooltip("What this creature can do. Authored directly, because a premade "
             + "has no class and equipment to derive a kit from.")]
        List<SkillAsset> m_Skills = new List<SkillAsset>();

        public string Id => m_Id;

        public string DisplayName => m_DisplayName;

        public Sprite Portrait => m_Portrait;

        public SpeciesDefinition Species => m_Species;

        public ClassDefinition Class => m_Class;

        /// <summary>The level this creature was authored at.</summary>
        public int Level => m_Level;

        public int MaxHp => m_MaxHp;

        /// <summary>
        /// Health at a level the host chose rather than the one this was authored at.
        ///
        /// One per level, which is the same thing the built-character formula gives: HP counts the
        /// level directly. A premade authored at level three and fielded at five is two tougher,
        /// not rebuilt from attributes it does not have.
        /// </summary>
        public int MaxHpAt(int level) =>
            Mathf.Max(1, m_MaxHp + (Mathf.Max(Progression.FirstLevel, level) - m_Level));

        /// <summary>
        /// The pool a creature of this level holds: what it starts with, then its authored picks in
        /// order, for as long as the budget stretches.
        ///
        /// Taken in order and stopped at the first one that will not fit rather than skipped over,
        /// so a designer reads the list as a plan and not as a set. An expensive pick that has to
        /// wait a level is the same decision a player makes in the creator.
        /// </summary>
        public ElementCounts PoolFor(int level)
        {
            var pool = m_StartingPool.ToCounts();
            var budget = Progression.PoolBudget(level);

            foreach (var pick in m_LevelUpPicks)
            {
                if (!ElementPricing.CanAdd(pool, pick, budget))
                {
                    break;
                }

                pool = pool.With(pick, pool[pick] + 1);
            }

            return pool;
        }

        /// <summary>Whether this creature answers a clash with the better of two elements.</summary>
        public bool Shielded => m_Shielded;

        public int MaxAp => m_MaxAp;

        public int Speed => m_Speed;

        /// <summary>
        /// The pool a premade creature starts with.
        ///
        /// Authored, because a premade has no player behind it to pick one. A built character gets
        /// its pool from the choices made in the creator instead -- same shape, different source.
        /// </summary>
        public ElementCounts StartingPool => m_StartingPool.ToCounts();

        /// <summary>
        /// The skills a premade creature can use: what its species grants, then what is authored
        /// here.
        ///
        /// Species first for the same reason a built character resolves it first -- being a Goblinoid
        /// is true before anything this particular goblin trained at. The rest is authored directly,
        /// because a premade has no class and equipment to derive a kit from.
        /// </summary>
        public IReadOnlyList<int> SkillIds => SkillIdsAt(m_Level);

        /// <summary>
        /// The skills a creature of this level has.
        ///
        /// Filtered by level for the same reason a built character's list is: a skill the creature
        /// has not reached is left out entirely rather than offered and refused.
        /// </summary>
        public IReadOnlyList<int> SkillIdsAt(int level)
        {
            var ids = new List<int>();

            if (m_Species != null)
            {
                ids.AddRange(m_Species.SkillIds);
            }

            foreach (var skill in m_Skills)
            {
                if (skill != null && skill.LevelRequired <= level && !ids.Contains(skill.Id))
                {
                    ids.Add(skill.Id);
                }
            }

            return ids;
        }

        public string SpeciesName => m_Species != null ? m_Species.DisplayName : "Unknown";

        public string ClassName => m_Class != null ? m_Class.DisplayName : "Unknown";

        public string Description => m_Species != null ? m_Species.Description : string.Empty;

        void OnValidate()
        {
            // An empty id would hash to the same value for every creature that forgot to set one,
            // which surfaces as the wrong creature appearing rather than as an error.
            if (string.IsNullOrWhiteSpace(m_Id))
            {
                m_Id = name;
            }
        }
    }
}
