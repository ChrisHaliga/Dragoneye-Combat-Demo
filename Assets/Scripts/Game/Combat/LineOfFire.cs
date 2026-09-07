using System.Collections.Generic;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// What a shot would do, priced for the cursor: where it flies from and to, the chance it
    /// lands, and what is in the way.
    /// </summary>
    public readonly struct ShotPlan
    {
        public readonly Cell From;
        public readonly Cell To;

        /// <summary>Percent chance to hit, cover already taken off. Zero when the line is blocked.</summary>
        public readonly int Chance;

        /// <summary>What the walls make of the line.</summary>
        public readonly LineVerdict Walls;

        /// <summary>Creatures the shot passes over. Each one costs accuracy.</summary>
        public readonly IReadOnlyList<CreatureState> Bodies;

        public ShotPlan(Cell from, Cell to, int chance, LineVerdict walls,
            IReadOnlyList<CreatureState> bodies)
        {
            From = from;
            To = to;
            Chance = chance;
            Walls = walls;
            Bodies = bodies ?? System.Array.Empty<CreatureState>();
        }

        public bool IsBlocked => Walls == LineVerdict.Blocked;

        /// <summary>Whether anything at all is between them, body or low wall.</summary>
        public bool IsCovered => Bodies.Count > 0 || Walls == LineVerdict.Obstructed;

        /// <summary>How many penalties the line carries: one per body, one for a low wall.</summary>
        public int Cover => Bodies.Count + (Walls == LineVerdict.Obstructed ? 1 : 0);
    }

    /// <summary>
    /// A line traced, both halves: the walls the map knows about, and the bodies the game does.
    /// </summary>
    public readonly struct ShotLine
    {
        public readonly LineVerdict Walls;
        public readonly IReadOnlyList<CreatureState> Bodies;

        public ShotLine(LineVerdict walls, IReadOnlyList<CreatureState> bodies)
        {
            Walls = walls;
            Bodies = bodies ?? System.Array.Empty<CreatureState>();
        }

        public bool IsBlocked => Walls == LineVerdict.Blocked;

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
    public static class LineOfFire
    {
        static readonly List<UnitState> s_Occupants = new List<UnitState>();

        public static ShotLine Trace(IGridRules grid, UnitIndex units, Cell from, Cell to)
        {
            var walls = grid != null ? LineOfSight.Verdict(grid, from, to) : LineVerdict.Clear;

            if (walls == LineVerdict.Blocked)
            {
                return new ShotLine(walls, null);
            }

            var bodies = new List<CreatureState>();

            if (units == null || Cell.Distance(from, to) < 2)
            {
                return new ShotLine(walls, bodies);
            }

            foreach (var tile in Dragoneye.Hex.Hex.Line(from.Tile, to.Tile))
            {
                if (tile == from.Tile || tile == to.Tile)
                {
                    continue;
                }

                s_Occupants.Clear();
                units.OccupantsOf(tile, s_Occupants);

                foreach (var occupant in s_Occupants)
                {
                    var creature = occupant.GetComponent<CreatureState>();

                    if (creature != null && creature.IsAlive)
                    {
                        bodies.Add(creature);
                    }
                }
            }

            return new ShotLine(walls, bodies);
        }
    }
}
