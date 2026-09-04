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
    /// Costs are <see cref="Ap"/>, which is half-units. Moving a tile costs half a point and a skill
    /// costs whole points, so a turn is a real trade between covering ground and acting -- which is
    /// the whole reason DE-000 asks for the half-unit.
    /// </summary>
    public static class CombatRules
    {
        /// <summary>What a single attack costs, whatever the attacker.</summary>
        public static readonly Ap AttackCost = Ap.FromWhole(2);

        /// <summary>What one tile of movement costs. Half a point, per DE-000.</summary>
        public static readonly Ap MoveCostPerTile = Ap.Step;

        /// <summary>Reach in hexes. One is melee: the attacker must be adjacent.</summary>
        public const int AttackRange = 1;

        /// <summary>Damage a hit deals.</summary>
        public const int AttackDamage = 5;

        /// <summary>Whether the attacker is close enough, given the distance between the two.</summary>
        public static bool InRange(int distance) => distance > 0 && distance <= AttackRange;

        /// <summary>What a move of this many steps costs.</summary>
        public static Ap MoveCost(int steps) => steps <= 0 ? Ap.Zero : MoveCostPerTile * steps;

        /// <summary>How many tiles a given amount of AP will carry a creature.</summary>
        public static int StepsAffordable(Ap available) =>
            MoveCostPerTile.IsZero ? 0 : available.Units / MoveCostPerTile.Units;

        /// <summary>
        /// What a hit actually lands after the defender's protection is taken off it.
        ///
        /// Floored at zero, which is the whole reason this is a function: reduction that outweighs
        /// the blow means the blow does nothing, not that the defender is healed by the difference.
        /// </summary>
        public static int DamageAfter(int damage, int reduction)
        {
            if (damage <= 0)
            {
                return 0;
            }

            var landed = damage - (reduction < 0 ? 0 : reduction);
            return landed < 0 ? 0 : landed;
        }

        /// <summary>
        /// Health after taking a hit, floored at zero.
        ///
        /// Returned rather than applied, so the rule can be checked without a creature to mutate.
        /// </summary>
        public static int Damaged(int currentHp, int damage, int reduction = 0)
        {
            var landed = DamageAfter(damage, reduction);
            return landed <= 0 ? currentHp : (currentHp - landed < 0 ? 0 : currentHp - landed);
        }

        /// <summary>Zero health is dead. The one place that comparison is written.</summary>
        public static bool IsAlive(int currentHp) => currentHp > 0;

        /// <summary>
        /// Whether a creature can still do anything at all with the AP it has left.
        ///
        /// Drives the End Turn button's prompt. It is only a prompt: the turn always ends on the
        /// player's click, never on this returning false.
        /// </summary>
        public static bool CanAffordAnything(Ap currentAp, bool anyMoveInRange, bool anyTargetInRange)
        {
            if (anyTargetInRange && currentAp >= AttackCost)
            {
                return true;
            }

            return anyMoveInRange && currentAp >= MoveCostPerTile;
        }
    }
}
