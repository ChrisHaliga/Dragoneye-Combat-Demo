using System;

namespace Dragoneye.Combat
{
    /// <summary>
    /// Action points, stored in half-units.
    ///
    /// Moving a tile costs half a point and a skill costs whole points, so the smallest quantity the
    /// game deals in is a half. Storing halves as integers means every sum is exact: a float budget
    /// would drift, and two peers adding the same costs in a different order could disagree about
    /// whether a creature can still afford to act.
    ///
    /// The type exists so the doubling happens here instead of at forty call sites, each of which
    /// would be a chance to forget.
    /// </summary>
    public readonly struct Ap : IEquatable<Ap>, IComparable<Ap>
    {
        /// <summary>Half-units in one whole action point.</summary>
        public const int UnitsPerPoint = 2;

        public static readonly Ap Zero = new Ap(0);

        /// <summary>One tile of movement.</summary>
        public static readonly Ap Step = new Ap(1);

        /// <summary>The raw count of half-units. This is what gets replicated and compared.</summary>
        public readonly int Units;

        Ap(int units) => Units = units;

        /// <summary>Whole action points, as a player reads them. Rounds a dangling half down.</summary>
        public int Whole => Units / UnitsPerPoint;

        /// <summary>True when there is an unpaired half-unit, so the display needs a fraction.</summary>
        public bool HasHalf => Units % UnitsPerPoint != 0;

        public bool IsZero => Units == 0;

        public static Ap FromUnits(int units) => new Ap(units);

        public static Ap FromWhole(int points) => new Ap(points * UnitsPerPoint);

        public static Ap operator +(Ap a, Ap b) => new Ap(a.Units + b.Units);

        /// <summary>Never goes below zero: spending more than you hold leaves you at nothing.</summary>
        public static Ap operator -(Ap a, Ap b) =>
            new Ap(a.Units - b.Units < 0 ? 0 : a.Units - b.Units);

        public static Ap operator *(Ap a, int scale) => new Ap(a.Units * (scale < 0 ? 0 : scale));

        public static bool operator >=(Ap a, Ap b) => a.Units >= b.Units;

        public static bool operator <=(Ap a, Ap b) => a.Units <= b.Units;

        public static bool operator >(Ap a, Ap b) => a.Units > b.Units;

        public static bool operator <(Ap a, Ap b) => a.Units < b.Units;

        public static bool operator ==(Ap a, Ap b) => a.Units == b.Units;

        public static bool operator !=(Ap a, Ap b) => a.Units != b.Units;

        public bool Equals(Ap other) => Units == other.Units;

        public override bool Equals(object obj) => obj is Ap other && Equals(other);

        public override int GetHashCode() => Units;

        public int CompareTo(Ap other) => Units.CompareTo(other.Units);

        /// <summary>"3" or "3.5" -- the only place a half-unit becomes a fraction.</summary>
        public override string ToString() => HasHalf ? $"{Whole}.5" : Whole.ToString();
    }
}
