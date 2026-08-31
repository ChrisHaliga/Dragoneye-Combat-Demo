using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// Owns the Unity Gaming Services lifecycle and the active multiplayer session.
    ///
    /// A session bundles a Lobby (player list, ready flags, session properties) with a
    /// Relay connection that drives <c>NetworkManager.Singleton</c>. Creating or joining a
    /// session therefore also starts netcode -- there is no separate StartHost/StartClient
    /// call, and no port forwarding, because Relay traverses NAT for us.
    ///
    /// Requires a NetworkManager in the scene (the quickstart scene already has one).
    /// </summary>
    [DisallowMultipleComponent]
    public class SessionRunner : MonoBehaviour
    {
        /// <summary>Player property key holding "1" when that player has readied up.</summary>
        public const string ReadyKey = "ready";

        /// <summary>Session property key set to "1" once the host starts the match.</summary>
        public const string MatchStartedKey = "matchStarted";

        [SerializeField, Tooltip("Maximum players in a session, including the host.")]
        int m_MaxPlayers = 4;

        [SerializeField, Tooltip("Session name shown in queries. Does not need to be unique.")]
        string m_SessionName = "Dragoneye Combat";

        [SerializeField, Tooltip("Force a Relay region (e.g. us-central1). Leave empty to auto-pick the lowest latency one.")]
        string m_RelayRegion = "";

        // Unity player names allow letters, digits, '-' and '_' only.
        static readonly Regex k_IllegalNameChars = new Regex("[^a-zA-Z0-9_-]");

        bool m_MatchStarted;

        public static SessionRunner Instance { get; private set; }

        /// <summary>The session we are currently in, or null.</summary>
        public ISession Session { get; private set; }

        /// <summary>True once UGS is initialised and we are signed in.</summary>
        public bool ServicesReady { get; private set; }

        /// <summary>True while a create/join/leave call is in flight, so the UI can lock out input.</summary>
        public bool IsBusy { get; private set; }

        /// <summary>Human readable status, surfaced in the UI status bar.</summary>
        public string Status { get; private set; } = "Connecting to Unity services...";

        public bool IsInSession => Session != null && Session.State == SessionState.Connected;

        public bool IsHost => Session != null && Session.IsHost;

        public string PlayerName => ServicesReady ? AuthenticationService.Instance.PlayerName : null;

        public int MaxPlayers => m_MaxPlayers;

        /// <summary>Raised whenever anything the UI renders may have changed.</summary>
        public event Action Changed;

        /// <summary>Raised on every client once the host starts the match.</summary>
        public event Action MatchStarted;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        async void Start()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync(BuildInitializationOptions());
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                // Generates a name like "Player#1234" the first time, then reuses it.
                await AuthenticationService.Instance.GetPlayerNameAsync();

                ServicesReady = true;
                SetStatus($"Signed in as {AuthenticationService.Instance.PlayerName}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetStatus($"Could not reach Unity services: {e.Message}");
            }
        }

        /// <summary>
        /// Anonymous credentials are cached per "profile" in PlayerPrefs. Multiplayer Play Mode
        /// virtual players share those PlayerPrefs, so without a distinct profile every editor
        /// instance signs in as the same player and the second one cannot join the lobby.
        /// Standalone builds keep the default profile so a player's identity persists.
        /// </summary>
        static InitializationOptions BuildInitializationOptions()
        {
            var options = new InitializationOptions();
#if UNITY_EDITOR
            options.SetProfile($"editor-{System.Diagnostics.Process.GetCurrentProcess().Id}");
#endif
            return options;
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            DetachSession();
        }

        /// <summary>
        /// Changes the Unity player name, which is synchronised into the session by
        /// <c>WithPlayerName</c> and shown in every other player's lobby list.
        /// </summary>
        public async Task SetPlayerNameAsync(string rawName)
        {
            var name = k_IllegalNameChars.Replace(rawName ?? string.Empty, "").Trim();
            if (name.Length == 0 || !ServicesReady || name == AuthenticationService.Instance.PlayerName)
            {
                return;
            }

            try
            {
                await AuthenticationService.Instance.UpdatePlayerNameAsync(name);
                SetStatus($"Playing as {AuthenticationService.Instance.PlayerName}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetStatus($"Could not set name: {e.Message}");
            }
        }

        /// <summary>
        /// Creates a Relay-backed session and starts netcode as the host.
        /// The resulting <see cref="ISession.Code"/> is what other players type in to join.
        /// </summary>
        public async Task HostAsync()
        {
            if (!CanStartOperation())
            {
                return;
            }

            BeginOperation("Creating session...");
            try
            {
                var options = new SessionOptions
                {
                    Type = "dragoneye-combat",
                    Name = m_SessionName,
                    MaxPlayers = Mathf.Max(2, m_MaxPlayers),
                    // Private only means "not listed in queries / quick-join".
                    // Joining by code still works, which is what we want here.
                    IsPrivate = true
                }
                    .WithRelayNetwork(string.IsNullOrWhiteSpace(m_RelayRegion) ? null : m_RelayRegion)
                    .WithPlayerName();

                options.PlayerProperties[ReadyKey] = NotReadyProperty();

                AttachSession(await MultiplayerService.Instance.CreateSessionAsync(options));
                SetStatus($"Hosting. Share join code {Session.Code}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetStatus(Explain(e));
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// Joins an existing session by its join code and starts netcode as a client.
        /// </summary>
        public async Task JoinAsync(string joinCode)
        {
            var code = (joinCode ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                SetStatus("Enter a join code first.");
                return;
            }

            if (!CanStartOperation())
            {
                return;
            }

            BeginOperation($"Joining {code}...");
            try
            {
                var options = new JoinSessionOptions { Type = "dragoneye-combat" }.WithPlayerName();
                options.PlayerProperties[ReadyKey] = NotReadyProperty();

                AttachSession(await MultiplayerService.Instance.JoinSessionByCodeAsync(code, options));
                SetStatus($"Joined {Session.Code}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetStatus(Explain(e));
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>
        /// Leaves the session, which also shuts down the Relay connection and netcode.
        /// </summary>
        public async Task LeaveAsync()
        {
            if (Session == null)
            {
                return;
            }

            var leaving = Session;
            DetachSession();
            BeginOperation("Leaving...");
            try
            {
                await leaving.LeaveAsync();
                SetStatus("Left the session.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetStatus(Explain(e));
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>Publishes our ready flag to everyone else in the lobby.</summary>
        public async Task SetReadyAsync(bool ready)
        {
            if (Session == null)
            {
                return;
            }

            try
            {
                Session.CurrentPlayer.SetProperty(ReadyKey,
                    new PlayerProperty(ready ? "1" : "0", VisibilityPropertyOptions.Member));
                await Session.SaveCurrentPlayerDataAsync();
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetStatus(Explain(e));
            }
        }

        /// <summary>
        /// Host only. Flips a session property that every client is listening for, which is
        /// how the lobby phase ends. Swap the body of <see cref="RaiseMatchStarted"/> for
        /// <c>NetworkManager.Singleton.SceneManager.LoadScene(...)</c> once you have a
        /// gameplay scene to move everyone into.
        /// </summary>
        public async Task StartMatchAsync()
        {
            if (Session == null || !Session.IsHost || m_MatchStarted)
            {
                return;
            }

            BeginOperation("Starting match...");
            try
            {
                var host = Session.AsHost();
                host.IsLocked = true;
                host.SetProperty(MatchStartedKey, new SessionProperty("1", VisibilityPropertyOptions.Member));
                await host.SavePropertiesAsync();

                // Property change events do not fire locally for the writer, so raise it here.
                RaiseMatchStarted();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetStatus(Explain(e));
            }
            finally
            {
                EndOperation();
            }
        }

        /// <summary>True when every player in the session has readied up.</summary>
        public bool EveryoneReady =>
            Session != null && Session.PlayerCount > 0 && Session.Players.All(IsPlayerReady);

        public static bool IsPlayerReady(IReadOnlyPlayer player) =>
            player.Properties != null
            && player.Properties.TryGetValue(ReadyKey, out var property)
            && property.Value == "1";

        public static string DisplayName(IReadOnlyPlayer player)
        {
            var name = player.GetPlayerName();
            return string.IsNullOrEmpty(name) ? player.Id : name;
        }

        static PlayerProperty NotReadyProperty() =>
            new PlayerProperty("0", VisibilityPropertyOptions.Member);

        void AttachSession(ISession session)
        {
            DetachSession();
            m_MatchStarted = false;
            Session = session;

            session.Changed += OnSessionChanged;
            session.PlayerJoined += OnPlayerChanged;
            session.PlayerHasLeft += OnPlayerChanged;
            session.PlayerPropertiesChanged += OnSessionChanged;
            session.SessionPropertiesChanged += OnSessionPropertiesChanged;
            session.RemovedFromSession += OnRemovedFromSession;
            session.Deleted += OnSessionDeleted;
        }

        void DetachSession()
        {
            if (Session == null)
            {
                return;
            }

            Session.Changed -= OnSessionChanged;
            Session.PlayerJoined -= OnPlayerChanged;
            Session.PlayerHasLeft -= OnPlayerChanged;
            Session.PlayerPropertiesChanged -= OnSessionChanged;
            Session.SessionPropertiesChanged -= OnSessionPropertiesChanged;
            Session.RemovedFromSession -= OnRemovedFromSession;
            Session.Deleted -= OnSessionDeleted;

            Session = null;
            m_MatchStarted = false;
            Changed?.Invoke();
        }

        void OnSessionChanged() => Changed?.Invoke();

        void OnPlayerChanged(string playerId) => Changed?.Invoke();

        void OnSessionPropertiesChanged()
        {
            if (!m_MatchStarted
                && Session != null
                && Session.Properties != null
                && Session.Properties.TryGetValue(MatchStartedKey, out var property)
                && property.Value == "1")
            {
                RaiseMatchStarted();
                return;
            }

            Changed?.Invoke();
        }

        void OnRemovedFromSession()
        {
            DetachSession();
            SetStatus("You were removed from the session.");
        }

        void OnSessionDeleted()
        {
            DetachSession();
            SetStatus("The host closed the session.");
        }

        void RaiseMatchStarted()
        {
            m_MatchStarted = true;
            SetStatus("Match started.");
            MatchStarted?.Invoke();
        }

        bool CanStartOperation()
        {
            if (IsBusy)
            {
                return false;
            }

            if (!ServicesReady)
            {
                SetStatus("Still connecting to Unity services -- try again in a moment.");
                return false;
            }

            if (Session != null)
            {
                SetStatus("Already in a session.");
                return false;
            }

            return true;
        }

        void BeginOperation(string status)
        {
            IsBusy = true;
            SetStatus(status);
        }

        void EndOperation()
        {
            IsBusy = false;
            Changed?.Invoke();
        }

        void SetStatus(string status)
        {
            Status = status;
            Changed?.Invoke();
        }

        /// <summary>Turns the common SessionExceptions into something a player can act on.</summary>
        static string Explain(Exception e)
        {
            if (e is SessionException sessionException)
            {
                switch (sessionException.Error)
                {
                    case SessionError.SessionNotFound:
                        return "No session with that join code.";
                    case SessionError.SessionDeleted:
                        return "That session no longer exists.";
                    case SessionError.Forbidden:
                        return "Cannot join -- the session is full or locked.";
                    case SessionError.NotAuthorized:
                        return "Not authorized. Check that this project is linked and Relay is enabled.";
                    case SessionError.RateLimitExceeded:
                        return "Too many requests -- wait a few seconds and retry.";
                    case SessionError.NetworkManagerNotInitialized:
                        return "No NetworkManager in the scene.";
                    case SessionError.NetworkManagerStartFailed:
                    case SessionError.NetworkSetupFailed:
                        return "Relay connected but netcode failed to start. See the console.";
                    case SessionError.SessionTypeAlreadyExists:
                        return "A session is already open on this client. Leave it first.";
                }
            }

            return e.Message;
        }
    }
}
