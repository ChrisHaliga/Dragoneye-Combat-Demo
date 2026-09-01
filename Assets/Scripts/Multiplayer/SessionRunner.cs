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

        /// <summary>
        /// Client-side key identifying this game's sessions. Must match between host and client:
        /// a mismatch surfaces as a confusing "session not found".
        /// </summary>
        public const string SessionType = "dragoneye-combat";

        /// <summary>PlayerPrefs key mirroring the server-side player name for instant display.</summary>
        const string k_CachedNameKey = "dragoneye.playerName";

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

        public bool IsHost => Session != null && Session.IsHost;

        /// <summary>
        /// The authoritative (server-side) player name once signed in. Null before that -- use
        /// <see cref="CachedPlayerName"/> to show something immediately.
        /// </summary>
        public string PlayerName => StripDiscriminator(QualifiedPlayerName);

        /// <summary>
        /// The full UGS name including its <c>#NNNN</c> discriminator, e.g. "Chris#1234".
        /// </summary>
        public string QualifiedPlayerName =>
            ServicesReady ? AuthenticationService.Instance.PlayerName : null;

        /// <summary>
        /// Drops the <c>#NNNN</c> that UGS appends to every player name.
        ///
        /// The discriminator is assigned by the service, not chosen by the player, and it is not
        /// part of the name they typed. It must never be fed back into
        /// <see cref="SetPlayerNameAsync"/>: the sanitiser strips the '#' but keeps the digits, so
        /// "Chris#1234" would be submitted as "Chris1234", come back as "Chris1234#5678", and grow
        /// a little longer every time the name was committed.
        /// </summary>
        public static string StripDiscriminator(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            var separator = name.IndexOf('#');
            return separator < 0 ? name : name.Substring(0, separator);
        }

        /// <summary>
        /// Last known player name, read from local PlayerPrefs. Display cache only: the UGS account
        /// is authoritative and overwrites this as soon as sign-in completes.
        /// </summary>
        public static string CachedPlayerName => PlayerPrefs.GetString(k_CachedNameKey, string.Empty);

        /// <summary>Raised whenever anything the UI renders may have changed.</summary>
        public event Action Changed;

        /// <summary>
        /// Raised on the host when it starts the match. Clients are not notified here -- they follow
        /// the netcode scene load, which is the actual match-start signal.
        /// </summary>
        public event Action MatchStarted;

        /// <summary>
        /// Raised when we are no longer in a session, whether we left, were removed, or the host
        /// closed it. <see cref="MatchFlow"/> uses this to return to the menu scene.
        /// </summary>
        public event Action SessionEnded;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // NetworkManager marks itself DontDestroyOnLoad when it starts. Without this the runner
            // would die on the first NetworkManager.SceneManager.LoadScene, taking the ISession
            // handle with it -- no clean leave, no roster, no session properties.
            DontDestroyOnLoad(gameObject);
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
                CachePlayerName(PlayerName);
                SetStatus($"Signed in as {PlayerName}");
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
            // A duplicate destroyed by the Awake guard must not tear down the real instance's state.
            if (Instance != this)
            {
                return;
            }

            Instance = null;
            DetachSession();
        }

        /// <summary>
        /// Changes the Unity player name, which is synchronised into the session by
        /// <c>WithPlayerName</c> and shown in every other player's lobby list.
        /// </summary>
        public async Task SetPlayerNameAsync(string rawName)
        {
            // Strip any discriminator before sanitising, so a name read back from the service can
            // be committed again unchanged instead of absorbing its own digits.
            var name = k_IllegalNameChars.Replace(StripDiscriminator(rawName) ?? string.Empty, "").Trim();

            // Compared against the bare name for the same reason: comparing against the qualified
            // one never matches, so every commit would be a needless write against a rate limit.
            if (name.Length == 0 || !ServicesReady || name == PlayerName)
            {
                return;
            }

            try
            {
                await AuthenticationService.Instance.UpdatePlayerNameAsync(name);
                CachePlayerName(PlayerName);
                SetStatus($"Playing as {PlayerName}");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetStatus($"Could not set name: {e.Message}");
            }
        }

        static void CachePlayerName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            PlayerPrefs.SetString(k_CachedNameKey, name);
            PlayerPrefs.Save();
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
                    Type = SessionType,
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
                var options = new JoinSessionOptions { Type = SessionType }.WithPlayerName();
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

            // Detach optimistically so the UI snaps back immediately, but keep the handle: if the
            // server-side leave fails we are locally out of a session that still lists us.
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
                SetStatus($"Left locally, but the server did not confirm ({Explain(e)}). " +
                    "You may linger in the lobby until it times you out.");
            }
            finally
            {
                EndOperation();
                SessionEnded?.Invoke();
            }
        }

        /// <summary>
        /// Publishes our ready flag to everyone else in the lobby.
        /// Returns false if the write failed, in which case the caller should re-sync its UI from
        /// <see cref="IsPlayerReady"/> -- lobby player-data writes are rate limited, so a player
        /// flicking the toggle will hit <see cref="SessionError.RateLimitExceeded"/>.
        /// </summary>
        public async Task<bool> SetReadyAsync(bool ready)
        {
            if (Session == null)
            {
                return false;
            }

            try
            {
                Session.CurrentPlayer.SetProperty(ReadyKey,
                    new PlayerProperty(ready ? "1" : "0", VisibilityPropertyOptions.Member));
                await Session.SaveCurrentPlayerDataAsync();
                Changed?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetStatus(Explain(e));
                Changed?.Invoke();
                return false;
            }
        }

        /// <summary>
        /// Host only. Locks the lobby against further joins and hands off to <see cref="MatchFlow"/>,
        /// which loads the arena through the netcode scene manager.
        ///
        /// The scene load itself is the match-start signal: it reaches clients over Relay, ordered
        /// against all other gameplay traffic. An earlier version broadcast a lobby session property
        /// instead -- that is the slow, rate-limited, eventually-consistent channel, and it should
        /// carry pre-connection metadata only.
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
                await host.SavePropertiesAsync();

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
            var name = StripDiscriminator(player.GetPlayerName());
            return string.IsNullOrEmpty(name) ? player.Id : name;
        }

        /// <summary>
        /// Runs a task without awaiting it, logging faults. Methods on this class catch their own
        /// exceptions, so nothing should ever surface here -- but an unobserved async void would
        /// vanish silently if one ever did.
        /// </summary>
        public static async void Forget(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
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

        void OnSessionPropertiesChanged() => Changed?.Invoke();

        void OnRemovedFromSession()
        {
            DetachSession();
            SetStatus("You were removed from the session.");
            SessionEnded?.Invoke();
        }

        void OnSessionDeleted()
        {
            DetachSession();
            SetStatus("The host closed the session.");
            SessionEnded?.Invoke();
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
