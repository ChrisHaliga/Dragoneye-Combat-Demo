using System;

namespace Dragoneye.Combat
{
    /// <summary>
    /// Every roll a fight makes, from one seed.
    ///
    /// The rules take rolls as parameters -- <see cref="SkillRules.Hits"/>,
    /// <see cref="ClashDefenceOdds.TryChoose"/> -- so they can be pinned in a test. The server
    /// used to hand them the engine's global generator, which is unseeded state nobody can
    /// reproduce: no fight could be replayed and no outcome could be asserted. This is that
    /// generator given a seed and an owner. One per fight, created by the director, its seed
    /// logged with the match.
    /// </summary>
    public sealed class Dice
    {
        readonly Random m_Random;

        public Dice(int seed)
        {
            Seed = seed;
            m_Random = new Random(seed);
        }

        /// <summary>What this fight was rolled from. Log it, and the fight can be rolled again.</summary>
        public int Seed { get; }

        /// <summary>A roll in [0, 1).</summary>
        public float Roll() => (float)m_Random.NextDouble();
    }
}
