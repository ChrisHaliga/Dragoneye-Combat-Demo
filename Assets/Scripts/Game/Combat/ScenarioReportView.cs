using Dragoneye.Multiplayer;
using Dragoneye.Scenarios;
using UnityEngine;
using UnityEngine.UIElements;
using Dragoneye.UI;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// What the scenario is doing, and what it came to, on the HUD.
    ///
    /// While it runs: the title and the round. When it is read: every check, passed or failed,
    /// and the way on -- the next queued scenario, or back to the test mode. Nothing here decides
    /// anything; it draws the runner's result and forwards two clicks to the match flow.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class ScenarioReportView : MonoBehaviour
    {
        VisualElement m_Panel;
        Label m_Title;
        Label m_Status;
        ScrollView m_Checks;
        Button m_Next;
        Button m_Back;
        int m_Round = -1;
        int m_NextIn = -1;
        bool m_Shown;

        void Start()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            m_Panel = root.Q<VisualElement>("scenario-report");
            m_Title = root.Q<Label>("scenario-title");
            m_Status = root.Q<Label>("scenario-status");
            m_Checks = root.Q<ScrollView>("scenario-checks");
            m_Next = root.Q<Button>("scenario-next-button");
            m_Back = root.Q<Button>("scenario-back-button");

            if (m_Panel == null || m_Title == null || m_Status == null || m_Checks == null
                || m_Next == null || m_Back == null)
            {
                Debug.LogError($"{nameof(ScenarioReportView)} could not find its elements; check ArenaHud.uxml.", this);
                enabled = false;
                return;
            }


            WheelScroll.Attach(m_Checks);

            m_Next.clicked += () => MatchFlow.Instance?.ContinueScenarios();
            m_Back.clicked += () => MatchFlow.Instance?.LeaveMatch();

            ScenarioRunner.Finished += OnFinished;
            m_Panel.AddToClassList("is-hidden");
        }

        void OnDestroy() => ScenarioRunner.Finished -= OnFinished;

        void Update()
        {
            var runner = ScenarioRunner.Current;

            if (runner == null || runner.Scenario == null)
            {
                return;
            }

            if (!m_Shown)
            {
                m_Shown = true;
                m_Panel.RemoveFromClassList("is-hidden");
                m_Title.text = runner.Scenario.Title;
                m_Next.EnableInClassList("is-hidden", true);
                m_Back.EnableInClassList("is-hidden", true);
            }

            if (runner.Result != null)
            {
                // Counted down on the button rather than in the verdict: the verdict is what the
                // reader came for, and it should not be changing while they read it.
                var waiting = runner.SecondsToNext;
                var seconds = waiting >= 0f ? Mathf.CeilToInt(waiting) : -1;

                if (seconds >= 0 && seconds != m_NextIn)
                {
                    m_NextIn = seconds;
                    m_Next.text = $"Next scenario in {seconds}";
                }

                return;
            }

            var turns = TurnState.Current;
            var round = turns != null ? turns.Round : 0;

            if (round != m_Round)
            {
                m_Round = round;
                m_Status.text = round > 0 ? $"Running, round {round}" : "Placing the pieces";
            }
        }

        void OnFinished(ScenarioResult result)
        {
            m_Status.text = result.Fault != null
                ? $"FAULT: {result.Fault}"
                : $"{(result.Passed ? "PASSED" : "FAILED")}  {result.PassedCount} of {result.Checks.Count} checks";
            m_Status.EnableInClassList("scenario-report__status--pass", result.Passed);
            m_Status.EnableInClassList("scenario-report__status--fail", !result.Passed);

            m_Checks.Clear();

            foreach (var check in result.Checks)
            {
                var row = new Label((check.Passed ? "✓  " : "✗  ") + check.What
                    + (check.Passed || check.Detail.Length == 0 ? "" : $"\n     {check.Detail}"));
                row.AddToClassList("scenario-report__check");
                row.AddToClassList(check.Passed ? "scenario-report__check--pass" : "scenario-report__check--fail");
                m_Checks.Add(row);
            }

            var flow = MatchFlow.Instance;
            var more = flow != null && flow.QueuedScenarios > 0;

            // The next one comes on its own; the button is there to skip the wait.
            m_Next.text = more ? $"Next scenario ({flow.QueuedScenarios} left)" : "Next scenario";
            m_Next.EnableInClassList("is-hidden", !more);
            m_Back.text = more ? "Stop here" : "Back to test mode";
            m_Back.EnableInClassList("is-hidden", false);
        }
    }
}
