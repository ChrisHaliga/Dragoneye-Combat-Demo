using Dragoneye.Combat;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Where a creature is looking, on the grid, and what walking past it costs.
    ///
    /// <c>Dragoneye.Combat</c> knows what a facing is worth in sectors and cannot see a cell;
    /// the grid knows cells and walls and nothing about facings. This is the seam between them,
    /// and it is pure -- cells, a facing and the grid in, a yes or no out -- so the rule it states
    /// can be checked without a creature, a scene or a session.
    ///
    /// The rule: a creature is watching the three cells in front of it that it can see into. A
    /// wall it cannot see through is a wall it cannot swing over; a low wall is neither. Walking
    /// *out* of a watched cell is what provokes a swing. Moving from one watched cell to another
    /// does not, and neither does moving while stood somewhere it was not watching.
    /// </summary>
    public static class ThreatGeometry
    {
        /// <summary>Which way one cell lies from another, as a facing. The map decides, so split tiles have an answer.</summary>
        public static Facing Bearing(IGridRules grid, Cell from, Cell to) =>
            Facing.Of((int)AreaGeometry.Direction(grid?.Map, from, to));

        /// <summary>Whether a creature stood here and turned this way is watching that cell.</summary>
        public static bool Watches(IGridRules grid, Cell watcher, Facing facing, Cell cell) =>
            Cell.Distance(watcher, cell) == Opportunity.Range
            && FacingRules.Threatens(facing, Bearing(grid, watcher, cell))
            && LineOfSight.Verdict(grid, watcher, cell) != LineVerdict.Blocked;

        /// <summary>
        /// Whether walking from one cell to another gives a creature stood here a swing.
        ///
        /// Leaving the watched cells, not moving while on one. The destination is what decides
        /// it: a step that ends somewhere still watched is a step the watcher can follow with its
        /// eyes, and one that ends out of sight is the one it has to strike at.
        /// </summary>
        public static bool Provokes(IGridRules grid, Cell watcher, Facing facing, Cell from, Cell to) =>
            from != to && Watches(grid, watcher, facing, from) && !Watches(grid, watcher, facing, to);
    }
}
