using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Spawns the draft once netcode starts, and only on the server.
    ///
    /// The draft has to exist from the moment the lobby opens and survive into the arena. A spawned
    /// prefab does that for free -- NGO moves every root-level spawned object with
    /// <c>DestroyWithScene == false</c> into DontDestroyOnLoad before a single-mode load -- whereas
    /// an in-scene object in Bootstrap would never spawn at all, because that scene is unloaded
    /// during boot and netcode never loaded it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DraftHost : MonoBehaviour
    {
        [SerializeField, Tooltip("Draft prefab. Must be in the NetworkPrefabsList.")]
        GameObject m_DraftPrefab;

        // Start, not OnEnable: NetworkManager assigns its Singleton in its own OnEnable and Unity
        // does not order OnEnable across GameObjects.
        void Start()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogError($"{nameof(DraftHost)} found no NetworkManager.", this);
                return;
            }

            networkManager.OnServerStarted += SpawnDraft;

            if (networkManager.IsServer)
            {
                SpawnDraft();
            }
        }

        void OnDestroy()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                networkManager.OnServerStarted -= SpawnDraft;
            }
        }

        void SpawnDraft()
        {
            if (m_DraftPrefab == null)
            {
                Debug.LogError($"{nameof(DraftHost)} has no draft prefab assigned.", this);
                return;
            }

            // Hosting a second match in one session must not leave two drafts alive.
            if (DraftState.Current != null)
            {
                return;
            }

            var instance = Instantiate(m_DraftPrefab);
            var networkObject = instance.GetComponent<NetworkObject>();

            if (networkObject == null)
            {
                Debug.LogError("Draft prefab has no NetworkObject.", m_DraftPrefab);
                Destroy(instance);
                return;
            }

            networkObject.Spawn();
        }
    }
}
