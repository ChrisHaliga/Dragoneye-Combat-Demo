using Dragoneye.Hex;
using UnityEngine;
using Dragoneye.Game.Combat;
using Dragoneye.Game.Creatures;

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

        // The marker's own mesh, and what it was before any area replaced it. A split tile lights
        // only the half under the cursor; a whole one lights the way it always did.
        MeshFilter m_MarkerMesh;
        Mesh m_TileMesh;

        void OnEnable()
        {
            if (m_Pointer == null || m_Marker == null)
            {
                Debug.LogError($"{nameof(HexHoverHighlight)} is missing its pointer or marker.", this);
                enabled = false;
                return;
            }

            if (m_MarkerMesh == null)
            {
                m_MarkerMesh = m_Marker.GetComponentInChildren<MeshFilter>();
                m_TileMesh = m_MarkerMesh != null ? m_MarkerMesh.sharedMesh : null;
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

        void OnHoverChanged(Cell? hex)
        {
            var context = ArenaContext.Current;
            if (!hex.HasValue || context == null || context.Map == null)
            {
                m_Marker.SetActive(false);
                return;
            }

            var cell = hex.Value;
            var map = context.Map.Map;
            var split = map != null && map.TryGetTile(cell.Tile, out var tile) && tile.Areas.Count > 1;

            if (m_MarkerMesh != null)
            {
                m_MarkerMesh.sharedMesh = split ? AreaMeshes.For(map, cell) : m_TileMesh;
            }

            // The fan is drawn in the tile's frame, apex at its centre, whichever area it is.
            m_Marker.transform.position = context.Map.ToWorld(cell.Tile) + Vector3.up * m_GroundOffset;
            m_Marker.transform.rotation = context.Map.transform.rotation;
            m_Marker.SetActive(true);
        }
    }
}
