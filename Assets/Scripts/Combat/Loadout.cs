using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>How heavy a suit of armour is. Costs Speed, prices every step, and stops damage.</summary>
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
    /// Two things, which is why the class is an enum rather than a pair of numbers on each asset:
    /// the pool it gives and the speed it costs are both this file's tables. Plate stopping
    /// sixteen rather than eight is a tuning decision that belongs in one place, not spread across
    /// every suit somebody authors. What a step costs is not armour's to say directly -- it
    /// follows from the speed, so a suit slows you down and *that* is what makes walking dear.
    /// </summary>
    public static class ArmourRules
    {
        /// <summary>
        /// Armour a suit of this class gives its wearer: a pool above health, worn down by every
        /// blow. It does not come back. Health can be healed; armour, once it is gone, is gone for
        /// the match -- which is what makes it a resource to protect rather than a number to
        /// outlast, and what makes the doubling worth the price of each step.
        /// </summary>
        public static int PointsFor(ArmourClass armour)
        {
            switch (armour)
            {
                case ArmourClass.Light: return 4;
                case ArmourClass.Medium: return 8;
                case ArmourClass.Heavy: return 16;
                default: return 0;
            }
        }

        /// <summary>
        /// Speed this costs its wearer. Doubling each class, against a base speed of eight: plate
        /// takes the whole of it, and only Endurance puts any back.
        /// </summary>
        public static int SpeedCostOf(ArmourClass armour)
        {
            switch (armour)
            {
                case ArmourClass.Light: return 2;
                case ArmourClass.Medium: return 4;
                case ArmourClass.Heavy: return 8;
                default: return 0;
            }
        }

        /// <summary>"Light armour", for a tooltip that has to name what a number came from.</summary>
        public static string NameOf(ArmourClass armour)
        {
            switch (armour)
            {
                case ArmourClass.Light: return "light armour";
                case ArmourClass.Medium: return "medium armour";
                case ArmourClass.Heavy: return "heavy armour";
                default: return "no armour";
            }
        }
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

        /// <summary>Speed before Endurance and before armour. What an unarmoured nobody moves at.</summary>
        public const int BaseSpeed = 8;

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

        /// <summary>Speed, which decides turn order and prices every step.</summary>
        public readonly int Speed;

        /// <summary>Health back at the start of every turn. Toughness, and never below zero.</summary>
        public readonly int Regen;

        public Vitals(int level, int maxHealth, Ap maxAp, int speed, int regen = 0)
        {
            Level = level;
            MaxHealth = maxHealth;
            MaxAp = maxAp;
            Speed = speed;
            Regen = regen < 0 ? 0 : regen;
        }

        /// <summary>What one tile costs at this speed. See <see cref="CombatRules.StepCostFor"/>.</summary>
        public Ap StepCost => CombatRules.StepCostFor(Speed);

        /// <summary>
        /// Resolves attributes into the stats a fight uses.
        ///
        /// HP = 3 + LVL + VIT.
        /// AP = the species base + WIL. There is no floor under it: a floor and an authored base
        /// are two answers to the same question, and with both in place the authored one did
        /// nothing until the attribute had already cleared the floor on its own.
        /// SPD = 8 + END - armour, so heavier protection costs initiative and every step.
        /// Regen = TGH, back every turn.
        ///
        /// Each attribute feeds exactly one stat. An attribute that fed two was worth two, and the
        /// point buy priced them all the same.
        /// </summary>
        public static Vitals From(AttributeBlock attributes, int level, ArmourClass armour,
            int baseAp = DefaultBaseAp)
        {
            var health = BaseHealth + level + attributes.Vitality;
            var ap = (baseAp < 1 ? 1 : baseAp) + attributes.Willpower;

            return new Vitals(
                level,
                health < 1 ? 1 : health,
                Ap.FromWhole(ap < 1 ? 1 : ap),
                BaseSpeed + attributes.Endurance - ArmourRules.SpeedCostOf(armour),
                attributes.Toughness);
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
            Skills = Scale(skills, attributes);
            Vitals = Vitals.From(attributes, level, armour,
                species != null ? species.BaseAp : Vitals.DefaultBaseAp);
            ArmourPoints = ResolveArmour(armour, Items);
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
        /// The armour pool: what the suit gives, plus anything else worn that guards without being
        /// armour. Worn down by every blow that lands, and never restored.
        /// </summary>
        public int ArmourPoints { get; }

        /// <summary>What one tile costs this creature, which its speed decides.</summary>
        public Ap StepCost => Vitals.StepCost;

        /// <summary>
        /// The skills with this creature's attributes folded into them.
        ///
        /// "4 + STR" is what a weapon says; "6" is what this fighter does with it. Settled here,
        /// once, so the sheet, the bar and the server all read the same number and none of them
        /// has to know that skills scale.
        /// </summary>
        static IReadOnlyList<SkillSpec> Scale(IReadOnlyList<SkillSpec> skills,
            AttributeBlock attributes)
        {
            if (skills == null || skills.Count == 0)
            {
                return System.Array.Empty<SkillSpec>();
            }

            var scaled = new List<SkillSpec>(skills.Count);

            foreach (var skill in skills)
            {
                scaled.Add(skill.Scaled(attributes));
            }

            return scaled;
        }

        /// <summary>
        /// Whether anything worn gives this creature the better of two elements in a clash.
        ///
        /// One flag rather than a count: two shields are not twice a shield, and DE-006 is explicit
        /// that advantage and disadvantage are states rather than quantities.
        /// </summary>
        public bool Advantage { get; }

        static int ResolveArmour(ArmourClass armour, IReadOnlyList<EquipmentSpec> items)
        {
            var total = ArmourRules.PointsFor(armour);

            for (var i = 0; i < items.Count; i++)
            {
                total += items[i].ArmourPoints;
            }

            return total;
        }

        /// <summary>What the creature is. Null when the build names a species that no longer exists.</summary>
        public SpeciesSpec Species { get; }

        public ClassSpec Class { get; }

        /// <summary>The species baseline, the class baseline and what was bought. Equipment adds nothing.</summary>
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
        /// Whether anything is in the weapon slot.
        ///
        /// The same answer the conditions were resolved against, asked of the finished loadout.
        /// One implementation, so what the skill list was filtered on and what anything else reads
        /// cannot come apart.
        /// </summary>
        public bool HasWeapon => LoadoutResolver.HasWeapon(Items);

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

            // Most equipment does not touch the attributes: a weapon is its skills, and armour is
            // its pool and its weight. What an item moves it moves here, added after the points
            // are spent, so what was bought and what is carried stay separate -- the budget is
            // checked against the numbers the player paid for, never against the ones they are
            // wearing.
            foreach (var item in items)
            {
                // The heaviest worn wins rather than the sum, so a second piece of armour cannot
                // stack a speed penalty that the rules never intended.
                if (item.Armour > armour)
                {
                    armour = item.Armour;
                }

                attributes += item.Modifiers;
            }

            var level = build.Level < Progression.FirstLevel ? Progression.FirstLevel : build.Level;

            // What the conditions on a skill get to ask about. Built once, from the same
            // resolution that decided everything else, so the sheet and the arena cannot disagree
            // about whether somebody is holding a weapon.
            var situation = new SkillSituation(level,
                species != null ? species.Id : 0,
                classSpec != null ? classSpec.Id : 0,
                HasWeapon(items));

            // Clamped low: a baseline may subtract, but never below zero, where the derived
            // numbers stop meaning anything.
            return new Loadout(species, classSpec, attributes.ClampedLow(0), level,
                armour, items, build.StartingPool,
                ResolveSkills(species, classSpec, items, build.LearnedSkillIds, content,
                    situation));
        }

        /// <summary>Whether anything is in the weapon slot.</summary>
        public static bool HasWeapon(IReadOnlyList<EquipmentSpec> items)
        {
            if (items == null)
            {
                return false;
            }

            foreach (var item in items)
            {
                if (item != null && item.Slot == EquipmentSlot.Weapon)
                {
                    return true;
                }
            }

            return false;
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
            List<EquipmentSpec> items, IReadOnlyList<int> learned, ISkillIndex skills,
            SkillSituation situation)
        {
            var resolved = new List<SkillSpec>();
            var seen = new HashSet<int>();

            if (species != null)
            {
                AddAll(species.SkillIds, skills, resolved, seen, situation);
            }

            if (classSpec != null)
            {
                AddAll(classSpec.SkillIds, skills, resolved, seen, situation);
            }

            foreach (var item in items)
            {
                AddAll(item.SkillIds, skills, resolved, seen, situation);
            }

            AddAll(learned, skills, resolved, seen, situation);

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
            HashSet<int> seen, SkillSituation situation)
        {
            if (ids == null)
            {
                return;
            }

            foreach (var id in ids)
            {
                // Marked seen whether it makes the cut or not: a skill granted twice and refused
                // once is refused, and letting the second grant slip it past the first would make
                // availability depend on how many things happened to hand it over.
                if (seen.Add(id) && skills.TryGetSkill(id, out var spec)
                    && spec.IsAvailable(situation))
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
