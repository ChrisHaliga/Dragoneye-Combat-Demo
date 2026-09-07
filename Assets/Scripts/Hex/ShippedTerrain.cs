using UnityEngine;

namespace Dragoneye.Hex
{
    /// <summary>What a kind of ground is, before it is an asset.</summary>
    public readonly struct TerrainSpec
    {
        public readonly string Name;
        public readonly string DisplayName;
        public readonly Color Color;
        public readonly bool IsWalkable;
        public readonly float MoveCost;
        public readonly bool BlocksSight;

        public TerrainSpec(string name, string displayName, Color color, bool isWalkable, float moveCost,
            bool blocksSight)
        {
            Name = name;
            DisplayName = displayName;
            Color = color;
            IsWalkable = isWalkable;
            MoveCost = moveCost;
            BlocksSight = blocksSight;
        }
    }

    /// <summary>
    /// The ground the game ships, by the names the maps use.
    ///
    /// One source for three readers: the editor step that writes the terrain assets, the arena
    /// that binds map names to those assets, and the harness, which has no assets and builds the
    /// same terrain from the same numbers. A map that names ground not listed here is a map that
    /// fails to build, loudly, on every one of them.
    /// </summary>
    public static class ShippedTerrain
    {
        public const string GrassName = "grass";
        public const string StoneName = "stone";
        public const string WaterName = "water";

        /// <summary>Open ground. Everything is this unless the map says otherwise.</summary>
        public static readonly TerrainSpec Grass =
            new TerrainSpec(GrassName, "Grass", new Color(0.36f, 0.52f, 0.30f), true, 1f, false);

        /// <summary>A boulder: nothing stands on it, and nothing sees through it.</summary>
        public static readonly TerrainSpec Stone =
            new TerrainSpec(StoneName, "Stone", new Color(0.44f, 0.42f, 0.40f), false, 1f, true);

        /// <summary>Water: nothing stands on it, and everything sees across it.</summary>
        public static readonly TerrainSpec Water =
            new TerrainSpec(WaterName, "Water", new Color(0.20f, 0.36f, 0.52f), false, 1f, false);

        public static readonly TerrainSpec[] All = { Grass, Stone, Water };

        /// <summary>The spec for a name, or null for ground the game does not ship.</summary>
        public static TerrainSpec? Named(string name)
        {
            foreach (var spec in All)
            {
                if (spec.Name == name)
                {
                    return spec;
                }
            }

            return null;
        }
    }
}
