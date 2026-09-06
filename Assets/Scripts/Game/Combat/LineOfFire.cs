using System.Collections.Generic;
using Dragoneye.Hex.Systems;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// What a shot would do, priced for the cursor: where it flies from and to, the chance it
    /// lands, and who is standing in the way.
    /// </summary>
    public readonly struct ShotPlan
    {
        public readonly Hex From;
        public readonly Hex To;

        /// <summary>Percent chance to hit, cover already taken off.</summary>
        public readonly int Chance;

        /// <summary>Creatures the shot passes over. Each one costs accuracy.</summary>
        public readonly IReadOnlyList<CreatureState> Cover;

        public ShotPlan(Hex from, Hex to, int chance, IReadOnlyList<CreatureState> cover)
        {
            From = from;
            To = to;
            Chance = chance;
            Cover = cover ?? System.Array.Empty<CreatureState>();
        }

        public bool IsCovered => Cover.Count > 0;
    }

    /// <summary>
    /// Who is standing between a shooter and a target.
    ///
    /// The straight line between the two tiles, with both ends left out: the shooter is not in
    /// their own way, and the target is what the shot is for. Anybody on a tile in between --
    /// friend or foe, it makes no difference to an arrow -- is cover, and cover costs accuracy.
    ///
    /// Why accuracy and not the clash: "disadvantage" in this game is a doubled commitment read
    /// worst-of, and a skill that commits one element commits two of the same one, so the flag
    /// cannot bite on an attacker. Cover has to be felt somewhere, and the roll to hit is where
    /// a ranged attack already lives.
    /// </summary>
    public static class LineOfFire
    {
        /// <summary>Everybody on the tiles between the two, in order from the shooter.</summary>
        public static List<CreatureState> Cover(Hex from, Hex to, UnitIndex units)
        {
            var cover = new List<CreatureState>();

            if (units == null || Hex.Distance(from, to) < 2)
            {
                return cover;
            }

            foreach (var tile in Hex.Line(from, to))
            {
                if (tile == from || tile == to)
                {
                    continue;
                }

                if (units.TryGet(tile, out var occupant))
                {
                    var creature = occupant.GetComponent<CreatureState>();

                    if (creature != null && creature.IsAlive)
                    {
                        cover.Add(creature);
                    }
                }
            }

            return cover;
        }

        /// <summary>How many creatures a shot along this line would pass over.</summary>
        public static int CoverCount(Hex from, Hex to, UnitIndex units) => Cover(from, to, units).Count;
    }
}
