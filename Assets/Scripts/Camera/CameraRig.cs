using Dragoneye.Settings;
using UnityEngine;

namespace Dragoneye.CameraControl
{
    /// <summary>
    /// How the camera looks at the focus point: which way round, and from how far.
    ///
    /// Sits on the focus position every frame and adds yaw, so Cinemachine has a single target
    /// carrying both. Owns no movement of its own -- that belongs to the <see cref="ICameraFocus"/>
    /// -- and publishes an arm offset rather than touching a camera, so it knows nothing about
    /// Cinemachine either.
    ///
    /// Every tunable is a serialised field on purpose. Speeds buried in method bodies are the
    /// single most common reason a camera "feels wrong" and cannot be fixed without a recompile.
    /// </summary>
    // Runs before CinemachineRigApplier so the applier pushes an offset computed this frame, and
    // before the brain so Cinemachine positions the camera from a rig that has already moved.
    // Without explicit ordering these three LateUpdates run in an arbitrary order and roughly half
    // the time each reads the previous frame.
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class CameraRig : MonoBehaviour
    {
        [Header("Orbit")]
        [SerializeField, Tooltip("Degrees per second from Q/E.")]
        float m_RotateSpeed = 120f;

        [SerializeField, Tooltip("Degrees per pixel of horizontal drag while orbit-dragging.")]
        float m_OrbitDragSpeed = 0.25f;

        [Header("Zoom")]
        [SerializeField, Range(0f, 1f), Tooltip("Starting zoom. 0 is closest, 1 is furthest out.")]
        float m_InitialZoom = 0.6f;

        [SerializeField, Min(1f)]
        float m_MinDistance = 8f;

        [SerializeField, Min(1f)]
        float m_MaxDistance = 34f;

        [SerializeField, Range(5f, 89f), Tooltip("Camera pitch when fully zoomed in. Lower is more cinematic.")]
        float m_MinPitch = 35f;

        [SerializeField, Range(5f, 89f), Tooltip("Camera pitch when fully zoomed out. Higher is more top-down.")]
        float m_MaxPitch = 68f;

        [SerializeField, Tooltip("Scales raw scroll input. Windows wheels report ~120 per notch, "
             + "giving roughly ten notches across the full zoom range. If scrolling feels wrong on "
             + "another platform, this is the knob.")]
        float m_ZoomSensitivity = 0.0008f;

        [SerializeField, Tooltip("Zoom change per pixel of vertical drag while orbit-dragging.")]
        float m_ZoomDragSpeed = 0.0025f;

        [SerializeField, Min(0f), Tooltip("Seconds to settle on a new zoom level. 0 is instant. "
             + "Only zoom is smoothed -- cursor movement is deliberately direct.")]
        float m_ZoomSmoothTime = 0.1f;

        float m_Yaw;
        float m_TargetZoom;
        float m_ZoomVelocity;

        /// <summary>Normalised zoom, 0 (closest) to 1 (furthest out).</summary>
        public float Zoom { get; private set; }

        public float Yaw => m_Yaw;

        // Player sensitivity is folded in here rather than at the input site, so the serialised
        // field stays the "feels right at 1x" baseline and the preference stays a pure multiplier.
        public float ZoomSensitivity => m_ZoomSensitivity * GameSettings.ZoomSensitivity;

        /// <summary>Signed: negative when the player has asked for inverted orbit drag.</summary>
        public float OrbitDragSpeed =>
            m_OrbitDragSpeed * GameSettings.OrbitSensitivity * GameSettings.OrbitDirection;

        public float ZoomDragSpeed => m_ZoomDragSpeed * GameSettings.ZoomSensitivity;

        /// <summary>
        /// Camera offset from the cursor, in rig-local space. An applier copies this onto whatever
        /// is actually driving the camera.
        /// </summary>
        public Vector3 ArmOffset { get; private set; }

        /// <summary>
        /// Widens cursor strides when zoomed out. Moving at a fixed world speed feels sluggish far
        /// out and twitchy up close, because the same distance covers a different slice of screen.
        /// </summary>
        public float PanSpeedScale =>
            Mathf.Lerp(m_MinDistance, m_MaxDistance, Zoom) / m_MaxDistance;

        // Not serialised: the focus is a networked object that only exists once a match starts, so
        // it is handed in by ArenaContext rather than wired in the scene.
        CameraFocusRef m_Focus;

        void Awake()
        {
            m_Yaw = transform.eulerAngles.y;
            Zoom = Mathf.Clamp01(m_InitialZoom);
            m_TargetZoom = Zoom;
            RecalculateArm();
        }

        void LateUpdate()
        {
            Zoom = m_ZoomSmoothTime > 0f
                // Unscaled to match CameraRigInput. With scaled time, pausing the game would freeze
                // zoom while panning kept working, because only one of the two reads timeScale.
                ? Mathf.SmoothDamp(Zoom, m_TargetZoom, ref m_ZoomVelocity, m_ZoomSmoothTime,
                    Mathf.Infinity, Time.unscaledDeltaTime)
                : m_TargetZoom;

            RecalculateArm();

            // Snap to the cursor with no smoothing of its own. Cinemachine's damping is disabled
            // for the same reason: on a directly-driven camera it reads as input lag.
            var focus = m_Focus.Value;

            if (focus != null)
            {
                transform.SetPositionAndRotation(focus.Position, Quaternion.Euler(0f, m_Yaw, 0f));
            }
            else
            {
                transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            }
        }

        /// <summary>
        /// Swaps the focus being followed, snapping to it so the first frame does not interpolate
        /// from wherever the rig happened to be.
        /// </summary>
        public void SetFocus(ICameraFocus focus)
        {
            m_Focus = new CameraFocusRef(focus);

            if (m_Focus.IsAlive)
            {
                transform.position = focus.Position;
            }
        }

        /// <param name="input">-1 to 1. Positive turns clockwise.</param>
        public void Rotate(float input, float deltaTime)
        {
            if (Mathf.Approximately(input, 0f))
            {
                return;
            }

            m_Yaw += input * m_RotateSpeed * GameSettings.OrbitSensitivity * deltaTime;
        }

        /// <summary>Orbit by a raw degree amount. Used by drag, which is already frame-independent.</summary>
        public void AddYaw(float degrees) => m_Yaw += degrees;

        /// <summary>Adds to the target zoom. Smoothing is applied in <see cref="LateUpdate"/>.</summary>
        public void AddZoom(float delta) => m_TargetZoom = Mathf.Clamp01(m_TargetZoom + delta);

        void RecalculateArm() =>
            ArmOffset = CameraRigMath.ArmOffset(
                Zoom, m_MinDistance, m_MaxDistance, m_MinPitch, m_MaxPitch);

        void OnValidate()
        {
            m_MaxDistance = Mathf.Max(m_MaxDistance, m_MinDistance);
            m_MaxPitch = Mathf.Max(m_MaxPitch, m_MinPitch);
        }
    }
}
