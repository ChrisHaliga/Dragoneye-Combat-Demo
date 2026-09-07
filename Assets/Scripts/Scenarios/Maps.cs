using Dragoneye.Hex;

namespace Dragoneye.Scenarios
{
    // Declared inside the namespace: out here the bare name Hex would bind to the Dragoneye.Hex
    // namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// The maps the game and its scenarios are played on, written as recipes.
    ///
    /// Every one of them is a recipe rather than an asset, so a scenario can stand on it, the
    /// harness can build it, and the editor step can write it to disk from the same lines. The
    /// three the host can pick from are symmetric about the north-south axis, by construction:
    /// half is written and <see cref="Symmetry.MirroredEastWest"/> writes the other half.
    ///
    /// Coordinates. Tiles are flat-topped, at <c>x = 1.5q</c> and, counting in half-rows,
    /// <c>h = q + 2r</c> (one half-row is 0.866 of a tile radius). A vertical wall runs up a
    /// column of tiles; a horizontal one runs along a half-row line, through the middles of the
    /// tiles centred on it and along the top edges of the tiles just below it.
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
        /// The field: the original hexagon with no walls on it, and four boulders placed in
        /// mirror so neither side starts with the better cover.
        /// </summary>
        public static MapRecipe Field() =>
            Symmetry.MirroredEastWest(Open(5)
                .Tile(new Hex(-2, -1), Ground.Stone)
                .Tile(new Hex(-2, 3), Ground.Stone));

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

        // ---------- the mansion ----------

        /// <summary>The tile outside the mansion's front door, and the one outside its back door.</summary>
        public static readonly Hex MansionFrontStep = new Hex(0, -3);
        public static readonly Hex MansionBackStep = new Hex(0, 3);

        /// <summary>The middle of the great hall, and the two ends of it inside the doors.</summary>
        public static readonly Hex MansionHall = new Hex(0, 0);
        public static readonly Hex MansionHallSouth = new Hex(0, -2);
        public static readonly Hex MansionHallNorth = new Hex(0, 2);

        /// <summary>A whole tile inside each kind of room, on the west side. The east mirrors them.</summary>
        public static readonly Hex MansionWestGallery = new Hex(-2, 2);
        public static readonly Hex MansionWestStudy = new Hex(-2, 0);
        public static readonly Hex MansionWestCellar = new Hex(-2, -1);

        /// <summary>Open ground outside the west wall.</summary>
        public static readonly Hex MansionWestLawn = new Hex(-5, 3);

        /// <summary>
        /// A mansion: an outer wall six tiles across with a door at the front and the back, a
        /// great hall running between them, a cross corridor, two galleries at the back, and
        /// two small rooms on each side of the front. Symmetric east to west.
        ///
        /// The outer wall stands on the columns at x = ±6 and the half-rows h = ±5; the hall is
        /// walled at x = ±1.5; the corridor at h = ±1; the front rooms are divided at h = -3.
        /// Doors are gaps: the hall walls stop a half-row short of the outer wall at both ends,
        /// and the corridor walls stop a tile short of the hall.
        /// </summary>
        public static MapRecipe Mansion()
        {
            var west = Open(6)
                // The west wall, x = -6, from h = -5 to h = 5.
                .Vertical(new Hex(-4, 0), 5, Solid)
                // The back wall, h = 5, from the corner to the back door.
                .HalfEdge(new Hex(-4, 4), 0, Solid)
                .Ray(new Hex(-3, 4), 3, Solid).Ray(new Hex(-3, 4), 9, Solid)
                .Edge(new Hex(-2, 3), HexDirection.North, Solid)
                .Ray(new Hex(-1, 3), 3, Solid).Ray(new Hex(-1, 3), 9, Solid)
                // The front wall, h = -5, from the corner to the front door.
                .HalfEdge(new Hex(-4, -1), 0, Solid)
                .Ray(new Hex(-3, -1), 3, Solid).Ray(new Hex(-3, -1), 9, Solid)
                .Edge(new Hex(-2, -2), HexDirection.North, Solid)
                .Ray(new Hex(-1, -2), 3, Solid).Ray(new Hex(-1, -2), 9, Solid)
                // The hall's west wall, x = -1.5: two pieces, with the corridor's mouth between
                // them and a doorway at each end where it stops short of the outer wall.
                .Vertical(new Hex(-1, -1), 1, Solid)
                .Vertical(new Hex(-1, 2), 1, Solid)
                // The cross corridor, h = -1 to h = 1, from the west wall to a tile short of the hall.
                .HalfEdge(new Hex(-4, 2), 0, Solid)
                .Ray(new Hex(-3, 2), 3, Solid).Ray(new Hex(-3, 2), 9, Solid)
                .Edge(new Hex(-2, 1), HexDirection.North, Solid)
                .HalfEdge(new Hex(-4, 1), 0, Solid)
                .Ray(new Hex(-3, 1), 3, Solid).Ray(new Hex(-3, 1), 9, Solid)
                .Edge(new Hex(-2, 0), HexDirection.North, Solid)
                // The wall between the two front rooms, h = -3, from the west wall to the hall.
                .HalfEdge(new Hex(-4, 0), 0, Solid)
                .Ray(new Hex(-3, 0), 3, Solid).Ray(new Hex(-3, 0), 9, Solid)
                .Edge(new Hex(-2, -1), HexDirection.North, Solid)
                .Ray(new Hex(-1, -1), 9, Solid);

            return Symmetry.MirroredEastWest(west);
        }

        // ---------- the islands ----------

        /// <summary>The middle of each island.</summary>
        public static readonly Hex WestIsland = new Hex(-4, 2);
        public static readonly Hex EastIsland = new Hex(4, -2);

        /// <summary>The three tiles of the bridge, west to east.</summary>
        public static readonly Hex BridgeWest = new Hex(-1, 1);
        public static readonly Hex BridgeMiddle = new Hex(0, 0);
        public static readonly Hex BridgeEast = new Hex(1, 0);

        /// <summary>Where each island meets the bridge.</summary>
        public static readonly Hex WestLanding = new Hex(-2, 1);
        public static readonly Hex EastLanding = new Hex(2, -1);

        /// <summary>How far an island reaches from its middle.</summary>
        public const int IslandRadius = 2;

        /// <summary>
        /// Two islands in open water, joined by a bridge one tile wide. Each island has a short
        /// wall north and south of its middle and a hedge at its landing, in mirror.
        ///
        /// Water is not stood on and not seen through by nothing: arrows cross it and feet do
        /// not, so the bridge is the only way over and the shore is the only place to shoot from.
        /// </summary>
        public static MapRecipe Islands()
        {
            var west = new MapRecipe(6, Ground.Water);

            foreach (var hex in Hex.Range(WestIsland, IslandRadius))
            {
                west.Tile(hex, Ground.Grass);
            }

            west.Tile(BridgeWest, Ground.Grass)
                .Tile(BridgeMiddle, Ground.Grass)
                // Cover north and south of the middle, leaving the middle row open.
                .Vertical(new Hex(-4, 3), 1, Solid)
                .Vertical(new Hex(-4, 1), 1, Solid)
                // A hedge along the top of the landing. Feet stop; arrows do not.
                .Edge(WestLanding, HexDirection.North, Low);

            return Symmetry.MirroredEastWest(west);
        }
    }

    /// <summary>
    /// Mirrors a recipe east to west.
    ///
    /// A tile at (q, r) sits at x = 1.5q and half-row q + 2r; its mirror keeps the half-row and
    /// flips x, which is (-q, q + r). A ray at angle a becomes one at -a, so ray i becomes
    /// ray 12 - i; the half-edge closing wedge i becomes the one closing wedge 11 - i.
    /// </summary>
    public static class Symmetry
    {
        public static Hex Mirror(Hex hex) => new Hex(-hex.Q, hex.Q + hex.R);

        public static int MirrorRay(int ray) => TileGeometry.Wrap(-ray);

        public static int MirrorHalfEdge(int halfEdge) => TileGeometry.Wrap(11 - halfEdge);

        public static WallSegment Mirror(WallSegment segment) =>
            segment.IsRay
                ? WallSegment.Ray(Mirror(segment.Tile), MirrorRay(segment.Index))
                : WallSegment.HalfEdge(Mirror(segment.Tile), MirrorHalfEdge(segment.Index));

        /// <summary>Everything the recipe says, and its mirror image.</summary>
        public static MapRecipe MirroredEastWest(MapRecipe half)
        {
            var whole = new MapRecipe(half.Radius, half.DefaultTerrain);

            // A tile on the axis, or a wall along it, is its own mirror and is written once.
            foreach (var tile in half.Tiles)
            {
                whole.Tile(tile.Tile, tile.Terrain);

                if (Mirror(tile.Tile) != tile.Tile)
                {
                    whole.Tile(Mirror(tile.Tile), tile.Terrain);
                }
            }

            foreach (var wall in half.Walls)
            {
                whole.Wall(wall.Segment, wall.Wall);

                if (Mirror(wall.Segment) != wall.Segment)
                {
                    whole.Wall(Mirror(wall.Segment), wall.Wall);
                }
            }

            return whole;
        }
    }
}
