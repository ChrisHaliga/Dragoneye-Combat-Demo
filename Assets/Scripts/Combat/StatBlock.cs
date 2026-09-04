using System;

namespace Dragoneye.Combat
{
    /// <summary>
    /// The stats a creature has.
    ///
    /// Hand-assigned and permanent, like <see cref="Element"/>: these index saved allocations and
    /// replicated blocks, so the order cannot change once a character has been stored.
    /// </summary>
    public enum StatKind
    {
        /// <summary>Health.</summary>
        Vitality = 0,

        /// <summary>Initiative, which decides turn order.</summary>
        Speed = 1,

        /// <summary>Damage dealt.</summary>
        Power = 2,

        /// <summary>Action points available each turn.</summary>
        Focus = 3
    }

    /// <summary>
    /// A value per stat.
    ///
    /// Immutable and addable, because a creature's stats are a class baseline plus every modifier
    /// its equipment supplies, and DE-003 requires that sum to be order-independent. Addition of
    /// whole blocks is commutative and associative, so two clients folding the same modifiers in a
    /// different order cannot arrive at different stats -- which a sequence of mutating "apply"
    /// calls would not guarantee.
    /// </summary>
    public readonly struct StatBlock : IEquatable<StatBlock>
    {
        public static readonly StatBlock Zero = default;

        public readonly int Vitality;
        public readonly int Speed;
        public readonly int Power;
        public readonly int Focus;

        public StatBlock(int vitality, int speed, int power, int focus)
        {
            Vitality = vitality;
            Speed = speed;
            Power = power;
            Focus = focus;
        }

        public int this[StatKind stat]
        {
            get
            {
                switch (stat)
                {
                    case StatKind.Vitality: return Vitality;
                    case StatKind.Speed: return Speed;
                    case StatKind.Power: return Power;
                    case StatKind.Focus: return Focus;
                    default: return 0;
                }
            }
        }

        /// <summary>A copy with one stat replaced. The original is untouched.</summary>
        public StatBlock With(StatKind stat, int value)
        {
            switch (stat)
            {
                case StatKind.Vitality: return new StatBlock(value, Speed, Power, Focus);
                case StatKind.Speed: return new StatBlock(Vitality, value, Power, Focus);
                case StatKind.Power: return new StatBlock(Vitality, Speed, value, Focus);
                case StatKind.Focus: return new StatBlock(Vitality, Speed, Power, value);
                default: return this;
            }
        }

        /// <summary>The sum of every stat, which is what a point budget is spent against.</summary>
        public int Total => Vitality + Speed + Power + Focus;

        /// <summary>True when no stat is negative. Equipment may subtract; an allocation may not.</summary>
        public bool IsNonNegative => Vitality >= 0 && Speed >= 0 && Power >= 0 && Focus >= 0;

        public static StatBlock operator +(StatBlock a, StatBlock b) =>
            new StatBlock(a.Vitality + b.Vitality, a.Speed + b.Speed,
                a.Power + b.Power, a.Focus + b.Focus);

        /// <summary>Raises every stat to at least <paramref name="floor"/>.</summary>
        public StatBlock ClampedLow(int floor) =>
            new StatBlock(
                Vitality < floor ? floor : Vitality,
                Speed < floor ? floor : Speed,
                Power < floor ? floor : Power,
                Focus < floor ? floor : Focus);

        public bool Equals(StatBlock other) =>
            Vitality == other.Vitality && Speed == other.Speed
            && Power == other.Power && Focus == other.Focus;

        public override bool Equals(object obj) => obj is StatBlock other && Equals(other);

        public override int GetHashCode() =>
            unchecked(((Vitality * 397 ^ Speed) * 397 ^ Power) * 397 ^ Focus);

        public override string ToString() =>
            $"VIT {Vitality} SPD {Speed} POW {Power} FOC {Focus}";
    }

    public static class StatInfo
    {
        /// <summary>Every stat, in a fixed order. See <see cref="ElementInfo.All"/> for why.</summary>
        public static readonly StatKind[] All =
        {
            StatKind.Vitality,
            StatKind.Speed,
            StatKind.Power,
            StatKind.Focus
        };

        public static string NameOf(StatKind stat)
        {
            switch (stat)
            {
                case StatKind.Vitality: return "Vitality";
                case StatKind.Speed: return "Speed";
                case StatKind.Power: return "Power";
                case StatKind.Focus: return "Focus";
                default: return "Unknown";
            }
        }

        /// <summary>What the stat actually does, for the creator screen.</summary>
        public static string DescribeEffect(StatKind stat)
        {
            switch (stat)
            {
                case StatKind.Vitality: return "Health";
                case StatKind.Speed: return "Turn order";
                case StatKind.Power: return "Damage dealt";
                case StatKind.Focus: return "Action points";
                default: return string.Empty;
            }
        }
    }
}
