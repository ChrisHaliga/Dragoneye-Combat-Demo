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
