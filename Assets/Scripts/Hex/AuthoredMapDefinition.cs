using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Hex
{
    /// <summary>
    /// A map somebody drew: a shape of ground, the tiles that differ from it, and every wall.
    ///
    /// Walls are stored the way the map stores them -- a ray or a half-edge on a tile, with its
    /// flags -- rather than as the straight lines they were drawn as. The editor step that authors
    /// one decomposes lines into these; a painting tool later would write the same records. The
    /// runtime never knows how a wall was drawn, only where it is.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Authored Map", fileName = "AuthoredMap")]
    public sealed class AuthoredMapDefinition : HexMapDefinition
    {
        [Serializable]
        public struct TileEntry
        {
            public int Q;
            public int R;
            public TerrainType Terrain;
        }

        [Serializable]
        public struct RayEntry
        {
            public int Q;
            public int R;

            [Range(0, 11), Tooltip("Clockwise from North: even rays reach an edge midpoint, odd reach a corner.")]
            public int Ray;

            public WallFlags Flags;

            [Tooltip("Hits to breach. Zero: it cannot be.")]
            public int Integrity;
        }

        [Serializable]
        public struct HalfEdgeEntry
        {
            public int Q;
            public int R;

            [Range(0, 11), Tooltip("Clockwise from North: half-edges 2d-1 and 2d make the edge in direction d.")]
            public int HalfEdge;

            public WallFlags Flags;

            [Tooltip("Hits to breach. Zero: it cannot be.")]
            public int Integrity;
        }

        [SerializeField, Min(1), Tooltip("Rings of ground out from the centre, before anything is drawn on it.")]
        int m_Radius = 5;

        [SerializeField]
        TerrainType m_DefaultTerrain;

        [SerializeField, Tooltip("Tiles whose ground differs from the default. A tile listed here "
             + "that is outside the radius is added.")]
        List<TileEntry> m_Tiles = new List<TileEntry>();

        [SerializeField, Tooltip("Walls through tiles.")]
        List<RayEntry> m_Rays = new List<RayEntry>();

        [SerializeField, Tooltip("Walls along tile edges. Recorded on either tile; the map "
             + "resolves the owner.")]
        List<HalfEdgeEntry> m_HalfEdges = new List<HalfEdgeEntry>();

        public int Radius => m_Radius;

        public IReadOnlyList<TileEntry> TileEntries => m_Tiles;

        public IReadOnlyList<RayEntry> RayEntries => m_Rays;

        public IReadOnlyList<HalfEdgeEntry> HalfEdgeEntries => m_HalfEdges;

        public override HexMap Build(int seed)
        {
            var tiles = new Dictionary<Hex, HexTile>();

            foreach (var hex in Hex.Range(Hex.Zero, m_Radius))
            {
                tiles[hex] = new HexTile(hex, m_DefaultTerrain);
            }

            foreach (var entry in m_Tiles)
            {
                var hex = new Hex(entry.Q, entry.R);
                tiles[hex] = new HexTile(hex, entry.Terrain != null ? entry.Terrain : m_DefaultTerrain);
            }

            var map = new HexMap(CreateLayout(), tiles.Values);

            foreach (var entry in m_Rays)
            {
                map.SetRay(new Hex(entry.Q, entry.R), entry.Ray,
                    new Wall(entry.Flags, (ushort)Mathf.Clamp(entry.Integrity, 0, ushort.MaxValue)));
            }

            foreach (var entry in m_HalfEdges)
            {
                map.SetHalfEdge(new Hex(entry.Q, entry.R), entry.HalfEdge,
                    new Wall(entry.Flags, (ushort)Mathf.Clamp(entry.Integrity, 0, ushort.MaxValue)));
            }

            return map;
        }
    }
}
