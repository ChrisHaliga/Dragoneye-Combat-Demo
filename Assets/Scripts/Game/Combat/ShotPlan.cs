using System.Collections.Generic;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// What a shot would do, priced for the cursor: where it flies from and to, the chance it
    /// lands, and what is in the way.
    ///
    /// The line is traced by <see cref="Dragoneye.Sim.ShotLines"/>, over the same board the
    /// server will trace it on; this only remembers the answer beside where it was asked from.
    /// </summary>
    public readonly struct ShotPlan
    {
        public readonly Cell From;
        public readonly Cell To;

        /// <summary>Percent chance to hit, cover already taken off. Zero when the line is blocked.</summary>
        public readonly int Chance;

        /// <summary>What the walls make of the line.</summary>
        public readonly LineVerdict Walls;

        /// <summary>Creatures the shot passes over, by turn id. Each one costs accuracy.</summary>
        public readonly IReadOnlyList<uint> Bodies;

        public ShotPlan(Cell from, Cell to, int chance, LineVerdict walls, IReadOnlyList<uint> bodies)
        {
            From = from;
            To = to;
            Chance = chance;
            Walls = walls;
            Bodies = bodies ?? System.Array.Empty<uint>();
        }

        public bool IsBlocked => Walls == LineVerdict.Blocked;

        /// <summary>Whether anything at all is between them, body or low wall.</summary>
        public bool IsCovered => Bodies.Count > 0 || Walls == LineVerdict.Obstructed;

        /// <summary>How many penalties the line carries: one per body, one for a low wall.</summary>
        public int Cover => Bodies.Count + (Walls == LineVerdict.Obstructed ? 1 : 0);
    }
}
