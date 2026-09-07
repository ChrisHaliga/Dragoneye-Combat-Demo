using System;

namespace Dragoneye.Hex
{
    /// <summary>What a wall stops. The two are independent: a waist-high wall stops feet and not eyes.</summary>
    [Flags]
    public enum WallFlags : byte
    {
        None = 0,
        BlocksMovement = 1,
        BlocksSight = 2,
        Solid = BlocksMovement | BlocksSight
    }

    /// <summary>
    /// One segment of wall: what it stops, and how much it takes to bring down.
    ///
    /// A value, so a map is a table of these rather than a scene full of objects. Integrity is
    /// carried now and read by nothing: no skill can break a wall yet, but a wall that will one
    /// day be breakable is data that already has a number on it. Zero means it cannot be.
    /// </summary>
    public readonly struct Wall : IEquatable<Wall>
    {
        public static readonly Wall None = default;

        public readonly WallFlags Flags;

        /// <summary>Hits it takes to breach. Zero: it cannot be breached.</summary>
        public readonly ushort Integrity;

        public Wall(WallFlags flags, ushort integrity = 0)
        {
            Flags = flags;
            Integrity = integrity;
        }

        public bool IsSet => Flags != WallFlags.None;

        public bool BlocksMovement => (Flags & WallFlags.BlocksMovement) != 0;

        public bool BlocksSight => (Flags & WallFlags.BlocksSight) != 0;

        public bool Breakable => Integrity > 0;

        public bool Equals(Wall other) => Flags == other.Flags && Integrity == other.Integrity;

        public override bool Equals(object obj) => obj is Wall other && Equals(other);

        public override int GetHashCode() => unchecked(((int)Flags * 397) ^ Integrity);

        public override string ToString() => IsSet ? $"Wall({Flags}, {Integrity})" : "no wall";
    }
}
