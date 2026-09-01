using Dragoneye.Hex;
using UnityEngine;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, so out here the bare name Hex would still bind to the
    // Dragoneye.Hex namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Shows which hex the mouse is over by moving a marker onto it.
    ///
    /// A separate marker rather than tinting the tile itself. Tinting would mean the highlight only
    /// works when tiles happen to be individual renderers -- the same coupling the pointer avoids by
    /// resolving with maths instead of colliders. A marker keeps working if tiles become one
    /// combined mesh, a prefab with its own art, or nothing at all.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HexHoverHighlight : MonoBehaviour
    {
        [SerializeField]
        HexPointer m_Pointer;

        [SerializeField, Tooltip("Marker moved onto the hovered tile. Hidden when nothing is hovered.")]
        GameObject m_Marker;

        [SerializeField, Tooltip("Height above the tile surface, to avoid z-fighting.")]
        float m_GroundOffset = 0.03f;

        void OnEnable()
        {
            if (m_Pointer == null || m_Marker == null)
            {
                Debug.LogError($"{nameof(HexHoverHighlight)} is missing its pointer or marker.", this);
                enabled = false;
                return;
            }

            m_Pointer.HoverChanged += OnHoverChanged;
            OnHoverChanged(m_Pointer.Hovered);
        }

        void OnDisable()
        {
            if (m_Pointer != null)
            {
                m_Pointer.HoverChanged -= OnHoverChanged;
            }

            if (m_Marker != null)
            {
                m_Marker.SetActive(false);
            }
        }

        void OnHoverChanged(Hex? hex)
        {
            var context = ArenaContext.Current;
            if (!hex.HasValue || context == null || context.Map == null)
            {
                m_Marker.SetActive(false);
                return;
            }

            m_Marker.transform.position = context.Map.ToWorld(hex.Value) + Vector3.up * m_GroundOffset;
            m_Marker.SetActive(true);
        }
    }
}
