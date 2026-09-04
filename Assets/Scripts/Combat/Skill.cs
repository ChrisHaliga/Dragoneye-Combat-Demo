using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>What a skill is aimed at.</summary>
    public enum SkillTarget
    {
        /// <summary>Another creature. Goes through a clash.</summary>
        Creature = 0,

        /// <summary>The user. Resolves immediately, with nobody to contest it.</summary>
        Self = 1,

        /// <summary>A place on the board. Resolves immediately.</summary>
        Tile = 2
    }

    /// <summary>
    /// What a skill does.
    ///
    /// An enum and an amount rather than a method, so a skill is authored rather than written. The
    /// set is deliberately small: these are the effects the game currently resolves, and a kind with
    /// no resolution behind it would be a promise the rules do not keep.
    /// </summary>
    public enum SkillEffectKind
    {
        /// <summary>Reduce health.</summary>
        Damage = 0,

        /// <summary>Restore health, never past the maximum.</summary>
        Heal = 1,

        /// <summary>Restore action points, never past the maximum.</summary>
        RestoreAp = 2,

        /// <summary>
        /// Put spent elements back into the pool, oldest first.
        ///
        /// The amount is a count of elements, not of any one element: which ones come back is
        /// decided by the order they were spent in, not by the skill.
        /// </summary>
        ReturnElement = 3
    }

    /// <summary>An effect and how much of it.</summary>
    public readonly struct SkillEffect
    {
        public readonly SkillEffectKind Kind;
        public readonly int Amount;

        public SkillEffect(SkillEffectKind kind, int amount)
        {
            Kind = kind;
            Amount = amount < 0 ? 0 : amount;
        }
    }

    /// <summary>
    /// An authored skill: the six fields DE-002 asks for, and nothing else.
    ///
    /// The element is fixed by the skill rather than chosen by the user, so a creature is limited to
    /// answering with what its kit actually grants. Both costs are authored rather than derived,
    /// because deriving them would tie a skill's price to stats that equipment can move.
    /// </summary>
    public sealed class SkillSpec
    {
        public SkillSpec(int id, string name, Element element, Ap apCost, int elementCost,
            int range, SkillTarget target, SkillEffect effect, string description = "")
        {
            Id = id;
            Name = name ?? string.Empty;
            Element = element;
            ApCost = apCost;
            ElementCost = elementCost < 0 ? 0 : elementCost;
            Range = range < 0 ? 0 : range;
            Target = target;
            Effect = effect;
            Description = description ?? string.Empty;
        }

        /// <summary>Stable and hand-assigned; it crosses the network.</summary>
        public int Id { get; }

        public string Name { get; }

        /// <summary>Fixed by the skill. The user does not choose it.</summary>
        public Element Element { get; }

        public Ap ApCost { get; }

        /// <summary>How much of <see cref="Element"/> using this consumes from the pool.</summary>
        public int ElementCost { get; }

        /// <summary>Reach in tiles. Zero means the user only.</summary>
        public int Range { get; }

        public SkillTarget Target { get; }

        public SkillEffect Effect { get; }

        public string Description { get; }

        /// <summary>
        /// Whether using this starts a clash.
        ///
        /// Only creature-targeted skills are contested; self- and tile-directed ones have nobody to
        /// contest them and resolve where they are used.
        /// </summary>
        public bool IsContested => Target == SkillTarget.Creature;
    }

    /// <summary>Everything a creature can do, in a fixed order.</summary>
    public interface ISkillIndex
    {
        bool TryGetSkill(int id, out SkillSpec spec);

        IReadOnlyList<SkillSpec> Skills { get; }
    }
}
