using Dragoneye.Hex;

namespace Dragoneye.Scenarios
{
    // Declared inside the namespace: out here the bare name Hex would bind to the Dragoneye.Hex
    // namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// The maps the game and its scenarios are played on, written as recipes.
    ///
    /// The Ruins is the arena's own map -- the editor step writes it into the asset from here --
    /// and the scenarios stand on it or on a bare hexagon, so what a scenario proves about walls
    /// it proves about the walls a player will meet.
    /// </summary>
    public static class Maps
    {
        public static readonly Wall Solid = new Wall(WallFlags.Solid);
        public static readonly Wall Low = new Wall(WallFlags.BlocksMovement);
        public static readonly Wall Curtain = new Wall(WallFlags.BlocksSight);

        // The roofless room's corners: quarter-cut tiles. The room lies to A's south-east.
        public static readonly Hex RoomNorthWest = new Hex(0, 3);
        public static readonly Hex RoomNorthEast = new Hex(2, 2);
        public static readonly Hex RoomSouthEast = new Hex(2, 0);
        public static readonly Hex RoomSouthWest = new Hex(0, 1);

        /// <summary>The tile whose North edge is the doorway: the only open side of the room.</summary>
        public static readonly Hex Doorway = new Hex(1, 0);

        /// <summary>The two whole tiles inside the room.</summary>
        public static readonly Hex RoomLower = new Hex(1, 1);
        public static readonly Hex RoomUpper = new Hex(1, 2);

        /// <summary>The tiles the room's long sides run through, split in half.</summary>
        public static readonly Hex WestSide = new Hex(0, 2);
        public static readonly Hex EastSide = new Hex(2, 1);

        /// <summary>The bottom tile of the hedge's three.</summary>
        public static readonly Hex HedgeFoot = new Hex(-3, 0);

        /// <summary>The tile whose North edge the curtain hangs across.</summary>
        public static readonly Hex CurtainFoot = new Hex(-1, -2);

        /// <summary>An open hexagon of grass.</summary>
        public static MapRecipe Open(int radius = 5) => new MapRecipe(radius, Ground.Grass);

        /// <summary>
        /// A hexagon of grass with ruins on it: boulders, a roofless room two tiles wide with its
        /// door on the south side, a waist-high hedge to the west, a hanging cloth to the south,
        /// and a stub of fallen wall.
        ///
        /// The room's corners are quarter-cut tiles and its long sides run through the middles of
        /// tiles, so it is the whole of what walls are for in one place: standing on the inside
        /// half of a cut tile, and not being able to reach the outside half of it.
        /// </summary>
        public static MapRecipe Ruins() =>
            Open(5)
                // Boulders. Something to stand behind, and something the line stops at.
                .Tile(new Hex(3, -3), Ground.Stone)
                .Tile(new Hex(-2, -1), Ground.Stone)
                .Tile(new Hex(1, -4), Ground.Stone)
                .Tile(new Hex(-4, 3), Ground.Stone)
                // The room.
                .Ray(RoomNorthWest, 3, Solid)
                .Ray(RoomNorthWest, 6, Solid)
                .Edge(RoomUpper, HexDirection.North, Solid)   // the top, between the corners
                .Ray(RoomNorthEast, 9, Solid)
                .Ray(RoomNorthEast, 6, Solid)
                .Vertical(EastSide, 1, Solid)                 // the east side
                .Ray(RoomSouthEast, 0, Solid)
                .Ray(RoomSouthEast, 9, Solid)
                // The bottom would run along the North edge of the doorway tile. Left open.
                .Ray(RoomSouthWest, 3, Solid)
                .Ray(RoomSouthWest, 0, Solid)
                .Vertical(WestSide, 1, Solid)                 // the west side
                // A hedge to the west: waist high, three tiles long. Feet stop, eyes and arrows do not.
                .Vertical(HedgeFoot, 3, Low)
                // A hanging cloth across one edge to the south: eyes stop, feet do not.
                .Edge(CurtainFoot, HexDirection.North, Curtain)
                // A stub of fallen wall, alone. It splits nothing; it is just in the way of a line.
                .Ray(new Hex(3, -1), 9, Solid);
    }
}
