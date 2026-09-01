using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Hex
{
    public enum HexMapShape
    {
        Hexagon,
        Rectangle
    }

    /// <summary>
    /// Builds a map of a regular shape, filled with a single terrain. The first concrete
    /// <see cref="HexMapDefinition"/>; a hand-authored one would be another subclass and nothing
    /// downstream would change.
    /// </summary>
    [CreateAssetMenu(menuName = "Dragoneye/Generated Map", fileName = "GeneratedMap")]
    public sealed class GeneratedMapDefinition : HexMapDefinition
    {
        [SerializeField]
        HexMapShape m_Shape = HexMapShape.Hexagon;

        [SerializeField, Min(1), Tooltip("Rings out from the centre. Hexagon shape only.")]
        int m_Radius = 5;

        [SerializeField, Min(1), Tooltip("Rectangle shape only.")]
        int m_Width = 12;

        [SerializeField, Min(1), Tooltip("Rectangle shape only.")]
        int m_Height = 10;

        [SerializeField]
        TerrainType m_DefaultTerrain;

        public override HexMap Build(int seed)
        {
            // Fixed for now. A procedural definition would use the seed here; the parameter exists
            // so that change never reaches callers.
            var tiles = new List<HexTile>();

            foreach (var hex in EnumerateShape())
            {
                tiles.Add(new HexTile(hex, m_DefaultTerrain));
            }

            return new HexMap(CreateLayout(), tiles);
        }

        IEnumerable<Hex> EnumerateShape() =>
            m_Shape == HexMapShape.Hexagon ? Hex.Range(Hex.Zero, m_Radius) : EnumerateRectangle();

        /// <summary>
        /// A rectangle in offset ("odd-q") coordinates, converted to axial and centred on the
        /// origin so the arena sits around <see cref="Hex.Zero"/> regardless of its dimensions.
        /// </summary>
        IEnumerable<Hex> EnumerateRectangle()
        {
            var halfWidth = m_Width / 2;
            var halfHeight = m_Height / 2;

            for (var column = -halfWidth; column < m_Width - halfWidth; column++)
            {
                for (var row = -halfHeight; row < m_Height - halfHeight; row++)
                {
                    // Integer-divide toward negative infinity: subtracting the low bit first keeps
                    // this correct for negative columns, where C# division truncates toward zero.
                    yield return new Hex(column, row - (column - (column & 1)) / 2);
                }
            }
        }
    }
}
