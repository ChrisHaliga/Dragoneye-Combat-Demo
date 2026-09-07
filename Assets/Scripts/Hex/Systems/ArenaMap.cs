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

        /// <summary>
        /// The cell under a world point, or null when the point is off the map or in a sliver of a
        /// tile nobody can stand in.
        /// </summary>
        public Cell? CellFromWorld(Vector3 world)
        {
            if (Map == null)
            {
                return null;
            }

            var local = transform.InverseTransformPoint(world);
            var hex = Map.Layout.FromWorld(local);

            if (!Map.TryGetTile(hex, out var tile))
            {
                return null;
            }

            if (tile.Areas.Count <= 1)
            {
                return tile.Areas.Count == 1 ? Cell.Whole(hex) : (Cell?)null;
            }

            var centre = Map.Layout.ToWorld(hex);
            var scale = TileGeometry.Scale / Map.Layout.Size;
            var x = (long)((local.x - centre.x) * scale);
            var z = (long)((local.z - centre.z) * scale);

            var area = tile.Areas.AreaOf(TileGeometry.WedgeAt(x, z));

            return area == AreaLayout.Dead ? (Cell?)null : new Cell(hex, area);
        }

        public Vector3 WorldCenter() =>
            Map == null ? transform.position : transform.TransformPoint(Map.WorldCenter());
    }
}
