using System;
using System.Collections;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;
using Dragoneye.Multiplayer;
using Dragoneye.Scenarios;
using UnityEngine;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Runs a scenario in the arena: builds its map, places its actors, hands their scripts to
    /// the fight, writes down everything the fight reports, and reads the scenario's checks
    /// against the world once the scripts have run out.
    ///
    /// The fight is the real one -- the same director, conductors, turn runner and announcer a
    /// match uses -- with a scripted brain in place of the thinking one and the scenario's seed
    /// in the dice. What this adds is the record: a trace of what was announced, by actor key,
    /// and a snapshot of the world for the checks to read. Server only in effect, since only the
    /// host runs scenarios; every machine could read the same announcements.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScenarioRunner : MonoBehaviour
    {
        [SerializeField, Tooltip("What the recipes' 'grass' means.")]
        TerrainType m_Grass;

        [SerializeField, Tooltip("What the recipes' 'stone' means.")]
        TerrainType m_Stone;

        [SerializeField, Min(0f), Tooltip("Seconds a finished scenario's report is left up before "
             + "the next queued one begins. The last one in a run stays up until it is dismissed.")]
        float m_NextDelay = 3f;

        /// <summary>The runner in the loaded arena, or null.</summary>
        public static ScenarioRunner Current { get; private set; }

        /// <summary>A scenario finished and its checks were read.</summary>
        public static event Action<ScenarioResult> Finished;

        readonly Dictionary<uint, string> m_KeyOf = new Dictionary<uint, string>();
        readonly Dictionary<string, CreatureState> m_Creatures = new Dictionary<string, CreatureState>();
        readonly Dictionary<string, Facts> m_Facts = new Dictionary<string, Facts>();
        readonly List<TraceEntry> m_Trace = new List<TraceEntry>();
        readonly List<ScenarioEvent> m_Pending = new List<ScenarioEvent>();
        readonly List<ScenarioCheck> m_Faults = new List<ScenarioCheck>();

        Scenario m_Scenario;
        ScriptedBrain m_Brain;
        AuthoredMapDefinition m_Definition;
        bool m_Listening;
        bool m_Finished;

        /// <summary>What was true of an actor when it was placed, kept past its death.</summary>
        sealed class Facts
        {
            public int MaxHp;
            public int MaxArmour;
            public ElementCounts StartingPool;
            public IReadOnlyList<SkillSpec> Skills;
            public bool Advantage;
            public int Speed;
            public int Regen;
        }

        public Scenario Scenario => m_Scenario;

        public bool IsRunning => m_Scenario != null && !m_Finished;

        public ScenarioResult Result { get; private set; }

        /// <summary>
        /// Seconds until the next queued scenario begins, or a negative number when none is
        /// waiting. What the report counts down.
        /// </summary>
        public float SecondsToNext { get; private set; } = -1f;

        void Awake() => Current = this;

        void OnDestroy()
        {
            Unlisten();

            if (m_Definition != null)
            {
                Destroy(m_Definition);
            }

            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>
        /// Server only. Puts the scenario on the board and opens the fight.
        ///
        /// The map first, so the spawner places creatures on the scenario's ground and not the
        /// arena's; then the actors, in the order they were written, which is the order the
        /// fight will break initiative ties in; then the fight, with the scenario's seed and a
        /// brain that reads the scripts.
        /// </summary>
        public void Begin(Scenario scenario, MatchSpawner spawner)
        {
            var context = ArenaContext.Current;
            var director = CombatDirector.Current;

            if (scenario == null || context == null || context.Map == null || director == null || spawner == null)
            {
                Debug.LogError("A scenario cannot begin: the arena is not wired.", this);
                return;
            }

            m_Scenario = scenario;
            m_Finished = false;
            m_Trace.Clear();
            m_KeyOf.Clear();
            m_Creatures.Clear();
            m_Facts.Clear();
            m_Faults.Clear();
            m_Pending.Clear();
            m_Pending.AddRange(scenario.Events);

            m_Definition = ScriptableObject.CreateInstance<AuthoredMapDefinition>();
            m_Definition.Author(scenario.Map, Palette());
            context.Map.Rebuild(m_Definition);

            m_Brain = new ScriptedBrain(IdOf, () => TurnState.Current != null ? TurnState.Current.Round : 0);
            m_Brain.Ordered += OnOrdered;

            Listen();

            var catalog = CreatureState.Catalog;

            foreach (var actor in scenario.Actors)
            {
                var id = CreatureCatalog.HashId(actor.CreatureId);

                if (catalog == null || catalog.Resolve(id) == null)
                {
                    m_Faults.Add(ScenarioCheck.That($"the catalog knows '{actor.CreatureId}' for {actor.Key}", false));
                    continue;
                }

                if (!context.Map.Grid.IsWalkable(actor.Cell) || context.Units.IsOccupied(actor.Cell))
                {
                    m_Faults.Add(ScenarioCheck.That($"{actor.Key} starts on free, standable ground at {actor.Cell}", false));
                    continue;
                }

                var creature = spawner.SpawnCreature(id, actor.Party, actor.Level, actor.Cell, actor.Facing,
                    OrdinalOf(scenario, actor));

                if (creature == null)
                {
                    m_Faults.Add(ScenarioCheck.That($"{actor.Key} spawned", false));
                    continue;
                }

                m_KeyOf[creature.TurnId] = actor.Key;
                m_Creatures[actor.Key] = creature;
                m_Facts[actor.Key] = new Facts
                {
                    MaxHp = creature.MaxHp,
                    MaxArmour = creature.MaxArmour,
                    StartingPool = creature.StartingPool,
                    Skills = new List<SkillSpec>(creature.SkillCommands.Skills),
                    Advantage = creature.HasAdvantage,
                    Speed = creature.Speed,
                    Regen = creature.Regen
                };

                m_Brain.Register(creature.TurnId, actor);
            }

            Debug.Log($"[Scenario] {scenario.Title} begins with seed {scenario.Seed}.", this);

            director.ServerBeginMatch(scenario.Seed, m_Brain);
        }

        List<AuthoredMapDefinition.TerrainEntry> Palette() =>
            new List<AuthoredMapDefinition.TerrainEntry>
            {
                new AuthoredMapDefinition.TerrainEntry { Name = Ground.Grass, Terrain = m_Grass },
                new AuthoredMapDefinition.TerrainEntry { Name = Ground.Stone, Terrain = m_Stone }
            };

        /// <summary>Which of its kind an actor is, so two goblins read as Goblin 1 and Goblin 2.</summary>
        static int OrdinalOf(Scenario scenario, Actor actor)
        {
            var kind = 0;
            var mine = 0;

            foreach (var other in scenario.Actors)
            {
                if (other.CreatureId != actor.CreatureId)
                {
                    continue;
                }

                kind++;

                if (other == actor)
                {
                    mine = kind;
                }
            }

            return kind > 1 ? mine : 0;
        }

        uint IdOf(string key) =>
            key != null && m_Creatures.TryGetValue(key, out var creature) && creature != null ? creature.TurnId : 0u;

        string KeyOf(uint turnId) => m_KeyOf.TryGetValue(turnId, out var key) ? key : $"#{turnId}";

        int Round => TurnState.Current != null ? TurnState.Current.Round : 0;

        // ---------- listening ----------

        void Listen()
        {
            if (m_Listening)
            {
                return;
            }

            m_Listening = true;
            CombatAnnouncer.TurnBegan += OnTurnBegan;
            CombatAnnouncer.Moved += OnMoved;
            CombatAnnouncer.Acted += OnActed;
            CombatAnnouncer.Shot += OnShot;
            CombatAnnouncer.Fell += OnFell;
            CombatAnnouncer.HeldBack += OnHeldBack;
            CombatAnnouncer.Recovered += OnRecovered;
            ClashCommands.Resolved += OnClash;
            WallCommands.Changed += OnWall;

            if (TurnState.Current != null)
            {
                TurnState.Current.Changed += OnTurnsChanged;
            }
        }

        void Unlisten()
        {
            if (!m_Listening)
            {
                return;
            }

            m_Listening = false;
            CombatAnnouncer.TurnBegan -= OnTurnBegan;
            CombatAnnouncer.Moved -= OnMoved;
            CombatAnnouncer.Acted -= OnActed;
            CombatAnnouncer.Shot -= OnShot;
            CombatAnnouncer.Fell -= OnFell;
            CombatAnnouncer.HeldBack -= OnHeldBack;
            CombatAnnouncer.Recovered -= OnRecovered;
            ClashCommands.Resolved -= OnClash;
            WallCommands.Changed -= OnWall;

            if (TurnState.Current != null)
            {
                TurnState.Current.Changed -= OnTurnsChanged;
            }

            if (m_Brain != null)
            {
                m_Brain.Ordered -= OnOrdered;
            }
        }

        void OnOrdered(uint id, Order order) =>
            m_Trace.Add(new TraceEntry(TraceKind.Ordered, Round, KeyOf(id), order: order));

        /// <summary>
        /// A turn began: the world's events for it happen now, before the actor decides, and
        /// the fight is read once every script has run out or the rounds have run out.
        /// </summary>
        void OnTurnBegan(uint id)
        {
            var key = KeyOf(id);
            var round = Round;

            m_Trace.Add(new TraceEntry(TraceKind.TurnBegan, round, key));

            for (var i = m_Pending.Count - 1; i >= 0; i--)
            {
                var pending = m_Pending[i];

                if (pending.Round == round && pending.BeforeActor == key)
                {
                    CombatDirector.Current?.ServerSetWall(pending.Segment, pending.Wall);
                    m_Pending.RemoveAt(i);
                }
            }

            if (m_Finished || m_Scenario == null)
            {
                return;
            }

            // A scenario that never gave an order has no script to run out of; the round limit
            // is what ends it, and until then it is a board being watched.
            var scriptsDone = m_Brain != null && m_Brain.HasOrders && !m_Brain.AnyThinks
                && m_Brain.AllScriptsSpent && m_Pending.Count == 0;

            if (scriptsDone || round > m_Scenario.MaxRounds)
            {
                StartCoroutine(FinishSoon());
            }
        }

        void OnTurnsChanged()
        {
            if (!m_Finished && TurnState.Current != null && TurnState.Current.IsOver)
            {
                StartCoroutine(FinishSoon());
            }
        }

        void OnMoved(uint id, Cell from, Cell to) =>
            m_Trace.Add(new TraceEntry(TraceKind.Moved, Round, KeyOf(id), from: from, to: to));

        void OnActed(ActionReport report) =>
            m_Trace.Add(new TraceEntry(TraceKind.Acted, Round, KeyOf(report.ActorId),
                report.HasTarget ? KeyOf(report.TargetId) : null, report.SkillId));

        void OnShot(ShotReport report) =>
            m_Trace.Add(new TraceEntry(TraceKind.Shot, Round, KeyOf(report.AttackerId), KeyOf(report.TargetId),
                report.SkillId, amount: report.Chance, landed: report.Landed));

        void OnFell(uint id) => m_Trace.Add(new TraceEntry(TraceKind.Fell, Round, KeyOf(id)));

        void OnHeldBack(uint watcher, uint mover) =>
            m_Trace.Add(new TraceEntry(TraceKind.HeldBack, Round, KeyOf(watcher), KeyOf(mover)));

        void OnRecovered(uint id, int amount) =>
            m_Trace.Add(new TraceEntry(TraceKind.Recovered, Round, KeyOf(id), amount: amount));

        void OnClash(ClashReport report) =>
            m_Trace.Add(new TraceEntry(TraceKind.Clash, Round, KeyOf(report.AttackerId), KeyOf(report.DefenderId),
                report.SkillId, outcome: report.Outcome, attackerElements: report.Attacker,
                defenderElements: report.Defender));

        void OnWall(WallSegment segment, Wall before, Wall after) =>
            m_Trace.Add(new TraceEntry(TraceKind.Wall, Round, segment: segment, wallAfter: after));

        // ---------- the end ----------

        /// <summary>
        /// Stops the fight, then reads the checks a frame later.
        ///
        /// Stopped first, and immediately: the scripts are spent, so every turn after this one
        /// is a creature with nothing to do passing to the next, and a board still playing
        /// behind a finished report is worse than no report. A frame then passes so the
        /// announcements of whatever ended it have all arrived before the checks read them.
        /// </summary>
        IEnumerator FinishSoon()
        {
            if (m_Finished)
            {
                yield break;
            }

            m_Finished = true;
            CombatDirector.Current?.ServerFinish();

            yield return null;
            Finish();
        }

        void Finish()
        {
            var lines = new List<string>();

            foreach (var entry in m_Trace)
            {
                lines.Add(entry.ToString());
            }

            ScenarioResult result;

            try
            {
                var checks = new List<ScenarioCheck>(m_Faults);
                checks.AddRange(m_Scenario.Expect(new World(this)));
                result = new ScenarioResult(m_Scenario, checks, lines);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                result = new ScenarioResult(m_Scenario, m_Faults, lines, $"the checks threw: {e.Message}");
            }

            Result = result;
            Log(result);
            MatchFlow.Instance?.ScenarioFinished(result);
            Finished?.Invoke(result);

            if (MatchFlow.Instance != null && MatchFlow.Instance.QueuedScenarios > 0)
            {
                StartCoroutine(NextSoon());
            }
        }

        /// <summary>
        /// Long enough to read the result, then on to the next one.
        ///
        /// A run of scenarios plays itself: stopping at every report to ask for a click makes
        /// twelve fights into twelve interruptions. The last one in a run has nothing to go on
        /// to, so it stays up until it is dismissed.
        /// </summary>
        IEnumerator NextSoon()
        {
            SecondsToNext = m_NextDelay;

            while (SecondsToNext > 0f)
            {
                yield return null;
                SecondsToNext -= Time.unscaledDeltaTime;
            }

            SecondsToNext = -1f;
            MatchFlow.Instance?.ContinueScenarios();
        }

        static void Log(ScenarioResult result)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"[Scenario] {result}");

            foreach (var check in result.Checks)
            {
                text.AppendLine("  " + check);
            }

            text.AppendLine("  trace:");

            foreach (var line in result.Trace)
            {
                text.AppendLine("    " + line);
            }

            if (result.Passed)
            {
                Debug.Log(text.ToString());
            }
            else
            {
                Debug.LogWarning(text.ToString());
            }
        }

        // ---------- the world, as the checks read it ----------

        sealed class World : IScenarioWorld
        {
            readonly ScenarioRunner m_Runner;

            public World(ScenarioRunner runner) => m_Runner = runner;

            CreatureState Live(string actor) =>
                m_Runner.m_Creatures.TryGetValue(actor, out var creature) && creature != null && creature.IsAlive
                    ? creature
                    : null;

            Facts FactsOf(string actor) =>
                m_Runner.m_Facts.TryGetValue(actor, out var facts)
                    ? facts
                    : throw new ArgumentException($"no actor '{actor}' in this scenario");

            public bool Exists(string actor) => m_Runner.m_Facts.ContainsKey(actor);

            public bool IsAlive(string actor) => Live(actor) != null;

            public Cell CellOf(string actor) => Live(actor)?.Cell ?? default;

            public Facing FacingOf(string actor) => Live(actor)?.Facing ?? Facing.Default;

            public int HpOf(string actor) => Live(actor)?.CurrentHp ?? 0;

            public int MaxHpOf(string actor) => FactsOf(actor).MaxHp;

            public int ArmourOf(string actor) => Live(actor)?.CurrentArmour ?? 0;

            public int MaxArmourOf(string actor) => FactsOf(actor).MaxArmour;

            public ElementCounts PoolOf(string actor)
            {
                var pool = Live(actor)?.Pool;
                return pool != null ? pool.Pool : ElementCounts.Empty;
            }

            public ElementCounts StartingPoolOf(string actor) => FactsOf(actor).StartingPool;

            public IReadOnlyList<SkillSpec> SkillsOf(string actor) => FactsOf(actor).Skills;

            public SkillSpec SkillOf(string actor, int skillId)
            {
                foreach (var skill in SkillsOf(actor))
                {
                    if (skill.Id == skillId)
                    {
                        return skill;
                    }
                }

                return null;
            }

            public bool HasAdvantage(string actor) => FactsOf(actor).Advantage;

            public int SpeedOf(string actor) => FactsOf(actor).Speed;

            public int RegenOf(string actor) => FactsOf(actor).Regen;

            public TerrainType TerrainNamed(string name) =>
                name == Ground.Stone ? m_Runner.m_Stone : name == Ground.Grass ? m_Runner.m_Grass : null;

            public IGridRules Grid => ArenaContext.Current != null ? ArenaContext.Current.Map.Grid : null;

            public IElementMatchup Matchups => ElementMatchups.Table;

            public bool IsWon => TurnState.Current != null && TurnState.Current.HasWinner;

            public Party Winner => TurnState.Current != null ? TurnState.Current.Winner : Party.Heroes;

            public int Round => m_Runner.Round;

            public IReadOnlyList<TraceEntry> Trace => m_Runner.m_Trace;
        }
    }
}
