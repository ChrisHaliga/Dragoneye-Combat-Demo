using System;
using UnityEngine;

namespace Dragoneye.Hex.Systems
{
    /// <summary>
    /// The map in the scene: built from its definition, placed by its transform, and read through
    /// <see cref="Grid"/> by everything that walks or looks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaMap : MonoBehaviour, IHexMapSource
    {
        [SerializeField]
        HexMapDefinition m_Definition;

        [SerializeField, Tooltip("Reserved for procedural definitions. Ignored by fixed maps.")]
        int m_Seed;

        public HexMap Map { get; private set; }

        /// <summary>Movement and sight, as this map has them. Rebuilt with the map.</summary>
        public IGridRules Grid { get; private set; }

        public event Action<HexMap> MapBuilt;

        public HexMapDefinition Definition => m_Definition;

        void Awake() => Rebuild();

        /// <summary>
        /// Builds a different map in place of the one the scene assigned.
        ///
        /// For a fight that brings its own board -- a test scenario, one day a chosen map -- and
        /// wants everything that draws and walks the arena to follow. They all listen for
        /// <see cref="MapBuilt"/>, so they do.
        /// </summary>
        public void Rebuild(HexMapDefinition definition)
        {
            m_Definition = definition;
            Rebuild();
        }

        public void Rebuild()
        {
            if (m_Definition == null)
            {
                Debug.LogError($"{nameof(ArenaMap)} has no map definition assigned.", this);
                return;
            }

            Map = m_Definition.Build(m_Seed);
            Grid = new GridRules(Map);
            MapBuilt?.Invoke(Map);
        }

        // All of these tolerate a null map. Rebuild already logs the real cause when a definition
        // is missing, and letting a NullReferenceException pile on top only buries that message.

        /// <summary>The centre of a tile, in the world.</summary>
        public Vector3 ToWorld(Hex hex) =>
            Map == null ? transform.position : transform.TransformPoint(Map.Layout.ToWorld(hex));

        /// <summary>
        /// The centre of a cell, in the world: the tile's centre plus the area's offset within it.
        ///
        /// The offset is the integer geometry's answer scaled to the layout, so the token stands
        /// exactly where the rules say the area is.
        /// </summary>
        public Vector3 ToWorld(Cell cell)
        {
            if (Map == null)
            {
                return transform.position;
            }

            var local = Map.Layout.ToWorld(cell.Tile);

            if (cell.Area != 0 && Map.TryGetTile(cell.Tile, out var tile))
            {
                AreaGeometry.Centre(tile.Areas, cell.Area, out var x, out var z);
                var scale = Map.Layout.Size / TileGeometry.Scale;
                local += new Vector3((float)(x * scale), 0f, (float)(z * scale));
            }

            return transform.TransformPoint(local);
        }

        public Hex FromWorld(Vector3 world) =>
            Map == null ? Hex.Zero : Map.Layout.FromWorld(transform.InverseTransformPoint(world));

        /// <summary>The cell under a world point, or null off the map or over dead footing.</summary>
        public Cell? CellFromWorld(Vector3 world) =>
            Map?.CellAt(transform.InverseTransformPoint(world));

        public Vector3 WorldCenter() =>
            Map == null ? transform.position : transform.TransformPoint(Map.WorldCenter());
    }
}
