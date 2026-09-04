using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>
    /// A character as its player assembled it: a class, an attribute spread, a starting pool, a kit
    /// and anything it has learned.
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

        /// <summary>What this character is. Grants a baseline and whatever comes with being one.</summary>
        public int SpeciesId;

        public int ClassId;

        /// <summary>What the player bought. Not the resolved values -- see <see cref="Loadout"/>.</summary>
        public AttributeBlock Attributes = AttributeBlock.Uniform(PointBuy.Floor);

        /// <summary>
        /// The elements this character starts holding.
        ///
        /// Any spread that totals the character's level. A level-four character may hold three Hydro
        /// and one Pyro, or one of four different elements -- the shape of the pool is as much a
        /// choice as its size.
        /// </summary>
        public ElementCounts StartingPool = ElementCounts.Empty;

        public int WeaponId = NoEquipment;

        public int ArmorId = NoEquipment;

        public int OffhandId = NoEquipment;

        /// <summary>
        /// Skills this character knows beyond what its class and equipment grant.
        ///
        /// A third source, because there are ways to learn a skill that are neither -- and folding
        /// them into the class list would make an earned skill indistinguishable from a birthright.
        /// </summary>
        public readonly List<int> LearnedSkillIds = new List<int>();

        public CharacterBuild() { }

        public CharacterBuild(CharacterBuild other)
        {
            if (other == null)
            {
                return;
            }

            Name = other.Name;
            SpeciesId = other.SpeciesId;
            ClassId = other.ClassId;
            Attributes = other.Attributes;
            StartingPool = other.StartingPool;
            WeaponId = other.WeaponId;
            ArmorId = other.ArmorId;
            OffhandId = other.OffhandId;
            LearnedSkillIds.AddRange(other.LearnedSkillIds);
        }

        /// <summary>
        /// A build at its starting position: every attribute at the floor, no kit, an empty pool.
        /// </summary>
        public static CharacterBuild StartingFrom(SpeciesSpec species, ClassSpec spec)
        {
            var build = new CharacterBuild();

            if (species != null)
            {
                build.SpeciesId = species.Id;
            }

            if (spec != null)
            {
                build.ClassId = spec.Id;
                build.WeaponId = spec.WeaponIds.Count > 0 ? spec.WeaponIds[0] : NoEquipment;
            }

            return build;
        }

        /// <summary>Points spent raising attributes off the floor.</summary>
        public int PointsSpent() => PointBuy.TotalCost(Attributes);

        public int PointsRemaining(CharacterRules rules) =>
            rules == null ? 0 : PointBuy.Remaining(Attributes, rules.PointBudget);
    }
}
