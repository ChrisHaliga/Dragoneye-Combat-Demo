using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>Where a piece of equipment sits. One item per slot.</summary>
    public enum EquipmentSlot
    {
        Weapon = 0,
        Armor = 1,

        /// <summary>The other hand. A shield lives here, so carrying one does not cost armour.</summary>
        Offhand = 2
    }

    /// <summary>
    /// An authored species, as the rules see it.
    ///
    /// What a creature is, as opposed to what it trained to do. Every creature has one -- premade or
    /// built -- which makes it the right place for anything true of all of them. Take a Breath is
    /// authored onto every species rather than written into the rules: it comes for free with being
    /// alive, but it is still content, and a species that cannot catch its breath is a species a
    /// designer is allowed to author.
    /// </summary>
    public sealed class SpeciesSpec
    {
        public SpeciesSpec(int id, string name, AttributeBlock baseline,
            IReadOnlyList<int> skillIds = null, string description = "",
            int baseAp = Vitals.DefaultBaseAp)
        {
            Id = id;
            Name = name ?? string.Empty;
            Baseline = baseline;
            SkillIds = skillIds ?? System.Array.Empty<int>();
            Description = description ?? string.Empty;
            BaseAp = baseAp < 1 ? 1 : baseAp;
        }

        /// <summary>Stable, hand-assigned. It crosses the network and is written into saved builds.</summary>
        public int Id { get; }

        public string Name { get; }

        /// <summary>Attributes every member of the species has before class, points or kit.</summary>
        public AttributeBlock Baseline { get; }

        /// <summary>What being this species lets you do, whatever else you are.</summary>
        public IReadOnlyList<int> SkillIds { get; }

        /// <summary>
        /// Action points a turn before Endurance is counted.
        ///
        /// On the species rather than in <see cref="Vitals"/> because how much a thing can do in a
        /// turn is a fact about what it is. Every species authored today has four; the field exists
        /// so that something quick or something ponderous does not need a rule of its own.
        /// </summary>
        public int BaseAp { get; }

        public string Description { get; }
    }

    /// <summary>
    /// An authored class, as the rules see it.
    ///
    /// A plain descriptor rather than the asset itself. Combat cannot reference a ScriptableObject
    /// and should not want to: what it needs is a baseline and a list of ids, and taking exactly
    /// that means the same validation runs in a test with no Unity present.
    /// </summary>
    public sealed class ClassSpec
    {
        public ClassSpec(int id, string name, AttributeBlock baseline, IReadOnlyList<int> weaponIds,
            IReadOnlyList<int> skillIds = null, string description = "")
        {
            Id = id;
            Name = name ?? string.Empty;
            Baseline = baseline;
            WeaponIds = weaponIds ?? System.Array.Empty<int>();
            SkillIds = skillIds ?? System.Array.Empty<int>();
            Description = description ?? string.Empty;
        }

        /// <summary>Stable, hand-assigned. It crosses the network and is written into saved builds.</summary>
        public int Id { get; }

        public string Name { get; }

        /// <summary>Stats before any allocation or equipment.</summary>
        public AttributeBlock Baseline { get; }

        /// <summary>The weapons this class may carry. A weapon outside this list fails validation.</summary>
        public IReadOnlyList<int> WeaponIds { get; }

        /// <summary>The core skill set. Everything else has to come from equipment.</summary>
        public IReadOnlyList<int> SkillIds { get; }

        public string Description { get; }

        public bool AllowsWeapon(int weaponId)
        {
            for (var i = 0; i < WeaponIds.Count; i++)
            {
                if (WeaponIds[i] == weaponId)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// An authored item, as the rules see it.
    ///
    /// Described by what it grants rather than by a kind, so two weapons may share nothing but a
    /// slot. "Heavy armour" is not a category the rules know about -- it is an item in the armour
    /// slot whose modifiers happen to trade speed for vitality.
    /// </summary>
    public sealed class EquipmentSpec
    {
        public EquipmentSpec(int id, string name, EquipmentSlot slot, AttributeBlock modifiers,
            IReadOnlyList<int> skillIds = null, ArmourClass armour = ArmourClass.None,
            string description = "", int damageReduction = 0)
        {
            Armour = armour;
            Id = id;
            Name = name ?? string.Empty;
            Slot = slot;
            Modifiers = modifiers;
            SkillIds = skillIds ?? System.Array.Empty<int>();
            Description = description ?? string.Empty;
            DamageReduction = damageReduction < 0 ? 0 : damageReduction;
        }

        /// <summary>Stable, hand-assigned. Zero is reserved to mean "nothing equipped".</summary>
        public int Id { get; }

        public string Name { get; }

        public EquipmentSlot Slot { get; }

        /// <summary>Added to the resolved attributes. May be negative.</summary>
        public AttributeBlock Modifiers { get; }

        /// <summary>How much this slows its wearer. Only armour is anything but None.</summary>
        public ArmourClass Armour { get; }

        /// <summary>
        /// Skills this item grants while it is equipped.
        ///
        /// The only way to gain a skill beyond the class set. Unequipping removes them, because the
        /// loadout is resolved from what is equipped rather than accumulated as things are worn.
        /// </summary>
        public IReadOnlyList<int> SkillIds { get; }

        /// <summary>
        /// Damage this item soaks on top of whatever its armour class soaks.
        ///
        /// For things that protect without being armour -- a shield, which is worn in the offhand so
        /// that carrying one costs no speed. Armour itself leaves this at zero and takes its
        /// reduction from its class, so the rule "medium armour stops two" lives in
        /// <see cref="ArmourRules"/> and not in ten separate assets that could disagree.
        /// </summary>
        public int DamageReduction { get; }

        public string Description { get; }
    }

    /// <summary>
    /// The constraints a build is checked against.
    ///
    /// Authored, because a playtest wants to move the budget without a recompile -- but read here
    /// rather than in the UI, so the screen and the server check the same numbers.
    /// </summary>
    public sealed class CharacterRules
    {
        public CharacterRules(int pointBudget, int maxPerAttribute, int startingLevel)
        {
            PointBudget = pointBudget < 0 ? 0 : pointBudget;
            MaxPerAttribute = maxPerAttribute < 1 ? 1 : maxPerAttribute;
            StartingLevel = startingLevel < Progression.FirstLevel
                ? Progression.FirstLevel
                : startingLevel;
        }

        /// <summary>Points available to spend across every stat.</summary>
        public int PointBudget { get; }

        /// <summary>The highest any one attribute may be bought to.</summary>
        public int MaxPerAttribute { get; }

        /// <summary>
        /// The level a newly created character starts at.
        ///
        /// Only the starting point. A character's level lives on the character from then on, because
        /// it is the thing experience changes -- see <see cref="Progression"/>.
        /// </summary>
        public int StartingLevel { get; }

        /// <summary>Where a fresh character starts: every attribute at the floor.</summary>
        public AttributeBlock StartingAttributes => AttributeBlock.Uniform(PointBuy.Floor);
    }

    /// <summary>
    /// Everything authored that Combat needs to look up by id.
    ///
    /// A seam. Combat states the question -- "what is class 3, and may it carry weapon 7" -- and
    /// something in Data answers from assets. This is what lets the validator run identically in a
    /// test, on a client and on the host.
    /// </summary>
    public interface IContentIndex : ISkillIndex
    {
        CharacterRules Rules { get; }

        IReadOnlyList<SpeciesSpec> Species { get; }

        IReadOnlyList<ClassSpec> Classes { get; }

        IReadOnlyList<EquipmentSpec> Equipment { get; }

        bool TryGetSpecies(int id, out SpeciesSpec spec);

        bool TryGetClass(int id, out ClassSpec spec);

        /// <summary>False for id zero, which means nothing equipped rather than a missing item.</summary>
        bool TryGetEquipment(int id, out EquipmentSpec spec);
    }
}
