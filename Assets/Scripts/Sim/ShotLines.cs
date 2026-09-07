using System.Collections.Generic;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;

namespace Dragoneye.Sim
{
    /// <summary>
    /// A line traced, both halves: the walls the map knows about, and the bodies the fight does.
    /// </summary>
    public readonly struct ShotLine
    {
        public readonly LineVerdict Walls;

        /// <summary>Creatures the shot passes over, by id. Each one costs accuracy.</summary>
        public readonly IReadOnlyList<uint> Bodies;

        public ShotLine(LineVerdict walls, IReadOnlyList<uint> bodies)
        {
            Walls = walls;
            Bodies = bodies ?? System.Array.Empty<uint>();
        }

        public bool IsBlocked => Walls == LineVerdict.Blocked;

        /// <summary>Whether anything at all is between them, body or low wall.</summary>
        public bool IsCovered => Bodies.Count > 0 || Walls == LineVerdict.Obstructed;

        /// <summary>Penalties to the roll: one per body passed over, one for a low wall.</summary>
        public int Cover => Bodies.Count + (Walls == LineVerdict.Obstructed ? 1 : 0);

        public static readonly ShotLine Clear = new ShotLine(LineVerdict.Clear, null);
    }

    /// <summary>
    /// Who and what is between a shooter and a target.
    ///
    /// Two passes. The walls first, from <see cref="LineOfSight"/>: a blocked line ends the
    /// question, and a low wall in the way is one obstruction. Then the bodies: anybody on a tile
    /// the line passes over, both ends left out -- the shooter is not in their own way, and the
    /// target is what the shot is for. Friend or foe, it makes no difference to an arrow.
    ///
    /// Why accuracy and not the clash: "disadvantage" in this game is a doubled commitment read
    /// worst-of, and a skill that commits one element commits two of the same one, so the flag
    /// cannot bite on an attacker. Cover has to be felt somewhere, and the roll to hit is where
    /// a ranged attack already lives.
    /// </summary>
    public static class ShotLines
    {
        static readonly List<uint> s_Occupants = new List<uint>();
        static readonly List<Dragoneye.Hex.Hex> s_Between = new List<Dragoneye.Hex.Hex>();

        public static ShotLine Trace(IGridRules grid, IOccupancy occupancy, Cell from, Cell to)
        {
            var walls = LineOfSight.Verdict(grid, from, to);

            if (walls == LineVerdict.Blocked)
            {
                return new ShotLine(walls, null);
            }

            var bodies = new List<uint>();

            if (occupancy == null)
            {
                return new ShotLine(walls, bodies);
            }

            LineOfSight.TilesBetween(from, to, s_Between);

            foreach (var tile in s_Between)
            {
                s_Occupants.Clear();
                occupancy.OccupantsOf(tile, s_Occupants);

                foreach (var id in s_Occupants)
                {
                    if (occupancy.IsAlive(id))
                    {
                        bodies.Add(id);
                    }
                }
            }

            return new ShotLine(walls, bodies);
        }
    }
}
