using UnityEngine;

namespace Dragoneye.Hex
{
    /// <summary>
    /// A recipe for building a <see cref="HexMap"/>.
    ///
    /// This is the seam that keeps arena shape and size out of the systems and rendering layers:
    /// they hold a <see cref="HexMapDefinition"/> reference and never learn which subclass it is.
    /// Swapping a radius-5 hexagon for a hand-authored 40x30 map is an inspector change.
    /// </summary>
    public abstract class HexMapDefinition : ScriptableObject
    {
        [SerializeField, Min(0.01f), Tooltip("Distance from a tile's centre to its corners.")]
        float m_TileSize = 1f;

        public float TileSize => m_TileSize;

        /// <summary>
        /// Builds the map. The <paramref name="seed"/> is threaded through from the start so a
        /// procedural definition can be added later without changing any caller; definitions that
        /// produce a fixed map ignore it.
        /// </summary>
        public abstract HexMap Build(int seed);

        public HexMap Build() => Build(0);

        protected HexLayout CreateLayout() => new HexLayout(m_TileSize, Vector3.zero);
    }
}
