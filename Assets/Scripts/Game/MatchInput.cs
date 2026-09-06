using Dragoneye.CameraControl;
using Dragoneye.Multiplayer;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Turns the arena's "leave" input into leaving the session.
    ///
    /// The binding lives in the Camera action map, which <see cref="CameraRigInput"/> owns; this
    /// only subscribes to the event it raises. That keeps one component enabling and disabling the
    /// map, and keeps device polling out of match code -- reading Keyboard.current here would
    /// hardcode the key and fight the action asset that everything else goes through.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchInput : MonoBehaviour
    {
        [SerializeField]
        CameraRigInput m_Input;

        [SerializeField, Tooltip("Optional. When something is selected, Escape dismisses it instead of leaving.")]
        CreatureSelection m_Selection;

        void OnEnable()
        {
            if (m_Input == null)
            {
                Debug.LogError($"{nameof(MatchInput)} has no input component assigned.", this);
                enabled = false;
                return;
            }

            m_Input.LeaveRequested += OnLeaveRequested;
        }

        void OnDisable()
        {
            if (m_Input != null)
            {
                m_Input.LeaveRequested -= OnLeaveRequested;
            }
        }

        /// <summary>
        /// Escape, in order of least surprising.
        ///
        /// The menu first, because a key that will not close what is on screen is a key nobody
        /// trusts. Then the summary card, which is the other thing Escape visibly opened. Only
        /// then the pause menu.
        ///
        /// Leaving the match used to happen right here, on the keypress. A destructive action with
        /// nothing between the press and the consequence is a bad bargain in a single-player game
        /// and a worse one in a match somebody else is in; it lives behind a button on the menu
        /// now, and this only opens the menu.
        /// </summary>
        void OnLeaveRequested()
        {
            var pause = PauseMenuView.Current;

            if (pause != null && pause.IsOpen)
            {
                pause.Back();
                return;
            }

            if (m_Selection != null && m_Selection.HasSelection)
            {
                m_Selection.Clear();
                return;
            }

            if (pause != null)
            {
                pause.Open();
                return;
            }

            // No menu in the scene: the setup has not been run. Falling back to the old behaviour
            // beats a key that does nothing at all, and MatchFlow rather than SessionRunner
            // because leaving is the same gesture hosted or solo.
            var flow = MatchFlow.Instance;
            if (flow != null && flow.InMatch)
            {
                flow.LeaveMatch();
            }
        }
    }
}
