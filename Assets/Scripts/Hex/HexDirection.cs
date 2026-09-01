namespace Dragoneye.Hex
{
    /// <summary>
    /// The six neighbours of a flat-top hex, clockwise from north.
    ///
    /// Flat-top hexes have a flat edge facing north and a point facing east, so neighbours sit at
    /// compass bearings 0, 60, 120, 180, 240 and 300 degrees. (Pointy-top hexes would instead have
    /// neighbours due east and west.)
    /// </summary>
    public enum HexDirection
    {
        North = 0,
        NorthEast = 1,
        SouthEast = 2,
        South = 3,
        SouthWest = 4,
        NorthWest = 5
    }

    public static class HexDirectionExtensions
    {
        /// <summary>The direction pointing back the way you came.</summary>
        public static HexDirection Opposite(this HexDirection direction) =>
            (HexDirection)(((int)direction + 3) % 6);

        public static HexDirection RotateClockwise(this HexDirection direction, int steps = 1) =>
            (HexDirection)(((int)direction + (steps % 6) + 6) % 6);
    }
}
