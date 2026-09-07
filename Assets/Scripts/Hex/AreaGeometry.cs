namespace Dragoneye.Hex
{
    /// <summary>
    /// Where an area is, and which way one is from another.
    ///
    /// The whole of this is integer arithmetic on <see cref="TileGeometry"/>'s scaled frame. The
    /// bearing it produces decides which attacks are flanks, on the server and on every client
    /// that previews one, so it is computed the same way on every machine or not at all --
    /// which is why nothing here goes near a float or a trigonometric function.
    /// </summary>
    public static class AreaGeometry
    {
        /// <summary>
        /// The centre of an area, in scaled tile-local units.
        ///
        /// The mean of its wedges' centroids. For a tile with one area that is the origin, which
        /// is what everything used before tiles could be split.
        /// </summary>
        public static void Centre(AreaLayout layout, byte area, out long x, out long z)
        {
            x = 0;
            z = 0;

            if (layout == null || layout.Count <= 1)
            {
                return;
            }

            long sumX = 0, sumZ = 0;
            var count = 0;

            for (var wedge = 0; wedge < TileGeometry.Wedges; wedge++)
            {
                if (layout.AreaOf(wedge) != area)
                {
                    continue;
                }

                TileGeometry.WedgeCentroid(wedge, out var wx, out var wz);
                sumX += wx;
                sumZ += wz;
                count++;
            }

            if (count > 0)
            {
                x = sumX / count;
                z = sumZ / count;
            }
        }

        /// <summary>Where a cell is on the map, in scaled units: its tile's centre plus its area's.</summary>
        public static void Position(HexMap map, Cell cell, out long x, out long z)
        {
            TileGeometry.TileCentre(cell.Tile, out x, out z);

            if (map != null && map.TryGetTile(cell.Tile, out var tile))
            {
                Centre(tile.Areas, cell.Area, out var ax, out var az);
                x += ax;
                z += az;
            }
        }

        /// <summary>
        /// Which of the six directions one cell lies in from another.
        ///
        /// For two tiles no wall runs through this is exactly <see cref="Hex.DirectionTo"/>, kept
        /// so nothing that worked before a tile could be split answers differently now. Once a
        /// split tile is involved -- either end, whichever area -- the answer comes from the two
        /// centres, quantised by <see cref="TileGeometry.Bearing"/>, and its tiebreak: a bearing
        /// exactly on a boundary belongs to the lower-numbered direction. A cell has no direction
        /// to itself; that answers North, as it always has.
        /// </summary>
        public static HexDirection Direction(HexMap map, Cell from, Cell to)
        {
            if (from == to)
            {
                return HexDirection.North;
            }

            if (IsWhole(map, from.Tile) && IsWhole(map, to.Tile))
            {
                return Hex.DirectionTo(from.Tile, to.Tile);
            }

            Position(map, from, out var fx, out var fz);
            Position(map, to, out var tx, out var tz);

            var dx = tx - fx;
            var dz = tz - fz;

            return dx == 0 && dz == 0 ? HexDirection.North : TileGeometry.Bearing(dx, dz);
        }

        /// <summary>Whether no wall runs through this tile, so its one area's centre is its own.</summary>
        static bool IsWhole(HexMap map, Hex hex) =>
            map == null || !map.TryGetTile(hex, out var tile) || tile.MovementRayMask == 0;
    }
}
