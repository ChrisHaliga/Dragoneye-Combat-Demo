using System;

namespace Dragoneye.Hex
{
    /// <summary>
    /// Where a creature stands: which tile, and which area of it.
    ///
    /// A wall can run through a tile and split it into separate footing, so a tile is not a
    /// position any more -- a tile plus an area is. Most tiles have one area, numbered zero, and
    /// for them a cell is the tile. Two cells on one tile are on opposite sides of a wall by
    /// definition: areas are the connected pieces the walls leave, so there is never a way from
    /// one to the other without leaving the tile.
    ///
    /// Distance counts tiles, as it always has, with one exception: two areas of the same tile are
    /// one apart rather than none. They are adjacent across the wall, and nothing at distance zero
    /// can be aimed at.
    /// </summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly Hex Tile;
        public readonly byte Area;

        public Cell(Hex tile, byte area = 0)
        {
            Tile = tile;
            Area = area;
        }

        /// <summary>The whole of a tile that has no walls through it.</summary>
        public static Cell Whole(Hex tile) => new Cell(tile, 0);

        public static int Distance(Cell a, Cell b) =>
            a.Tile == b.Tile ? (a.Area == b.Area ? 0 : 1) : Hex.Distance(a.Tile, b.Tile);

        public bool Equals(Cell other) => Tile == other.Tile && Area == other.Area;

        public override bool Equals(object obj) => obj is Cell other && Equals(other);

        public override int GetHashCode() => unchecked((Tile.GetHashCode() * 31) + Area);

        public static bool operator ==(Cell a, Cell b) => a.Equals(b);

        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);

        public override string ToString() =>
            Area == 0 ? Tile.ToString() : $"{Tile}/{Area}";
    }
}
