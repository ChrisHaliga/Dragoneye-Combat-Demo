using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;

namespace Dragoneye.Scenarios
{
    // Declared inside the namespace: out here the bare name Hex would bind to the Dragoneye.Hex
    // namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>What a scripted creature does with its next decision.</summary>
    public enum OrderKind
    {
        Move,
        UseSkill
    }

    /// <summary>
    /// One decision, written in advance.
    ///
    /// The same shape a brain's decision takes, because it is handed to the fight through the
    /// same door: the director is asked to do it, and may refuse it. A refused order is not an
    /// error in the script -- some scenarios exist to prove a refusal -- so the trace records what
    /// was ordered as well as what happened. A refusal ends the turn, as it does for the game's
    /// own opponent, which is why orders are written per turn.
    /// </summary>
    public readonly struct Order
    {
        public readonly OrderKind Kind;
        public readonly Cell Destination;
        public readonly int SkillId;
        public readonly string Target;

        Order(OrderKind kind, Cell destination, int skillId, string target)
        {
            Kind = kind;
            Destination = destination;
            SkillId = skillId;
            Target = target;
        }

        public static Order Move(Cell destination) => new Order(OrderKind.Move, destination, 0, null);

        public static Order Move(Hex tile) => Move(Cell.Whole(tile));

        /// <summary>A skill aimed at an actor, by key. A self-directed skill names the user.</summary>
        public static Order Use(int skillId, string target) =>
            new Order(OrderKind.UseSkill, default, skillId, target);

        public override string ToString() =>
            Kind == OrderKind.Move ? $"move to {Destination}" : $"use skill {SkillId} on {Target}";
    }

    /// <summary>
    /// A creature in a scenario: which premade, which side, where it starts, and what it does.
    ///
    /// Named by a key so orders and expectations can point at it without knowing the id the
    /// fight will give it. Orders are grouped by turn: <see cref="Then"/> adds to the current
    /// turn, <see cref="NextTurn"/> starts the next, and a turn with nothing left in it ends. An
    /// actor handed to the game's own opponent with <see cref="RunByTheGame"/> ignores its
    /// script, which is how a scenario proves the opponent.
    /// </summary>
    public sealed class Actor
    {
        readonly List<List<Order>> m_Turns = new List<List<Order>>();

        public Actor(string key, string creatureId, Party party, Cell cell, Facing facing,
            int level = Progression.FirstLevel)
        {
            Key = key;
            CreatureId = creatureId;
            Party = party;
            Cell = cell;
            Facing = facing;
            Level = level;
        }

        public string Key { get; }

        /// <summary>The authored premade's id string, as the creature catalog names it.</summary>
        public string CreatureId { get; }

        public Party Party { get; }

        public Cell Cell { get; }

        public Facing Facing { get; }

        public int Level { get; }

        /// <summary>Whether the game's own opponent decides for this actor instead of a script.</summary>
        public bool Thinks { get; private set; }

        /// <summary>The script: one list of orders per turn, in the order the turns come.</summary>
        public IReadOnlyList<IReadOnlyList<Order>> Turns => m_Turns;

        public Actor Then(Order order)
        {
            if (m_Turns.Count == 0)
            {
                m_Turns.Add(new List<Order>());
            }

            m_Turns[m_Turns.Count - 1].Add(order);
            return this;
        }

        public Actor NextTurn()
        {
            m_Turns.Add(new List<Order>());
            return this;
        }

        /// <summary>Hands this actor to the game's opponent. Any orders are ignored.</summary>
        public Actor RunByTheGame()
        {
            Thinks = true;
            return this;
        }
    }

    /// <summary>
    /// Something the world does to itself mid-fight: a wall changing, at the start of a named
    /// actor's turn in a given round. What a breach, a door or a collapse will one day be.
    /// </summary>
    public readonly struct ScenarioEvent
    {
        public readonly int Round;
        public readonly string BeforeActor;
        public readonly WallSegment Segment;
        public readonly Wall Wall;

        public ScenarioEvent(int round, string beforeActor, WallSegment segment, Wall wall)
        {
            Round = round;
            BeforeActor = beforeActor;
            Segment = segment;
            Wall = wall;
        }
    }

    /// <summary>
    /// A fight written down, and what it proves.
    ///
    /// A map, the creatures on it with their scripts, anything the world does mid-fight, and a
    /// set of checks read against the world once the scripts have run out. The seed is part of
    /// the scenario: every roll the fight makes comes from it, so the checks can say in advance
    /// what a roll will be and then see whether the fight agreed.
    /// </summary>
    public sealed class Scenario
    {
        readonly List<Actor> m_Actors = new List<Actor>();
        readonly List<ScenarioEvent> m_Events = new List<ScenarioEvent>();

        public Scenario(string id, string title, string summary, int seed, MapRecipe map)
        {
            Id = id;
            Title = title;
            Summary = summary;
            Seed = seed;
            Map = map;
        }

        public string Id { get; }

        public string Title { get; }

        public string Summary { get; }

        /// <summary>What the fight rolls from. Never zero: zero would mean "pick one".</summary>
        public int Seed { get; }

        public MapRecipe Map { get; }

        public IReadOnlyList<Actor> Actors => m_Actors;

        public IReadOnlyList<ScenarioEvent> Events => m_Events;

        /// <summary>
        /// Rounds the fight is allowed before the checks are read regardless. A script that runs
        /// out ends the fight sooner; this is the ceiling for one the game's opponent plays.
        /// </summary>
        public int MaxRounds { get; private set; } = 6;

        /// <summary>The checks, read against the world at the end.</summary>
        public Func<IScenarioWorld, IReadOnlyList<ScenarioCheck>> Expect { get; private set; } =
            world => Array.Empty<ScenarioCheck>();

        public Scenario With(Actor actor)
        {
            m_Actors.Add(actor);
            return this;
        }

        public Scenario At(int round, string beforeActor, WallSegment segment, Wall wall)
        {
            m_Events.Add(new ScenarioEvent(round, beforeActor, segment, wall));
            return this;
        }

        public Scenario Lasting(int rounds)
        {
            MaxRounds = rounds < 1 ? 1 : rounds;
            return this;
        }

        public Scenario Expecting(Func<IScenarioWorld, IReadOnlyList<ScenarioCheck>> expect)
        {
            Expect = expect ?? Expect;
            return this;
        }

        /// <summary>The actor with this key, or null.</summary>
        public Actor Find(string key)
        {
            foreach (var actor in m_Actors)
            {
                if (actor.Key == key)
                {
                    return actor;
                }
            }

            return null;
        }
    }

    /// <summary>What a scenario came to: its checks, and the trace they were read against.</summary>
    public sealed class ScenarioResult
    {
        public ScenarioResult(Scenario scenario, IReadOnlyList<ScenarioCheck> checks,
            IReadOnlyList<string> trace, string fault = null)
        {
            Scenario = scenario;
            Checks = checks ?? Array.Empty<ScenarioCheck>();
            Trace = trace ?? Array.Empty<string>();
            Fault = fault;
        }

        public Scenario Scenario { get; }

        public string Id => Scenario.Id;

        public IReadOnlyList<ScenarioCheck> Checks { get; }

        /// <summary>What the fight reported, one line per entry, for reading a failure.</summary>
        public IReadOnlyList<string> Trace { get; }

        /// <summary>Why the checks could not be read at all, when they could not.</summary>
        public string Fault { get; }

        public int PassedCount
        {
            get
            {
                var passed = 0;

                foreach (var check in Checks)
                {
                    if (check.Passed)
                    {
                        passed++;
                    }
                }

                return passed;
            }
        }

        public bool Passed => Fault == null && PassedCount == Checks.Count;

        public override string ToString() =>
            Fault != null ? $"{Id}: FAULT {Fault}" : $"{Id}: {PassedCount}/{Checks.Count} {(Passed ? "PASSED" : "FAILED")}";
    }
}
