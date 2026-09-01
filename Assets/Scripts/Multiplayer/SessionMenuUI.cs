using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// Binds SessionMenu.uxml to a <see cref="SessionRunner"/>.
    ///
    /// UI Toolkit has no per-element inspector wiring: we look elements up by name from the
    /// UIDocument's visual tree, register callbacks, then re-render everything from
    /// <see cref="Refresh"/> whenever the session changes.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SessionMenuUI : MonoBehaviour
    {
        SessionRunner m_Runner;
        bool m_NameSeeded;

        VisualElement m_Root;
        VisualElement m_MenuPanel;
        VisualElement m_LobbyPanel;

        TextField m_NameField;
        TextField m_CodeField;
        Button m_HostButton;
        Button m_JoinButton;
        Button m_CopyButton;
        Button m_LeaveButton;
        Button m_StartButton;
        Toggle m_ReadyToggle;
        Label m_CodeLabel;
        Label m_RosterLabel;
        Label m_StatusLabel;
        ScrollView m_PlayerList;

        void Start()
        {
            m_Runner = SessionRunner.Instance != null
                ? SessionRunner.Instance
                : FindAnyObjectByType<SessionRunner>();

            if (m_Runner == null)
            {
                Debug.LogError($"{nameof(SessionMenuUI)} needs a {nameof(SessionRunner)} in the scene.", this);
                enabled = false;
                return;
            }

            m_Root = GetComponent<UIDocument>().rootVisualElement;

            m_MenuPanel = m_Root.Q<VisualElement>("menu-panel");
            m_LobbyPanel = m_Root.Q<VisualElement>("lobby-panel");
            m_NameField = m_Root.Q<TextField>("name-field");
            m_CodeField = m_Root.Q<TextField>("code-field");
            m_HostButton = m_Root.Q<Button>("host-button");
            m_JoinButton = m_Root.Q<Button>("join-button");
            m_CopyButton = m_Root.Q<Button>("copy-button");
            m_LeaveButton = m_Root.Q<Button>("leave-button");
            m_StartButton = m_Root.Q<Button>("start-button");
            m_ReadyToggle = m_Root.Q<Toggle>("ready-toggle");
            m_CodeLabel = m_Root.Q<Label>("code-label");
            m_RosterLabel = m_Root.Q<Label>("roster-label");
            m_StatusLabel = m_Root.Q<Label>("status-label");
            m_PlayerList = m_Root.Q<ScrollView>("player-list");

            m_HostButton.clicked += OnHostClicked;
            m_JoinButton.clicked += OnJoinClicked;
            m_CopyButton.clicked += OnCopyClicked;
            m_LeaveButton.clicked += OnLeaveClicked;
            m_StartButton.clicked += OnStartClicked;
            m_ReadyToggle.RegisterValueChangedCallback(OnReadyChanged);

            // Join codes are uppercase; do the shouting for the player.
            m_CodeField.RegisterValueChangedCallback(evt =>
            {
                var upper = evt.newValue.ToUpperInvariant();
                if (upper != evt.newValue)
                {
                    m_CodeField.SetValueWithoutNotify(upper);
                }
            });

            m_NameField.RegisterCallback<BlurEvent>(_ => OnNameCommitted());
            m_NameField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    OnNameCommitted();
                }
            });

            // Show the last known name instantly; sign-in overwrites it with the server value.
            var cached = SessionRunner.CachedPlayerName;
            if (!string.IsNullOrEmpty(cached))
            {
                m_NameField.SetValueWithoutNotify(cached);
            }

            m_Runner.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (m_Runner == null)
            {
                return;
            }

            m_Runner.Changed -= Refresh;
        }

        // SessionRunner catches its own exceptions, so nothing below should ever fault. Routing
        // through Forget anyway means the day one of them does, it lands in the console instead of
        // vanishing into the async-void unhandled path.
        void OnHostClicked() => Forget(HostFlowAsync());

        void OnJoinClicked() => Forget(JoinFlowAsync());

        void OnLeaveClicked() => Forget(m_Runner.LeaveAsync());

        void OnStartClicked() => Forget(m_Runner.StartMatchAsync());

        void OnReadyChanged(ChangeEvent<bool> evt) => Forget(ReadyFlowAsync(evt.newValue));

        void OnNameCommitted() => Forget(m_Runner.SetPlayerNameAsync(m_NameField.value));

        // Commit the typed name before connecting: BlurEvent usually fires first when a button is
        // clicked, but that is focus behaviour we should not depend on.
        async Task HostFlowAsync()
        {
            await m_Runner.SetPlayerNameAsync(m_NameField.value);
            await m_Runner.HostAsync();
        }

        async Task JoinFlowAsync()
        {
            await m_Runner.SetPlayerNameAsync(m_NameField.value);
            await m_Runner.JoinAsync(m_CodeField.value);
        }

        /// <summary>
        /// Lobby player-data writes are rate limited. Lock the toggle for the duration of the write
        /// and snap it back to the server value if the write is rejected, so the local checkbox can
        /// never disagree with what the other players see.
        /// </summary>
        async Task ReadyFlowAsync(bool ready)
        {
            m_ReadyToggle.SetEnabled(false);
            try
            {
                if (!await m_Runner.SetReadyAsync(ready) && m_Runner.Session != null)
                {
                    m_ReadyToggle.SetValueWithoutNotify(
                        SessionRunner.IsPlayerReady(m_Runner.Session.CurrentPlayer));
                }
            }
            finally
            {
                m_ReadyToggle.SetEnabled(m_Runner.Session != null && !m_Runner.IsBusy);
            }
        }

        static void Forget(Task task) => SessionRunner.Forget(task);

        void OnCopyClicked()
        {
            if (m_Runner.Session != null)
            {
                GUIUtility.systemCopyBuffer = m_Runner.Session.Code;
                m_CopyButton.text = "Copied";
                m_CopyButton.schedule.Execute(() => m_CopyButton.text = "Copy").StartingIn(1200);
            }
        }

        void Refresh()
        {
            var session = m_Runner.Session;
            var inSession = session != null;

            SetVisible(m_MenuPanel, !inSession);
            SetVisible(m_LobbyPanel, inSession);

            m_StatusLabel.text = m_Runner.Status;

            if (!inSession)
            {
                m_HostButton.SetEnabled(m_Runner.ServicesReady && !m_Runner.IsBusy);
                m_JoinButton.SetEnabled(m_Runner.ServicesReady && !m_Runner.IsBusy);
                m_NameField.SetEnabled(m_Runner.ServicesReady && !m_Runner.IsBusy);

                // Seed the field with the auto-generated name once, then leave the player's
                // typing alone -- Refresh runs on every session change.
                if (!m_NameSeeded && !string.IsNullOrEmpty(m_Runner.PlayerName))
                {
                    m_NameField.SetValueWithoutNotify(m_Runner.PlayerName);
                    m_NameSeeded = true;
                }

                return;
            }

            m_CodeLabel.text = session.Code;
            m_RosterLabel.text = $"Players ({session.PlayerCount}/{session.MaxPlayers})";

            m_ReadyToggle.SetValueWithoutNotify(SessionRunner.IsPlayerReady(session.CurrentPlayer));
            m_ReadyToggle.SetEnabled(!m_Runner.IsBusy);
            m_LeaveButton.SetEnabled(!m_Runner.IsBusy);

            // Only the host can start, and only once everyone has readied up.
            SetVisible(m_StartButton, m_Runner.IsHost);
            m_StartButton.SetEnabled(m_Runner.IsHost && m_Runner.EveryoneReady && !m_Runner.IsBusy);
            m_StartButton.text = m_Runner.EveryoneReady ? "Start match" : "Waiting for players";

            RebuildPlayerList();
        }

        void RebuildPlayerList()
        {
            m_PlayerList.Clear();

            foreach (var player in m_Runner.Session.Players)
            {
                var ready = SessionRunner.IsPlayerReady(player);
                var isSelf = player.Id == m_Runner.Session.CurrentPlayer.Id;

                var row = new VisualElement();
                row.AddToClassList("player-row");

                var nameGroup = new VisualElement { style = { flexDirection = FlexDirection.Row } };

                var name = new Label(SessionRunner.DisplayName(player));
                name.AddToClassList("player-row__name");
                nameGroup.Add(name);

                if (player.Id == m_Runner.Session.Host)
                {
                    nameGroup.Add(Tag("host"));
                }

                if (isSelf)
                {
                    nameGroup.Add(Tag("you"));
                }

                var state = new Label(ready ? "Ready" : "Not ready");
                state.AddToClassList("player-row__state");
                if (ready)
                {
                    state.AddToClassList("player-row__state--ready");
                }

                row.Add(nameGroup);
                row.Add(state);
                m_PlayerList.Add(row);
            }
        }

        static Label Tag(string text)
        {
            var label = new Label(text);
            label.AddToClassList("player-row__tag");
            return label;
        }

        static void SetVisible(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
