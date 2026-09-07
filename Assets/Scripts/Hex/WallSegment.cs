using System;

namespace Dragoneye.Hex
{
    /// <summary>
    /// One place a wall can be: a ray through a tile, or a half of one of its edges.
    ///
    /// The address and nothing else. A map answers what stands there; a recipe says what to put
    /// there; a message across the wire says which one changed. A half-edge named from either of
    /// the two tiles that share it is the same wall, and the map resolves which tile keeps it.
    /// </summary>
    public readonly struct WallSegment : IEquatable<WallSegment>
    {
        public readonly Hex Tile;

        /// <summary>The ray, or the half-edge, clockwise from North. See <see cref="TileGeometry"/>.</summary>
        public readonly int Index;

        /// <summary>A ray through the tile, rather than a half of its edge.</summary>
        public readonly bool IsRay;

        WallSegment(Hex tile, int index, bool isRay)
        {
            Tile = tile;
            Index = TileGeometry.Wrap(index);
            IsRay = isRay;
        }

        public static WallSegment Ray(Hex tile, int ray) => new WallSegment(tile, ray, true);

        public static WallSegment HalfEdge(Hex tile, int halfEdge) => new WallSegment(tile, halfEdge, false);

        /// <summary>The two halves of an edge, in clockwise order.</summary>
        public static void Edge(Hex tile, HexDirection edge, out WallSegment first, out WallSegment second)
        {
            TileGeometry.HalvesOf(edge, out var a, out var b);
            first = HalfEdge(tile, a);
            second = HalfEdge(tile, b);
        }

        public bool Equals(WallSegment other) =>
            Tile == other.Tile && Index == other.Index && IsRay == other.IsRay;

        public override bool Equals(object obj) => obj is WallSegment other && Equals(other);

        public override int GetHashCode() =>
            unchecked((Tile.GetHashCode() * 397 + Index) * 2 + (IsRay ? 1 : 0));

        public static bool operator ==(WallSegment a, WallSegment b) => a.Equals(b);

        public static bool operator !=(WallSegment a, WallSegment b) => !a.Equals(b);

        public override string ToString() => $"{(IsRay ? "ray" : "half-edge")} {Index} of {Tile}";
    }
}
