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
            "Unknown", 1, Ap.Zero, 0, null, null);

        public readonly string Name;
        public readonly int MaxHealth;
        public readonly Ap MaxAp;
        public readonly int Initiative;
        public readonly IReadOnlyList<int> SkillIds;
        public readonly IReadOnlyList<Element> StartingPool;

        public CreatureProfile(string name, int maxHealth, Ap maxAp, int initiative,
            IReadOnlyList<int> skillIds, IReadOnlyList<Element> startingPool)
        {
            Name = string.IsNullOrEmpty(name) ? "Unknown" : name;
            MaxHealth = maxHealth < 1 ? 1 : maxHealth;
            MaxAp = maxAp;
            Initiative = initiative;
            SkillIds = skillIds ?? System.Array.Empty<int>();
            StartingPool = startingPool ?? System.Array.Empty<Element>();
        }

        /// <summary>An authored premade.</summary>
        public static CreatureProfile FromDefinition(CreatureDefinition definition) =>
            definition == null
                ? Unknown
                : new CreatureProfile(definition.DisplayName, definition.MaxHp,
                    Ap.FromWhole(definition.MaxAp), definition.Speed,
                    definition.SkillIds, definition.StartingPool);

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
                loadout.Vitals.Initiative, skillIds, loadout.StartingPool);
        }
    }
}
