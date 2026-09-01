using Dragoneye.Hex.Systems;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Tells the focus point how far it may roam, derived from whatever arena is loaded.
    ///
    /// This exists so neither side has to know about the other. The camera assembly has no
    /// reference to the hex assemblies and would work in a game with no grid at all; the hex
    /// assemblies have never heard of a camera. Bounds are pushed in rather than polled, so there
    /// is no per-frame cost and no ordering puzzle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HexArenaCameraBounds : MonoBehaviour
    {
        [SerializeField]
        ArenaMap m_Arena;

        [SerializeField]
        FocusPoint m_Focus;

        [SerializeField, Tooltip("Extra world units the camera may travel beyond the outermost tile.")]
        float m_Margin = 2f;

        void OnEnable()
        {
            if (m_Arena != null)
            {
                m_Arena.MapBuilt += OnMapBuilt;
            }
        }

        void OnDisable()
        {
            if (m_Arena != null)
            {
                m_Arena.MapBuilt -= OnMapBuilt;
            }
        }

        void Start()
        {
            // ArenaMap builds in Awake, so MapBuilt has usually already fired by now.
            if (m_Arena != null && m_Arena.Map != null)
            {
                Apply();
            }
        }

        /// <summary>
        /// Points at a different focus point and immediately re-applies the bounds. Called when the
        /// local player's networked focus spawns.
        /// </summary>
        public void SetFocus(FocusPoint focus)
        {
            m_Focus = focus;
            Apply();
        }

        void OnMapBuilt(Dragoneye.Hex.HexMap map) => Apply();

        void Apply()
        {
            if (m_Focus == null || m_Arena == null || m_Arena.Map == null || m_Arena.Map.Count == 0)
            {
                return;
            }

            // Not seeded from a real position: the first tile below sets it, and seeding from an
            // arbitrary hex would silently widen the box if the loop ever ran zero times.
            var bounds = default(Bounds);
            var first = true;

            foreach (var hex in m_Arena.Map.Coordinates)
            {
                var position = m_Arena.ToWorld(hex);
                if (first)
                {
                    bounds = new Bounds(position, Vector3.zero);
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(position);
                }
            }

            bounds.Expand(new Vector3(m_Margin * 2f, 0f, m_Margin * 2f));
            m_Focus.SetBounds(bounds);
        }
    }
}
