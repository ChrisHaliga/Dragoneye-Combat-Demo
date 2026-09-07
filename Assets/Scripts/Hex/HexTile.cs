namespace Dragoneye.Hex
{
    /// <summary>
    /// One tile of the map: its ground, and the walls it keeps the record of.
    ///
    /// Twelve interior rays and the six half-edges this tile owns (its North, NorthEast and
    /// SouthEast edges; the neighbour owns the other six -- see <see cref="TileGeometry"/>). The
    /// walls that block movement decide the tile's <see cref="Areas"/>, which is recomputed
    /// whenever a ray changes, so what a tile is split into is never stale.
    ///
    /// Half-edge walls do not change the areas. That matters for anything that will one day open
    /// a door or knock a wall down mid-fight: a change on a half-edge leaves every creature's
    /// position meaningful, while a change to a ray can merge or split the areas creatures are
    /// standing in and needs them re-placed. Doors belong on half-edges.
    /// </summary>
    public sealed class HexTile
    {
        readonly Wall[] m_Rays = new Wall[TileGeometry.Rays];
        readonly Wall[] m_HalfEdges = new Wall[TileGeometry.HalfEdges];

        public Hex Coordinates { get; }

        public TerrainType Terrain { get; private set; }

        /// <summary>How the tile is cut. <see cref="AreaTable.Whole"/> until a ray blocks movement.</summary>
        public AreaLayout Areas { get; private set; } = AreaTable.Whole;

        internal void ApplyTerrain(TerrainType terrain) => Terrain = terrain;

        public HexTile(Hex coordinates, TerrainType terrain)
        {
            Coordinates = coordinates;
            Terrain = terrain;
        }

        public bool IsWalkable => Terrain == null || Terrain.IsWalkable;

        /// <summary>Whether the ground itself stops a line: a boulder, not a pit.</summary>
        public bool BlocksSight => Terrain != null && Terrain.BlocksSight;

        /// <summary>Steps it costs to enter. One for open ground, more for anything difficult.</summary>
        public int StepsToEnter => Terrain != null ? Terrain.StepsToEnter : 1;

        public Wall Ray(int ray) => m_Rays[TileGeometry.Wrap(ray)];

        /// <summary>The record for a half-edge this tile owns. Ask the map for one it does not.</summary>
        public Wall OwnedHalfEdge(int halfEdge) => m_HalfEdges[TileGeometry.Wrap(halfEdge)];

        /// <summary>The rays that block movement, as a bit per ray. What the areas are derived from.</summary>
        public int MovementRayMask
        {
            get
            {
                var mask = 0;

                for (var i = 0; i < TileGeometry.Rays; i++)
                {
                    if (m_Rays[i].BlocksMovement)
                    {
                        mask |= 1 << i;
                    }
                }

                return mask;
            }
        }

        /// <summary>Whether any wall touches this tile at all, which is what the renderer asks.</summary>
        public bool HasWalls
        {
            get
            {
                for (var i = 0; i < TileGeometry.Rays; i++)
                {
                    if (m_Rays[i].IsSet || m_HalfEdges[i].IsSet)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal void ApplyRay(int ray, Wall wall)
        {
            m_Rays[TileGeometry.Wrap(ray)] = wall;
            Areas = AreaTable.For(MovementRayMask);
        }

        internal void ApplyHalfEdge(int halfEdge, Wall wall) =>
            m_HalfEdges[TileGeometry.Wrap(halfEdge)] = wall;

        public override string ToString() =>
            $"{Coordinates} [{(Terrain != null ? Terrain.DisplayName : "empty")}"
            + (Areas.Count > 1 ? $", {Areas.Count} areas]" : "]");
    }
}
