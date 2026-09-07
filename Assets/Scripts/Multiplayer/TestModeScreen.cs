using System;
using Dragoneye.Scenarios;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// The test mode: every scenario the library offers, what it proves, and how it went last
    /// time, with a button to run it and one to run them all.
    ///
    /// A scenario is a fight that plays itself and reads its own checks. Running one leaves the
    /// menu for the arena and comes back here with a result, which is why the list is rebuilt
    /// every time it is shown.
    /// </summary>
    public sealed class TestModeScreen
    {
        readonly ScrollView m_List;
        readonly Label m_Note;
        readonly Button m_RunAll;
        readonly Button m_Copy;

        public TestModeScreen(VisualElement root, Action back)
        {
            m_List = root.Q<ScrollView>("test-list");
            m_Note = root.Q<Label>("test-note");
            m_RunAll = root.Q<Button>("test-run-all-button");
            m_Copy = root.Q<Button>("test-copy-button");

            var backButton = root.Q<Button>("test-back-button");

            IsBound = m_List != null && m_Note != null && m_RunAll != null && m_Copy != null
                && backButton != null;

            if (!IsBound)
            {
                return;
            }

            backButton.clicked += back;
            m_RunAll.clicked += () => MatchFlow.Instance?.StartScenarios(ScenarioLibrary.All);
            m_Copy.clicked += CopyReport;
        }

        public bool IsBound { get; }

        public void Refresh()
        {
            if (!IsBound)
            {
                return;
            }

            var flow = MatchFlow.Instance;
            var results = flow != null ? flow.ScenarioResults : null;
            var passed = 0;
            var failed = 0;

            m_List.Clear();

            foreach (var scenario in ScenarioLibrary.All)
            {
                ScenarioResult result = null;
                results?.TryGetValue(scenario.Id, out result);

                if (result != null)
                {
                    if (result.Passed) passed++; else failed++;
                }

                m_List.Add(Row(scenario, result, flow));
            }

            m_RunAll.SetEnabled(flow != null);

            // Nothing to copy until something has run.
            m_Copy.SetEnabled(results != null && results.Count > 0);

            m_Note.text = results == null || results.Count == 0
                ? $"{ScenarioLibrary.All.Count} scenarios. Each plays a fight by itself and checks what happened."
                : $"{passed} passed, {failed} failed, {ScenarioLibrary.All.Count - passed - failed} not run";
        }

        /// <summary>
        /// The whole run on the clipboard: every verdict, and for anything that failed, its
        /// failing checks and the trace of what the fight announced.
        ///
        /// Composed by <see cref="ScenarioReport"/>, which is where the wording lives, so what is
        /// copied and what could be written to a file are the same text.
        /// </summary>
        void CopyReport()
        {
            var flow = MatchFlow.Instance;
            var report = ScenarioReport.Compose(ScenarioLibrary.All, flow?.ScenarioResults);

            GUIUtility.systemCopyBuffer = report;
            m_Note.text = "Report copied to the clipboard.";
        }

        static VisualElement Row(Scenario scenario, ScenarioResult result, MatchFlow flow)
        {
            var row = new VisualElement();
            row.AddToClassList("test-row");

            var text = new VisualElement();
            text.AddToClassList("test-row__text");

            var title = new Label(scenario.Title);
            title.AddToClassList("test-row__title");
            text.Add(title);

            var summary = new Label(scenario.Summary);
            summary.AddToClassList("test-row__summary");
            text.Add(summary);

            if (result != null)
            {
                var status = new Label(Describe(result));
                status.AddToClassList("test-row__status");
                status.AddToClassList(result.Passed ? "test-row__status--pass" : "test-row__status--fail");
                text.Add(status);

                if (!result.Passed)
                {
                    foreach (var check in result.Checks)
                    {
                        if (check.Passed)
                        {
                            continue;
                        }

                        var failure = new Label("✗ " + check.What + (check.Detail.Length > 0 ? $" ({check.Detail})" : ""));
                        failure.AddToClassList("test-row__failure");
                        text.Add(failure);
                    }
                }
            }

            row.Add(text);

            var run = new Button(() => flow?.StartScenario(scenario)) { text = result == null ? "Run" : "Run again" };
            run.AddToClassList("btn");
            run.AddToClassList("btn--compact");
            run.SetEnabled(flow != null);
            row.Add(run);

            return row;
        }

        static string Describe(ScenarioResult result) =>
            result.Fault != null
                ? $"FAULT: {result.Fault}"
                : $"{(result.Passed ? "PASSED" : "FAILED")}  {result.PassedCount} of {result.Checks.Count} checks";
    }
}
