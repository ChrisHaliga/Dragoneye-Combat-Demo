using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;

namespace Dragoneye.Scenarios
{
    /// <summary>What kind of thing the fight reported.</summary>
    public enum TraceKind
    {
        /// <summary>A scripted decision was handed to the fight. Whether it happened is a later entry.</summary>
        Ordered,

        TurnBegan,
        Moved,

        /// <summary>An uncontested skill landed: a heal, a breath, a swing at nobody.</summary>
        Acted,

        /// <summary>A skill that rolls to hit was rolled. <see cref="TraceEntry.Landed"/> says how it went.</summary>
        Shot,

        /// <summary>A contested attack was answered and revealed.</summary>
        Clash,

        Fell,

        /// <summary>A creature offered a swing at somebody walking past kept its element.</summary>
        HeldBack,

        /// <summary>Health came back at the start of a turn.</summary>
        Recovered,

        /// <summary>A wall changed.</summary>
        Wall
    }

    /// <summary>
    /// One thing the fight reported, in the order it was reported.
    ///
    /// Actors are named by their scenario keys. Fields that do not apply to a kind are left at
    /// their defaults; the kind says which to read.
    /// </summary>
    public readonly struct TraceEntry
    {
        public readonly TraceKind Kind;
        public readonly int Round;
        public readonly string Actor;
        public readonly string Target;
        public readonly int SkillId;
        public readonly Cell From;
        public readonly Cell To;

        /// <summary>A shot's percent chance, or health recovered.</summary>
        public readonly int Amount;

        public readonly bool Landed;
        public readonly ClashOutcome Outcome;
        public readonly IReadOnlyList<Element> AttackerElements;
        public readonly IReadOnlyList<Element> DefenderElements;
        public readonly Order Order;
        public readonly WallSegment Segment;
        public readonly Wall WallAfter;

        public TraceEntry(TraceKind kind, int round, string actor = null, string target = null,
            int skillId = 0, Cell from = default, Cell to = default, int amount = 0,
            bool landed = false, ClashOutcome outcome = default,
            IReadOnlyList<Element> attackerElements = null,
            IReadOnlyList<Element> defenderElements = null, Order order = default,
            WallSegment segment = default, Wall wallAfter = default)
        {
            Kind = kind;
            Round = round;
            Actor = actor;
            Target = target;
            SkillId = skillId;
            From = from;
            To = to;
            Amount = amount;
            Landed = landed;
            Outcome = outcome;
            AttackerElements = attackerElements ?? System.Array.Empty<Element>();
            DefenderElements = defenderElements ?? System.Array.Empty<Element>();
            Order = order;
            Segment = segment;
            WallAfter = wallAfter;
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case TraceKind.Ordered: return $"R{Round} {Actor} ordered: {Order}";
                case TraceKind.TurnBegan: return $"R{Round} {Actor}'s turn";
                case TraceKind.Moved: return $"R{Round} {Actor} moved {From} -> {To}";
                case TraceKind.Acted: return $"R{Round} {Actor} used {SkillId}" + (Target != null ? $" on {Target}" : "");
                case TraceKind.Shot: return $"R{Round} {Actor} shot {SkillId} at {Target}: {Amount}% {(Landed ? "hit" : "miss")}";
                case TraceKind.Clash: return $"R{Round} {Actor} vs {Target} with {SkillId}: {Outcome}";
                case TraceKind.Fell: return $"R{Round} {Actor} fell";
                case TraceKind.HeldBack: return $"R{Round} {Actor} held back from {Target}";
                case TraceKind.Recovered: return $"R{Round} {Actor} recovered {Amount}";
                case TraceKind.Wall: return $"R{Round} wall {Segment}: {WallAfter}";
                default: return Kind.ToString();
            }
        }
    }

    /// <summary>
    /// The fight as it stands once the scripts have run out, for the checks to read.
    ///
    /// Everything a check might want: where each actor is and what state it is in, the rules'
    /// own view of the board, the skills exactly as each actor holds them, and the trace of what
    /// the fight reported. Implemented by the game, read by scenarios, and small enough that a
    /// test can implement it by hand.
    /// </summary>
    public interface IScenarioWorld
    {
        bool Exists(string actor);

        /// <summary>Whether the actor is still on the board. A fallen one answers nothing else.</summary>
        bool IsAlive(string actor);

        Cell CellOf(string actor);

        Facing FacingOf(string actor);

        int HpOf(string actor);

        int MaxHpOf(string actor);

        int ArmourOf(string actor);

        /// <summary>What the armour pool was when the fight began.</summary>
        int MaxArmourOf(string actor);

        /// <summary>What the actor's pool holds now.</summary>
        ElementCounts PoolOf(string actor);

        /// <summary>What the actor's pool held at the start.</summary>
        ElementCounts StartingPoolOf(string actor);

        /// <summary>Everything the actor can do, in the order it holds them, attributes folded in.</summary>
        IReadOnlyList<SkillSpec> SkillsOf(string actor);

        /// <summary>A skill as this actor holds it, or null when it does not.</summary>
        SkillSpec SkillOf(string actor, int skillId);

        bool HasAdvantage(string actor);

        /// <summary>Initiative: what the turn order is built from.</summary>
        int SpeedOf(string actor);

        /// <summary>Health back at the start of each of its turns.</summary>
        int RegenOf(string actor);

        /// <summary>What a terrain name in a recipe refers to. Null for open ground.</summary>
        TerrainType TerrainNamed(string name);

        IGridRules Grid { get; }

        IElementMatchup Matchups { get; }

        /// <summary>
        /// Whether a side won the fight, and which.
        ///
        /// Not merely whether the fight stopped: a scenario stops as soon as its script runs
        /// out, and that is not a victory. A check that wants "the match ended because a side
        /// was wiped out" wants this.
        /// </summary>
        bool IsWon { get; }

        Party Winner { get; }

        int Round { get; }

        IReadOnlyList<TraceEntry> Trace { get; }
    }

    /// <summary>One thing a scenario claims, and whether the fight bore it out.</summary>
    public readonly struct ScenarioCheck
    {
        public readonly string What;
        public readonly bool Passed;
        public readonly string Detail;

        public ScenarioCheck(string what, bool passed, string detail = "")
        {
            What = what;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public static ScenarioCheck That(string what, bool passed, string detail = "") =>
            new ScenarioCheck(what, passed, detail);

        public static ScenarioCheck Equal<T>(string what, T expected, T actual) =>
            new ScenarioCheck(what, EqualityComparer<T>.Default.Equals(expected, actual),
                $"expected {expected}, got {actual}");

        public override string ToString() =>
            $"{(Passed ? "PASS" : "FAIL")} {What}{(Detail.Length > 0 && !Passed ? $" ({Detail})" : "")}";
    }
}
