using System;
using Dragoneye.Hex;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, so out here the bare name Hex would still bind to the
    // Dragoneye.Hex namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Resolves the mouse to a hex, every frame, and reports clicks on one.
    ///
    /// Deliberately not a physics raycast. That would mean adding ninety-odd colliders whose only
    /// purpose is hit-testing, a broadphase query per frame, and a pointer whose accuracy depends on
    /// how tiles happen to be drawn. Intersecting a plane and calling
    /// <see cref="Hex.ArenaMapExtensions">FromWorld</see> is constant time, reuses the transform
    /// that already has edit-mode tests behind it, and works whether tiles are separate objects, one
    /// combined mesh, or not rendered at all.
    ///
    /// The pointer is not a GameObject and nothing about it is replicated -- it is a per-frame
    /// answer to "which hex is under the cursor on this machine".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HexPointer : MonoBehaviour
    {
        [SerializeField, Tooltip("Asset containing the Camera action map.")]
        InputActionAsset m_Actions;

        [SerializeField]
        string m_ActionMapName = "Camera";

        InputAction m_PointerPosition;
        InputAction m_Select;

        /// <summary>The hex under the cursor, or null when the cursor is off the map.</summary>
        public Hex? Hovered { get; private set; }

        /// <summary>Raised when a click lands on a hex that exists.</summary>
        public event Action<Hex> Clicked;

        /// <summary>Raised whenever <see cref="Hovered"/> changes, including to null.</summary>
        public event Action<Hex?> HoverChanged;

        void Awake()
        {
            if (m_Actions == null)
            {
                Debug.LogError($"{nameof(HexPointer)} has no input actions assigned.", this);
                enabled = false;
                return;
            }

            var map = m_Actions.FindActionMap(m_ActionMapName, throwIfNotFound: false);
            if (map == null)
            {
                Debug.LogError($"No '{m_ActionMapName}' action map in {m_Actions.name}.", this);
                enabled = false;
                return;
            }

            m_PointerPosition = map.FindAction("PointerPosition", throwIfNotFound: true);
            m_Select = map.FindAction("Select", throwIfNotFound: true);
        }

        void OnEnable()
        {
            if (m_Select != null)
            {
                m_Select.performed += OnSelectPerformed;
            }
        }

        void OnDisable()
        {
            if (m_Select != null)
            {
                m_Select.performed -= OnSelectPerformed;
            }

            SetHovered(null);
        }

        void Update()
        {
            var resolved = Resolve();
            if (!Equals(resolved, Hovered))
            {
                SetHovered(resolved);
            }
        }

        Hex? Resolve()
        {
            var context = ArenaContext.Current;
            if (context == null || context.OutputCamera == null || context.Map == null
                || context.Map.Map == null || m_PointerPosition == null)
            {
                return null;
            }

            var arena = context.Map;

            // The arena's own plane, not world XZ: the map can be moved or tilted in the scene and
            // the pointer should follow it.
            var plane = new Plane(arena.transform.up, arena.transform.position);
            var ray = context.OutputCamera.ScreenPointToRay(m_PointerPosition.ReadValue<Vector2>());

            if (!HexPointerMath.TryGroundPoint(ray, plane, out var point))
            {
                return null;
            }

            var hex = arena.FromWorld(point);
            return arena.Map.Contains(hex) ? hex : (Hex?)null;
        }

        void SetHovered(Hex? hex)
        {
            Hovered = hex;
            HoverChanged?.Invoke(hex);
        }

        void OnSelectPerformed(InputAction.CallbackContext _)
        {
            // Resolved fresh rather than reusing the cached hover: a click can arrive before this
            // component's Update has run for the frame.
            var hex = Resolve();
            if (hex.HasValue)
            {
                Clicked?.Invoke(hex.Value);
            }
        }
    }
}
