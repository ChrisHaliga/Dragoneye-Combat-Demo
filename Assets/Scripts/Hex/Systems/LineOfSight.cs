using System.Collections.Generic;

namespace Dragoneye.Hex.Systems
{
    /// <summary>What a line between two cells meets on the way.</summary>
    public enum LineVerdict
    {
        /// <summary>Nothing in the way.</summary>
        Clear,

        /// <summary>Something low in the way. It can be shot over, at a price.</summary>
        Obstructed,

        /// <summary>Something in the way that cannot be seen through. No shot, no swing.</summary>
        Blocked
    }

    /// <summary>
    /// The static half of a line: the walls and the ground, traced through the map.
    ///
    /// The line runs from the centre of one area to the centre of another. It is walked tile by
    /// tile along <see cref="Hex.Line"/>, and in every tile it visits -- the endpoints included --
    /// it is tested against the twelve rays; at every boundary it crosses, against the half-edge
    /// it crosses. A ray or half-edge that blocks sight ends the walk with <see cref="LineVerdict.Blocked"/>;
    /// one that blocks only movement is a low wall, and marks the line obstructed and carries on.
    /// Ground that blocks sight on an intermediate tile blocks; impassable ground that does not is
    /// obstruction.
    ///
    /// Bodies are the other half, and they are the game's to count: which creature stands where is
    /// not the map's business. See <c>LineOfFire</c>.
    ///
    /// Integer arithmetic throughout, on <see cref="TileGeometry"/>'s scaled frame, for the same
    /// reason a bearing is: the server refuses a shot on this verdict and every client previews
    /// one, and they must agree.
    /// </summary>
    public static class LineOfSight
    {
        /// <summary>
        /// The tiles a line passes over between its ends, neither end included: everywhere a
        /// body could be in its way. Nothing for two cells a step apart, or two on one tile.
        /// </summary>
        public static void TilesBetween(Cell from, Cell to, List<Hex> into)
        {
            into.Clear();

            if (Cell.Distance(from, to) < 2)
            {
                return;
            }

            foreach (var tile in Hex.Line(from.Tile, to.Tile))
            {
                if (tile != from.Tile && tile != to.Tile)
                {
                    into.Add(tile);
                }
            }
        }

        public static LineVerdict Verdict(IGridRules grid, Cell from, Cell to)
        {
            if (grid == null || grid.Map == null || from == to)
            {
                return LineVerdict.Clear;
            }

            var map = grid.Map;

            AreaGeometry.Position(map, from, out var ax, out var az);
            AreaGeometry.Position(map, to, out var bx, out var bz);

            var obstructed = false;

            // Every tile the line passes through, in order, with the segment expressed in that
            // tile's own frame so the ray tests can use the tile's geometry directly.
            Hex? previous = null;

            foreach (var tile in Hex.Line(from.Tile, to.Tile))
            {
                if (previous.HasValue)
                {
                    var crossing = CrossHalfEdge(map, previous.Value, tile, ax, az, bx, bz);

                    if (crossing == LineVerdict.Blocked)
                    {
                        return LineVerdict.Blocked;
                    }

                    obstructed |= crossing == LineVerdict.Obstructed;
                }

                if (previous.HasValue && tile != to.Tile && map.TryGetTile(tile, out var ground))
                {
                    if (ground.BlocksSight)
                    {
                        return LineVerdict.Blocked;
                    }

                    obstructed |= !ground.IsWalkable;
                }

                var rays = CrossRays(map, tile, ax, az, bx, bz);

                if (rays == LineVerdict.Blocked)
                {
                    return LineVerdict.Blocked;
                }

                obstructed |= rays == LineVerdict.Obstructed;
                previous = tile;
            }

            return obstructed ? LineVerdict.Obstructed : LineVerdict.Clear;
        }

        /// <summary>
        /// The rays of one tile the segment crosses, and the worst of their walls.
        ///
        /// A segment through the tile's centre crosses every ray at their common root, and so is
        /// stopped by any wall on any of them -- which is what a wall through the middle of a tile
        /// should do to a line through the middle of it. Otherwise a ray is crossed when the
        /// segment separates its two ends and it separates the segment's; touching counts, so a
        /// line that grazes the end of a wall is still a line that met it.
        /// </summary>
        static LineVerdict CrossRays(HexMap map, Hex tile, long ax, long az, long bx, long bz)
        {
            if (!map.TryGetTile(tile, out var ground) || !ground.HasWalls)
            {
                return LineVerdict.Clear;
            }

            TileGeometry.TileCentre(tile, out var cx, out var cz);

            // The segment in this tile's frame.
            var sx = ax - cx;
            var sz = az - cz;
            var ex = bx - cx;
            var ez = bz - cz;

            var throughCentre = Orient(sx, sz, ex, ez, 0, 0) == 0
                && Between(sx, sz, ex, ez, 0, 0);

            var worst = LineVerdict.Clear;

            for (var ray = 0; ray < TileGeometry.Rays; ray++)
            {
                var wall = ground.Ray(ray);

                if (!wall.IsSet)
                {
                    continue;
                }

                TileGeometry.RayEnd(ray, out var rx, out var rz);

                var crossed = throughCentre
                    || (Orient(sx, sz, ex, ez, 0, 0) * Orient(sx, sz, ex, ez, rx, rz) <= 0
                        && Orient(0, 0, rx, rz, sx, sz) * Orient(0, 0, rx, rz, ex, ez) <= 0);

                if (!crossed)
                {
                    continue;
                }

                if (wall.BlocksSight)
                {
                    return LineVerdict.Blocked;
                }

                if (wall.BlocksMovement)
                {
                    worst = LineVerdict.Obstructed;
                }
            }

            return worst;
        }

        /// <summary>
        /// The half-edge the segment crosses between two consecutive tiles, and its wall.
        ///
        /// The edge between them is known from their direction; which half is decided by which
        /// side of the segment the edge's midpoint lies on. A segment through the midpoint exactly
        /// touches both halves, and the worse of the two counts.
        /// </summary>
        static LineVerdict CrossHalfEdge(HexMap map, Hex fromTile, Hex toTile, long ax, long az,
            long bx, long bz)
        {
            var direction = Hex.DirectionTo(fromTile, toTile);
            TileGeometry.HalvesOf(direction, out var first, out var second);

            TileGeometry.TileCentre(fromTile, out var cx, out var cz);

            // The corner shared by the two halves (the ray between them), and the ends of the edge.
            TileGeometry.RayEnd(first, out var c0x, out var c0z);       // start corner of the first half
            TileGeometry.RayEnd(second, out var mx, out var mz);        // the midpoint
            TileGeometry.RayEnd(second + 1, out var c1x, out var c1z);  // end corner of the second half

            var sx = ax - cx;
            var sz = az - cz;
            var ex = bx - cx;
            var ez = bz - cz;

            var midSide = Orient(sx, sz, ex, ez, mx, mz);
            var firstSide = Orient(sx, sz, ex, ez, c0x, c0z);
            var secondSide = Orient(sx, sz, ex, ez, c1x, c1z);

            var worst = LineVerdict.Clear;

            // The first half is crossed when its two ends are on different sides of the segment
            // (or one is on it); likewise the second.
            if (firstSide * midSide <= 0)
            {
                worst = Worse(worst, map.HalfEdge(fromTile, first));
            }

            if (secondSide * midSide <= 0)
            {
                worst = Worse(worst, map.HalfEdge(fromTile, second));
            }

            return worst;
        }

        static LineVerdict Worse(LineVerdict current, Wall wall)
        {
            if (wall.BlocksSight || current == LineVerdict.Blocked)
            {
                return LineVerdict.Blocked;
            }

            return wall.BlocksMovement ? LineVerdict.Obstructed : current;
        }

        /// <summary>Sign of where (px, pz) lies relative to the directed segment a->b. Zero on it.</summary>
        static long Orient(long ax, long az, long bx, long bz, long px, long pz)
        {
            var value = (bx - ax) * (pz - az) - (bz - az) * (px - ax);
            return value > 0 ? 1 : value < 0 ? -1 : 0;
        }

        /// <summary>Whether a collinear point lies within the segment's extent.</summary>
        static bool Between(long ax, long az, long bx, long bz, long px, long pz) =>
            px >= (ax < bx ? ax : bx) && px <= (ax < bx ? bx : ax)
            && pz >= (az < bz ? az : bz) && pz <= (az < bz ? bz : az);
    }
}
