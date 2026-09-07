using System;

namespace Dragoneye.Hex
{
    /// <summary>
    /// Something that owns the board to draw, and announces when it is rebuilt.
    ///
    /// Exists so views can consume a map without knowing who produced it. Without it the rendering
    /// assembly would have to reference the systems assembly purely to name a concrete type in a
    /// serialised field, which is a dependency in the wrong direction: rendering should be the leaf.
    ///
    /// What is offered here is the board as it should look now, which is not always the board the
    /// fight is being played on. A fight runs ahead of what has been shown of it, so a wall it has
    /// already brought down is still standing here until the moment it falls comes round to be
    /// watched. Nothing that draws should ever be handed the other one.
    /// </summary>
    public interface IHexMapSource
    {
        /// <summary>The board to draw, or null before it has been built.</summary>
        HexMap Shown { get; }

        /// <summary>Raised whenever the board is (re)built, so views can rebuild from scratch.</summary>
        event Action<HexMap> MapBuilt;
    }
}
