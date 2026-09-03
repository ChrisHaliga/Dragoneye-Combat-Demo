namespace Dragoneye.Game
{
    /// <summary>
    /// The numbers combat runs on, and the arithmetic that uses them.
    ///
    /// Constants rather than authored stats, deliberately and temporarily: every creature attacks
    /// identically until the stats are designed. They live here rather than scattered through the
    /// callers so that authoring them later is a change to one file plus the definition asset, and
    /// so a test can state the rule without hard-coding a literal that has drifted.
    /// </summary>
    public static class CombatRules
    {
        /// <summary>AP a single attack costs, whatever the attacker.</summary>
        public const int AttackApCost = 2;

        /// <summary>AP one step of movement costs. Distance is measured in steps, not hexes.</summary>
        public const int MoveApPerTile = 1;

        /// <summary>Reach in hexes. One is melee: the attacker must be adjacent.</summary>
        public const int AttackRange = 1;

        /// <summary>Damage a hit deals.</summary>
        public const int AttackDamage = 5;

        /// <summary>Whether the attacker is close enough, given the distance between the two.</summary>
        public static bool InRange(int distance) => distance > 0 && distance <= AttackRange;

        /// <summary>What a move of this many steps costs.</summary>
        public static int MoveCost(int steps) => steps <= 0 ? 0 : steps * MoveApPerTile;

        /// <summary>
        /// Health after taking a hit, floored at zero.
        ///
        /// Returned rather than applied, so the rule can be checked without a creature to mutate.
        /// </summary>
        public static int Damaged(int currentHp, int damage) =>
            damage <= 0 ? currentHp : (currentHp - damage < 0 ? 0 : currentHp - damage);

        /// <summary>Zero health is dead. The one place that comparison is written.</summary>
        public static bool IsAlive(int currentHp) => currentHp > 0;

        /// <summary>
        /// Whether a creature can still do anything at all with the AP it has left.
        ///
        /// Drives the End Turn button's prompt. It is only a prompt: the turn always ends on the
        /// player's click, never on this returning false.
        /// </summary>
        public static bool CanAffordAnything(int currentAp, bool anyMoveInRange, bool anyTargetInRange)
        {
            if (anyTargetInRange && currentAp >= AttackApCost)
            {
                return true;
            }

            return anyMoveInRange && currentAp >= MoveApPerTile;
        }
    }
}
