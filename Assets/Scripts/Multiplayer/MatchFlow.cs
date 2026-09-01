using System.Collections.Generic;
using Dragoneye.Hex.Systems;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// Owns scene transitions for a match. Lives on the persistent Bootstrap object alongside
    /// <see cref="SessionRunner"/> and the NetworkManager.
    ///
    /// Boot        -> load the menu scene.
    /// Match start -> host loads the arena through the *netcode* scene manager, so clients follow.
    /// Session end -> everyone loads the menu scene locally (non-networked -- netcode is gone by then).
    /// </summary>
    [RequireComponent(typeof(SessionRunner))]
    public class MatchFlow : MonoBehaviour
    {
        [SerializeField, Tooltip("Scene shown before and after a match. Must be in Build Settings.")]
        string m_MenuScene = "MainMenu";

        [SerializeField, Tooltip("Gameplay scene. Must be in Build Settings.")]
        string m_ArenaScene = "Arena";

        [SerializeField, Tooltip("Networked cursor spawned per player. Must be in the NetworkPrefabsList.")]
        GameObject m_CursorPrefab;

        [SerializeField, Tooltip("Optional player character, spawned at each player's spawn hex. "
             + "Leave empty for cursor-only play. Held here rather than on NetworkConfig.PlayerPrefab, "
             + "because NGO auto-spawns that one at connect time and we want spawning gated on match start.")]
        GameObject m_PlayerPrefab;

        SessionRunner m_Runner;
        bool m_ReturningToMenu;
        bool m_SubscribedToSceneEvents;

        void Start()
        {
            m_Runner = GetComponent<SessionRunner>();
            m_Runner.MatchStarted += OnMatchStarted;
            m_Runner.SessionEnded += OnSessionEnded;

            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                networkManager.OnServerStarted += OnServerStarted;
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
            if (m_Runner != null)
            {
                m_Runner.MatchStarted -= OnMatchStarted;
                m_Runner.SessionEnded -= OnSessionEnded;
            }

            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                networkManager.OnServerStarted -= OnServerStarted;
                networkManager.OnClientStopped -= OnClientStopped;

                if (m_SubscribedToSceneEvents && networkManager.SceneManager != null)
                {
                    networkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
                }
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void Update()
        {
            if (SceneManager.GetActiveScene().name != m_ArenaScene)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null
                && keyboard.escapeKey.wasPressedThisFrame
                && m_Runner.Session != null
                && !m_Runner.IsBusy)
            {
                SessionRunner.Forget(m_Runner.LeaveAsync());
            }
#endif
        }

        void OnMatchStarted()
        {
            // Only the host drives the load; NGO replicates it to every client.
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer)
            {
                return;
            }

            networkManager.SceneManager.LoadScene(m_ArenaScene, LoadSceneMode.Single);
        }

        void OnServerStarted()
        {
            // NetworkManager.SceneManager only exists once netcode is running.
            var sceneManager = NetworkManager.Singleton.SceneManager;
            if (sceneManager == null || m_SubscribedToSceneEvents)
            {
                return;
            }

            sceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            m_SubscribedToSceneEvents = true;
        }

        void OnLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName == m_ArenaScene)
            {
                SpawnPlayers();
            }
        }

        /// <summary>
        /// Spawns player objects onto hexes once everyone is in the arena. Auto-spawn is disabled
        /// on the NetworkManager on purpose: it fires on connect, which would drop every player
        /// into the world while they were still sitting in the lobby.
        /// </summary>
        void SpawnPlayers()
        {
            var networkManager = NetworkManager.Singleton;

            // Deliberately not NetworkConfig.PlayerPrefab. NGO creates a player object for every
            // client the moment it connects whenever that field is set -- see
            // NetworkManager.HandleConnectionApproval, where createPlayerObject is simply
            // `PlayerPrefab != null`. That happens back in the menu, long before the match starts,
            // which both defeats gated spawning and leaves everyone standing on the origin instead
            // of on their spawn hex. So NetworkConfig.PlayerPrefab is left empty and we spawn here.
            // A player character is optional: the cursor is currently the whole representation of a
            // player. Assign a prefab here when there are real units to place.
            var prefab = m_PlayerPrefab;

            // Sorted by name so spawn assignment is deterministic across host and clients.
            var arena = FindAnyObjectByType<ArenaMap>();
            if (arena == null || arena.Map == null)
            {
                Debug.LogError("No ArenaMap in the arena scene; players have nowhere to stand.", this);
                return;
            }

            // Ask for one spawn per connected client so they are spread evenly around the rim
            // regardless of how many are playing.
            var clients = networkManager.ConnectedClientsIds;
            var spawns = HexSpawnPlacement.ChooseSpawns(arena.Map, clients.Count);
            var center = arena.WorldCenter();
            var index = 0;

            foreach (var clientId in clients)
            {
                var position = spawns.Count > 0 ? arena.ToWorld(spawns[index % spawns.Count]) : center;

                // Face the middle of the arena.
                var toCenter = center - position;
                var rotation = toCenter.sqrMagnitude > 1e-4f
                    ? Quaternion.LookRotation(new Vector3(toCenter.x, 0f, toCenter.z))
                    : Quaternion.identity;

                if (prefab != null && networkManager.ConnectedClients[clientId].PlayerObject == null)
                {
                    var player = Instantiate(prefab, position, rotation);
                    player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
                }

                SpawnCursor(clientId, position);
                index++;
            }
        }

        /// <summary>
        /// Gives each player a cursor they own. Ownership is what makes it theirs to drive and
        /// theirs for the camera to follow; everyone else just sees it move.
        /// </summary>
        void SpawnCursor(ulong clientId, Vector3 position)
        {
            if (m_CursorPrefab == null)
            {
                Debug.LogError("MatchFlow has no cursor prefab assigned; no cursors will spawn.", this);
                return;
            }

            // Isolated from player spawning: a cursor failing must not abort the loop and leave
            // later players with no character at all.
            try
            {
                var cursor = Instantiate(m_CursorPrefab, position, Quaternion.identity);

                var networkObject = cursor.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    Debug.LogError("Cursor prefab has no NetworkObject.", m_CursorPrefab);
                    Destroy(cursor);
                    return;
                }

                networkObject.SpawnWithOwnership(clientId);
                Debug.Log($"[MatchFlow] Spawned cursor for client {clientId} at {position}.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MatchFlow] Cursor spawn failed for client {clientId}: {e}", this);
            }
        }

        void OnSessionEnded() => ReturnToMenu();

        void OnClientStopped(bool wasHost) => ReturnToMenu();

        /// <summary>
        /// Idempotent: the host leaving fires both a netcode disconnect and a session Deleted event,
        /// and either can arrive first.
        /// </summary>
        void ReturnToMenu()
        {
            if (m_ReturningToMenu || SceneManager.GetActiveScene().name == m_MenuScene)
            {
                return;
            }

            m_ReturningToMenu = true;

            var networkManager = NetworkManager.Singleton;
            if (networkManager != null && (networkManager.IsListening || networkManager.IsClient))
            {
                networkManager.Shutdown();
            }

            SceneManager.LoadScene(m_MenuScene);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == m_MenuScene)
            {
                m_ReturningToMenu = false;
            }
        }
    }
}
