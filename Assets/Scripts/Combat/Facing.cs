using System;

namespace Dragoneye.Combat
{
    /// <summary>
    /// Which of six ways a creature is turned.
    ///
    /// Its own type rather than the grid's direction enum, because Combat references nothing: which
    /// sectors are a flank is a rule and has to be decidable without a grid. The two agree by index
    /// -- both are numbered clockwise from north -- and the conversion is a cast the grid layer
    /// makes. That agreement is asserted by a test rather than left to a comment, because a
    /// reordering on either side would turn every flank check into a lie without changing a line of
    /// this file.
    ///
    /// Stored, never derived. DE-006 is explicit: facing is a consequence of the last thing a
    /// creature did, so it is state that moving and attacking write, not something recomputed from
    /// where anybody is standing.
    /// </summary>
    public readonly struct Facing : IEquatable<Facing>
    {
        /// <summary>How many ways there are to be turned.</summary>
        public const int Count = 6;

        /// <summary>Clockwise from north, matching the grid's direction numbering.</summary>
        public readonly int Index;

        Facing(int index)
        {
            Index = index;
        }

        /// <summary>Whichever of the six this is, wrapping anything outside the range.</summary>
        public static Facing Of(int index) => new Facing(((index % Count) + Count) % Count);

        /// <summary>North. What a creature faces before anything has turned it.</summary>
        public static Facing Default => new Facing(0);

        public Facing Opposite => Of(Index + (Count / 2));

        public Facing Turned(int steps) => Of(Index + steps);

        public bool Equals(Facing other) => Index == other.Index;

        public override bool Equals(object obj) => obj is Facing other && Equals(other);

        public override int GetHashCode() => Index;

        public override string ToString() => $"Facing {Index}";

        public static bool operator ==(Facing a, Facing b) => a.Index == b.Index;

        public static bool operator !=(Facing a, Facing b) => a.Index != b.Index;
    }

    /// <summary>
    /// What a facing is worth: which arrivals it covers, and which it does not.
    ///
    /// Three of six sectors are the front -- the way the creature is turned and the sector to
    /// either side of it -- leaving three that are not. DE-006 names that split as the obvious
    /// start, and it is the one that makes turning to attack a real cost: whichever way you turn,
    /// you have opened exactly half the board on yourself.
    /// </summary>
    public static class FacingRules
    {
        /// <summary>Sectors a creature covers: the way it is turned, and one either side.</summary>
        public const int FrontSectors = 3;

        /// <summary>
        /// How many sectors apart two facings are, the short way round.
        ///
        /// Zero when they are the same, three when they are opposed, and never more -- going the
        /// long way round is a different number for the same angle, and a rule that could pick
        /// either is a rule two machines can disagree about.
        /// </summary>
        public static int Separation(Facing a, Facing b)
        {
            var raw = ((b.Index - a.Index) % Facing.Count + Facing.Count) % Facing.Count;
            return raw > Facing.Count / 2 ? Facing.Count - raw : raw;
        }

        /// <summary>
        /// Whether an attack arriving on this bearing lands outside the defender's front.
        ///
        /// <paramref name="bearing"/> is the direction from the defender toward the attacker: which
        /// way the blow is coming from, not which way anybody is walking.
        /// </summary>
        public static bool IsFlank(Facing facing, Facing bearing) =>
            Separation(facing, bearing) > FrontSectors / 2;
    }
}
