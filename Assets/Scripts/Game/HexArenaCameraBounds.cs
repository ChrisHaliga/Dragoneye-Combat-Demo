using Dragoneye.CameraControl;
using Dragoneye.Hex.Systems;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// Glue: tells the camera how far it may roam, derived from whatever arena is loaded.
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
        CameraCursor m_Cursor;

        [SerializeField, Tooltip("Extra world units the camera may travel beyond the outermost tile.")]
        float m_Margin = 2f;

        void Awake()
        {
            if (m_Arena == null)
            {
                m_Arena = FindAnyObjectByType<ArenaMap>();
            }

            if (m_Cursor == null)
            {
                m_Cursor = FindAnyObjectByType<CameraCursor>();
            }
        }

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
        /// Points at a different cursor and immediately re-applies the bounds. Called when the
        /// local player's networked cursor spawns.
        /// </summary>
        public void SetCursor(CameraCursor cursor)
        {
            m_Cursor = cursor;
            Apply();
        }

        void OnMapBuilt(Dragoneye.Hex.HexMap map) => Apply();

        void Apply()
        {
            if (m_Cursor == null || m_Arena == null || m_Arena.Map == null || m_Arena.Map.Count == 0)
            {
                return;
            }

            var bounds = new Bounds(m_Arena.ToWorld(default), Vector3.zero);
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
            m_Cursor.SetBounds(bounds);
        }
    }
}
