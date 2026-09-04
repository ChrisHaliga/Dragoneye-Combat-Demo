namespace Dragoneye.Combat
{
    /// <summary>
    /// The result of pouring experience into a character: where it started, where it ended, and what
    /// is left over.
    ///
    /// Every level at once rather than one at a time. A character that comes out of a long fight
    /// several levels up should be asked what it becomes once, not once per level, and working that
    /// out in the screen would put the arithmetic somewhere the server cannot check it.
    /// </summary>
    public readonly struct LevelGain
    {
        public readonly int FromLevel;
        public readonly int ToLevel;

        /// <summary>Experience left after paying for every level gained.</summary>
        public readonly int RemainingXp;

        public LevelGain(int fromLevel, int toLevel, int remainingXp)
        {
            FromLevel = fromLevel;
            ToLevel = toLevel;
            RemainingXp = remainingXp;
        }

        public int Levels => ToLevel - FromLevel;

        public bool Any => ToLevel > FromLevel;
    }

    /// <summary>
    /// Levels, and the experience that buys them.
    ///
    /// Pure arithmetic, here rather than in the level-up screen, because the host has to agree with
    /// the screen about what a character is entitled to. A client that awards itself four levels is
    /// checked against this same function.
    /// </summary>
    public static class Progression
    {
        public const int FirstLevel = 1;

        /// <summary>
        /// Where levelling stops.
        ///
        /// Not a design statement so much as a guard: the cost doubles per level, so the shift below
        /// has to stay inside an int, and a level nobody can reach is a level nothing has to be
        /// balanced for.
        /// </summary>
        public const int MaxLevel = 20;

        /// <summary>What one more element point costs in experience: two to the current level.</summary>
        public static int XpToLeave(int level)
        {
            var clamped = level < FirstLevel ? FirstLevel : (level > MaxLevel ? MaxLevel : level);
            return 1 << clamped;
        }

        /// <summary>
        /// What killing a creature is worth: its level.
        ///
        /// So a fight against things beneath you pays little and one against something above you
        /// pays for itself. No multipliers, no party split -- the creature that landed the blow
        /// takes it.
        /// </summary>
        public static int XpForKill(int victimLevel) =>
            victimLevel < FirstLevel ? FirstLevel : victimLevel;

        /// <summary>
        /// How far a pile of experience carries a character, and what it leaves behind.
        ///
        /// Spent rather than accumulated: each level takes its own cost out of the total, so the
        /// remainder is what counts towards the next one. That keeps a single number on the
        /// character rather than a total and a spent-so-far that can disagree.
        /// </summary>
        public static LevelGain Resolve(int level, int xp)
        {
            var from = level < FirstLevel ? FirstLevel : (level > MaxLevel ? MaxLevel : level);
            var to = from;
            var left = xp < 0 ? 0 : xp;

            while (to < MaxLevel && left >= XpToLeave(to))
            {
                left -= XpToLeave(to);
                to++;
            }

            return new LevelGain(from, to, left);
        }

        /// <summary>
        /// The element points a character of this level has to spend.
        ///
        /// One per level, which is what makes the pool the thing a level-up buys. Elements are not
        /// all the same price, so this is a budget rather than a count -- see
        /// <see cref="ElementPricing"/>.
        /// </summary>
        public static int PoolBudget(int level) =>
            level < FirstLevel ? FirstLevel : level;
    }
}
