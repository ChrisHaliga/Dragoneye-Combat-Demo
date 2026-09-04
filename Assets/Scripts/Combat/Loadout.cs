using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>How much a suit of armour slows its wearer. Subtracted from Speed.</summary>
    public enum ArmourClass
    {
        None = 0,
        Light = 1,
        Medium = 2,
        Heavy = 3
    }

    /// <summary>
    /// The stats a creature actually fights with, derived from its attributes.
    ///
    /// Attributes are bought; stats are worked out. Keeping the derivation in one place is what lets
    /// the creation screen promise a number the arena then honours -- both call this.
    ///
    /// The formulas are constants rather than authored data on purpose: they are the shape of the
    /// game, not a tuning dial, and changing one should show up in a diff.
    /// </summary>
    public readonly struct Vitals
    {
        /// <summary>Health every creature has before its attributes are counted.</summary>
        public const int BaseHealth = 3;

        /// <summary>Action points before Endurance, and the floor the total cannot fall below.</summary>
        public const int BaseAp = 4;

        public const int MinimumAp = 8;

        public readonly int Level;
        public readonly int MaxHealth;

        /// <summary>Action points a turn, in half-units.</summary>
        public readonly Ap MaxAp;

        /// <summary>Speed, which decides turn order.</summary>
        public readonly int Speed;

        public Vitals(int level, int maxHealth, Ap maxAp, int speed)
        {
            Level = level;
            MaxHealth = maxHealth;
            MaxAp = maxAp;
            Speed = speed;
        }

        /// <summary>
        /// Resolves attributes into the stats a fight uses.
        ///
        /// HP = 3 + LVL + VIT + TGH.
        /// AP = max(4 + END, 8), so Endurance only starts buying points once it clears the floor --
        /// which is deliberate: everybody gets a workable turn, and Endurance is what takes you past it.
        /// SPD = DEX + END - armour class, so heavier protection costs initiative.
        /// </summary>
        public static Vitals From(AttributeBlock attributes, int level, ArmourClass armour)
        {
            var health = BaseHealth + level + attributes.Vitality + attributes.Toughness;
            var ap = BaseAp + attributes.Endurance;

            return new Vitals(
                level,
                health < 1 ? 1 : health,
                Ap.FromWhole(ap < MinimumAp ? MinimumAp : ap),
                attributes.Dexterity + attributes.Endurance - (int)armour);
        }
    }


    /// <summary>
    /// A class plus everything equipped, resolved.
    ///
    /// The one answer to "what are this creature's stats". DE-003 requires that two clients
    /// resolving the same build reach the same numbers, which is why the sum is over whole
    /// <see cref="AttributeBlock"/>s -- addition is commutative, so the order modifiers are folded in
    /// cannot change the result.
    /// </summary>
    public sealed class Loadout
    {
        public Loadout(SpeciesSpec species, ClassSpec classSpec, AttributeBlock attributes, int level,
            ArmourClass armour, IReadOnlyList<EquipmentSpec> items, ElementCounts startingPool,
            IReadOnlyList<SkillSpec> skills = null, PassiveSet passives = null)
        {
            Species = species;
            Class = classSpec;
            Attributes = attributes;
            Armour = armour;
            Items = items ?? System.Array.Empty<EquipmentSpec>();
            StartingPool = startingPool;
            Skills = skills ?? System.Array.Empty<SkillSpec>();
            Passives = passives ?? PassiveSet.Empty;
            Vitals = Vitals.From(attributes, level, armour);
        }

        /// <summary>What the creature is. Null when the build names a species that no longer exists.</summary>
        public SpeciesSpec Species { get; }

        public ClassSpec Class { get; }

        /// <summary>Baseline plus what was bought plus every equipped modifier.</summary>
        public AttributeBlock Attributes { get; }

        /// <summary>The heaviest armour worn, which costs Speed.</summary>
        public ArmourClass Armour { get; }

        public Vitals Vitals { get; }

        /// <summary>Everything equipped, in slot order. Empty slots are absent, not null entries.</summary>
        public IReadOnlyList<EquipmentSpec> Items { get; }

        /// <summary>The elements this creature starts holding.</summary>
        public ElementCounts StartingPool { get; }

        /// <summary>
        /// Everything this creature can do: the class set plus every skill its equipment grants.
        ///
        /// Resolved from what is equipped rather than accumulated as items are worn, which is what
        /// makes "no sword, no sword skills" true by construction -- unequipping cannot leave a
        /// skill behind because nothing ever added one to a running total.
        /// </summary>
        public IReadOnlyList<SkillSpec> Skills { get; }

        /// <summary>
        /// Every passive this creature holds, from whatever granted it.
        ///
        /// The clash asks this rather than inspecting the equipment list, so a second source of
        /// passives later -- a class, a status -- needs no change where they are read.
        /// </summary>
        public PassiveSet Passives { get; }
    }

    /// <summary>
    /// Turns a build into a loadout.
    ///
    /// Resolves whatever it can rather than refusing: an unknown class or a missing item yields a
    /// loadout without it. Deciding a build is unacceptable is <see cref="BuildValidator"/>'s job,
    /// and a resolver that also refused would be a second opinion on the same question -- one the
    /// creation screen would have to handle separately while the player is still typing.
    /// </summary>
    public static class LoadoutResolver
    {
        public static Loadout Resolve(CharacterBuild build, IContentIndex content)
        {
            if (build == null || content == null)
            {
                return new Loadout(null, null, AttributeBlock.Zero, 1, ArmourClass.None, null,
                    ElementCounts.Empty);
            }

            content.TryGetSpecies(build.SpeciesId, out var species);
            content.TryGetClass(build.ClassId, out var classSpec);

            var items = new List<EquipmentSpec>();
            Collect(content, build.WeaponId, items);
            Collect(content, build.ArmorId, items);
            Collect(content, build.OffhandId, items);

            var attributes = species != null ? species.Baseline : AttributeBlock.Zero;

            if (classSpec != null)
            {
                attributes += classSpec.Baseline;
            }

            attributes += build.Attributes;

            var armour = ArmourClass.None;

            foreach (var item in items)
            {
                attributes += item.Modifiers;

                // The heaviest worn wins rather than the sum, so a second piece of armour cannot
                // stack a speed penalty that the rules never intended.
                if (item.Armour > armour)
                {
                    armour = item.Armour;
                }
            }

            // Equipment may subtract, but never below zero, where the derived numbers stop meaning
            // anything.
            return new Loadout(species, classSpec, attributes.ClampedLow(0), content.Rules.Level,
                armour, items, build.StartingPool,
                ResolveSkills(species, classSpec, items, build.LearnedSkillIds, content),
                ResolvePassives(items));
        }

        /// <summary>
        /// What the species grants, then the class set, then everything equipped grants, then what
        /// the character has learned -- in that order, without duplicates.
        ///
        /// Species first because it is the least conditional: it is true of the creature before it
        /// picked anything. Order is fixed so two clients list a creature's skills identically, and
        /// duplicates are dropped rather than stacked -- two sources granting the same skill grant
        /// one skill.
        /// </summary>
        static List<SkillSpec> ResolveSkills(SpeciesSpec species, ClassSpec classSpec,
            List<EquipmentSpec> items, IReadOnlyList<int> learned, ISkillIndex skills)
        {
            var resolved = new List<SkillSpec>();
            var seen = new HashSet<int>();

            if (species != null)
            {
                AddAll(species.SkillIds, skills, resolved, seen);
            }

            if (classSpec != null)
            {
                AddAll(classSpec.SkillIds, skills, resolved, seen);
            }

            foreach (var item in items)
            {
                AddAll(item.SkillIds, skills, resolved, seen);
            }

            AddAll(learned, skills, resolved, seen);

            return resolved;
        }

        /// <summary>Every passive on every equipped item, deduplicated by the set itself.</summary>
        static PassiveSet ResolvePassives(List<EquipmentSpec> items)
        {
            var passives = new List<Passive>();

            foreach (var item in items)
            {
                passives.AddRange(item.Passives);
            }

            return new PassiveSet(passives);
        }

        static void AddAll(IReadOnlyList<int> ids, ISkillIndex skills, List<SkillSpec> into,
            HashSet<int> seen)
        {
            if (ids == null)
            {
                return;
            }

            foreach (var id in ids)
            {
                if (seen.Add(id) && skills.TryGetSkill(id, out var spec))
                {
                    into.Add(spec);
                }
            }
        }

        static void Collect(IContentIndex content, int id, List<EquipmentSpec> into)
        {
            if (id != CharacterBuild.NoEquipment && content.TryGetEquipment(id, out var spec))
            {
                into.Add(spec);
            }
        }
    }
}
