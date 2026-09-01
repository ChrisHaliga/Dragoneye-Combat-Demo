namespace Dragoneye.Hex
{
    /// <summary>
    /// The contents of one hex.
    ///
    /// Deliberately holds no geometry, no GameObject and no neighbour pointers: neighbours come
    /// from <see cref="Hex.Neighbors"/> plus a map lookup, so topology can never drift out of sync
    /// with coordinates.
    /// </summary>
    public sealed class HexTile
    {
        public Hex Coordinates { get; }

        /// <summary>
        /// Mutated only through <see cref="HexMap.SetTerrain"/>, which raises the notification views
        /// depend on. An accessible setter would let a tile change silently behind the map.
        /// </summary>
        public TerrainType Terrain { get; private set; }

        internal void ApplyTerrain(TerrainType terrain) => Terrain = terrain;

        public HexTile(Hex coordinates, TerrainType terrain)
        {
            Coordinates = coordinates;
            Terrain = terrain;
        }

        public bool IsWalkable => Terrain == null || Terrain.IsWalkable;

        public override string ToString() =>
            $"{Coordinates} [{(Terrain != null ? Terrain.DisplayName : "empty")}]";
    }
}
