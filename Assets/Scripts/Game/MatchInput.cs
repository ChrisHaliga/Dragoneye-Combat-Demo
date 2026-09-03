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

        void OnLeaveRequested()
        {
            // The summary card takes Escape first. Quitting a match by accident because a card was
            // open is a bad surprise, so leaving only happens when nothing is selected.
            if (m_Selection != null && m_Selection.HasSelection)
            {
                m_Selection.Clear();
                return;
            }

            // MatchFlow, not SessionRunner: leaving is the same gesture whether a UGS session is
            // involved or the match is a solo host, and the input layer should not have to know
            // which kind it is in.
            var flow = MatchFlow.Instance;
            if (flow != null && flow.InMatch)
            {
                flow.LeaveMatch();
            }
        }
    }
}
