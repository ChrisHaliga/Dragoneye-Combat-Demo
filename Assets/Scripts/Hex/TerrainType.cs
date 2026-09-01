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

        [SerializeField, Min(0f), Tooltip("Movement points consumed to enter. Used by pathfinding later.")]
        float m_MoveCost = 1f;

        public string DisplayName => m_DisplayName;

        public Color Color => m_Color;

        public bool IsWalkable => m_IsWalkable;

        public float MoveCost => m_MoveCost;
    }
}
