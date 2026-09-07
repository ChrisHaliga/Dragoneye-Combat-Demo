using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;

namespace Dragoneye.Sim
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Who is standing where, and the two things about them a board question can turn on.
    ///
    /// The grid knows walls and ground and nothing about creatures; the fight knows creatures and
    /// asks the grid about ground. Everything that reads the two together -- a route, a line of
    /// fire, whether an enemy is in reach -- reads the creatures through this, by id, so it can
    /// be answered from the fight's own table on the server, from replicated state on a client,
    /// or from a dictionary in a test.
    /// </summary>
    public interface IOccupancy
    {
        bool IsOccupied(Cell cell);

        /// <summary>The creature standing on this cell, or false.</summary>
        bool TryGet(Cell cell, out uint id);

        /// <summary>Everybody on any area of a tile. A line passing over the tile passes them all.</summary>
        void OccupantsOf(Hex tile, List<uint> into);

        /// <summary>Every occupied cell but one, for blocking a route round the creature walking it.</summary>
        void CopyOccupiedTo(ICollection<Cell> into, Cell except);

        bool IsAlive(uint id);

        Party PartyOf(uint id);
    }
}
