using System;
using System.Collections.Generic;

namespace Dragoneye.Hex
{
    /// <summary>
    /// A map written down: a hexagon of ground, the tiles that differ from it, and every wall.
    ///
    /// Terrain is named, not referenced, so a recipe can be written and checked with no asset
    /// in sight -- the editor step, a test scenario and the harness all author the same thing,
    /// and whoever builds it supplies what the names mean. Walls are stored as the map stores
    /// them, a segment and what stands on it; the helpers below know how a straight line lies on
    /// a hex grid and write the segments for it.
    /// </summary>
    public sealed class MapRecipe
    {
        public readonly struct TileEntry
        {
            public readonly Hex Tile;
            public readonly string Terrain;

            public TileEntry(Hex tile, string terrain)
            {
                Tile = tile;
                Terrain = terrain;
            }
        }

        public readonly struct WallEntry
        {
            public readonly WallSegment Segment;
            public readonly Wall Wall;

            public WallEntry(WallSegment segment, Wall wall)
            {
                Segment = segment;
                Wall = wall;
            }
        }

        readonly List<TileEntry> m_Tiles = new List<TileEntry>();
        readonly List<WallEntry> m_Walls = new List<WallEntry>();

        public MapRecipe(int radius, string defaultTerrain)
        {
            Radius = radius < 0 ? 0 : radius;
            DefaultTerrain = defaultTerrain ?? string.Empty;
        }

        /// <summary>Rings of ground out from the centre.</summary>
        public int Radius { get; }

        /// <summary>What the ground is where nothing says otherwise.</summary>
        public string DefaultTerrain { get; }

        /// <summary>Tiles whose ground differs, or that lie outside the radius.</summary>
        public IReadOnlyList<TileEntry> Tiles => m_Tiles;

        public IReadOnlyList<WallEntry> Walls => m_Walls;

        public MapRecipe Tile(Hex hex, string terrain)
        {
            m_Tiles.Add(new TileEntry(hex, terrain));
            return this;
        }

        public MapRecipe Wall(WallSegment segment, Wall wall)
        {
            m_Walls.Add(new WallEntry(segment, wall));
            return this;
        }

        public MapRecipe Ray(Hex hex, int ray, Wall wall) => Wall(WallSegment.Ray(hex, ray), wall);

        public MapRecipe HalfEdge(Hex hex, int halfEdge, Wall wall) =>
            Wall(WallSegment.HalfEdge(hex, halfEdge), wall);

        /// <summary>Both halves of an edge. The usual way to wall one.</summary>
        public MapRecipe Edge(Hex hex, HexDirection edge, Wall wall)
        {
            WallSegment.Edge(hex, edge, out var first, out var second);
            return Wall(first, wall).Wall(second, wall);
        }

        /// <summary>
        /// A vertical wall up a column of tiles, midpoint to midpoint: rays 0 and 6 on each. The
        /// north midpoint of one tile is the south midpoint of the next, so the line is continuous.
        /// </summary>
        public MapRecipe Vertical(Hex bottom, int tiles, Wall wall)
        {
            for (var i = 0; i < tiles; i++)
            {
                var hex = new Hex(bottom.Q, bottom.R + i);
                Ray(hex, 0, wall);
                Ray(hex, 6, wall);
            }

            return this;
        }

        /// <summary>
        /// A horizontal wall eastward from a tile's centre: corner to corner through the tile
        /// (rays 3 and 9), then along the flat North edge of the tile below-right, then through
        /// the next tile on the row, and so on. Each piece is one unit of length, so a run of 2n
        /// pieces spans n tiles of the row.
        /// </summary>
        public MapRecipe Horizontal(Hex start, int pieces, Wall wall)
        {
            for (var i = 0; i < pieces; i++)
            {
                var hex = new Hex(start.Q + i, start.R - (i + 1) / 2);

                if (i % 2 == 0)
                {
                    Ray(hex, 3, wall);
                    Ray(hex, 9, wall);
                }
                else
                {
                    Edge(hex, HexDirection.North, wall);
                }
            }

            return this;
        }

        /// <summary>
        /// The map this describes.
        ///
        /// The one builder. The authored asset and every test go through here, so what a recipe
        /// means is decided once. <paramref name="terrainOf"/> says what a name refers to; null
        /// is open ground with no terrain asset behind it, which a test is content with.
        /// </summary>
        public HexMap Build(HexLayout layout, Func<string, TerrainType> terrainOf)
        {
            var tiles = new Dictionary<Hex, HexTile>();
            var ground = terrainOf != null ? terrainOf(DefaultTerrain) : null;

            foreach (var hex in Hex.Range(Hex.Zero, Radius))
            {
                tiles[hex] = new HexTile(hex, ground);
            }

            foreach (var entry in m_Tiles)
            {
                var terrain = terrainOf != null ? terrainOf(entry.Terrain) : null;
                tiles[entry.Tile] = new HexTile(entry.Tile, terrain != null ? terrain : ground);
            }

            var map = new HexMap(layout, tiles.Values);

            foreach (var entry in m_Walls)
            {
                map.SetWall(entry.Segment, entry.Wall);
            }

            return map;
        }
    }
}
