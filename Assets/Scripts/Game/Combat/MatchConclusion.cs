using Dragoneye.Combat;
using Dragoneye.Multiplayer;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Closes the match once it has been won, after a pause long enough to read the result.
    ///
    /// Its own component rather than a few lines in the HUD, because tearing netcode down and
    /// returning to the menu is match lifecycle, not presentation. A view that could do it would be
    /// a view that decides when the game ends -- and it would put a dependency on
    /// <see cref="MatchFlow"/> inside a class whose job is drawing a banner.
    ///
    /// This is also the only file in the combat slice that knows the multiplayer assembly exists.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchConclusion : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("Seconds the result is shown before returning to the menu.")]
        float m_Dwell = 4f;

        float m_Elapsed;
        bool m_Closing;

        void Update()
        {
            var turns = TurnState.Current;

            if (turns == null || !turns.IsOver || m_Closing)
            {
                // Counted from the moment the fight actually ended, not from the first frame this
                // component happened to look. TurnState outlives a match -- it rides the draft
                // object from lobby to arena and back -- so a fresh arena sees the *previous*
                // match's result for the few frames before this one is begun, and a dwell that
                // kept accumulating across that would be a dwell measuring the wrong match.
                m_Elapsed = 0f;
                return;
            }

            m_Elapsed += Time.unscaledDeltaTime;

            if (m_Elapsed < m_Dwell)
            {
                return;
            }

            m_Closing = true;
            Close();
        }

        /// <summary>
        /// Every peer closes itself.
        ///
        /// Back to the lobby rather than out of the session: a fight ending is not a player leaving.
        /// The party is still assembled and the next roster is about to be argued over, so the
        /// board they argue over it on should still be there.
        ///
        /// The outcome is replicated, so each peer reaches this independently. Only the host
        /// actually drives the scene change; the rest follow it.
        /// </summary>
        void Close()
        {
            var flow = MatchFlow.Instance;

            if (flow == null)
            {
                Debug.LogError("No MatchFlow; the match cannot return to the lobby.", this);
                return;
            }

            flow.ReturnToLobby();
        }
    }
}
