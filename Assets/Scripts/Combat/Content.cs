using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>Where a piece of equipment sits. One item per slot.</summary>
    public enum EquipmentSlot
    {
        Weapon = 0,
        Armor = 1
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
        public ClassSpec(int id, string name, StatBlock baseline, IReadOnlyList<int> weaponIds,
            string description = "")
        {
            Id = id;
            Name = name ?? string.Empty;
            Baseline = baseline;
            WeaponIds = weaponIds ?? System.Array.Empty<int>();
            Description = description ?? string.Empty;
        }

        /// <summary>Stable, hand-assigned. It crosses the network and is written into saved builds.</summary>
        public int Id { get; }

        public string Name { get; }

        /// <summary>Stats before any allocation or equipment.</summary>
        public StatBlock Baseline { get; }

        /// <summary>The weapons this class may carry. A weapon outside this list fails validation.</summary>
        public IReadOnlyList<int> WeaponIds { get; }

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
        public EquipmentSpec(int id, string name, EquipmentSlot slot, StatBlock modifiers,
            string description = "")
        {
            Id = id;
            Name = name ?? string.Empty;
            Slot = slot;
            Modifiers = modifiers;
            Description = description ?? string.Empty;
        }

        /// <summary>Stable, hand-assigned. Zero is reserved to mean "nothing equipped".</summary>
        public int Id { get; }

        public string Name { get; }

        public EquipmentSlot Slot { get; }

        /// <summary>Added to the resolved stats. May be negative.</summary>
        public StatBlock Modifiers { get; }

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
        public CharacterRules(int pointBudget, int minPerStat, int maxPerStat, int level)
        {
            PointBudget = pointBudget < 0 ? 0 : pointBudget;
            MinPerStat = minPerStat < 0 ? 0 : minPerStat;
            MaxPerStat = maxPerStat < MinPerStat ? MinPerStat : maxPerStat;
            Level = level < 1 ? 1 : level;
        }

        /// <summary>Points available to spend across every stat.</summary>
        public int PointBudget { get; }

        public int MinPerStat { get; }

        public int MaxPerStat { get; }

        /// <summary>
        /// How many element resources make up the starting pool -- one per level.
        ///
        /// Fixed for everyone rather than per character. Progression is out of scope; this is the
        /// dial that decides how deep a pool is, and it belongs with the point budget.
        /// </summary>
        public int Level { get; }

        /// <summary>The allocation a fresh character starts from: the minimum in every stat.</summary>
        public StatBlock StartingAllocation =>
            new StatBlock(MinPerStat, MinPerStat, MinPerStat, MinPerStat);
    }

    /// <summary>
    /// Everything authored that Combat needs to look up by id.
    ///
    /// A seam. Combat states the question -- "what is class 3, and may it carry weapon 7" -- and
    /// something in Data answers from assets. This is what lets the validator run identically in a
    /// test, on a client and on the host.
    /// </summary>
    public interface IContentIndex
    {
        CharacterRules Rules { get; }

        IReadOnlyList<ClassSpec> Classes { get; }

        IReadOnlyList<EquipmentSpec> Equipment { get; }

        bool TryGetClass(int id, out ClassSpec spec);

        /// <summary>False for id zero, which means nothing equipped rather than a missing item.</summary>
        bool TryGetEquipment(int id, out EquipmentSpec spec);
    }
}
