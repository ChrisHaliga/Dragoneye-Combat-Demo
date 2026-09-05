using System.Collections;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// Owns the life of a match: starting one, ending one, and the scene transitions either way.
    /// Lives on the persistent Bootstrap object alongside <see cref="SessionRunner"/> and the
    /// NetworkManager.
    ///
    /// Boot        -> load the menu scene.
    /// Match start -> the host loads the arena through the *netcode* scene manager, so clients follow.
    /// Session end -> everyone loads the menu scene locally (non-networked -- netcode is gone).
    ///
    /// A solo match takes the same path with the services skipped: netcode still runs, as a host on
    /// loopback with nobody to connect to it. That is deliberate. The alternative -- a second,
    /// netcode-free path through spawning, ownership and the draft -- would be a parallel
    /// implementation of the same rules, free to drift from the multiplayer one until a playtest
    /// caught it.
    ///
    /// Who gets placed where, once a scene has loaded, belongs to the spawner in the game assembly.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SessionRunner))]
    public sealed class MatchFlow : MonoBehaviour
    {
        [SerializeField, Tooltip("Scene shown before and after a match. Must be in Build Settings.")]
        string m_MenuScene = "MainMenu";

        [SerializeField, Tooltip("Gameplay scene. Must be in Build Settings.")]
        string m_ArenaScene = "Arena";

        SessionRunner m_Runner;
        bool m_ReturningToMenu;

        /// <summary>The persistent flow. Null until Bootstrap has run.</summary>
        public static MatchFlow Instance { get; private set; }

        /// <summary>True once a match is running, however it was started.</summary>
        public bool InMatch
        {
            get
            {
                var networkManager = NetworkManager.Singleton;
                return networkManager != null && (networkManager.IsListening || networkManager.IsClient);
            }
        }

        void Awake()
        {
            Instance = this;

            // A netcode connection is a heartbeat, and an unfocused window that stops running stops
            // answering it -- which reads as the other player hanging on a loading screen rather
            // than as anything to do with focus. Set in Player Settings as well, because this only
            // covers a process that got as far as loading Bootstrap.
            Application.runInBackground = true;
        }

        void Start()
        {
            m_Runner = GetComponent<SessionRunner>();
            m_Runner.MatchStarted += OnMatchStarted;
            m_Runner.SessionEnded += OnSessionEnded;

            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                networkManager.OnClientStopped += OnClientStopped;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;

            // Bootstrap holds only the persistent objects; there is nothing to look at here.
            var active = SceneManager.GetActiveScene().name;
            if (active != m_MenuScene && active != m_ArenaScene)
            {
                SceneManager.LoadScene(m_MenuScene);
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (m_Runner != null)
            {
                m_Runner.MatchStarted -= OnMatchStarted;
                m_Runner.SessionEnded -= OnSessionEnded;
            }

            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                networkManager.OnClientStopped -= OnClientStopped;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// Opens the setup stage for one player: no sign-in, no session, no relay, no join code.
        ///
        /// Netcode still starts, as a host bound to loopback. Nothing dials out and nothing can dial
        /// in, because there is no join code to hand anybody. From the player's side this is the
        /// same game they get by hosting alone, without the wait for Unity services.
        ///
        /// It stops here rather than loading the arena. Starting a host is what spawns the draft, and
        /// picking a side and claiming creatures is the same stage a solo host goes through -- going
        /// straight to the arena skipped it and left the player with whatever the seeding happened to
        /// deal them. <see cref="BeginArena"/> is the second half.
        /// </summary>
        /// <returns>False if a match is already running, or netcode refused to start.</returns>
        public bool StartSoloMatch()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogError("No NetworkManager; a solo match cannot start.", this);
                return false;
            }

            if (InMatch)
            {
                return false;
            }

            RestoreLoopbackTransport(networkManager);

            if (!networkManager.StartHost())
            {
                Debug.LogError("Netcode refused to start a solo host. See the console.", this);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Leaves the arena-load half of a match start callable on its own, for a solo setup that has
        /// no lobby to press Start in. Multiplayer reaches the same call through
        /// <see cref="SessionRunner.StartMatchAsync"/>, which locks the lobby first.
        /// </summary>
        public void BeginArena() => LoadArena();

        /// <summary>
        /// Backs out of the solo setup stage: shuts netcode down and leaves the menu where it is.
        ///
        /// Not <see cref="LeaveMatch"/>, which routes through a return-to-menu that does nothing
        /// when the menu is already the active scene -- so it would leave the host running and the
        /// draft on screen.
        /// </summary>
        public void CancelSoloSetup()
        {
            var networkManager = NetworkManager.Singleton;

            if (networkManager != null
                && !networkManager.ShutdownInProgress
                && (networkManager.IsListening || networkManager.IsClient))
            {
                networkManager.Shutdown();
            }
        }

        /// <summary>
        /// Ends the match and goes back to the lobby, with the session left standing.
        ///
        /// What a finished match should do. Leaving tears down the session, which is right when a
        /// player quits and wrong when a fight simply ended: the party is still assembled, the join
        /// code is still worth something, and everyone is about to argue over the next roster.
        ///
        /// Driven through the netcode scene manager for the same reason the arena load is -- the
        /// host decides, and clients follow rather than each racing to a scene of their own.
        /// </summary>
        public void ReturnToLobby()
        {
            var networkManager = NetworkManager.Singleton;

            if (networkManager == null || !networkManager.IsListening)
            {
                // No session left to go back to: a client whose host has gone, or a match that was
                // never networked at all. The menu is the only place there is.
                ReturnToMenu();
                return;
            }

            if (!networkManager.IsServer)
            {
                return;
            }

            networkManager.SceneManager.LoadScene(m_MenuScene, LoadSceneMode.Single);
        }

        /// <summary>
        /// Ends whatever kind of match is running and returns to the menu.
        ///
        /// The caller does not need to know whether a UGS session is involved: leaving a session
        /// tears down netcode as a consequence, and a solo match has nothing to leave but netcode.
        /// </summary>
        public void LeaveMatch()
        {
            if (m_Runner != null && m_Runner.IsInSession)
            {
                if (!m_Runner.IsBusy)
                {
                    TaskUtil.Forget(m_Runner.LeaveAsync());
                }

                return;
            }

            ReturnToMenu();
        }

        /// <summary>
        /// Undoes the relay configuration a previous session left on the transport, and puts the
        /// solo host somewhere it can actually bind.
        ///
        /// The Sessions SDK rewrites the transport connection data when it allocates relay, and
        /// those values survive a shutdown. Without this, a solo match started after a multiplayer
        /// one would try to host against a relay allocation that no longer exists.
        /// </summary>
        static void RestoreLoopbackTransport(NetworkManager networkManager)
        {
            var transport = networkManager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                return;
            }

            transport.UseWebSockets = false;
            transport.UseEncryption = false;
            transport.SetConnectionData("127.0.0.1", FreeLoopbackPort(), "127.0.0.1");
        }

        /// <summary>
        /// A loopback UDP port nothing currently holds.
        ///
        /// Asked for rather than assumed. A hard-coded 7777 fails outright the moment anything else
        /// has it -- a second copy of the game, or, on Windows, Hyper-V or WSL having reserved the
        /// range it falls in, which takes the port away for good and reports only "address already
        /// in use". Multiplayer never noticed because relay dials out instead of binding, so solo
        /// was the only mode that broke.
        ///
        /// Nobody dials in to a solo host, so which port it lands on does not matter to anyone.
        /// Releasing the probe leaves a window for something else to take the port first; on a
        /// machine about to play a single-player game that is a better bet than a fixed port that
        /// is either free or permanently broken.
        /// </summary>
        static ushort FreeLoopbackPort()
        {
            try
            {
                using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram,
                    ProtocolType.Udp))
                {
                    probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                    return (ushort)((IPEndPoint)probe.LocalEndPoint).Port;
                }
            }
            catch (SocketException e)
            {
                Debug.LogWarning("Could not ask the OS for a free port "
                    + $"({e.SocketErrorCode}); falling back to 7777.");
                return 7777;
            }
        }

        void OnMatchStarted() => LoadArena();

        /// <summary>Only the host drives the load; NGO replicates it to every client.</summary>
        void LoadArena()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                return;
            }

            networkManager.SceneManager.LoadScene(m_ArenaScene, LoadSceneMode.Single);
        }

        void OnSessionEnded() => ReturnToMenu();

        void OnClientStopped(bool wasHost) => ReturnToMenu();

        /// <summary>
        /// Idempotent: the host leaving fires both a netcode disconnect and a session Deleted
        /// event, and either can arrive first.
        /// </summary>
        void ReturnToMenu()
        {
            if (m_ReturningToMenu || SceneManager.GetActiveScene().name == m_MenuScene)
            {
                return;
            }

            m_ReturningToMenu = true;
            StartCoroutine(ReturnToMenuRoutine());
        }

        IEnumerator ReturnToMenuRoutine()
        {
            var networkManager = NetworkManager.Singleton;

            // ShutdownInProgress matters because one of the callers is OnClientStopped, which NGO
            // raises *during* shutdown -- calling Shutdown again from inside it is re-entrant.
            if (networkManager != null
                && !networkManager.ShutdownInProgress
                && (networkManager.IsListening || networkManager.IsClient))
            {
                networkManager.Shutdown();
            }

            // Shutdown is deferred, so loading a scene in the same frame would tear the scene's
            // NetworkObjects out from under it. One frame is enough for NGO to finish.
            yield return null;

            SceneManager.LoadScene(m_MenuScene);
        }

        /// <summary>
        /// Everything that has to be true again once we are back on the menu.
        ///
        /// Hung on the scene load rather than on any of the ways of getting here. A match can end
        /// by being won, by the host leaving, by a client following the host's scene load or by
        /// netcode falling over, and a reset attached to one of those is a reset the others skip --
        /// which is exactly how the host ended up with a Start button that did nothing after the
        /// first match. Arriving at the menu is the one event common to all of them.
        /// </summary>
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != m_MenuScene)
            {
                return;
            }

            m_ReturningToMenu = false;
            m_Runner?.ReturnedToLobby();
        }
    }
}
