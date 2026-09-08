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
        /// How far from the tile's centre an area's anchor sits, in scaled units.
        ///
        /// Half the tile radius: far enough out that a creature drawn there is clear of the rays
        /// that cut its piece off, near enough in that it is still plainly on this tile.
        /// </summary>
        public const long Reach = TileGeometry.Scale / 2;

        /// <summary>
        /// The centre of an area, in scaled tile-local units.
        ///
        /// The mean of its wedges' centroids gives the direction the piece lies in; the anchor is
        /// then pushed out to <see cref="Reach"/> along it. For a tile with one area that is the
        /// origin, which is what everything used before tiles could be split.
        ///
        /// **The mean on its own is not far enough out.** A three-quarter piece averages nine
        /// centroids spread around three sides, and they very nearly cancel: the answer lands
        /// almost exactly on the tile's centre, which is the one point on that tile touching both
        /// walls of the quarter cut out of it. A creature standing there is drawn inside its own
        /// wall. Keeping the direction and fixing the distance puts it in the middle of the ground
        /// it actually has, and does it the same way for a quarter, a half and a three-quarter.
        ///
        /// Integer throughout, like everything else here: this decides bearings, so the server and
        /// every client must reach the same answer bit for bit.
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

            if (count == 0)
            {
                return;
            }

            var length = Root(sumX * sumX + sumZ * sumZ);

            if (length == 0)
            {
                // Wedges that cancel exactly. Nothing sensible to point at, so the centre stands.
                return;
            }

            x = sumX * Reach / length;
            z = sumZ * Reach / length;
        }

        /// <summary>
        /// The integer square root: the largest whole number whose square is no greater than
        /// <paramref name="value"/>.
        ///
        /// Written out rather than taken from Math.Sqrt because a double would answer this
        /// differently on different hardware, and the answer decides flanks.
        /// </summary>
        static long Root(long value)
        {
            if (value <= 0)
            {
                return 0;
            }

            var guess = value;
            var next = (guess + 1) / 2;

            while (next < guess)
            {
                guess = next;
                next = (guess + value / guess) / 2;
            }

            return guess;
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
