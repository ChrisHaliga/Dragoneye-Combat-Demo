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
                m_Rig = FindAnyObjectByType<CameraRig>();
            }

            if (m_Rig == null)
            {
                Debug.LogError($"{nameof(CinemachineRigApplier)} could not find a {nameof(CameraRig)}.", this);
                enabled = false;
            }
        }

        // LateUpdate so the rig has finished moving for this frame before Cinemachine reads it.
        void LateUpdate() => m_Follow.FollowOffset = m_Rig.ArmOffset;
    }
}
