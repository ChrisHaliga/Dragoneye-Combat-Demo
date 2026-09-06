using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Whether the fight is stopped on somebody's question, as every client can see it.
    ///
    /// The server knows this as <see cref="CombatDirector.IsBusy"/>. A client does not run the
    /// director, so it used to find out by sending an order and having it refused in silence --
    /// which, with two enemies each owed a swing, read as the game eating clicks. Both postboxes
    /// now replicate whether they are holding a question open, and this is the one place the board
    /// asks.
    /// </summary>
    public static class FightPause
    {
        public static bool IsPaused =>
            (ClashCommands.Current != null && ClashCommands.Current.IsAsking)
            || (OpportunityCommands.Current != null && OpportunityCommands.Current.IsHolding);
    }
}
