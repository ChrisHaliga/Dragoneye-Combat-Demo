using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>How heavy a suit of armour is. Costs Speed, and stops damage.</summary>
    public enum ArmourClass
    {
        None = 0,
        Light = 1,
        Medium = 2,
        Heavy = 3
    }

    /// <summary>
    /// What armour does.
    ///
    /// Two separate things, which is why the class is an enum rather than a pair of numbers on each
    /// asset: the speed it costs is its ordinal, and the damage it stops is this table. Heavy
    /// armour stopping four rather than three is a tuning decision that belongs in one place, not
    /// spread across every suit somebody authors.
    /// </summary>
    public static class ArmourRules
    {
        /// <summary>Damage a suit of this class takes off every blow that lands on its wearer.</summary>
        public static int ReductionFor(ArmourClass armour)
        {
            switch (armour)
            {
                case ArmourClass.Light: return 1;
                case ArmourClass.Medium: return 2;
                case ArmourClass.Heavy: return 4;
                default: return 0;
            }
        }

        /// <summary>Speed this costs its wearer.</summary>
        public static int SpeedCostOf(ArmourClass armour) => (int)armour;
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

        /// <summary>
        /// Action points before Endurance, for a species that does not say otherwise.
        ///
        /// A default rather than the rule. What a creature can get through in a turn is a fact about
        /// what it is, so the number it starts from lives on <see cref="SpeciesSpec"/>; this is what
        /// a species written without one gets.
        /// </summary>
        public const int DefaultBaseAp = 4;

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
        /// AP = the species base + END. There is no floor under it any more: a floor and an authored
        /// base are two answers to the same question, and with both in place the authored one did
        /// nothing until Endurance had already cleared the floor on its own.
        /// SPD = DEX + END - armour class, so heavier protection costs initiative.
        /// </summary>
        public static Vitals From(AttributeBlock attributes, int level, ArmourClass armour,
            int baseAp = DefaultBaseAp)
        {
            var health = BaseHealth + level + attributes.Vitality + attributes.Toughness;
            var ap = (baseAp < 1 ? 1 : baseAp) + attributes.Endurance;

            return new Vitals(
                level,
                health < 1 ? 1 : health,
                Ap.FromWhole(ap < 1 ? 1 : ap),
                attributes.Dexterity + attributes.Endurance - ArmourRules.SpeedCostOf(armour));
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
            IReadOnlyList<SkillSpec> skills = null)
        {
            Species = species;
            Class = classSpec;
            Attributes = attributes;
            Armour = armour;
            Items = items ?? System.Array.Empty<EquipmentSpec>();
            StartingPool = startingPool;
            Skills = skills ?? System.Array.Empty<SkillSpec>();
            Vitals = Vitals.From(attributes, level, armour,
                species != null ? species.BaseAp : Vitals.DefaultBaseAp);
            DamageReduction = ResolveReduction(armour, Items);
            Advantage = ResolveAdvantage(Items);
        }

        static bool ResolveAdvantage(IReadOnlyList<EquipmentSpec> items)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].GrantsAdvantage)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Damage taken off every blow that lands: what the armour stops, plus anything else worn
        /// that stops damage without being armour.
        /// </summary>
        public int DamageReduction { get; }

        /// <summary>
        /// Whether anything worn gives this creature the better of two elements in a clash.
        ///
        /// One flag rather than a count: two shields are not twice a shield, and DE-006 is explicit
        /// that advantage and disadvantage are states rather than quantities.
        /// </summary>
        public bool Advantage { get; }

        static int ResolveReduction(ArmourClass armour, IReadOnlyList<EquipmentSpec> items)
        {
            var total = ArmourRules.ReductionFor(armour);

            for (var i = 0; i < items.Count; i++)
            {
                total += items[i].DamageReduction;
            }

            return total;
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
                return new Loadout(null, null, AttributeBlock.Zero, Progression.FirstLevel,
                    ArmourClass.None, null, ElementCounts.Empty);
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

            var level = build.Level < Progression.FirstLevel ? Progression.FirstLevel : build.Level;

            // Equipment may subtract, but never below zero, where the derived numbers stop meaning
            // anything.
            return new Loadout(species, classSpec, attributes.ClampedLow(0), level,
                armour, items, build.StartingPool,
                ResolveSkills(species, classSpec, items, build.LearnedSkillIds, content, level));
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
            List<EquipmentSpec> items, IReadOnlyList<int> learned, ISkillIndex skills, int level)
        {
            var resolved = new List<SkillSpec>();
            var seen = new HashSet<int>();

            if (species != null)
            {
                AddAll(species.SkillIds, skills, resolved, seen, level);
            }

            if (classSpec != null)
            {
                AddAll(classSpec.SkillIds, skills, resolved, seen, level);
            }

            foreach (var item in items)
            {
                AddAll(item.SkillIds, skills, resolved, seen, level);
            }

            AddAll(learned, skills, resolved, seen, level);

            return resolved;
        }

        /// <summary>
        /// Adds the skills a creature of this level is entitled to, in order, without duplicates.
        ///
        /// A skill above the creature's level is left out rather than included and disabled. It is
        /// not a choice the player is being offered yet, and it is the resolved list that the
        /// creation sheet, the arena bar and the server all read -- so leaving it out here is what
        /// makes "not until you are high enough" true everywhere at once.
        /// </summary>
        static void AddAll(IReadOnlyList<int> ids, ISkillIndex skills, List<SkillSpec> into,
            HashSet<int> seen, int level)
        {
            if (ids == null)
            {
                return;
            }

            foreach (var id in ids)
            {
                if (seen.Add(id) && skills.TryGetSkill(id, out var spec)
                    && spec.LevelRequired <= level)
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
