using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;

namespace Dragoneye.Scenarios
{
    /// <summary>
    /// Every scenario the test mode offers, in the order it offers them.
    ///
    /// Code, not assets: a scenario is a fight and a set of claims about it, and both belong
    /// next to the rules they exercise. Adding one is adding a method here and a line to the
    /// list.
    /// </summary>
    public static class ScenarioLibrary
    {
        static IReadOnlyList<Scenario> s_All;

        public static IReadOnlyList<Scenario> All => s_All ?? (s_All = Build());

        public static Scenario Find(string id)
        {
            foreach (var scenario in All)
            {
                if (scenario.Id == id)
                {
                    return scenario;
                }
            }

            return null;
        }

        static IReadOnlyList<Scenario> Build() => new List<Scenario>
        {
            WallScenarios.RoomAndDoor(),
            WallScenarios.WallBreak(),
            WallScenarios.Movement(),
            WallScenarios.LineOfSight(),
            CombatScenarios.Flank(),
            CombatScenarios.Ranged(),
            CombatScenarios.Opportunity(),
            CombatScenarios.ArmourAndRegen(),
            CombatScenarios.Recovery(),
            CombatScenarios.Kill(),
            CombatScenarios.Initiative(),
            CombatScenarios.BrainDuel()
        };
    }

    /// <summary>Ways of reading a trace, shared by the scenarios' checks.</summary>
    public static class Reading
    {
        public static int Count(IScenarioWorld world, TraceKind kind, string actor = null,
            int round = 0, int skillId = 0)
        {
            var count = 0;

            foreach (var entry in world.Trace)
            {
                if (Matches(entry, kind, actor, round, skillId))
                {
                    count++;
                }
            }

            return count;
        }

        public static bool Has(IScenarioWorld world, TraceKind kind, string actor = null,
            int round = 0, int skillId = 0) =>
            Count(world, kind, actor, round, skillId) > 0;

        public static TraceEntry? First(IScenarioWorld world, TraceKind kind, string actor = null,
            int round = 0, int skillId = 0)
        {
            foreach (var entry in world.Trace)
            {
                if (Matches(entry, kind, actor, round, skillId))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>Every clash in the order it was fought.</summary>
        public static List<TraceEntry> Clashes(IScenarioWorld world)
        {
            var clashes = new List<TraceEntry>();

            foreach (var entry in world.Trace)
            {
                if (entry.Kind == TraceKind.Clash)
                {
                    clashes.Add(entry);
                }
            }

            return clashes;
        }

        /// <summary>Who acted in a round, in the order their turns began.</summary>
        public static List<string> TurnsOf(IScenarioWorld world, int round)
        {
            var turns = new List<string>();

            foreach (var entry in world.Trace)
            {
                if (entry.Kind == TraceKind.TurnBegan && entry.Round == round)
                {
                    turns.Add(entry.Actor);
                }
            }

            return turns;
        }

        static bool Matches(TraceEntry entry, TraceKind kind, string actor, int round, int skillId) =>
            entry.Kind == kind
            && (actor == null || entry.Actor == actor)
            && (round == 0 || entry.Round == round)
            && (skillId == 0 || entry.SkillId == skillId);

        public static string Join(IReadOnlyList<string> keys) => string.Join(", ", keys);
    }
}
