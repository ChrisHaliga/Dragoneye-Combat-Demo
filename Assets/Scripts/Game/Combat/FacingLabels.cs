using Dragoneye.Combat;
using Dragoneye.Hex;

namespace Dragoneye.Game
{
    /// <summary>
    /// What a facing is called.
    ///
    /// The words, separate from the rule, in the assembly that is allowed to hold English --
    /// <c>Dragoneye.Combat</c> knows a facing is an index and has no business knowing it is called
    /// north. The names come from the grid's own direction enum rather than a second list, so the
    /// two cannot drift apart: a facing and a hex direction are the same number by design, and this
    /// is one more place that is true.
    /// </summary>
    public static class FacingLabels
    {
        public static string NameOf(Facing facing)
        {
            switch ((HexDirection)facing.Index)
            {
                case HexDirection.North: return "north";
                case HexDirection.NorthEast: return "north-east";
                case HexDirection.SouthEast: return "south-east";
                case HexDirection.South: return "south";
                case HexDirection.SouthWest: return "south-west";
                default: return "north-west";
            }
        }

        /// <summary>The compass form, for a button too small for a word.</summary>
        public static string ShortNameOf(Facing facing)
        {
            switch ((HexDirection)facing.Index)
            {
                case HexDirection.North: return "N";
                case HexDirection.NorthEast: return "NE";
                case HexDirection.SouthEast: return "SE";
                case HexDirection.South: return "S";
                case HexDirection.SouthWest: return "SW";
                default: return "NW";
            }
        }
    }
}
