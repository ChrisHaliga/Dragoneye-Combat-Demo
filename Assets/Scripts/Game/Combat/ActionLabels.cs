using Dragoneye.Combat;

namespace Dragoneye.Game
{
    /// <summary>
    /// The words shown beside the cursor for a priced action.
    ///
    /// Separate from <see cref="ActionResolver"/>, and in a different assembly: Dragoneye.Combat is
    /// the rules the server runs, and those have no business holding English. One place for the
    /// wording all the same, so a new <see cref="ActionRefusal"/> cannot be added without deciding
    /// what the player is told -- and a static class, so it stays testable outside a view.
    /// </summary>
    public static class ActionLabels
    {
        public static string Describe(ActionPlan plan)
        {
            switch (plan.Refusal)
            {
                case ActionRefusal.NotYourTurn:
                    return "Not your turn";
                case ActionRefusal.Unreachable:
                    return "No route";

                // Nothing to say. Hovering a creature, empty space you already occupy, or the board
                // during someone else's turn should leave the cursor clean rather than explaining
                // itself -- and reaching a creature is now a skill's job, offered on the bar.
                case ActionRefusal.NotYours:
                case ActionRefusal.Occupied:
                case ActionRefusal.NoTarget:
                    return string.Empty;

                case ActionRefusal.TooExpensive:
                    return $"Move -- {plan.Cost} AP (not enough)";
            }

            return $"Move -- {plan.Cost} AP";
        }
    }
}
