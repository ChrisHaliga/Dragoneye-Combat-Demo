using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// Binds SessionMenu.uxml to a <see cref="SessionRunner"/>.
    ///
    /// UI Toolkit has no per-element inspector wiring: elements are looked up by name from the
    /// UIDocument's visual tree, callbacks registered, then everything re-rendered from
    /// <see cref="Refresh"/> whenever the session changes.
    ///
    /// All player-facing wording lives here. The runner reports a <see cref="SessionPhase"/> and a
    /// <see cref="SessionFault"/>; turning those into English is a presentation job, and keeping it
    /// in one place is what makes the strings localisable later.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class SessionMenuUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Leave empty to use the persistent runner.")]
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
            if (m_Runner == null)
            {
                m_Runner = SessionRunner.Instance;
            }

            if (m_Runner == null)
            {
                Debug.LogError($"{nameof(SessionMenuUI)} has no {nameof(SessionRunner)}.", this);
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
            if (m_Runner != null)
            {
                m_Runner.Changed -= Refresh;
            }
        }

        void OnHostClicked() => TaskUtil.Forget(HostFlowAsync());

        void OnJoinClicked() => TaskUtil.Forget(JoinFlowAsync());

        void OnLeaveClicked() => TaskUtil.Forget(m_Runner.LeaveAsync());

        void OnStartClicked() => TaskUtil.Forget(m_Runner.StartMatchAsync());

        void OnReadyChanged(ChangeEvent<bool> evt) => TaskUtil.Forget(ReadyFlowAsync(evt.newValue));

        void OnNameCommitted() => TaskUtil.Forget(m_Runner.SetPlayerNameAsync(m_NameField.value));

        /// <summary>
        /// Commits the typed name, then connects.
        ///
        /// The buttons are locked for the whole flow rather than only for the connect call. Setting
        /// a name is a network round trip of its own, and leaving the buttons live during it relied
        /// on continuations happening to resume in order -- true today, but nothing enforces it.
        /// </summary>
        async Task HostFlowAsync()
        {
            SetInteractable(false);
            try
            {
                await m_Runner.SetPlayerNameAsync(m_NameField.value);
                await m_Runner.HostAsync();
            }
            finally
            {
                Refresh();
            }
        }

        async Task JoinFlowAsync()
        {
            SetInteractable(false);
            try
            {
                await m_Runner.SetPlayerNameAsync(m_NameField.value);
                await m_Runner.JoinAsync(m_CodeField.value);
            }
            finally
            {
                Refresh();
            }
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
                if (!await m_Runner.SetReadyAsync(ready))
                {
                    var lobby = m_Runner.CurrentLobby;
                    if (lobby.HasValue)
                    {
                        m_ReadyToggle.SetValueWithoutNotify(lobby.Value.SelfIsReady);
                    }
                }
            }
            finally
            {
                Refresh();
            }
        }

        void OnCopyClicked()
        {
            var lobby = m_Runner.CurrentLobby;
            if (!lobby.HasValue)
            {
                return;
            }

            GUIUtility.systemCopyBuffer = lobby.Value.Code;
            m_CopyButton.text = "Copied";
            m_CopyButton.schedule.Execute(() => m_CopyButton.text = "Copy").StartingIn(1200);
        }

        void SetInteractable(bool interactable)
        {
            m_HostButton.SetEnabled(interactable);
            m_JoinButton.SetEnabled(interactable);
            m_NameField.SetEnabled(interactable);
        }

        void Refresh()
        {
            var lobby = m_Runner.CurrentLobby;
            var inLobby = lobby.HasValue;

            SetVisible(m_MenuPanel, !inLobby);
            SetVisible(m_LobbyPanel, inLobby);

            m_StatusLabel.text = Describe(m_Runner.Phase, m_Runner.Fault, m_Runner.PlayerName);

            if (!inLobby)
            {
                SetInteractable(m_Runner.ServicesReady && !m_Runner.IsBusy);

                // Seed the field with the auto-generated name once, then leave the player's typing
                // alone -- Refresh runs on every session change.
                if (!m_NameSeeded && !string.IsNullOrEmpty(m_Runner.PlayerName))
                {
                    m_NameField.SetValueWithoutNotify(m_Runner.PlayerName);
                    m_NameSeeded = true;
                }

                return;
            }

            var view = lobby.Value;

            m_CodeLabel.text = view.Code;
            m_RosterLabel.text = $"Players ({view.PlayerCount}/{view.MaxPlayers})";

            m_ReadyToggle.SetValueWithoutNotify(view.SelfIsReady);
            m_ReadyToggle.SetEnabled(!m_Runner.IsBusy);
            m_LeaveButton.SetEnabled(!m_Runner.IsBusy);

            // Only the host can start, and only once everyone has readied up.
            SetVisible(m_StartButton, view.IsHost);
            m_StartButton.SetEnabled(view.IsHost && view.EveryoneReady && !m_Runner.IsBusy);
            m_StartButton.text = view.EveryoneReady ? "Start match" : "Waiting for players";

            RebuildPlayerList(view);
        }

        void RebuildPlayerList(LobbyView view)
        {
            m_PlayerList.Clear();

            foreach (var player in view.Players)
            {
                var row = new VisualElement();
                row.AddToClassList("player-row");

                var nameGroup = new VisualElement { style = { flexDirection = FlexDirection.Row } };

                var name = new Label(player.Name);
                name.AddToClassList("player-row__name");
                nameGroup.Add(name);

                if (player.IsHost)
                {
                    nameGroup.Add(Tag("host"));
                }

                if (player.IsSelf)
                {
                    nameGroup.Add(Tag("you"));
                }

                var state = new Label(player.IsReady ? "Ready" : "Not ready");
                state.AddToClassList("player-row__state");
                if (player.IsReady)
                {
                    state.AddToClassList("player-row__state--ready");
                }

                row.Add(nameGroup);
                row.Add(state);
                m_PlayerList.Add(row);
            }
        }

        /// <summary>
        /// The single place session state becomes words. A fault outranks the phase, because when
        /// something has gone wrong that is the only thing the player needs to read.
        /// </summary>
        static string Describe(SessionPhase phase, SessionFault fault, string playerName)
        {
            switch (fault)
            {
                case SessionFault.NotFound:
                    return "No session with that join code.";
                case SessionFault.Deleted:
                    return "That session no longer exists.";
                case SessionFault.Forbidden:
                    return "Cannot join -- the session is full or locked.";
                case SessionFault.NotAuthorized:
                    return "Not authorized. Check that this project is linked and Relay is enabled.";
                case SessionFault.RateLimited:
                    return "Too many requests -- wait a few seconds and retry.";
                case SessionFault.NetcodeFailed:
                    return "Relay connected but netcode failed to start. See the console.";
                case SessionFault.AlreadyInSession:
                    return "A session is already open on this client. Leave it first.";
                case SessionFault.NoJoinCode:
                    return "Enter a join code first.";
                case SessionFault.NotReady:
                    return "Still connecting to Unity services -- try again in a moment.";
                case SessionFault.RemovedFromSession:
                    return "You were removed from the session.";
                case SessionFault.NameRejected:
                    return "Could not set that name.";
                case SessionFault.LeaveNotConfirmed:
                    return "Left locally, but the server did not confirm. "
                        + "You may linger in the lobby until it times you out.";
                case SessionFault.ServicesUnreachable:
                    return "Could not reach Unity services. See the console.";
                case SessionFault.Unknown:
                    return "Something went wrong. See the console.";
            }

            switch (phase)
            {
                case SessionPhase.Connecting:
                    return "Connecting to Unity services...";
                case SessionPhase.Hosting:
                    return "Creating session...";
                case SessionPhase.Joining:
                    return "Joining...";
                case SessionPhase.Leaving:
                    return "Leaving...";
                case SessionPhase.InLobby:
                    return "In lobby.";
                default:
                    return string.IsNullOrEmpty(playerName) ? "Ready." : $"Signed in as {playerName}";
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
