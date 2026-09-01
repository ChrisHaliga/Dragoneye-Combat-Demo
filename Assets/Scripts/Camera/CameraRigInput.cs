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
    /// Actions are resolved once on Awake. Resolving by name every frame is a string lookup in the
    /// hot path.
    /// </summary>
    [RequireComponent(typeof(CameraRig))]
    public sealed class CameraRigInput : MonoBehaviour
    {
        [SerializeField, Tooltip("Asset containing the Camera action map.")]
        InputActionAsset m_Actions;

        [SerializeField]
        string m_ActionMapName = "Camera";

        [SerializeField, Tooltip("The cursor that movement input drives.")]
        CameraCursor m_Cursor;

        CameraRig m_Rig;
        InputActionMap m_Map;

        InputAction m_Pan;
        InputAction m_Rotate;
        InputAction m_Zoom;
        InputAction m_DragPan;
        InputAction m_OrbitDrag;
        InputAction m_PointerDelta;

        void Awake()
        {
            m_Rig = GetComponent<CameraRig>();

            if (m_Cursor == null)
            {
                m_Cursor = FindAnyObjectByType<CameraCursor>();
            }

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

            m_Pan = m_Map.FindAction("Pan");
            m_Rotate = m_Map.FindAction("Rotate");
            m_Zoom = m_Map.FindAction("Zoom");
            m_DragPan = m_Map.FindAction("DragPan");
            m_OrbitDrag = m_Map.FindAction("OrbitDrag");
            m_PointerDelta = m_Map.FindAction("PointerDelta");
        }

        /// <summary>Swaps the cursor this input drives. See <see cref="CameraRig.SetCursor"/>.</summary>
        public void SetCursor(CameraCursor cursor) => m_Cursor = cursor;

        // Enabling the map here rather than globally keeps camera input a context that can be
        // switched off wholesale -- during a cutscene, or while a modal UI has focus.
        void OnEnable() => m_Map?.Enable();

        void OnDisable() => m_Map?.Disable();

        void Update()
        {
            var deltaTime = Time.unscaledDeltaTime;
            var yaw = m_Rig.Yaw;
            var speedScale = m_Rig.PanSpeedScale;

            if (m_Pan != null && m_Cursor != null)
            {
                m_Cursor.Move(m_Pan.ReadValue<Vector2>(), yaw, deltaTime, speedScale);
            }

            if (m_Rotate != null)
            {
                m_Rig.Rotate(m_Rotate.ReadValue<float>(), deltaTime);
            }

            if (m_Zoom != null)
            {
                m_Rig.AddZoom(CameraRigMath.ZoomDelta(m_Zoom.ReadValue<float>(), m_Rig.ZoomSensitivity));
            }

            var pointerDelta = m_PointerDelta != null
                ? m_PointerDelta.ReadValue<Vector2>()
                : Vector2.zero;

            // Right-drag orbits and zooms in one gesture: horizontal matches Q/E, vertical matches
            // the scroll wheel.
            if (m_OrbitDrag != null && m_OrbitDrag.IsPressed())
            {
                m_Rig.AddYaw(CameraRigMath.OrbitDragYaw(pointerDelta, m_Rig.OrbitDragSpeed));
                m_Rig.AddZoom(CameraRigMath.OrbitDragZoom(pointerDelta, m_Rig.ZoomDragSpeed));
            }
            else if (m_DragPan != null && m_DragPan.IsPressed() && m_Cursor != null)
            {
                m_Cursor.Drag(pointerDelta, yaw, speedScale);
            }
        }
    }
}
