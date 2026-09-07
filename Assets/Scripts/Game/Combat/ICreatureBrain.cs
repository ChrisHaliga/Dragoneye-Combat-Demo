using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;
using Dragoneye.Hex;

namespace Dragoneye.Game.Combat
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// One creature as a brain sees it. Coordinates, numbers and what it can do -- no components.
    ///
    /// Flat and read-only so a brain cannot reach through it and change the world -- deciding and
    /// doing are separate, which is what lets a decision be tested by asserting on its return value.
    ///
    /// It carries the creature's skills and its element ledger because there is no generic attack
    /// any more: what a creature can do to somebody else is entirely a question of what it knows and
    /// what it is holding, and a brain that could not see either could only ever decide to walk.
    /// </summary>
    public readonly struct BrainView
    {
        public readonly uint Id;
        public readonly Cell Cell;
        public readonly Party Party;
        public readonly Ap CurrentAp;
        public readonly int CurrentHp;

        /// <summary>Everything this creature could use, whether or not it can afford it now.</summary>
        public readonly IReadOnlyList<SkillSpec> Skills;

        /// <summary>What it is holding, so affordability is the same question the server asks.</summary>
        public readonly ElementLedger Ledger;

        /// <summary>What one tile costs it, so a brain in plate does not plan a walk in leather.</summary>
        public readonly Ap StepCost;

        public BrainView(uint id, Cell cell, Party party, Ap currentAp, int currentHp,
            IReadOnlyList<SkillSpec> skills = null, ElementLedger ledger = default,
            Ap? stepCost = null)
        {
            Id = id;
            Cell = cell;
            Party = party;
            CurrentAp = currentAp;
            CurrentHp = currentHp;
            Skills = skills ?? System.Array.Empty<SkillSpec>();
            Ledger = ledger;
            StepCost = stepCost ?? CombatRules.BaseStepCost;
        }
    }

    /// <summary>What kind of thing a brain decided to do.</summary>
    public enum BrainAction
    {
        /// <summary>Nothing worth doing. The caller should end the turn.</summary>
        None,

        Move,
        UseSkill
    }

    /// <summary>What a brain decided to do. One action; the caller asks again afterwards.</summary>
    public readonly struct BrainDecision
    {
        public readonly BrainAction Action;

        /// <summary>The creature to aim at. Meaningful only for <see cref="BrainAction.UseSkill"/>.</summary>
        public readonly uint TargetId;

        /// <summary>Which skill to use. Meaningful only for <see cref="BrainAction.UseSkill"/>.</summary>
        public readonly int SkillId;

        /// <summary>Where to move. Meaningful only for <see cref="BrainAction.Move"/>.</summary>
        public readonly Cell Destination;

        BrainDecision(BrainAction action, uint targetId, int skillId, Cell destination)
        {
            Action = action;
            TargetId = targetId;
            SkillId = skillId;
            Destination = destination;
        }

        public static readonly BrainDecision Pass =
            new BrainDecision(BrainAction.None, 0, 0, default);

        public static BrainDecision UseSkill(int skillId, uint targetId) =>
            new BrainDecision(BrainAction.UseSkill, targetId, skillId, default);

        public static BrainDecision MoveTo(Cell destination) =>
            new BrainDecision(BrainAction.Move, 0, 0, destination);
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
        int CostTo(Cell from, Cell to);

        /// <summary>The cheapest route, destination last, empty if unreachable.</summary>
        IReadOnlyList<Cell> PathTo(Cell from, Cell to);

        /// <summary>Whether a creature stands on this hex.</summary>
        bool IsOccupied(Cell cell);

        /// <summary>
        /// The tile within this many steps that ends nearest the target, when the target itself
        /// cannot be reached. False when nowhere is nearer than where the creature stands.
        /// </summary>
        bool TryClosest(Cell from, Cell target, int budget, out Cell tile);

        /// <summary>Every cell one step from this one, by the map's rules: across open edges only.</summary>
        void Neighbours(Cell of, List<Cell> into);

        /// <summary>Steps it costs to enter a cell. One for open ground.</summary>
        int StepsToEnter(Cell cell);

        /// <summary>Whether nothing opaque stands between two cells. No line, no attack.</summary>
        bool HasLine(Cell from, Cell to);
    }
}
