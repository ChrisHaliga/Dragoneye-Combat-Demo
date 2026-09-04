using System.Collections.Generic;
using Dragoneye.Combat;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// One creature as a brain sees it. Coordinates and numbers, no components.
    ///
    /// Flat and read-only so a brain cannot reach through it and change the world -- deciding and
    /// doing are separate, which is what lets a decision be tested by asserting on its return value.
    /// </summary>
    public readonly struct BrainView
    {
        public readonly uint Id;
        public readonly Hex Cell;
        public readonly Party Party;
        public readonly int CurrentAp;
        public readonly int CurrentHp;

        public BrainView(uint id, Hex cell, Party party, int currentAp, int currentHp)
        {
            Id = id;
            Cell = cell;
            Party = party;
            CurrentAp = currentAp;
            CurrentHp = currentHp;
        }
    }

    /// <summary>What a brain decided to do. One action; the caller asks again afterwards.</summary>
    public readonly struct BrainDecision
    {
        public readonly BoardAction Action;

        /// <summary>The creature to attack. Meaningful only for <see cref="BoardAction.Attack"/>.</summary>
        public readonly uint TargetId;

        /// <summary>Where to move. Meaningful only for <see cref="BoardAction.Move"/>.</summary>
        public readonly Hex Destination;

        BrainDecision(BoardAction action, uint targetId, Hex destination)
        {
            Action = action;
            TargetId = targetId;
            Destination = destination;
        }

        /// <summary>Nothing worth doing. The caller should end the turn.</summary>
        public static readonly BrainDecision Pass =
            new BrainDecision(BoardAction.None, 0, default);

        public static BrainDecision Attack(uint targetId) =>
            new BrainDecision(BoardAction.Attack, targetId, default);

        public static BrainDecision MoveTo(Hex destination) =>
            new BrainDecision(BoardAction.Move, 0, destination);
    }

    /// <summary>
    /// Decides what a computer-run creature does next.
    ///
    /// One action per call rather than a whole turn, so the caller keeps control of pacing and can
    /// stop early -- and so a brain never needs to know how a move is executed or replicated.
    ///
    /// Deliberately narrow, because it is meant to be replaced. A better opponent is a new
    /// implementation of this one method; nothing outside it needs to change, and
    /// <see cref="BasicBrain"/> can be deleted the day something better exists.
    /// </summary>
    public interface ICreatureBrain
    {
        /// <summary>
        /// The next action for <paramref name="actor"/>, or <see cref="BrainDecision.Pass"/>.
        /// </summary>
        /// <param name="others">Every other living creature on the board.</param>
        /// <param name="board">Map and occupancy, for costing routes.</param>
        BrainDecision Decide(BrainView actor, IReadOnlyList<BrainView> others, IBoardQuery board);
    }

    /// <summary>
    /// What a brain is allowed to ask about the board.
    ///
    /// An interface rather than the map itself, so a test can answer these three questions with a
    /// dictionary and no Unity types at all.
    /// </summary>
    public interface IBoardQuery
    {
        /// <summary>Steps along the cheapest route, or -1 if there is none.</summary>
        int CostTo(Hex from, Hex to);

        /// <summary>The cheapest route, destination last, empty if unreachable.</summary>
        IReadOnlyList<Hex> PathTo(Hex from, Hex to);

        /// <summary>Whether a creature stands on this hex.</summary>
        bool IsOccupied(Hex hex);
    }
}
