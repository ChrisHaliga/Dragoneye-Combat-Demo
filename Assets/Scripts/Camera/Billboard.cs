using UnityEngine;

namespace Dragoneye.CameraControl
{
    /// <summary>
    /// Keeps a transform turned toward the camera, so world-space labels stay readable however the
    /// camera orbits.
    ///
    /// Runs in LateUpdate so it sees the camera's final position for the frame. Doing this in
    /// Update would leave labels a frame behind and visibly swim while orbiting.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Billboard : MonoBehaviour
    {
        [SerializeField, Tooltip("Leave empty to use the main camera.")]
        Camera m_Camera;

        [SerializeField, Tooltip("Keep the label upright rather than rolling with the camera.")]
        bool m_KeepUpright = true;

        [SerializeField, Tooltip("Tick if the label renders mirrored. Meshes differ on which face is the front.")]
        bool m_FaceAway;

        void LateUpdate()
        {
            // Re-resolved when missing: the camera is spawned per scene, and Camera.main returns
            // null for a frame or two around a scene load.
            if (m_Camera == null)
            {
                m_Camera = Camera.main;
                if (m_Camera == null)
                {
                    return;
                }
            }

            var toCamera = transform.position - m_Camera.transform.position;
            if (toCamera.sqrMagnitude < 1e-6f)
            {
                return;
            }

            if (m_FaceAway)
            {
                toCamera = -toCamera;
            }

            var up = m_KeepUpright ? Vector3.up : m_Camera.transform.up;
            transform.rotation = Quaternion.LookRotation(toCamera, up);
        }
    }
}
