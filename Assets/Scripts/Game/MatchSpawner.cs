using System;
using System.Collections.Generic;
using Dragoneye.Hex.Systems;
using Dragoneye.Multiplayer;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, so out here the bare name Hex would still bind to the
    // Dragoneye.Hex namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Places creatures in the arena once everyone has finished loading it.
    ///
    /// Split out of <see cref="MatchFlow"/>, which owns scene transitions: putting objects into the
    /// world is a different job from deciding which world to load.
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

        [SerializeField, Tooltip("Unit spawned per roster entry. Must be in the NetworkPrefabsList.")]
        GameObject m_UnitPrefab;

        [SerializeField, Min(0), Tooltip("Creatures dealt to each party when the draft is empty. "
             + "A stand-in until the lobby draft UI exists; set to 0 to require a real draft.")]
        int m_SeedCreaturesPerParty = 3;

        NetworkSceneManager m_SceneManager;

        // Start, not OnEnable. NetworkManager assigns its Singleton in its own OnEnable, and Unity
        // does not order OnEnable across GameObjects -- this component lives on a different object,
        // so in OnEnable the Singleton may still be null. Missing it there is silent: the hook is
        // never installed and nothing ever spawns, with no error to show for it.
        void Start()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogError("MatchSpawner found no NetworkManager; nothing will spawn.", this);
                return;
            }

            networkManager.OnServerStarted += OnServerStarted;

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
            // second match spawn.
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

        /// <summary>
        /// Spawns one unit per roster entry rather than one per player.
        ///
        /// A player may run several creatures and some are run by nobody, so the roster -- not the
        /// connected client list -- decides what exists.
        /// </summary>
        void SpawnAll()
        {
            var context = ArenaContext.Current;
            if (context == null || context.Map == null || context.Map.Map == null)
            {
                Debug.LogError("No arena context in the loaded scene; nobody can be placed.", this);
                return;
            }

            var arena = context.Map;
            var draft = DraftState.Current;
            if (draft == null)
            {
                Debug.LogError("No draft state spawned; nothing to place.", this);
                return;
            }

            var slots = RegisterPlayers();

            // Whatever the lobby did or did not do, everyone ends up on a side holding their share.
            // An untouched draft also gets a starting roster dealt to it.
            draft.ServerPrepareForMatch(m_SeedCreaturesPerParty, slots);

            var roster = draft.Snapshot();
            if (roster.Count == 0)
            {
                Debug.LogError("Draft roster is empty; check the creature catalog.", this);
                return;
            }

            SpawnFocusPoints(arena);
            SpawnCreatures(arena, roster);
        }

        void SpawnFocusPoints(ArenaMap arena)
        {
            var clients = NetworkManager.Singleton.ConnectedClientsIds;
            var spawns = HexSpawnPlacement.ChooseSpawns(arena.Map, clients.Count);
            var index = 0;

            foreach (var clientId in clients)
            {
                var cell = spawns.Count > 0 ? spawns[index % spawns.Count] : Hex.Zero;
                index++;

                SpawnFocus(clientId, arena.ToWorld(cell));
            }
        }

        void SpawnCreatures(ArenaMap arena, List<RosterEntry> roster)
        {
            // Placement only needs to know which creatures belong together, so parties are handed
            // down as group indices; HexSpawnPlacement gives each group its own anchor.
            var parties = DraftQueries.PartiesPresent(roster);
            var groups = new List<int>(roster.Count);
            foreach (var entry in roster)
            {
                groups.Add(Mathf.Max(0, parties.IndexOf(entry.Party)));
            }

            var cells = HexSpawnPlacement.PlaceGrouped(arena.Map, groups, parties.Count);

            for (var i = 0; i < roster.Count; i++)
            {
                SpawnUnit(roster[i], cells[i]);
            }
        }

        /// <summary>
        /// Ensures every connected player holds a slot before creatures reference one, and returns
        /// those slots in connection order.
        /// </summary>
        List<byte> RegisterPlayers()
        {
            var slots = new List<byte>();
            var roster = PlayerRoster.Current;
            if (roster == null)
            {
                return slots;
            }

            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                slots.Add((byte)roster.Register(clientId, NameFor(clientId)));
            }

            return slots;
        }

        /// <summary>
        /// A fallback name only. Every client reports its own name to
        /// <see cref="PlayerRoster"/> directly, which is the only way the server can learn a remote
        /// player's lobby name -- it lives on that player's UGS account. Using the server's own
        /// SessionRunner for everyone is what left every remote player called "Player N".
        /// </summary>
        static string NameFor(ulong clientId)
        {
            var runner = SessionRunner.Instance;
            return runner != null && clientId == NetworkManager.Singleton.LocalClientId
                ? runner.DisplayName
                : string.Empty;
        }

        void SpawnFocus(ulong clientId, Vector3 position)
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

                var roster = PlayerRoster.Current;
                if (roster != null && roster.TryGet(clientId, out var entry))
                {
                    instance.GetComponent<FocusState>().AssignSlot(entry.Slot);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Focus spawn failed for client {clientId}: {e}", this);
            }
        }

        /// <summary>
        /// Spawns one creature. Ownership goes to the claiming player so their client may command
        /// it; an unclaimed creature stays owned by the server, which is what "computer-controlled"
        /// means for now.
        /// </summary>
        void SpawnUnit(RosterEntry entry, Hex cell)
        {
            if (m_UnitPrefab == null)
            {
                Debug.LogError("MatchSpawner has no unit prefab assigned.", this);
                return;
            }

            try
            {
                var instance = Instantiate(m_UnitPrefab);
                var networkObject = instance.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    Debug.LogError("Unit prefab has no NetworkObject.", m_UnitPrefab);
                    Destroy(instance);
                    return;
                }

                var owner = OwnerClientFor(entry.ClaimedBySlot);

                // Configure, then spawn. Both components publish what they were given from inside
                // OnNetworkSpawn, which the server runs before the spawn message goes out -- so the
                // unit reaches every client already on its hex and already the right creature.
                instance.GetComponent<UnitState>().ServerPlaceAt(cell);
                instance.GetComponent<CreatureState>()
                    .ServerConfigure(entry.CreatureId, entry.Party, entry.ClaimedBySlot);

                if (owner.HasValue)
                {
                    networkObject.SpawnWithOwnership(owner.Value);
                }
                else
                {
                    networkObject.Spawn();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Unit spawn failed for creature {entry.CreatureId}: {e}", this);
            }
        }

        static ulong? OwnerClientFor(byte slot)
        {
            if (slot == PartyInfo.Unclaimed)
            {
                return null;
            }

            var roster = PlayerRoster.Current;
            return roster != null && roster.TryGetBySlot(slot, out var entry)
                ? entry.ClientId
                : (ulong?)null;
        }
    }
}
