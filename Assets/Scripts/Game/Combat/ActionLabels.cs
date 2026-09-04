namespace Dragoneye.Game
{
    /// <summary>
    /// The words shown beside the cursor for a priced action.
    ///
    /// Separate from <see cref="ActionResolver"/>, which the server runs too and which has no
    /// business holding English. One place for the wording all the same, so a new
    /// <see cref="ActionRefusal"/> cannot be added without deciding what the player is told -- and
    /// so it stays testable, which it would not be inside a view.
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
                case ActionRefusal.OutOfRange:
                    return "Out of range";

                // Nothing to say. Hovering your own creature, empty space you already occupy, or the
                // board during someone else's turn should leave the cursor clean rather than
                // explaining itself.
                case ActionRefusal.NotYours:
                case ActionRefusal.Friendly:
                case ActionRefusal.NoTarget:
                    return string.Empty;

                case ActionRefusal.TooExpensive:
                    return $"{Name(plan.Action)} -- {plan.Cost} AP (not enough)";
            }

            return $"{Name(plan.Action)} -- {plan.Cost} AP";
        }

        static string Name(BoardAction action) => action == BoardAction.Attack ? "Attack" : "Move";
    }
}
