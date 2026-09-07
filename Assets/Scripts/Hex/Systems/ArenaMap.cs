using System;
using System.Collections.Generic;
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

        // The definition the scene assigned, kept because it carries the palette: every recipe
        // built here binds its terrain names through it.
        AuthoredMapDefinition m_Authored;

        // A definition made for a recipe, replaced -- and destroyed -- by the next.
        AuthoredMapDefinition m_Runtime;

        public HexMap Map { get; private set; }

        /// <summary>Movement and sight, as this map has them. Rebuilt with the map.</summary>
        public IGridRules Grid { get; private set; }

        public event Action<HexMap> MapBuilt;

        public HexMapDefinition Definition => m_Definition;

        /// <summary>What each terrain name means on this arena: the scene's authored palette.</summary>
        public IReadOnlyList<AuthoredMapDefinition.TerrainEntry> Palette =>
            m_Authored != null ? m_Authored.Palette : Array.Empty<AuthoredMapDefinition.TerrainEntry>();

        /// <summary>The asset a terrain name means here, or null when the palette does not say.</summary>
        public TerrainType TerrainNamed(string name) => m_Authored != null ? m_Authored.TerrainNamed(name) : null;

        void Awake()
        {
            m_Authored = m_Definition as AuthoredMapDefinition;
            Rebuild();
        }

        void OnDestroy() => DropRuntime();

        /// <summary>
        /// Builds a different map in place of the one the scene assigned.
        ///
        /// For a fight that brings its own board -- a test scenario, the map the host picked --
        /// and wants everything that draws and walks the arena to follow. They all listen for
        /// <see cref="MapBuilt"/>, so they do.
        /// </summary>
        public void Rebuild(HexMapDefinition definition)
        {
            m_Definition = definition;
            Rebuild();
        }

        /// <summary>
        /// Builds a recipe onto this arena, with its terrain names bound through the scene's
        /// palette. The one way a recipe becomes a board, so a scenario and a picked map cannot
        /// bind "grass" differently.
        /// </summary>
        public void Rebuild(MapRecipe recipe)
        {
            if (recipe == null)
            {
                return;
            }

            if (m_Authored == null)
            {
                Debug.LogError($"{nameof(ArenaMap)} has no authored map assigned, so it has no palette "
                    + "to build a recipe with.", this);
                return;
            }

            DropRuntime();

            m_Runtime = ScriptableObject.CreateInstance<AuthoredMapDefinition>();
            m_Runtime.Author(recipe, m_Authored.Palette);
            Rebuild(m_Runtime);
        }

        void DropRuntime()
        {
            if (m_Runtime != null)
            {
                Destroy(m_Runtime);
                m_Runtime = null;
            }
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

        /// <summary>
        /// The middle of the gap a step from one cell to the next goes through, in the world.
        ///
        /// For a token drawing a walk. The straight line between two cell centres can cut through
        /// the very wall the step went round -- on a cut tile the centre of a piece sits well to
        /// one side of the gap -- so a walk is drawn centre, gap, centre.
        /// </summary>
        public bool TryCrossingPoint(Cell from, Cell to, out Vector3 world)
        {
            world = ToWorld(to);

            if (Map == null || Grid == null || !Grid.TryCrossing(from, to, out var halfEdge))
            {
                return false;
            }

            TileGeometry.RayEnd(halfEdge, out var ax, out var az);
            TileGeometry.RayEnd(halfEdge + 1, out var bx, out var bz);

            var scale = Map.Layout.Size / TileGeometry.Scale;
            var local = Map.Layout.ToWorld(from.Tile)
                + new Vector3((float)((ax + bx) * 0.5 * scale), 0f, (float)((az + bz) * 0.5 * scale));

            world = transform.TransformPoint(local);
            return true;
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
