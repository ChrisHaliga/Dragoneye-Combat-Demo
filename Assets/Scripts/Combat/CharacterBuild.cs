using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>
    /// A character as its player assembled it: a class, an allocation, a starting pool and a kit.
    ///
    /// Mutable, because the creation screen edits one of these directly and a value type would mean
    /// copying it back on every keystroke. Correctness comes from <see cref="BuildValidator"/>
    /// rather than from immutability -- a build is checked where it is accepted, not trusted because
    /// of how it was constructed. That matters: one of these arrives from a client.
    ///
    /// Ids rather than references throughout. A build is saved to disk and sent over the wire, and
    /// both need something stable that survives an asset being moved.
    /// </summary>
    public sealed class CharacterBuild
    {
        /// <summary>Nothing equipped. Reserved, so no item may use it.</summary>
        public const int NoEquipment = 0;

        public const int MaxNameLength = 24;

        public string Name = string.Empty;

        public int ClassId;

        /// <summary>Points the player distributed. Not the resolved stats -- see <see cref="Loadout"/>.</summary>
        public StatBlock Allocation;

        /// <summary>One element per level; together these are the starting pool.</summary>
        public readonly List<Element> ElementPicks = new List<Element>();

        public int WeaponId = NoEquipment;

        public int ArmorId = NoEquipment;

        public int OffhandId = NoEquipment;

        public CharacterBuild() { }

        public CharacterBuild(CharacterBuild other)
        {
            if (other == null)
            {
                return;
            }

            Name = other.Name;
            ClassId = other.ClassId;
            Allocation = other.Allocation;
            WeaponId = other.WeaponId;
            ArmorId = other.ArmorId;
            OffhandId = other.OffhandId;
            ElementPicks.AddRange(other.ElementPicks);
        }

        /// <summary>
        /// A build at its starting position for a class: minimum stats, no kit, an empty pool.
        ///
        /// Starting at the minimum rather than at zero means the budget is spent on the interesting
        /// part of the range, and a character can never be built with nothing in a stat.
        /// </summary>
        public static CharacterBuild StartingFrom(ClassSpec spec, CharacterRules rules)
        {
            var build = new CharacterBuild();

            if (rules != null)
            {
                build.Allocation = rules.StartingAllocation;
            }

            if (spec != null)
            {
                build.ClassId = spec.Id;
                build.WeaponId = spec.WeaponIds.Count > 0 ? spec.WeaponIds[0] : NoEquipment;
            }

            return build;
        }

        /// <summary>Points spent beyond the floor every stat starts at.</summary>
        public int PointsSpent(CharacterRules rules) =>
            rules == null ? Allocation.Total : Allocation.Total - rules.MinPerStat * StatInfo.All.Length;

        public int PointsRemaining(CharacterRules rules) =>
            rules == null ? 0 : rules.PointBudget - PointsSpent(rules);
    }
}
