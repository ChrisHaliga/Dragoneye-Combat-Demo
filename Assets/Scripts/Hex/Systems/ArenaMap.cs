using System;
using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Hex.Systems
{
    /// <summary>
    /// The map in the scene: built from its definition, placed by its transform, and read through
    /// <see cref="Grid"/> by everything that walks or looks.
    ///
    /// Two boards, not one. <see cref="Map"/> is the board the fight is played on, and it changes
    /// the instant the fight changes it. <see cref="Shown"/> is the board that is drawn, and it
    /// changes only when the playback reaches the moment a wall changed -- because the fight runs
    /// ahead of what has been shown of it, and a wall that falls on screen several turns before the
    /// blow that felled it is not a pacing detail, it is a lie about what is happening. They are
    /// built from the same definition and are the same board in every respect until a wall moves,
    /// which is the only thing about a map that changes mid-fight.
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

        /// <summary>
        /// The board the fight is played on: what the rules ask about, and where a wall change
        /// lands the moment the server makes it.
        /// </summary>
        public HexMap Map { get; private set; }

        /// <summary>Movement and sight as the fight's board has them. Rebuilt with it.</summary>
        public IGridRules Grid { get; private set; }

        /// <summary>
        /// The board that is drawn, and pointed at: the same map, changed only when the playback
        /// reaches the moment a wall changed. Everything visible reads this one.
        /// </summary>
        public HexMap Shown { get; private set; }

        /// <summary>
        /// Movement and sight as the drawn board has them, for drawing a walk through the gaps a
        /// watcher can see.
        /// </summary>
        public IGridRules ShownGrid { get; private set; }

        /// <summary>Raised with the drawn board whenever the arena is rebuilt.</summary>
        public event Action<HexMap> MapBuilt;

        public HexMapDefinition Definition => m_Definition;

        /// <summary>What each terrain name means on this arena: the scene's authored palette.</summary>
        public IReadOnlyList<AuthoredMapDefinition.TerrainEntry> Palette =>
            m_Authored != null ? m_Authored.Palette : Array.Empty<AuthoredMapDefinition.TerrainEntry>();

        /// <summary>The asset a terrain name means here, or null when the palette does not say.</summary>
        public TerrainType TerrainNamed(string name) => m_Authored != null ? m_Authored.TerrainNamed(name) : null;

        void Awake()
        {
            EnsureAuthored();

            // Unless something has built one already. The arena is told which map the host picked
            // from another object's OnEnable, and Unity does not order Awake and OnEnable across
            // GameObjects -- so that pick can arrive before this runs, and it must not then be
            // overwritten by the map the scene happens to ship with.
            if (Map == null)
            {
                Rebuild();
            }
        }

        /// <summary>
        /// Remembers the map the scene assigned, which is where a recipe's terrain names are bound.
        ///
        /// Not left to Awake for the reason above: a recipe that arrived first found no palette,
        /// was refused, and left the match on the scene's own map -- whichever map had been picked.
        /// Once set it is kept, so a rebuild cannot lose the palette by replacing the definition.
        /// </summary>
        void EnsureAuthored()
        {
            if (m_Authored == null)
            {
                m_Authored = m_Definition as AuthoredMapDefinition;
            }
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

            EnsureAuthored();

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

            // Built a second time rather than shared: a definition builds the same board from the
            // same seed every time, so these start identical, and they have to be able to differ
            // for as long as it takes to show a wall coming down.
            Shown = m_Definition.Build(m_Seed);
            ShownGrid = new GridRules(Shown);

            MapBuilt?.Invoke(Shown);
        }

        /// <summary>
        /// Changes a wall on the board that is drawn, leaving the fight's board alone.
        ///
        /// The other half of <see cref="HexMap.SetWall"/>: the fight changes its own board at once,
        /// and this is called when the playback reaches the event, so a wall falls on screen at the
        /// moment the watcher is shown it falling. Integrity is the rules' business and stays as
        /// this board has it -- what a drawn wall needs to know is how tall to stand.
        /// </summary>
        public void ShowWall(WallSegment segment, WallFlags flags)
        {
            if (Shown == null || !Shown.Contains(segment.Tile))
            {
                return;
            }

            Shown.SetWall(segment, new Wall(flags, Shown.WallAt(segment).Integrity));
        }

        // All of these answer where something is on screen, so all of them read the drawn board.
        // They tolerate it being null: Rebuild already logs the real cause when a definition is
        // missing, and letting a NullReferenceException pile on top only buries that message.

        /// <summary>The centre of a tile, in the world.</summary>
        public Vector3 ToWorld(Hex hex) =>
            Shown == null ? transform.position : transform.TransformPoint(Shown.Layout.ToWorld(hex));

        /// <summary>
        /// The centre of a cell, in the world: the tile's centre plus the area's offset within it.
        ///
        /// The offset is the integer geometry's answer scaled to the layout, so the token stands
        /// exactly where the rules say the area is.
        /// </summary>
        public Vector3 ToWorld(Cell cell)
        {
            if (Shown == null)
            {
                return transform.position;
            }

            var local = Shown.Layout.ToWorld(cell.Tile);

            // Every area, including the first. Skipping area zero drew a creature standing in the
            // first piece of a split tile at the tile's centre instead -- which on a tile cut by
            // rays is across the wall, in the other piece. It read as walking through the wall,
            // and the rules never agreed: AreaGeometry.Position, which decides bearings and lines
            // of sight, has always offset area zero like any other.
            if (Shown.TryGetTile(cell.Tile, out var tile))
            {
                AreaGeometry.Centre(tile.Areas, cell.Area, out var x, out var z);
                var scale = Shown.Layout.Size / TileGeometry.Scale;
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

            if (Shown == null || ShownGrid == null || !ShownGrid.TryCrossing(from, to, out var halfEdge))
            {
                return false;
            }

            TileGeometry.RayEnd(halfEdge, out var ax, out var az);
            TileGeometry.RayEnd(halfEdge + 1, out var bx, out var bz);

            var scale = Shown.Layout.Size / TileGeometry.Scale;
            var local = Shown.Layout.ToWorld(from.Tile)
                + new Vector3((float)((ax + bx) * 0.5 * scale), 0f, (float)((az + bz) * 0.5 * scale));

            world = transform.TransformPoint(local);
            return true;
        }

        public Hex FromWorld(Vector3 world) =>
            Shown == null ? Hex.Zero : Shown.Layout.FromWorld(transform.InverseTransformPoint(world));

        /// <summary>
        /// The cell under a world point, or null off the map or over dead footing. Picked on the
        /// drawn board, because what a player points at is what they can see.
        /// </summary>
        public Cell? CellFromWorld(Vector3 world) =>
            Shown?.CellAt(transform.InverseTransformPoint(world));

        public Vector3 WorldCenter() =>
            Shown == null ? transform.position : transform.TransformPoint(Shown.WorldCenter());
    }
}
