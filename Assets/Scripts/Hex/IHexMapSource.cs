using System;

namespace Dragoneye.Hex
{
    /// <summary>
    /// Something that owns a live <see cref="HexMap"/> and announces when it is rebuilt.
    ///
    /// Exists so views can consume a map without knowing who produced it. Without it the rendering
    /// assembly would have to reference the systems assembly purely to name a concrete type in a
    /// serialised field, which is a dependency in the wrong direction: rendering should be the leaf.
    /// </summary>
    public interface IHexMapSource
    {
        /// <summary>The live map, or null before it has been built.</summary>
        HexMap Map { get; }

        /// <summary>Raised whenever the map is (re)built, so views can rebuild from scratch.</summary>
        event Action<HexMap> MapBuilt;
    }
}
