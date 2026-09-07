using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Hex
{
    /// <summary>
    /// The tiles, and every wall between and through them.
    ///
    /// Walls are looked up by tile and index and resolved to whichever tile keeps the record, so
    /// a caller never has to know which side of an edge owns it. They can be changed after the map
    /// is built -- a door, a breach -- and <see cref="WallChanged"/> says so; nothing here bakes a
    /// graph that would then be stale. Whether that is fast enough is the pathfinder's business,
    /// and today it is.
    /// </summary>
    public sealed class HexMap
    {
        readonly Dictionary<Hex, HexTile> m_Tiles;

        public HexLayout Layout { get; }

        public event Action<HexTile> TileChanged;

        /// <summary>
        /// A wall changed on this tile -- the record's owner, for a half-edge -- and the areas the
        /// tile had before. A ray can merge or split them, and whoever keeps creatures on this
        /// map carries each one across with <see cref="AreaLayout.Carry"/>.
        /// </summary>
        public event Action<HexTile, AreaLayout> WallChanged;

        public HexMap(HexLayout layout, IEnumerable<HexTile> tiles)
        {
            Layout = layout;
            m_Tiles = new Dictionary<Hex, HexTile>();

            foreach (var tile in tiles)
            {
                m_Tiles[tile.Coordinates] = tile;
            }
        }

        public int Count => m_Tiles.Count;

        public IEnumerable<HexTile> Tiles => m_Tiles.Values;

        public IEnumerable<Hex> Coordinates => m_Tiles.Keys;

        public bool Contains(Hex hex) => m_Tiles.ContainsKey(hex);

        /// <summary>Whether this is a real position: a tile that exists, and an area it has.</summary>
        public bool Contains(Cell cell) =>
            m_Tiles.TryGetValue(cell.Tile, out var tile) && cell.Area < tile.Areas.Count;

        public bool TryGetTile(Hex hex, out HexTile tile) => m_Tiles.TryGetValue(hex, out tile);

        public HexTile this[Hex hex] => m_Tiles[hex];

        /// <summary>Every cell of a tile: one per area.</summary>
        public IEnumerable<Cell> CellsOf(Hex hex)
        {
            if (m_Tiles.TryGetValue(hex, out var tile))
            {
                for (var area = 0; area < tile.Areas.Count; area++)
                {
                    yield return new Cell(hex, (byte)area);
                }
            }
        }

        // ---------- walls ----------

        /// <summary>The wall along a ray of this tile. No tile, no wall.</summary>
        public Wall Ray(Hex hex, int ray) =>
            m_Tiles.TryGetValue(hex, out var tile) ? tile.Ray(ray) : Wall.None;

        /// <summary>
        /// The wall on a half-edge of this tile, from whichever tile keeps the record.
        ///
        /// Off the edge of the map there is no neighbour to keep it, and no wall: the map's rim is
        /// the end of the world, not a wall around it.
        /// </summary>
        public Wall HalfEdge(Hex hex, int halfEdge)
        {
            halfEdge = TileGeometry.Wrap(halfEdge);

            if (TileGeometry.IsOwned(halfEdge))
            {
                return m_Tiles.TryGetValue(hex, out var tile) ? tile.OwnedHalfEdge(halfEdge) : Wall.None;
            }

            var neighbour = hex.Neighbor(TileGeometry.EdgeOf(halfEdge));

            return m_Tiles.TryGetValue(neighbour, out var other)
                ? other.OwnedHalfEdge(TileGeometry.Twin(halfEdge))
                : Wall.None;
        }

        public void SetRay(Hex hex, int ray, Wall wall)
        {
            if (!m_Tiles.TryGetValue(hex, out var tile) || tile.Ray(ray).Equals(wall))
            {
                return;
            }

            var before = tile.Areas;
            tile.ApplyRay(ray, wall);
            WallChanged?.Invoke(tile, before);
        }

        /// <summary>Sets a half-edge, on whichever tile keeps the record.</summary>
        public void SetHalfEdge(Hex hex, int halfEdge, Wall wall)
        {
            halfEdge = TileGeometry.Wrap(halfEdge);

            if (!TileGeometry.IsOwned(halfEdge))
            {
                hex = hex.Neighbor(TileGeometry.EdgeOf(halfEdge));
                halfEdge = TileGeometry.Twin(halfEdge);
            }

            if (!m_Tiles.TryGetValue(hex, out var tile) || tile.OwnedHalfEdge(halfEdge).Equals(wall))
            {
                return;
            }

            tile.ApplyHalfEdge(halfEdge, wall);
            WallChanged?.Invoke(tile, tile.Areas);
        }

        /// <summary>Both halves of an edge at once. The usual way to author one.</summary>
        public void SetEdge(Hex hex, HexDirection edge, Wall wall)
        {
            TileGeometry.HalvesOf(edge, out var first, out var second);
            SetHalfEdge(hex, first, wall);
            SetHalfEdge(hex, second, wall);
        }

        /// <summary>What stands on a segment, wherever the record is kept.</summary>
        public Wall WallAt(WallSegment segment) =>
            segment.IsRay ? Ray(segment.Tile, segment.Index) : HalfEdge(segment.Tile, segment.Index);

        public void SetWall(WallSegment segment, Wall wall)
        {
            if (segment.IsRay)
            {
                SetRay(segment.Tile, segment.Index, wall);
            }
            else
            {
                SetHalfEdge(segment.Tile, segment.Index, wall);
            }
        }

        // ---------- picking ----------

        /// <summary>
        /// The cell under a point in the map's own space, or null off the map or over footing
        /// nobody can stand on.
        ///
        /// The tile from the layout, then the wedge the point lies in by its bearing off the
        /// tile's centre, then that wedge's area. This is a picking question, not a rules one, so
        /// the float-to-scaled step here is allowed what a bearing between creatures is not.
        /// </summary>
        public Cell? CellAt(Vector3 local)
        {
            var hex = Layout.FromWorld(local);

            if (!m_Tiles.TryGetValue(hex, out var tile))
            {
                return null;
            }

            // Only a tile with no wall through it is all one area: a single walled pair of rays
            // leaves one area and a sliver, and the sliver is nowhere.
            if (tile.MovementRayMask == 0)
            {
                return Cell.Whole(hex);
            }

            var centre = Layout.ToWorld(hex);
            var scale = TileGeometry.Scale / Layout.Size;
            var x = (long)((local.x - centre.x) * scale);
            var z = (long)((local.z - centre.z) * scale);

            var area = tile.Areas.AreaOf(TileGeometry.WedgeAt(x, z));

            return area == AreaLayout.Dead ? (Cell?)null : new Cell(hex, area);
        }

        // ---------- terrain ----------

        public void SetTerrain(Hex hex, TerrainType terrain)
        {
            if (!m_Tiles.TryGetValue(hex, out var tile) || tile.Terrain == terrain)
            {
                return;
            }

            tile.ApplyTerrain(terrain);
            TileChanged?.Invoke(tile);
        }

        public Vector3 WorldCenter()
        {
            if (m_Tiles.Count == 0)
            {
                return Layout.Origin;
            }

            var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
            var max = new Vector3(float.MinValue, 0f, float.MinValue);

            foreach (var hex in m_Tiles.Keys)
            {
                var position = Layout.ToWorld(hex);
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            var center = (min + max) * 0.5f;
            return new Vector3(center.x, Layout.Origin.y, center.z);
        }
    }
}
