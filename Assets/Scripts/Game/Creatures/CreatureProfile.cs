using System.Collections.Generic;
using Dragoneye.Combat;

namespace Dragoneye.Game
{
    /// <summary>
    /// What a creature is, whichever source answered.
    ///
    /// A premade is authored on a <see cref="CreatureDefinition"/>; a player character is resolved
    /// from the build its owner submitted. They are different sources for the same five questions,
    /// and every reader -- the turn bar, the card, the spawner, the initiative order -- wants the
    /// answers rather than the provenance.
    ///
    /// A value rather than an interface: it is read far more often than it is built, and an
    /// interface would mean an allocation and a virtual call for what is five fields.
    /// </summary>
    public readonly struct CreatureProfile
    {
        public static readonly CreatureProfile Unknown = new CreatureProfile(
            "Unknown", 1, Ap.Zero, 0, null, ElementCounts.Empty, Progression.FirstLevel);

        public readonly string Name;
        public readonly int MaxHealth;
        public readonly Ap MaxAp;
        public readonly int Initiative;
        public readonly IReadOnlyList<int> SkillIds;
        public readonly ElementCounts StartingPool;

        /// <summary>What killing this is worth, and what it was strong enough to be.</summary>
        public readonly int Level;

        public CreatureProfile(string name, int maxHealth, Ap maxAp, int initiative,
            IReadOnlyList<int> skillIds, ElementCounts startingPool,
            int level = Progression.FirstLevel)
        {
            Name = string.IsNullOrEmpty(name) ? "Unknown" : name;
            MaxHealth = maxHealth < 1 ? 1 : maxHealth;
            MaxAp = maxAp;
            Initiative = initiative;
            SkillIds = skillIds ?? System.Array.Empty<int>();
            StartingPool = startingPool;
            Level = level < Progression.FirstLevel ? Progression.FirstLevel : level;
        }

        /// <summary>
        /// An authored premade, fielded at a level.
        ///
        /// The definition says what the creature is; the level says how much of it the host wanted
        /// today. Health, the skills it has reached and the pool it has bought all follow from that,
        /// which is what makes raising a goblin's level mean something rather than only relabelling
        /// it.
        /// </summary>
        public static CreatureProfile FromDefinition(CreatureDefinition definition,
            int level = Progression.FirstLevel) =>
            definition == null
                ? Unknown
                : new CreatureProfile(definition.DisplayName, definition.MaxHpAt(level),
                    Ap.FromWhole(definition.MaxAp), definition.Speed,
                    definition.SkillIdsAt(level), definition.PoolFor(level), level);

        /// <summary>
        /// A character somebody built.
        ///
        /// Every number comes from the resolved loadout, so what reaches the board is exactly what
        /// the creation screen showed -- the same resolver produced both.
        /// </summary>
        public static CreatureProfile FromLoadout(string name, Loadout loadout)
        {
            if (loadout == null)
            {
                return Unknown;
            }

            var skillIds = new List<int>();

            foreach (var skill in loadout.Skills)
            {
                skillIds.Add(skill.Id);
            }

            return new CreatureProfile(name, loadout.Vitals.MaxHealth, loadout.Vitals.MaxAp,
                loadout.Vitals.Speed, skillIds, loadout.StartingPool, loadout.Vitals.Level);
        }
    }
}
