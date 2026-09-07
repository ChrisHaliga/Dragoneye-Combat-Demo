using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Hex
{
    /// <summary>
    /// A map somebody drew, as an asset: a <see cref="MapRecipe"/> with its terrain names bound.
    ///
    /// Stored the way the recipe is stored -- tiles by terrain name, walls by segment -- with a
    /// palette that says which asset each name means. Building it is the recipe's job; this only
    /// keeps the numbers and the bindings, so what a map means is decided in one place whether it
    /// came from an asset, an editor step or a test.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Authored Map", fileName = "AuthoredMap")]
    public sealed class AuthoredMapDefinition : HexMapDefinition
    {
        [Serializable]
        public struct TerrainEntry
        {
            public string Name;
            public TerrainType Terrain;
        }

        [Serializable]
        public struct TileEntry
        {
            public int Q;
            public int R;
            public string Terrain;
        }

        [Serializable]
        public struct WallEntry
        {
            public int Q;
            public int R;

            [Range(0, 11), Tooltip("Clockwise from North. Rays: even reach an edge midpoint, odd a corner. "
                 + "Half-edges: 2d-1 and 2d make the edge in direction d.")]
            public int Index;

            public bool IsRay;
            public WallFlags Flags;

            [Tooltip("Hits to breach. Zero: it cannot be.")]
            public int Integrity;
        }

        [SerializeField, Min(0), Tooltip("Rings of ground out from the centre, before anything is drawn on it.")]
        int m_Radius = 5;

        [SerializeField, Tooltip("The terrain name of the ground where nothing says otherwise.")]
        string m_DefaultTerrain = "grass";

        [SerializeField, Tooltip("What each terrain name means.")]
        List<TerrainEntry> m_Palette = new List<TerrainEntry>();

        [SerializeField, Tooltip("Tiles whose ground differs from the default. One outside the radius is added.")]
        List<TileEntry> m_Tiles = new List<TileEntry>();

        [SerializeField, Tooltip("Every wall: a ray through a tile or a half of its edge, and what it stops.")]
        List<WallEntry> m_Walls = new List<WallEntry>();

        public int Radius => m_Radius;

        public string DefaultTerrain => m_DefaultTerrain;

        public IReadOnlyList<TerrainEntry> Palette => m_Palette;

        public IReadOnlyList<TileEntry> Tiles => m_Tiles;

        public IReadOnlyList<WallEntry> Walls => m_Walls;

        /// <summary>
        /// Writes a recipe into this asset, and binds its terrain names.
        ///
        /// Everything the recipe says, and nothing it does not: an asset authored twice from the
        /// same recipe is the same asset.
        /// </summary>
        public void Author(MapRecipe recipe, IReadOnlyList<TerrainEntry> palette)
        {
            m_Radius = recipe.Radius;
            m_DefaultTerrain = recipe.DefaultTerrain;
            m_Palette = new List<TerrainEntry>(palette ?? Array.Empty<TerrainEntry>());
            m_Tiles = new List<TileEntry>();
            m_Walls = new List<WallEntry>();

            foreach (var tile in recipe.Tiles)
            {
                m_Tiles.Add(new TileEntry { Q = tile.Tile.Q, R = tile.Tile.R, Terrain = tile.Terrain });
            }

            foreach (var wall in recipe.Walls)
            {
                m_Walls.Add(new WallEntry
                {
                    Q = wall.Segment.Tile.Q,
                    R = wall.Segment.Tile.R,
                    Index = wall.Segment.Index,
                    IsRay = wall.Segment.IsRay,
                    Flags = wall.Wall.Flags,
                    Integrity = wall.Wall.Integrity
                });
            }
        }

        /// <summary>The recipe this asset holds, as it would be written.</summary>
        public MapRecipe ToRecipe()
        {
            var recipe = new MapRecipe(m_Radius, m_DefaultTerrain);

            foreach (var tile in m_Tiles)
            {
                recipe.Tile(new Hex(tile.Q, tile.R), tile.Terrain);
            }

            foreach (var wall in m_Walls)
            {
                var hex = new Hex(wall.Q, wall.R);
                var segment = wall.IsRay ? WallSegment.Ray(hex, wall.Index) : WallSegment.HalfEdge(hex, wall.Index);
                var integrity = (ushort)Mathf.Clamp(wall.Integrity, 0, ushort.MaxValue);

                recipe.Wall(segment, new Wall(wall.Flags, integrity));
            }

            return recipe;
        }

        /// <summary>The asset a terrain name means here, or null when the palette does not say.</summary>
        public TerrainType TerrainNamed(string name)
        {
            foreach (var entry in m_Palette)
            {
                if (entry.Name == name)
                {
                    return entry.Terrain;
                }
            }

            return null;
        }

        public override HexMap Build(int seed) => ToRecipe().Build(CreateLayout(), TerrainNamed);
    }
}
