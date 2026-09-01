using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Hex
{
    /// <summary>
    /// A set of tiles keyed by coordinate, plus the layout that places them in the world.
    ///
    /// Sparse by design: a map is whatever tiles were put in it, so shape and size are properties
    /// of the data rather than of this class. Mutation raises <see cref="TileChanged"/> so views can
    /// react to exactly what changed instead of rebuilding or polling.
    /// </summary>
    public sealed class HexMap
    {
        readonly Dictionary<Hex, HexTile> m_Tiles;

        public HexLayout Layout { get; }

        /// <summary>Raised after a tile's contents change. Carries the tile that changed.</summary>
        public event Action<HexTile> TileChanged;

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

        public bool TryGetTile(Hex hex, out HexTile tile) => m_Tiles.TryGetValue(hex, out tile);

        public HexTile this[Hex hex] => m_Tiles[hex];

        /// <summary>The tiles adjacent to <paramref name="hex"/> that actually exist on this map.</summary>
        public IEnumerable<HexTile> NeighborsOf(Hex hex)
        {
            foreach (var neighbor in hex.Neighbors())
            {
                if (m_Tiles.TryGetValue(neighbor, out var tile))
                {
                    yield return tile;
                }
            }
        }

        public void SetTerrain(Hex hex, TerrainType terrain)
        {
            if (!m_Tiles.TryGetValue(hex, out var tile) || tile.Terrain == terrain)
            {
                return;
            }

            tile.ApplyTerrain(terrain);
            TileChanged?.Invoke(tile);
        }

        /// <summary>
        /// World-space centre of the map's bounding box. Useful for framing a camera or placing
        /// spawns without assuming the map is centred on <see cref="Hex.Zero"/>.
        /// </summary>
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
