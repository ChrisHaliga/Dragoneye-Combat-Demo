using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// Owns scene transitions for a match, and nothing else. Lives on the persistent Bootstrap
    /// object alongside <see cref="SessionRunner"/> and the NetworkManager.
    ///
    /// Boot        -> load the menu scene.
    /// Match start -> host loads the arena through the *netcode* scene manager, so clients follow.
    /// Session end -> everyone loads the menu scene locally (non-networked -- netcode is gone).
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

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == m_MenuScene)
            {
                m_ReturningToMenu = false;
            }
        }
    }
}
