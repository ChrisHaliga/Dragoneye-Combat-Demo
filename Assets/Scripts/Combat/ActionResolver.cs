namespace Dragoneye.Combat
{
    /// <summary>
    /// What clicking a hex would do.
    ///
    /// Moving, and nothing else. There used to be a bare attack here, standing in for skills until
    /// there were any; now that a weapon grants one, a creature with no skill has no attack, which
    /// is the correct answer rather than a gap. A click on an occupied hex reads its card.
    /// </summary>
    public enum BoardAction
    {
        None,
        Move,

        /// <summary>Use the armed skill, walking into range first if that is what it takes.</summary>
        UseSkill
    }

    /// <summary>Why an action is not available. <see cref="None"/> means it is.</summary>
    public enum ActionRefusal
    {
        None,
        NotYourTurn,
        NotYours,
        NoTarget,
        Unreachable,

        /// <summary>Somebody is standing there. Clicking reads their card; a skill has to be armed.</summary>
        Occupied,

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

        /// <summary>Everything the click costs, in half-units. Walking included.</summary>
        public readonly Ap Cost;

        /// <summary>
        /// The walking half of it, which is zero when the target is already in reach.
        ///
        /// Kept apart from the total so the cursor can say "Move 1.5 + Strike 1" rather than a bare
        /// 2.5 -- the two halves are separately worth knowing, because one of them is avoidable by
        /// standing somewhere else first.
        /// </summary>
        public readonly Ap MoveCost;

        /// <summary>The skill being aimed, when there is one. Null for a plain move.</summary>
        public readonly SkillSpec Skill;

        public readonly ActionRefusal Refusal;

        public ActionPlan(BoardAction action, Ap cost, ActionRefusal refusal,
            Ap moveCost = default, SkillSpec skill = null)
        {
            Action = action;
            Cost = cost;
            Refusal = refusal;
            MoveCost = moveCost;
            Skill = skill;
        }

        /// <summary>True when the click would actually do something.</summary>
        public bool IsAllowed => Action != BoardAction.None && Refusal == ActionRefusal.None;

        /// <summary>An action the player could take if they had the AP. Priced, but refused.</summary>
        public bool IsUnaffordable => Refusal == ActionRefusal.TooExpensive;

        public static readonly ActionPlan Nothing =
            new ActionPlan(BoardAction.None, Ap.Zero, ActionRefusal.NoTarget);
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
        /// <param name="moveSteps">
        /// Tiles along the cheapest route to the hovered hex, or -1 if there is no route. Ignored
        /// when the hex is occupied. A count of tiles, not a price -- pricing is this method.
        /// </param>
        public static ActionPlan Resolve(bool isActorsTurn, bool controlsActor, Ap currentAp,
            bool targetOccupied, int moveSteps)
        {
            if (!controlsActor)
            {
                return new ActionPlan(BoardAction.None, Ap.Zero, ActionRefusal.NotYours);
            }

            if (!isActorsTurn)
            {
                return new ActionPlan(BoardAction.None, Ap.Zero, ActionRefusal.NotYourTurn);
            }

            if (targetOccupied)
            {
                // Nothing to price. Reaching somebody is a skill's job, and which skill is a
                // decision the player makes on the bar before they click the board.
                return new ActionPlan(BoardAction.None, Ap.Zero, ActionRefusal.Occupied);
            }

            return ResolveMove(currentAp, moveSteps);
        }

        /// <summary>
        /// Prices a click while a skill is armed.
        ///
        /// Walking is folded into the action rather than offered separately: with a skill in hand a
        /// click means "use this on that", and the steps in between are part of the price. That is
        /// also why a click on empty ground offers nothing -- a misclick beside an enemy should not
        /// quietly spend the turn walking there.
        /// </summary>
        /// <param name="stepsToReach">
        /// Tiles along the cheapest route to the nearest hex the skill would reach from. Zero when
        /// the target is already in reach, -1 when there is no such hex.
        /// </param>
        public static ActionPlan ResolveSkill(bool isActorsTurn, bool controlsActor, Ap currentAp,
            SkillSpec skill, bool targetIsCreature, bool targetIsEnemy, int stepsToReach)
        {
            if (!controlsActor)
            {
                return new ActionPlan(BoardAction.None, Ap.Zero, ActionRefusal.NotYours);
            }

            if (!isActorsTurn)
            {
                return new ActionPlan(BoardAction.None, Ap.Zero, ActionRefusal.NotYourTurn);
            }

            if (skill == null)
            {
                return ActionPlan.Nothing;
            }

            // What a creature-directed skill is for. Aiming one at bare ground is not a cheaper
            // version of aiming it at somebody, it is nothing at all.
            if (skill.Target == SkillTarget.Creature && (!targetIsCreature || !targetIsEnemy))
            {
                return new ActionPlan(BoardAction.None, Ap.Zero, ActionRefusal.NoTarget,
                    skill: skill);
            }

            if (stepsToReach < 0)
            {
                return new ActionPlan(BoardAction.UseSkill, skill.ApCost, ActionRefusal.Unreachable,
                    skill: skill);
            }

            var move = CombatRules.MoveCost(stepsToReach);
            var total = move + skill.ApCost;

            return currentAp < total
                ? new ActionPlan(BoardAction.UseSkill, total, ActionRefusal.TooExpensive, move, skill)
                : new ActionPlan(BoardAction.UseSkill, total, ActionRefusal.None, move, skill);
        }

        static ActionPlan ResolveMove(Ap currentAp, int moveSteps)
        {
            if (moveSteps < 0)
            {
                return new ActionPlan(BoardAction.None, Ap.Zero, ActionRefusal.Unreachable);
            }

            if (moveSteps == 0)
            {
                // The hex the actor is already standing on.
                return ActionPlan.Nothing;
            }

            var cost = CombatRules.MoveCost(moveSteps);

            return currentAp < cost
                ? new ActionPlan(BoardAction.Move, cost, ActionRefusal.TooExpensive)
                : new ActionPlan(BoardAction.Move, cost, ActionRefusal.None);
        }
    }
}
