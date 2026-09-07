using System.Collections.Generic;
using System.Text;

namespace Dragoneye.Scenarios
{
    /// <summary>
    /// Every result as one block of plain text, for pasting into a message.
    ///
    /// A run of scenarios says more than a screen can hold: twelve verdicts, every check behind
    /// each of them, and for the ones that failed, the whole trace of what the fight announced.
    /// Reading that off a monitor and typing it out is how a report gets shortened until it no
    /// longer says what went wrong, so it is composed here instead and copied whole.
    ///
    /// A scenario that passed is one line -- there is nothing to diagnose. One that failed carries
    /// its failing checks and its trace, which together say what the rules expected, what the
    /// board did, and every announcement in between.
    /// </summary>
    public static class ScenarioReport
    {
        public static string Compose(IReadOnlyList<Scenario> library,
            IReadOnlyDictionary<string, ScenarioResult> results)
        {
            var text = new StringBuilder();
            var passed = 0;
            var failed = 0;
            var missing = 0;

            foreach (var scenario in library)
            {
                if (results != null && results.TryGetValue(scenario.Id, out var result) && result != null)
                {
                    if (result.Passed)
                    {
                        passed++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                else
                {
                    missing++;
                }
            }

            text.AppendLine($"Dragoneye test mode: {passed} passed, {failed} failed, {missing} not run.");

            foreach (var scenario in library)
            {
                ScenarioResult result = null;
                results?.TryGetValue(scenario.Id, out result);

                text.AppendLine();

                if (result == null)
                {
                    text.AppendLine($"[not run] {scenario.Id} -- {scenario.Title}");
                    continue;
                }

                if (result.Fault != null)
                {
                    text.AppendLine($"[FAULT] {scenario.Id} -- {scenario.Title}");
                    text.AppendLine($"  {result.Fault}");
                }
                else
                {
                    text.AppendLine($"[{(result.Passed ? "PASS" : "FAIL")}] {scenario.Id} -- {scenario.Title}"
                        + $"  ({result.PassedCount}/{result.Checks.Count})");
                }

                if (result.Passed)
                {
                    continue;
                }

                foreach (var check in result.Checks)
                {
                    if (check.Passed)
                    {
                        continue;
                    }

                    text.AppendLine($"  FAILED: {check.What}");

                    if (check.Detail.Length > 0)
                    {
                        text.AppendLine($"          {check.Detail}");
                    }
                }

                if (result.Trace.Count == 0)
                {
                    continue;
                }

                text.AppendLine("  what the fight did:");

                foreach (var line in result.Trace)
                {
                    text.AppendLine($"    {line}");
                }
            }

            return text.ToString();
        }
    }
}
