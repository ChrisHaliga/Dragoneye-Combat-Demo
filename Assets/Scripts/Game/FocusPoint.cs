using Dragoneye.CameraControl;
using Dragoneye.Settings;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// The point of interest the camera watches: world state, replicated to every client.
    ///
    /// It lives in the game assembly, not the camera assembly, because its transform is replicated --
    /// where a player is looking is something other players can see. The camera reaches it only
    /// through <see cref="ICameraFocus"/>, so the view never names a networked type.
    ///
    /// Named "focus", not "cursor": a mouse cursor is a different thing this project will also need.
    ///
    /// Moves instantly, with no smoothing. Damping on a directly-driven point reads as input lag,
    /// not as polish.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FocusPoint : MonoBehaviour, ICameraFocus
    {
        [SerializeField, Tooltip("World units per second at full deflection.")]
        float m_MoveSpeed = 22f;

        [SerializeField, Tooltip("World units per pixel while drag-panning.")]
        float m_DragSpeed = 0.05f;

        [SerializeField, Tooltip("Confine the cursor. Bounds are supplied at runtime; see HexArenaCameraBounds.")]
        bool m_ClampToBounds = true;

        Bounds m_Bounds;
        bool m_HasBounds;

        public Vector3 Position => transform.position;

        /// <summary>
        /// Confines the focus point. Supplied at runtime so it adapts to whatever arena is loaded
        /// without knowing anything about maps.
        /// </summary>
        public void SetBounds(Bounds bounds)
        {
            m_Bounds = bounds;
            m_HasBounds = true;
            ApplyBounds();
        }

        public void ClearBounds() => m_HasBounds = false;

        /// <summary>
        /// Continuous movement from a held control, relative to the given yaw so "up" always means
        /// away from the camera. Time-scaled, because the input is a sustained direction.
        /// </summary>
        /// <param name="input">x = right, y = forward, each -1 to 1.</param>
        /// <param name="yawDegrees">The camera's current yaw.</param>
        /// <param name="speedScale">Lets the caller widen strides when zoomed out.</param>
        public void Move(Vector2 input, float yawDegrees, float deltaTime, float speedScale = 1f)
        {
            if (input.sqrMagnitude < 1e-6f)
            {
                return;
            }

            var direction = CameraRigMath.PanDirection(input, yawDegrees);
            Translate(direction * (m_MoveSpeed * GameSettings.PanSensitivity * speedScale * deltaTime));
        }

        /// <summary>
        /// Move by a pointer movement measured in pixels.
        ///
        /// Deliberately NOT scaled by delta time: a pointer delta is already a discrete distance
        /// moved since the last frame. Scaling it by delta time as well makes the same physical
        /// mouse movement travel half as far at 120fps as at 60fps.
        /// </summary>
        public void Drag(Vector2 pixelDelta, float yawDegrees, float speedScale = 1f)
        {
            if (pixelDelta.sqrMagnitude < 1e-6f)
            {
                return;
            }

            // Dragging grabs the world, so the cursor moves opposite to the pointer.
            var direction = CameraRigMath.PanDirectionFromDelta(-pixelDelta, yawDegrees);
            Translate(direction
                * (pixelDelta.magnitude * m_DragSpeed * GameSettings.PanSensitivity * speedScale));
        }

        /// <summary>Jumps the focus somewhere, respecting bounds. For "focus my unit".</summary>
        public void SnapTo(Vector3 position)
        {
            transform.position = new Vector3(position.x, transform.position.y, position.z);
            ApplyBounds();
        }

        void Translate(Vector3 delta)
        {
            transform.position += delta;
            ApplyBounds();
        }

        void ApplyBounds()
        {
            if (m_ClampToBounds && m_HasBounds)
            {
                transform.position = CameraRigMath.ClampToBounds(transform.position, m_Bounds);
            }
        }

        void OnDrawGizmosSelected()
        {
            if (!m_HasBounds)
            {
                return;
            }

            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.5f);
            Gizmos.DrawWireCube(m_Bounds.center, m_Bounds.size);
        }
    }
}
