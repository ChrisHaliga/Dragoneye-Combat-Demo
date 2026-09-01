using System;
using System.Collections.Generic;
using Dragoneye.Hex.Systems;
using Dragoneye.Multiplayer;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dragoneye.Game
{
    /// <summary>
    /// Places players in the arena once everyone has finished loading it.
    ///
    /// Split out of <see cref="MatchFlow"/>, which owns scene transitions: putting objects into the
    /// world is a different job from deciding which world to load, and the two have changed for
    /// different reasons every time this project grew.
    ///
    /// Spawning is gated on the arena load completing rather than on connection. NGO creates a
    /// player object the moment a client connects whenever NetworkConfig.PlayerPrefab is set --
    /// that fires back in the menu, before anyone has readied up, and drops everybody on the world
    /// origin instead of a spawn hex. That field is deliberately left empty; this runs instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchSpawner : MonoBehaviour
    {
        [SerializeField, Tooltip("Scene whose load completing triggers spawning.")]
        string m_ArenaScene = "Arena";

        [SerializeField, Tooltip("Networked focus point spawned per player. Must be in the NetworkPrefabsList.")]
        GameObject m_FocusPrefab;

        [SerializeField, Tooltip("Optional player character, spawned on each spawn hex. Leave empty for focus-only play.")]
        GameObject m_PlayerPrefab;

        NetworkSceneManager m_SceneManager;

        // Start, not OnEnable. NetworkManager assigns its Singleton in its own OnEnable, and Unity
        // does not order OnEnable across GameObjects -- this component lives on a different object,
        // so in OnEnable the Singleton may still be null. Missing it there is silent: the hook is
        // never installed and nothing ever spawns, with no error to show for it. By Start every
        // Awake and OnEnable in the scene has run.
        void Start()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogError("MatchSpawner found no NetworkManager; nothing will spawn.", this);
                return;
            }

            networkManager.OnServerStarted += OnServerStarted;

            // A session started before this ran would already have fired OnServerStarted.
            if (networkManager.IsServer)
            {
                OnServerStarted();
            }
        }

        void OnDestroy()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                networkManager.OnServerStarted -= OnServerStarted;
            }

            Unsubscribe();
        }

        void OnServerStarted()
        {
            // NetworkManager.SceneManager only exists once netcode is running, and NGO builds a
            // fresh one per session. Re-subscribing every time rather than once is what makes a
            // second match spawn: a "have I subscribed" flag would still be set from the first
            // session while pointing at a scene manager that no longer exists.
            var sceneManager = NetworkManager.Singleton.SceneManager;
            if (sceneManager == null)
            {
                return;
            }

            Unsubscribe();
            sceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
            m_SceneManager = sceneManager;
        }

        void Unsubscribe()
        {
            if (m_SceneManager != null)
            {
                m_SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
                m_SceneManager = null;
            }
        }


        void OnLoadEventCompleted(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName == m_ArenaScene)
            {
                SpawnAll();
            }
        }

        void SpawnAll()
        {
            var context = ArenaContext.Current;
            if (context == null || context.Map == null || context.Map.Map == null)
            {
                Debug.LogError("No arena context in the loaded scene; nobody can be placed.", this);
                return;
            }

            var arena = context.Map;
            var clients = NetworkManager.Singleton.ConnectedClientsIds;

            // One spawn per connected client, so players spread evenly around the rim whatever the
            // headcount. Determinism comes from HexSpawnPlacement's own (Q, R) ordering.
            var spawns = HexSpawnPlacement.ChooseSpawns(arena.Map, clients.Count);
            var center = arena.WorldCenter();
            var index = 0;

            foreach (var clientId in clients)
            {
                var position = spawns.Count > 0 ? arena.ToWorld(spawns[index % spawns.Count]) : center;
                index++;

                // Resolved statically, not serialised: the roster is an in-scene network object in
                // the arena, and this component lives in Bootstrap, so no scene reference can span
                // the two. It has spawned by the time a load event completes.
                var roster = PlayerRoster.Current;
                var slot = roster != null ? roster.Register(clientId, NameFor(clientId)) : index - 1;

                // Each spawn is isolated. A single failure must not strand every player after it in
                // the loop with neither a focus point nor a character.
                SpawnFocus(clientId, position, slot);
                SpawnCharacter(clientId, position, center);
            }
        }

        static string NameFor(ulong clientId)
        {
            // The host knows its own lobby name. Remote names are filled in by the owning client
            // once its focus point spawns.
            var runner = SessionRunner.Instance;
            return runner != null && clientId == NetworkManager.Singleton.LocalClientId
                ? runner.PlayerName
                : string.Empty;
        }

        void SpawnFocus(ulong clientId, Vector3 position, int slot)
        {
            if (m_FocusPrefab == null)
            {
                Debug.LogError("MatchSpawner has no focus prefab assigned.", this);
                return;
            }

            try
            {
                var instance = Instantiate(m_FocusPrefab, position, Quaternion.identity);

                var networkObject = instance.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    Debug.LogError("Focus prefab has no NetworkObject.", m_FocusPrefab);
                    Destroy(instance);
                    return;
                }

                networkObject.SpawnWithOwnership(clientId);
                instance.GetComponent<FocusState>().AssignSlot(slot);
            }
            catch (Exception e)
            {
                Debug.LogError($"Focus spawn failed for client {clientId}: {e}", this);
            }
        }

        void SpawnCharacter(ulong clientId, Vector3 position, Vector3 center)
        {
            // A character is optional: the focus point is currently the whole representation of a
            // player. Assign a prefab when there are real units to place.
            if (m_PlayerPrefab == null
                || NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject != null)
            {
                return;
            }

            try
            {
                var toCenter = center - position;
                var rotation = toCenter.sqrMagnitude > 1e-4f
                    ? Quaternion.LookRotation(new Vector3(toCenter.x, 0f, toCenter.z))
                    : Quaternion.identity;

                var player = Instantiate(m_PlayerPrefab, position, rotation);
                player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
            }
            catch (Exception e)
            {
                Debug.LogError($"Character spawn failed for client {clientId}: {e}", this);
            }
        }
    }
}
