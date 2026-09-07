using UnityEngine;
using Dragoneye.CameraControl;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Puts the camera on whoever is acting, and lets go the moment the player pans away.
    ///
    /// A board this size is bigger than the screen, and a turn that begins off screen is a turn
    /// the player finds out about from the log. So each turn, as it is shown, starts by bringing
    /// its creature into view, and the camera stays with it while it walks.
    ///
    /// **It never fights the player for the camera.** Panning is what breaks the follow, and it
    /// breaks it for the rest of that turn -- so looking around is never a tug of war, and the
    /// next turn starts fresh. Orbiting and zooming do not break it: those are still looking at
    /// the same creature.
    ///
    /// The lean an attacker makes at its target lives on the token itself, driven by the same
    /// record; this component is only the camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurnCameraFocus : MonoBehaviour
    {
        [SerializeField]
        CreatureRegistry m_Creatures;

        [SerializeField, Min(0f), Tooltip("How quickly the camera closes on the acting creature. "
             + "Seconds to cover most of the distance.")]
        float m_Ease = 0.35f;

        [SerializeField, Min(0f), Tooltip("Below this the camera is already there and stops moving.")]
        float m_Settled = 0.05f;

        uint m_Following;
        bool m_Broken;
        CameraRigInput m_Input;
        CombatPlayback m_Playback;

        void OnEnable()
        {
            m_Input = ArenaContext.Current != null ? ArenaContext.Current.RigInput : null;

            if (m_Input != null)
            {
                m_Input.Panned += Release;
            }

            Listen();
        }

        void OnDisable()
        {
            if (m_Input != null)
            {
                m_Input.Panned -= Release;
                m_Input = null;
            }

            if (m_Playback != null)
            {
                m_Playback.Presenting -= OnPresenting;
                m_Playback = null;
            }
        }

        void Listen()
        {
            var playback = CombatPlayback.Current;

            if (playback == null || playback == m_Playback)
            {
                return;
            }

            m_Playback = playback;
            m_Playback.Presenting += OnPresenting;
        }

        void OnPresenting(CombatEvent e)
        {
            if (e.Kind == CombatEventKind.TurnBegan)
            {
                m_Following = e.Actor;
                m_Broken = false;
            }
        }

        /// <summary>The player took the camera. It is theirs until the next turn begins.</summary>
        void Release() => m_Broken = true;

        void LateUpdate()
        {
            Listen();

            if (m_Broken || m_Following == 0 || m_Creatures == null)
            {
                return;
            }

            var context = ArenaContext.Current;
            var focus = context != null ? context.Focus : null;
            var creature = m_Creatures.ByTurnId(m_Following);

            if (focus == null || creature == null || !Shown.IsAlive(creature))
            {
                return;
            }

            // The token's own position, so the camera follows the walk rather than jumping to
            // where the record has already put it.
            var wanted = creature.transform.position;
            var here = focus.Position;
            var gap = new Vector3(wanted.x - here.x, 0f, wanted.z - here.z);

            if (gap.sqrMagnitude < m_Settled * m_Settled)
            {
                return;
            }

            // Unscaled, like every other camera motion: the fight can be waiting on somebody's
            // answer and the camera still has to arrive.
            var step = m_Ease > 0f
                ? 1f - Mathf.Exp(-Time.unscaledDeltaTime / m_Ease)
                : 1f;

            focus.SnapTo(here + (gap * Mathf.Clamp01(step)));
        }
    }
}
