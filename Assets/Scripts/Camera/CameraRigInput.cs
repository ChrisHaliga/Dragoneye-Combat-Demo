using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dragoneye.CameraControl
{
    /// <summary>
    /// Translates input into camera intents. The only file that knows which buttons do what.
    ///
    /// Uses Input Actions rather than polling devices directly, so bindings are data: rebindable,
    /// gamepad-capable, and safe when a device is absent. Polling <c>Keyboard.current.wKey</c>
    /// hardcodes the layout and throws the moment someone plays without a keyboard attached.
    ///
    /// Actions are resolved once on Awake, and resolution throws if one is missing: a typo in an
    /// action name should fail loudly at startup, not silently disable a control.
    /// </summary>
    [RequireComponent(typeof(CameraRig))]
    [DisallowMultipleComponent]
    public sealed class CameraRigInput : MonoBehaviour
    {
        [SerializeField, Tooltip("Asset containing the Camera action map.")]
        InputActionAsset m_Actions;

        [SerializeField]
        string m_ActionMapName = "Camera";

        CameraRig m_Rig;
        InputActionMap m_Map;

        // Not serialised: the focus is a networked object that only exists once a match starts, so
        // it is handed in by ArenaContext rather than wired in the scene.
        CameraFocusRef m_Focus;

        InputAction m_Pan;
        InputAction m_Rotate;
        InputAction m_Zoom;
        InputAction m_DragPan;
        InputAction m_OrbitDrag;
        InputAction m_PointerDelta;
        InputAction m_Leave;

        /// <summary>
        /// Raised when the player asks to leave the match.
        ///
        /// Surfaced as an event rather than acted on here: this component owns the action map, but
        /// leaving a match is not a camera concern. Whoever owns match lifetime subscribes.
        /// </summary>
        public event Action LeaveRequested;

        void Awake()
        {
            m_Rig = GetComponent<CameraRig>();

            if (m_Actions == null)
            {
                Debug.LogError($"{nameof(CameraRigInput)} has no input actions assigned.", this);
                enabled = false;
                return;
            }

            m_Map = m_Actions.FindActionMap(m_ActionMapName, throwIfNotFound: false);
            if (m_Map == null)
            {
                Debug.LogError($"No '{m_ActionMapName}' action map in {m_Actions.name}.", this);
                enabled = false;
                return;
            }

            m_Pan = m_Map.FindAction("Pan", throwIfNotFound: true);
            m_Rotate = m_Map.FindAction("Rotate", throwIfNotFound: true);
            m_Zoom = m_Map.FindAction("Zoom", throwIfNotFound: true);
            m_DragPan = m_Map.FindAction("DragPan", throwIfNotFound: true);
            m_OrbitDrag = m_Map.FindAction("OrbitDrag", throwIfNotFound: true);
            m_PointerDelta = m_Map.FindAction("PointerDelta", throwIfNotFound: true);
            m_Leave = m_Map.FindAction("Leave", throwIfNotFound: true);
        }

        // Enabling the map here rather than globally keeps camera input a context that can be
        // switched off wholesale -- during a cutscene, or while a modal UI has focus.
        void OnEnable()
        {
            if (m_Map == null)
            {
                return;
            }

            m_Map.Enable();
            m_Leave.performed += OnLeavePerformed;
        }

        void OnDisable()
        {
            if (m_Map == null)
            {
                return;
            }

            m_Leave.performed -= OnLeavePerformed;
            m_Map.Disable();
        }

        /// <summary>Swaps the focus this input drives. See <see cref="CameraRig.SetFocus"/>.</summary>
        public void SetFocus(ICameraFocus focus) => m_Focus = new CameraFocusRef(focus);

        void OnLeavePerformed(InputAction.CallbackContext _) => LeaveRequested?.Invoke();

        void Update()
        {
            if (m_Map == null)
            {
                return;
            }

            // Unscaled so the camera keeps responding while the game is paused.
            var deltaTime = Time.unscaledDeltaTime;
            var yaw = m_Rig.Yaw;
            var speedScale = m_Rig.PanSpeedScale;

            var focus = m_Focus.Value;

            if (focus != null)
            {
                focus.Move(m_Pan.ReadValue<Vector2>(), yaw, deltaTime, speedScale);
            }

            m_Rig.Rotate(m_Rotate.ReadValue<float>(), deltaTime);
            m_Rig.AddZoom(CameraRigMath.ZoomDelta(m_Zoom.ReadValue<float>(), m_Rig.ZoomSensitivity));

            var pointerDelta = m_PointerDelta.ReadValue<Vector2>();

            // Right-drag orbits and zooms in one gesture: horizontal matches Q/E, vertical matches
            // the scroll wheel. It takes priority so the two drag gestures cannot fight.
            if (m_OrbitDrag.IsPressed())
            {
                m_Rig.AddYaw(CameraRigMath.OrbitDragYaw(pointerDelta, m_Rig.OrbitDragSpeed));
                m_Rig.AddZoom(CameraRigMath.OrbitDragZoom(pointerDelta, m_Rig.ZoomDragSpeed));
            }
            else if (m_DragPan.IsPressed() && focus != null)
            {
                focus.Drag(pointerDelta, yaw, speedScale);
            }
        }
    }
}
