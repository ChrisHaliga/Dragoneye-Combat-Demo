namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Where the fight writes down what it did.
    ///
    /// One call, from the director and its conductors, at the moment a thing happens. The round
    /// is stamped on here so no writer has to remember to, and the event goes out through the
    /// announcer to every machine -- this one included, where the playback queues it like any
    /// other.
    ///
    /// Server only in effect: the announcer refuses to send from anywhere else.
    /// </summary>
    public static class FightRecord
    {
        /// <summary>The round the fight is on. Zero before it begins.</summary>
        public static int Round => TurnState.Current != null ? TurnState.Current.Round : 0;

        public static void Say(CombatEvent e)
        {
            if (e == null)
            {
                return;
            }

            e.Round = Round;
            CombatAnnouncer.Current?.ServerSend(e);
        }
    }
}
