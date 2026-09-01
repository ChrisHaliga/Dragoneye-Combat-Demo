using Dragoneye.CameraControl;
using Dragoneye.Hex.Systems;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// The arena's wiring, declared once in the scene instead of discovered at runtime.
    ///
    /// Replaces a spread of <c>FindAnyObjectByType</c> calls. Those made every dependency invisible
    /// at the call site, unmockable in a test, and quietly order-sensitive: a scan that runs before
    /// the object it wants exists returns null and the feature simply does not happen, with nothing
    /// to show for it. Serialised references fail loudly at edit time instead.
    ///
    /// Registered statically because the things that need it -- a network object spawning mid-match
    /// -- are created by netcode, not placed in the scene, so they cannot hold a serialised
    /// reference to it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaContext : MonoBehaviour
    {
        [SerializeField]
        ArenaMap m_Map;

        [SerializeField]
        CameraRig m_Rig;

        [SerializeField]
        CameraRigInput m_RigInput;

        [SerializeField]
        HexArenaCameraBounds m_CameraBounds;

        [SerializeField, Tooltip("The camera Cinemachine drives. Used by world-space labels.")]
        Camera m_OutputCamera;

        /// <summary>The context for the arena currently loaded, or null outside a match.</summary>
        public static ArenaContext Current { get; private set; }

        public ArenaMap Map => m_Map;

        public CameraRig Rig => m_Rig;

        public CameraRigInput RigInput => m_RigInput;

        public Camera OutputCamera => m_OutputCamera;

        void OnEnable()
        {
            if (Current != null && Current != this)
            {
                Debug.LogError($"A second {nameof(ArenaContext)} was enabled; the arena expects one.", this);
                return;
            }

            if (m_Map == null || m_Rig == null || m_RigInput == null || m_CameraBounds == null)
            {
                Debug.LogError($"{nameof(ArenaContext)} is missing references; wire them in the scene.", this);
                enabled = false;
                return;
            }

            Current = this;
        }

        void OnDisable()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>
        /// Points the camera at the local player's focus. Called once, when that focus spawns.
        /// </summary>
        public void FollowFocus(FocusPoint focus)
        {
            if (focus == null)
            {
                return;
            }

            m_Rig.SetFocus(focus);
            m_RigInput.SetFocus(focus);

            // Re-applies the arena bounds to the new focus.
            m_CameraBounds.SetFocus(focus);
        }
    }
}
