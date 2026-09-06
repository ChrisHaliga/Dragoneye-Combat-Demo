using Dragoneye.Combat;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Where a creature is looking, on the grid, and what walking past it costs.
    ///
    /// <c>Dragoneye.Combat</c> knows what a facing is worth in sectors and cannot see a hex;
    /// the grid knows hexes and nothing about facings. This is the seam between them, and it is
    /// pure -- three cells and a facing in, a yes or no out -- so the rule it states can be
    /// checked without a creature, a scene or a session.
    ///
    /// The rule: a creature is watching the three tiles in front of it. Walking *out* of one of
    /// those tiles is what provokes a swing. Moving from one watched tile to another does not,
    /// and neither does moving while stood somewhere it was not watching -- which is how every
    /// tactics game that has this rule plays it, and how an earlier version of this game did not.
    /// That version provoked on any move made while watched, so a creature could not sidestep one
    /// hex in front of an enemy without being hit for it.
    /// </summary>
    public static class ThreatGeometry
    {
        /// <summary>Which way one hex lies from another, as a facing.</summary>
        public static Facing Bearing(Hex from, Hex to) => Facing.Of((int)Hex.DirectionTo(from, to));

        /// <summary>Whether a creature stood here and turned this way is watching that tile.</summary>
        public static bool Watches(Hex watcher, Facing facing, Hex tile) =>
            Hex.Distance(watcher, tile) == Opportunity.Range
            && FacingRules.Threatens(facing, Bearing(watcher, tile));

        /// <summary>
        /// Whether walking from one tile to another gives a creature stood here a swing.
        ///
        /// Leaving the watched tiles, not moving while on one. The destination is what decides
        /// it: a step that ends somewhere still watched is a step the watcher can follow with its
        /// eyes, and one that ends out of sight is the one it has to strike at.
        /// </summary>
        public static bool Provokes(Hex watcher, Facing facing, Hex from, Hex to) =>
            from != to && Watches(watcher, facing, from) && !Watches(watcher, facing, to);
    }
}
