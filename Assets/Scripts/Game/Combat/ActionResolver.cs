namespace Dragoneye.Game
{
    /// <summary>What clicking a hex would do.</summary>
    public enum BoardAction
    {
        None,
        Move,
        Attack
    }

    /// <summary>Why an action is not available. <see cref="None"/> means it is.</summary>
    public enum ActionRefusal
    {
        None,
        NotYourTurn,
        NotYours,
        NoTarget,
        Unreachable,
        OutOfRange,
        Friendly,
        TooExpensive
    }

    /// <summary>
    /// What the player would get for a click, priced.
    ///
    /// Carries the refusal as well as the action, because the cursor has to distinguish "nothing
    /// here" from "you cannot afford this" -- they are the same absence of an action and want very
    /// different words.
    /// </summary>
    public readonly struct ActionPlan
    {
        public readonly BoardAction Action;
        public readonly int Cost;
        public readonly ActionRefusal Refusal;

        public ActionPlan(BoardAction action, int cost, ActionRefusal refusal)
        {
            Action = action;
            Cost = cost;
            Refusal = refusal;
        }

        /// <summary>True when the click would actually do something.</summary>
        public bool IsAllowed => Action != BoardAction.None && Refusal == ActionRefusal.None;

        /// <summary>An action the player could take if they had the AP. Priced, but refused.</summary>
        public bool IsUnaffordable => Refusal == ActionRefusal.TooExpensive;

        public static readonly ActionPlan Nothing =
            new ActionPlan(BoardAction.None, 0, ActionRefusal.NoTarget);
    }

    /// <summary>
    /// Turns "the cursor is over this hex" into "this is what would happen, and what it costs".
    ///
    /// Pure, and the single authority for both the label under the cursor and the command that gets
    /// sent. Two implementations of this question -- one to display and one to act -- is how a UI
    /// ends up promising a move the server then refuses.
    ///
    /// It does not decide legality of the route; the caller supplies a path cost, because computing
    /// one needs the map and the occupancy and this needs neither.
    /// </summary>
    public static class ActionResolver
    {
        /// <summary>
        /// Prices a click.
        /// </summary>
        /// <param name="isActorsTurn">Whether the acting creature is the one whose turn it is.</param>
        /// <param name="controlsActor">Whether the local player may command the actor at all.</param>
        /// <param name="currentAp">The actor's remaining AP.</param>
        /// <param name="targetOccupied">Whether a creature stands on the hovered hex.</param>
        /// <param name="targetIsEnemy">
        /// Whether that creature is on another side. Ignored when the hex is empty.
        /// </param>
        /// <param name="distanceToTarget">Hex distance from actor to the hovered hex.</param>
        /// <param name="moveCost">
        /// Steps along the cheapest route to the hovered hex, or -1 if there is no route. Ignored
        /// when the hex is occupied.
        /// </param>
        public static ActionPlan Resolve(bool isActorsTurn, bool controlsActor, int currentAp,
            bool targetOccupied, bool targetIsEnemy, int distanceToTarget, int moveCost)
        {
            if (!controlsActor)
            {
                return new ActionPlan(BoardAction.None, 0, ActionRefusal.NotYours);
            }

            if (!isActorsTurn)
            {
                return new ActionPlan(BoardAction.None, 0, ActionRefusal.NotYourTurn);
            }

            if (targetOccupied)
            {
                return ResolveAttack(currentAp, targetIsEnemy, distanceToTarget);
            }

            return ResolveMove(currentAp, moveCost);
        }

        static ActionPlan ResolveAttack(int currentAp, bool targetIsEnemy, int distance)
        {
            if (!targetIsEnemy)
            {
                // Selecting an ally to read its card is handled elsewhere; there is simply no
                // action to price here.
                return new ActionPlan(BoardAction.None, 0, ActionRefusal.Friendly);
            }

            if (!CombatRules.InRange(distance))
            {
                // Priced anyway. "Attack -- 2 AP, out of range" tells the player what to fix; a bare
                // "no action" does not.
                return new ActionPlan(BoardAction.Attack, CombatRules.AttackApCost,
                    ActionRefusal.OutOfRange);
            }

            return currentAp < CombatRules.AttackApCost
                ? new ActionPlan(BoardAction.Attack, CombatRules.AttackApCost, ActionRefusal.TooExpensive)
                : new ActionPlan(BoardAction.Attack, CombatRules.AttackApCost, ActionRefusal.None);
        }

        static ActionPlan ResolveMove(int currentAp, int moveCost)
        {
            if (moveCost < 0)
            {
                return new ActionPlan(BoardAction.None, 0, ActionRefusal.Unreachable);
            }

            if (moveCost == 0)
            {
                // The hex the actor is already standing on.
                return ActionPlan.Nothing;
            }

            var cost = CombatRules.MoveCost(moveCost);

            return currentAp < cost
                ? new ActionPlan(BoardAction.Move, cost, ActionRefusal.TooExpensive)
                : new ActionPlan(BoardAction.Move, cost, ActionRefusal.None);
        }

        /// <summary>
        /// The words shown beside the cursor. Kept next to the rule that produced them so a new
        /// refusal cannot be added without deciding what it says.
        /// </summary>
        public static string Describe(ActionPlan plan)
        {
            switch (plan.Refusal)
            {
                case ActionRefusal.NotYourTurn:
                    return "Not your turn";
                case ActionRefusal.NotYours:
                    return string.Empty;
                case ActionRefusal.Unreachable:
                    return "No route";
                case ActionRefusal.OutOfRange:
                    return "Out of range";
                case ActionRefusal.Friendly:
                    return string.Empty;
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
