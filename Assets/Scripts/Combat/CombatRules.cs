namespace Dragoneye.Combat
{
    /// <summary>
    /// The numbers combat runs on, and the arithmetic that uses them.
    ///
    /// Constants rather than authored stats, deliberately and temporarily: every creature attacks
    /// identically until the stats are designed. They live here rather than scattered through the
    /// callers so that authoring them later is a change to one file plus the definition asset, and
    /// so a test can state the rule without hard-coding a literal that has drifted.
    ///
    /// Costs are <see cref="Ap"/>, which is half-units. Moving a tile costs half a point unarmoured
    /// and a skill costs whole points, so a turn is a real trade between covering ground and acting
    /// -- which is the whole reason DE-000 asks for the half-unit.
    ///
    /// What a step costs is not a constant any more: it follows from the walker's speed, through
    /// <see cref="StepCostFor"/>, so every method here that prices a walk takes the step cost in
    /// rather than assuming one. A caller that wants the ordinary price can pass
    /// <see cref="BaseStepCost"/>; a caller pricing a creature's walk asks the creature.
    /// </summary>
    public static class CombatRules
    {
        /// <summary>What one tile costs at the base speed. Half a point, per DE-000.</summary>
        public static readonly Ap BaseStepCost = Ap.Step;

        /// <summary>Half-units a tile costs at the base speed. Half a point.</summary>
        const int BaseStepUnits = 1;

        /// <summary>
        /// Half-units a tile costs at no speed at all. Two whole points: plate with nothing to
        /// make up for it, and never dearer than that.
        /// </summary>
        const int SlowestStepUnits = 4;

        /// <summary>
        /// What one tile costs at this speed.
        ///
        /// Half a point at the base speed of eight, and half a point more for roughly every two
        /// and a half points of speed lost below that, rounded up to the half and capped at two:
        /// speed eight is half a point, six is a whole one, four is one and a half, and nought is
        /// two. That is the unarmoured / light / medium / heavy ladder with no Endurance bought,
        /// and Endurance climbs back up it. Faster than eight never prices a step below the half.
        /// </summary>
        public static Ap StepCostFor(int speed)
        {
            var slow = Vitals.BaseSpeed - speed;

            if (slow <= 0)
            {
                return Ap.FromUnits(BaseStepUnits);
            }

            // ceil(slow * 3 / 8): three extra half-units spread over the eight speed below base.
            var span = SlowestStepUnits - BaseStepUnits;
            var extra = (slow * span + Vitals.BaseSpeed - 1) / Vitals.BaseSpeed;
            var units = BaseStepUnits + extra;

            return Ap.FromUnits(units > SlowestStepUnits ? SlowestStepUnits : units);
        }

        /// <summary>
        /// Whether a target at this distance is inside a reach.
        ///
        /// Zero distance is the user's own hex and never in reach of anything aimed at somebody
        /// else, which is why this is not a bare comparison.
        /// </summary>
        public static bool InRange(int distance, int reach) => distance > 0 && distance <= reach;

        /// <summary>What a move of this many steps costs, at this price per step.</summary>
        public static Ap MoveCost(int steps, Ap stepCost) => steps <= 0 ? Ap.Zero : stepCost * steps;

        /// <summary>How many tiles a given amount of AP will carry a creature, at this price per step.</summary>
        public static int StepsAffordable(Ap available, Ap stepCost) =>
            stepCost.IsZero ? 0 : available.Units / stepCost.Units;

        /// <summary>
        /// Armour takes the hit first, and what it cannot hold goes through.
        ///
        /// A pool rather than a subtraction. Flat reduction had a wall in it: any blow smaller than
        /// the reduction did nothing at all, forever, so a creature in plate could stand in front
        /// of a dagger for the rest of the match. A pool is worn down by anything, and it does not
        /// come back -- which is the difference between being hard to hurt and being impossible to.
        /// </summary>
        /// <returns>The damage that reached health.</returns>
        public static int Absorb(int damage, int armour, out int armourAfter)
        {
            damage = damage < 0 ? 0 : damage;
            armour = armour < 0 ? 0 : armour;

            var held = damage < armour ? damage : armour;
            armourAfter = armour - held;

            return damage - held;
        }

        /// <summary>
        /// Health after taking a hit, floored at zero.
        ///
        /// Returned rather than applied, so the rule can be checked without a creature to mutate.
        /// </summary>
        public static int Damaged(int currentHp, int damage)
        {
            if (damage <= 0)
            {
                return currentHp;
            }

            return currentHp - damage < 0 ? 0 : currentHp - damage;
        }

        /// <summary>Zero health is dead. The one place that comparison is written.</summary>
        public static bool IsAlive(int currentHp) => currentHp > 0;

        /// <summary>
        /// Whether a creature can still do anything at all with the AP it has left.
        ///
        /// Drives the End Turn button's prompt. It is only a prompt: the turn always ends on the
        /// player's click, never on this returning false.
        /// </summary>
        public static bool CanAffordAnything(Ap currentAp, bool anyMoveInRange, bool anySkillUsable,
            Ap stepCost)
        {
            if (anySkillUsable)
            {
                return true;
            }

            return anyMoveInRange && currentAp >= stepCost;
        }
    }
}
