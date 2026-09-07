using Dragoneye.Sim;
namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// How long each kind of event is left on screen before the next one is shown.
    ///
    /// These are the whole of the fight's rhythm, and they are here rather than on the server
    /// because the server has no rhythm: it resolves a turn the instant it is decided and moves
    /// on. What a person needs -- a beat to read a result, a walk they can follow, a pause before
    /// the next creature starts -- is a property of watching, so it belongs to the thing that
    /// draws.
    ///
    /// Pure, so the numbers can be checked and changed in one place.
    /// </summary>
    public static class PresentationPacing
    {
        /// <summary>Seconds a token takes to cross one tile.</summary>
        public const float SecondsPerTile = 0.3f;

        /// <summary>Seconds a shot takes before distance is counted, and per tile of it.</summary>
        public const float ShotBase = 0.14f;
        public const float ShotPerTile = 0.07f;

        /// <summary>What holding the fast-forward key multiplies the pace by.</summary>
        public const float FastForward = 4f;

        /// <summary>
        /// The beat after an event.
        /// </summary>
        /// <param name="ranged">Whether the skill involved flies rather than swings.</param>
        /// <param name="tiles">Tiles walked or flown, for the events that do either.</param>
        /// <param name="mine">Whether the local player is the one acting, where that shortens the wait.</param>
        public static float BeatFor(CombatEvent e, bool ranged, int tiles, bool mine)
        {
            switch (e.Kind)
            {
                case CombatEventKind.Began:
                    return 0.6f;

                case CombatEventKind.RoundBegan:
                    return 0.5f;

                // A beat before anything moves: the banner is up and the log has the last
                // exchange in it. Shorter for your own turn, which you were waiting for.
                case CombatEventKind.TurnBegan:
                    return mine ? 0.45f : 1.0f;

                case CombatEventKind.Recovered:
                    return 0.55f;

                // The walk itself, plus a moment standing still at the end of it.
                case CombatEventKind.Moved:
                    return (tiles * SecondsPerTile) + 0.25f;

                case CombatEventKind.Faced:
                    return 0.25f;

                // The lean, or the flight. The result is not shown until it has landed.
                case CombatEventKind.Swung:
                    return ranged ? Flight(tiles) + 0.15f : 0.5f;

                case CombatEventKind.Shot:
                    return e.Landed ? Flight(tiles) + 0.15f : Flight(tiles) + 0.7f;

                case CombatEventKind.Acted:
                    return ranged ? Flight(tiles) + 0.6f : 0.9f;

                // Long enough to read two hands and a verdict.
                case CombatEventKind.ClashResolved:
                    return 1.2f;

                case CombatEventKind.Damaged:
                    return 0.75f;

                case CombatEventKind.Healed:
                case CombatEventKind.ApRestored:
                    return 0.6f;

                case CombatEventKind.HeldBack:
                    return 0.6f;

                case CombatEventKind.Fell:
                    return 0.9f;

                case CombatEventKind.WallChanged:
                    return 0.6f;

                case CombatEventKind.Ended:
                    return 0f;

                default:
                    return 0.4f;
            }
        }

        /// <summary>How long a shot is in the air.</summary>
        public static float Flight(int tiles) => ShotBase + (ShotPerTile * (tiles < 0 ? 0 : tiles));
    }
}
