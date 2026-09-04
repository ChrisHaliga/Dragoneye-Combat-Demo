using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// Binds the host and join panels to a <see cref="SessionRunner"/>: getting into a session, and
    /// nothing after that.
    ///
    /// Everything that needs Unity services lives here, which is what lets the rest of the menu --
    /// Singleplayer, Settings, Quit -- work with the services never signed in to.
    ///
    /// Once a lobby exists the prepare-for-battle board covers the menu, and the controls that go
    /// with a live session -- the join code, readiness, Start, Leave -- belong to it. See
    /// <see cref="MatchSetupBar"/>.
    ///
    /// It does not decide which panel is on screen. It reports what the session is doing and
    /// <see cref="MainMenuUI"/> routes, so there is one owner of "which screen am I on".
    /// </summary>
    public sealed class SessionScreens
    {
        readonly SessionRunner m_Runner;

        readonly TextField m_HostName;
        readonly TextField m_JoinName;
        readonly TextField m_CodeField;
        readonly Button m_HostButton;
        readonly Button m_JoinButton;

        bool m_NameSeeded;

        /// <summary>True when every control was found. False means the UXML and this disagree.</summary>
        public bool IsBound { get; }

        /// <summary>True while a lobby exists, so the router knows the board has taken over.</summary>
        public bool InLobby => m_Runner.CurrentLobby.HasValue;

        public SessionScreens(VisualElement root, SessionRunner runner)
        {
            m_Runner = runner;

            m_HostName = root.Q<TextField>("host-name-field");
            m_JoinName = root.Q<TextField>("join-name-field");
            m_CodeField = root.Q<TextField>("code-field");
            m_HostButton = root.Q<Button>("host-button");
            m_JoinButton = root.Q<Button>("join-button");

            IsBound = m_HostName != null && m_JoinName != null && m_CodeField != null
                && m_HostButton != null && m_JoinButton != null;

            if (!IsBound)
            {
                return;
            }

            m_HostButton.clicked += () => TaskUtil.Forget(HostFlowAsync());
            m_JoinButton.clicked += () => TaskUtil.Forget(JoinFlowAsync());

            // Join codes are uppercase; do the shouting for the player.
            m_CodeField.RegisterValueChangedCallback(evt =>
            {
                var upper = evt.newValue.ToUpperInvariant();
                if (upper != evt.newValue)
                {
                    m_CodeField.SetValueWithoutNotify(upper);
                }
            });

            BindNameField(m_HostName, m_JoinName);
            BindNameField(m_JoinName, m_HostName);

            // Show the last known name instantly; sign-in overwrites it with the server value.
            var cached = SessionRunner.CachedPlayerName;
            if (!string.IsNullOrEmpty(cached))
            {
                SetName(cached);
            }
        }

        /// <summary>
        /// The host and join screens each carry a name field, so whichever one the player opens is
        /// already filled in. They are two views of one value: editing either updates the other, or
        /// a player who typed a name on the host screen would find it missing on the join screen.
        /// </summary>
        void BindNameField(TextField field, TextField mirror)
        {
            field.RegisterValueChangedCallback(evt => mirror.SetValueWithoutNotify(evt.newValue));
            field.RegisterCallback<BlurEvent>(_ => Commit(field));
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Commit(field);
                }
            });
        }

        void Commit(TextField field) => TaskUtil.Forget(m_Runner.SetPlayerNameAsync(field.value));

        void SetName(string name)
        {
            m_HostName.SetValueWithoutNotify(name);
            m_JoinName.SetValueWithoutNotify(name);
        }

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
                await m_Runner.SetPlayerNameAsync(m_HostName.value);
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
                await m_Runner.SetPlayerNameAsync(m_JoinName.value);
                await m_Runner.JoinAsync(m_CodeField.value);
            }
            finally
            {
                Refresh();
            }
        }

        void SetInteractable(bool interactable)
        {
            m_HostButton.SetEnabled(interactable);
            m_JoinButton.SetEnabled(interactable);
            m_HostName.SetEnabled(interactable);
            m_JoinName.SetEnabled(interactable);
            m_CodeField.SetEnabled(interactable);
        }

        /// <summary>
        /// Repaints from the runner.
        ///
        /// A lobby means these panels are behind the draft board and out of play, so there is
        /// nothing left here to repaint.
        /// </summary>
        public void Refresh()
        {
            if (!IsBound || m_Runner.CurrentLobby.HasValue)
            {
                return;
            }

            SetInteractable(m_Runner.ServicesReady && !m_Runner.IsBusy);

            // Seed the field with the auto-generated name once, then leave the player typing
            // alone -- Refresh runs on every session change.
            if (!m_NameSeeded && !string.IsNullOrEmpty(m_Runner.PlayerName))
            {
                SetName(m_Runner.PlayerName);
                m_NameSeeded = true;
            }
        }

        /// <summary>
        /// The single place session state becomes words. A fault outranks the phase, because when
        /// something has gone wrong that is the only thing the player needs to read.
        /// </summary>
        public static string Describe(SessionPhase phase, SessionFault fault, string playerName)
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
    }
}
