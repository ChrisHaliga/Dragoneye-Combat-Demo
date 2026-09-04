using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>
    /// What a stat block means once a creature is fighting.
    ///
    /// Derivation lives in one place so the creation screen and the arena cannot disagree about what
    /// four Vitality is worth. The multipliers are constants rather than authored data on purpose --
    /// they are the shape of the game, not a tuning dial, and moving them is a design decision that
    /// should show up in a diff.
    /// </summary>
    public readonly struct Vitals
    {
        /// <summary>Health per point of Vitality.</summary>
        public const int HealthPerVitality = 5;

        /// <summary>Damage per point of Power.</summary>
        public const int DamagePerPower = 1;

        public readonly int MaxHealth;

        /// <summary>Action points a turn, in half-units.</summary>
        public readonly Ap MaxAp;

        /// <summary>Initiative. Higher acts first.</summary>
        public readonly int Initiative;

        public readonly int Damage;

        public Vitals(int maxHealth, Ap maxAp, int initiative, int damage)
        {
            MaxHealth = maxHealth;
            MaxAp = maxAp;
            Initiative = initiative;
            Damage = damage;
        }

        /// <summary>
        /// Resolves stats into the numbers a fight uses.
        ///
        /// Floors health at one: a creature derived to zero health would be dead before its first
        /// turn, which is a confusing way to discover a bad stat block.
        /// </summary>
        public static Vitals From(StatBlock stats)
        {
            var health = stats.Vitality * HealthPerVitality;

            return new Vitals(
                health < 1 ? 1 : health,
                Ap.FromWhole(stats.Focus < 0 ? 0 : stats.Focus),
                stats.Speed,
                stats.Power * DamagePerPower);
        }
    }

    /// <summary>
    /// A class plus everything equipped, resolved.
    ///
    /// The one answer to "what are this creature's stats". DE-003 requires that two clients
    /// resolving the same build reach the same numbers, which is why the sum is over whole
    /// <see cref="StatBlock"/>s -- addition is commutative, so the order modifiers are folded in
    /// cannot change the result.
    /// </summary>
    public sealed class Loadout
    {
        public Loadout(ClassSpec classSpec, StatBlock stats, IReadOnlyList<EquipmentSpec> items,
            IReadOnlyList<Element> startingPool, IReadOnlyList<SkillSpec> skills = null,
            PassiveSet passives = null)
        {
            Passives = passives ?? PassiveSet.Empty;
            Class = classSpec;
            Stats = stats;
            Items = items ?? System.Array.Empty<EquipmentSpec>();
            StartingPool = startingPool ?? System.Array.Empty<Element>();
            Skills = skills ?? System.Array.Empty<SkillSpec>();
            Vitals = Vitals.From(stats);
        }

        public ClassSpec Class { get; }

        /// <summary>Baseline plus allocation plus every equipped modifier.</summary>
        public StatBlock Stats { get; }

        public Vitals Vitals { get; }

        /// <summary>Everything equipped, in slot order. Empty slots are absent, not null entries.</summary>
        public IReadOnlyList<EquipmentSpec> Items { get; }

        /// <summary>The elements this creature starts holding. Order is the order they were picked.</summary>
        public IReadOnlyList<Element> StartingPool { get; }

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
                return new Loadout(null, StatBlock.Zero, null, null);
            }

            content.TryGetClass(build.ClassId, out var classSpec);

            var items = new List<EquipmentSpec>();
            Collect(content, build.WeaponId, items);
            Collect(content, build.ArmorId, items);
            Collect(content, build.OffhandId, items);

            var stats = classSpec != null ? classSpec.Baseline : StatBlock.Zero;
            stats += build.Allocation;

            foreach (var item in items)
            {
                stats += item.Modifiers;
            }

            // Equipment may subtract -- heavy armour costs speed -- but never below zero, where the
            // derived numbers stop meaning anything.
            return new Loadout(classSpec, stats.ClampedLow(0), items,
                new List<Element>(build.ElementPicks), ResolveSkills(classSpec, items, content),
                ResolvePassives(items));
        }

        /// <summary>
        /// The class set plus every equipped item grants, in that order, without duplicates.
        ///
        /// Order is fixed so two clients list a creature's skills identically. Duplicates are
        /// dropped rather than stacked: two items granting the same skill grant one skill.
        /// </summary>
        static List<SkillSpec> ResolveSkills(ClassSpec classSpec, List<EquipmentSpec> items,
            ISkillIndex skills)
        {
            var resolved = new List<SkillSpec>();
            var seen = new HashSet<int>();

            if (classSpec != null)
            {
                AddAll(classSpec.SkillIds, skills, resolved, seen);
            }

            foreach (var item in items)
            {
                AddAll(item.SkillIds, skills, resolved, seen);
            }

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
