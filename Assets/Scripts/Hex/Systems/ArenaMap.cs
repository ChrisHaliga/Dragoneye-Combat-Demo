using System;
using UnityEngine;

namespace Dragoneye.Hex.Systems
{
    /// <summary>
    /// The seam between the scene and the map data. Builds a <see cref="HexMap"/> from a definition
    /// asset and owns it for the lifetime of the arena; everything else in the scene asks this
    /// component rather than constructing maps of its own.
    ///
    /// Positions are offset by this transform, so the arena can be moved or rotated in the scene
    /// without the data layer knowing anything about it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaMap : MonoBehaviour
    {
        [SerializeField]
        HexMapDefinition m_Definition;

        [SerializeField, Tooltip("Reserved for procedural definitions. Ignored by fixed maps.")]
        int m_Seed;

        /// <summary>The live map. Null until <see cref="Awake"/> has run.</summary>
        public HexMap Map { get; private set; }

        /// <summary>Raised whenever the map is (re)built, so views can rebuild from scratch.</summary>
        public event Action<HexMap> MapBuilt;

        public HexMapDefinition Definition => m_Definition;

        void Awake() => Rebuild();

        public void Rebuild()
        {
            if (m_Definition == null)
            {
                Debug.LogError($"{nameof(ArenaMap)} has no map definition assigned.", this);
                return;
            }

            Map = m_Definition.Build(m_Seed);
            MapBuilt?.Invoke(Map);
        }

        public Vector3 ToWorld(Hex hex) =>
            transform.TransformPoint(Map.Layout.ToWorld(hex));

        public Hex FromWorld(Vector3 world) =>
            Map.Layout.FromWorld(transform.InverseTransformPoint(world));

        public Vector3 WorldCenter() => transform.TransformPoint(Map.WorldCenter());
    }
}
