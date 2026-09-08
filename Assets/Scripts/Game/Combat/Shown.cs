using Dragoneye.Combat;
using Dragoneye.Hex;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// The fight as the watcher has been shown it, for anything that draws.
    ///
    /// One door to <see cref="CombatPlayback.Fight"/>, with the answer a view wants before the
    /// fight has opened: until the opening is shown a creature's vitals are what it spawned
    /// with, which is what the live state says. After it, everything comes from the record and
    /// nothing from the live state -- so a health bar, a token and the log agree with each other
    /// and with what the player has actually watched happen.
    /// </summary>
    public static class Shown
    {
        public static PresentedFight Fight => CombatPlayback.Current != null ? CombatPlayback.Current.Fight : null;

        /// <summary>Whether the opening has been shown, so the record is the thing to read.</summary>
        public static bool Began => Fight != null && Fight.Began;

        public static PresentedCreature Of(CreatureState creature) =>
            creature != null && Fight != null ? Fight.Of(creature.TurnId) : null;

        public static PresentedCreature Of(uint turnId) => Fight?.Of(turnId);

        public static int Hp(CreatureState creature) => Of(creature)?.Hp ?? (creature != null ? creature.CurrentHp : 0);

        public static int Armour(CreatureState creature) =>
            Of(creature)?.Armour ?? (creature != null ? creature.CurrentArmour : 0);

        public static Ap Ap(CreatureState creature) =>
            Of(creature)?.Ap ?? (creature != null ? creature.CurrentAp : Dragoneye.Combat.Ap.Zero);

        public static Cell Cell(CreatureState creature) =>
            Of(creature)?.Cell ?? (creature != null ? creature.Cell : default);

        public static Facing Facing(CreatureState creature) =>
            Of(creature)?.Facing ?? (creature != null ? creature.Facing : Dragoneye.Combat.Facing.Default);

        public static bool IsAlive(CreatureState creature) =>
            Of(creature)?.IsAlive ?? (creature != null && creature.IsAlive);

        /// <summary>
        /// What a creature is holding, as the watcher has been shown it.
        ///
        /// Worked out rather than replicated: the hand it started with, less what the playback has
        /// seen it spend and not get back. Both halves are things this viewer knows -- the starting
        /// pool is authored, and every spend was announced -- so this is honest for anybody's
        /// creature, and it moves when the blow is shown rather than when the server resolves it.
        ///
        /// Falls back to the live pool before the fight has begun, which is the only moment there
        /// is nothing shown yet and the creature is standing there with a full hand.
        /// </summary>
        public static ElementCounts Hand(CreatureState creature)
        {
            if (creature == null)
            {
                return ElementCounts.Empty;
            }

            var shown = Of(creature);

            if (shown == null)
            {
                var pool = creature.Pool;
                return pool != null ? pool.Pool : creature.StartingPool;
            }

            var hand = creature.StartingPool;

            foreach (var spent in shown.Outstanding)
            {
                var left = hand[spent] - 1;
                hand = hand.With(spent, left < 0 ? 0 : left);
            }

            return hand;
        }

        public static int Round => Fight?.Round ?? 0;

        public static uint ActiveId => Fight?.ActiveId ?? 0;

        public static bool IsActive(CreatureState creature) =>
            creature != null && Fight != null && Fight.IsActive(creature.TurnId);

        public static bool IsOver => Fight != null && Fight.IsOver;

        public static bool HasWinner => Fight != null && Fight.HasWinner;

        public static Party Winner => Fight != null ? Fight.Winner : Party.Heroes;

        /// <summary>Whether everything the fight has said has been shown. Prompts wait on this.</summary>
        public static bool IsCaughtUp => CombatPlayback.Current == null || CombatPlayback.Current.IsCaughtUp;
    }
}
