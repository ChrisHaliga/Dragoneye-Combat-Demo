using UnityEngine;

namespace Dragoneye.CameraControl
{
    /// <summary>
    /// How the camera looks at the cursor: which way round, and from how far.
    ///
    /// Sits on the cursor's position every frame and adds yaw, so Cinemachine has a single target
    /// carrying both. Owns no movement of its own -- that belongs to <see cref="CameraCursor"/> --
    /// and publishes an arm offset rather than touching a camera, so it knows nothing about
    /// Cinemachine either.
    ///
    /// Every tunable is a serialised field on purpose. Speeds buried in method bodies are the
    /// single most common reason a camera "feels wrong" and cannot be fixed without a recompile.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraRig : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField, Tooltip("The cursor this camera orbits and follows.")]
        CameraCursor m_Cursor;

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

        public float ZoomSensitivity => m_ZoomSensitivity;

        public float OrbitDragSpeed => m_OrbitDragSpeed;

        public float ZoomDragSpeed => m_ZoomDragSpeed;

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

        void Awake()
        {
            if (m_Cursor == null)
            {
                m_Cursor = FindAnyObjectByType<CameraCursor>();
            }

            m_Yaw = transform.eulerAngles.y;
            Zoom = Mathf.Clamp01(m_InitialZoom);
            m_TargetZoom = Zoom;
            RecalculateArm();
        }

        void LateUpdate()
        {
            Zoom = m_ZoomSmoothTime > 0f
                ? Mathf.SmoothDamp(Zoom, m_TargetZoom, ref m_ZoomVelocity, m_ZoomSmoothTime)
                : m_TargetZoom;

            RecalculateArm();

            // Snap to the cursor with no smoothing of its own. Cinemachine's damping is disabled
            // for the same reason: on a directly-driven camera it reads as input lag.
            if (m_Cursor != null)
            {
                transform.SetPositionAndRotation(m_Cursor.Position, Quaternion.Euler(0f, m_Yaw, 0f));
            }
            else
            {
                transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);
            }
        }

        /// <summary>
        /// Swaps the cursor being followed. Networked cursors only exist once a match starts, so
        /// this cannot always be wired up in the scene.
        /// </summary>
        public void SetCursor(CameraCursor cursor)
        {
            m_Cursor = cursor;

            if (cursor != null)
            {
                transform.position = cursor.Position;
            }
        }

        /// <param name="input">-1 to 1. Positive turns clockwise.</param>
        public void Rotate(float input, float deltaTime)
        {
            if (Mathf.Approximately(input, 0f))
            {
                return;
            }

            m_Yaw += input * m_RotateSpeed * deltaTime;
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
