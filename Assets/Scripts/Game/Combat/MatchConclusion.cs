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
        /// The outcome is replicated, so each client reaches this independently and leaves under its
        /// own steam. Routing through <see cref="MatchFlow"/> rather than shutting netcode down here
        /// means a solo match and a hosted one end by the same path -- the one that already knows
        /// how to get back to the menu.
        /// </summary>
        void Close()
        {
            var flow = MatchFlow.Instance;

            if (flow == null)
            {
                Debug.LogError("No MatchFlow; the match cannot return to the menu.", this);
                return;
            }

            flow.LeaveMatch();
        }
    }
}
