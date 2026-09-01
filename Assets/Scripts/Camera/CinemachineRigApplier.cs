using Unity.Cinemachine;
using UnityEngine;

namespace Dragoneye.CameraControl
{
    /// <summary>
    /// Copies the rig's arm offset onto a Cinemachine camera. The only file in the project that
    /// references Cinemachine at runtime.
    ///
    /// The whole point of keeping this separate: <see cref="CameraRig"/> decides where the camera
    /// should be, and this decides who is told. Swapping Cinemachine for a plain transform, or
    /// adding a second camera for cutscenes, touches only this file.
    ///
    /// It writes exactly one public field. Reaching further into Cinemachine's internals -- the
    /// <c>GetCinemachineComponent&lt;T&gt;().m_FollowOffset</c> pattern found in most tutorials --
    /// is what makes camera code break on every package upgrade.
    /// </summary>
    // After CameraRig (-100) so the offset pushed here was computed from this frame's rig, and
    // before the Cinemachine brain, which runs in LateUpdate.
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(CinemachineFollow))]
    [DisallowMultipleComponent]
    public sealed class CinemachineRigApplier : MonoBehaviour
    {
        [SerializeField, Tooltip("The rig whose arm offset drives this camera.")]
        CameraRig m_Rig;

        CinemachineFollow m_Follow;

        void Awake()
        {
            m_Follow = GetComponent<CinemachineFollow>();

            if (m_Rig == null)
            {
                Debug.LogError($"{nameof(CinemachineRigApplier)} has no {nameof(CameraRig)} assigned.", this);
                enabled = false;
            }
        }

        // LateUpdate so the rig has finished moving for this frame before Cinemachine reads it.
        void LateUpdate() => m_Follow.FollowOffset = m_Rig.ArmOffset;
    }
}
