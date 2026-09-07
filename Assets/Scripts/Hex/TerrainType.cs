using UnityEngine;

namespace Dragoneye.Hex
{
    /// <summary>
    /// What a tile is made of. An asset rather than an enum so terrain can be added and retuned
    /// without recompiling, and so the renderer has somewhere to read colour from without the data
    /// layer growing a dependency on materials.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Terrain Type", fileName = "TerrainType")]
    public sealed class TerrainType : ScriptableObject
    {
        [SerializeField]
        string m_DisplayName = "Terrain";

        [SerializeField]
        Color m_Color = Color.white;

        [SerializeField, Tooltip("Whether units may occupy this tile at all.")]
        bool m_IsWalkable = true;

        [SerializeField, Min(0f), Tooltip("Steps it costs to enter, rounded to a whole number: one "
             + "for open ground, two for anything difficult. A step is priced by the walker's speed.")]
        float m_MoveCost = 1f;

        [SerializeField, Tooltip("Whether the ground itself stops a line of sight -- a boulder, a "
             + "pillar. A pit is impassable and stops nothing.")]
        bool m_BlocksSight;

        public string DisplayName => m_DisplayName;

        public Color Color => m_Color;

        public bool IsWalkable => m_IsWalkable;

        public float MoveCost => m_MoveCost;

        /// <summary>Whole steps to enter. Never less than one.</summary>
        public int StepsToEnter => Mathf.Max(1, Mathf.RoundToInt(m_MoveCost));

        public bool BlocksSight => m_BlocksSight;

        /// <summary>
        /// A terrain from its spec. What the editor step writes to disk and what a test builds
        /// in memory, from the same numbers.
        /// </summary>
        public static TerrainType Create(TerrainSpec spec)
        {
            var terrain = CreateInstance<TerrainType>();
            terrain.Apply(spec);
            return terrain;
        }

        /// <summary>Writes a spec over this terrain. For the editor step keeping an asset current.</summary>
        public void Apply(TerrainSpec spec)
        {
            name = spec.DisplayName;
            m_DisplayName = spec.DisplayName;
            m_Color = spec.Color;
            m_IsWalkable = spec.IsWalkable;
            m_MoveCost = spec.MoveCost;
            m_BlocksSight = spec.BlocksSight;
        }
    }
}
