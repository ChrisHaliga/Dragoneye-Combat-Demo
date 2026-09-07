using System.Collections.Generic;

namespace Dragoneye.Hex.Systems
{
    /// <summary>
    /// What the map says about moving and seeing: where a creature can step from a cell, what a
    /// step costs, and which edges a line passes.
    ///
    /// The seam DE-000 named and nothing had. Every question that used to be put to the coordinate
    /// -- "what is next to this hex" -- is put to the map instead, because with walls the answer is
    /// the map's and not the coordinate's. Terrain cost sits behind the same seam so a later map
    /// with mud or rubble changes nothing that walks.
    /// </summary>
    public interface IGridRules
    {
        /// <summary>Whether this is a real position on this map.</summary>
        bool Contains(Cell cell);

        /// <summary>Whether a creature could stand here, walls and terrain considered.</summary>
        bool IsWalkable(Cell cell);

        /// <summary>Steps it costs to enter this cell. One for open ground.</summary>
        int StepsToEnter(Cell cell);

        /// <summary>Every cell one step from this one: across an open half-edge, onto walkable ground.</summary>
        void Neighbours(Cell from, List<Cell> into);

        /// <summary>Every cell of a tile.</summary>
        void CellsOf(Hex tile, List<Cell> into);

        /// <summary>The wall on a half-edge, from either side.</summary>
        Wall HalfEdge(Hex tile, int halfEdge);

        /// <summary>The wall along a ray.</summary>
        Wall Ray(Hex tile, int ray);

        /// <summary>The map the rules are read from. For geometry that needs its layouts.</summary>
        HexMap Map { get; }
    }

    /// <summary>
    /// <see cref="IGridRules"/> read live from a <see cref="HexMap"/>.
    ///
    /// Nothing is baked. A neighbour query reads the wall flags as they are now, so a wall that
    /// opens mid-fight is open to the next path search without anybody rebuilding anything.
    /// </summary>
    public sealed class GridRules : IGridRules
    {
        public GridRules(HexMap map)
        {
            Map = map;
        }

        public HexMap Map { get; }

        public bool Contains(Cell cell) => Map != null && Map.Contains(cell);

        public bool IsWalkable(Cell cell) =>
            Map != null && Map.TryGetTile(cell.Tile, out var tile) && tile.IsWalkable
            && cell.Area < tile.Areas.Count;

        public int StepsToEnter(Cell cell) =>
            Map != null && Map.TryGetTile(cell.Tile, out var tile) ? tile.StepsToEnter : 1;

        public Wall HalfEdge(Hex tile, int halfEdge) => Map != null ? Map.HalfEdge(tile, halfEdge) : Wall.None;

        public Wall Ray(Hex tile, int ray) => Map != null ? Map.Ray(tile, ray) : Wall.None;

        public void CellsOf(Hex tile, List<Cell> into)
        {
            if (Map == null)
            {
                return;
            }

            foreach (var cell in Map.CellsOf(tile))
            {
                into.Add(cell);
            }
        }

        /// <summary>
        /// The cells a step can reach: for every wedge of this area, the half-edge that closes
        /// it, and if that is open, the area on the far side of it in the neighbouring tile.
        ///
        /// A wedge on the far side that is dead footing leads nowhere; a tile that is not walkable
        /// leads nowhere. Duplicates -- two wedges of one area facing the same far area -- are
        /// dropped, so a caller gets each neighbour once.
        /// </summary>
        public void Neighbours(Cell from, List<Cell> into)
        {
            if (Map == null || !Map.TryGetTile(from.Tile, out var tile) || from.Area >= tile.Areas.Count)
            {
                return;
            }

            var first = into.Count;

            for (var wedge = 0; wedge < TileGeometry.Wedges; wedge++)
            {
                if (tile.Areas.AreaOf(wedge) != from.Area)
                {
                    continue;
                }

                if (Map.HalfEdge(from.Tile, wedge).BlocksMovement)
                {
                    continue;
                }

                var beyond = from.Tile.Neighbor(TileGeometry.EdgeOf(wedge));

                if (!Map.TryGetTile(beyond, out var far) || !far.IsWalkable)
                {
                    continue;
                }

                var farArea = far.Areas.AreaOf(TileGeometry.Twin(wedge));

                if (farArea == AreaLayout.Dead)
                {
                    continue;
                }

                var cell = new Cell(beyond, farArea);
                var seen = false;

                for (var i = first; i < into.Count; i++)
                {
                    if (into[i] == cell)
                    {
                        seen = true;
                        break;
                    }
                }

                if (!seen)
                {
                    into.Add(cell);
                }
            }
        }
    }
}
