# Hex grid

A flat hex grid, split into three assemblies so the layer boundaries are enforced by the compiler
rather than by convention.

| Assembly | Folder | May reference |
|---|---|---|
| `Dragoneye.Hex` | `Hex/` | *nothing* — engine only |
| `Dragoneye.Hex.Systems` | `Hex/Systems/` | Data |
| `Dragoneye.Hex.Rendering` | `Hex/Rendering/` | Data, Systems |
| `Dragoneye.Hex.Tests` | `Assets/Tests/EditMode/` | Data, Systems |

The data layer physically cannot reference the renderer: it is a separate DLL that does not list it.
If someone tries, the build breaks rather than the review catching it.

## Data layer

- **`Hex`** — a coordinate. Axial `(q, r)`; the third cube axis `S` is derived, since `q + r + s == 0`
  always holds. Cube space makes distance, rings and rounding trivial while storage stays two ints.
  Provides `Neighbor`, `Neighbors`, `Distance`, `Ring`, `Range`, `Line`, `Round`.
- **`HexLayout`** — the only type that knows about world space. `ToWorld` / `FromWorld` are exact
  inverses, which is what makes mouse picking two lines instead of a pile of special cases.
- **`Cell`** — a position: a tile and an area within it. Every creature, path and line is in terms
  of cells. Two areas of one tile are distance one apart; whole tiles keep `Hex.Distance`.
- **`Wall`** — what stands on a ray or a half-edge: `WallFlags.BlocksMovement`, `BlocksSight`, or
  both (`Solid`), plus an `Integrity` for the day something knocks one down.
- **`TileGeometry`** — the twelve rays of a tile, clockwise from North, and the integer frame every
  bearing is measured in. Even rays end at edge midpoints, odd ones at corners; half-edge *i* closes
  the wedge between rays *i* and *i + 1*. Vertical walls run rays 0 and 6, horizontal ones rays 3
  and 9 and then along a neighbour's flat edge — which is how a rectangle sits on a hex grid.
- **`HexTile`** — coordinates, terrain, and the walls it keeps the record of: its twelve rays and
  the six half-edges it owns (North, NorthEast and SouthEast; the neighbour owns the rest). Its
  `Areas` are derived from the rays that block movement whenever one changes. No geometry, no
  GameObject, and deliberately **no neighbour pointers**: who is next to what is `GridRules`' answer.
- **`AreaTable` / `AreaLayout`** — how a pattern of walled rays cuts a tile, worked out once per
  pattern and shared. A piece narrower than ninety degrees is *dead footing*: nobody stands in it, and
  that rule is what keeps a tile to at most four areas. `Carry` says where an area goes when the
  pattern changes.
- **`AreaGeometry`** — where an area's centre is and which of six directions one cell lies in from
  another. Integer arithmetic only: a bearing decides flanks on the server and on every client.
- **`HexMap`** — tiles keyed by coordinate, plus the layout. Sparse, so shape and size are properties
  of the *data*, not of the class. Resolves any wall by tile and index to whichever tile keeps it, so
  a caller never asks which side of an edge owns it. Raises `TileChanged` and `WallChanged` (with
  the areas the tile had before) so views react to exactly what changed. `CellAt` picks the cell
  under a point.
- **`TerrainType`** — a ScriptableObject, not an enum, so terrain can be added and retuned without
  recompiling.
- **`HexMapDefinition`** — abstract ScriptableObject with `Build(int seed)`. This is the seam that
  keeps arena shape out of the upper layers: they hold a definition reference and never learn which
  subclass it is. `GeneratedMapDefinition` (hexagon or rectangle) and `AuthoredMapDefinition` (a
  radius, a default terrain, tile overrides and the walls, by ray and by half-edge) are the two
  concrete ones; `ArenaMapSetup` writes the Ruins as the latter.

The `seed` parameter is threaded through from the start and currently ignored. Adding a procedural
definition later touches only the definition.

## Orientation

**Flat-top**: a flat edge faces north and a point faces east, so neighbours sit at compass bearings
0, 60, 120, 180, 240 and 300 degrees. There is a neighbour due north and none due east — the
opposite of pointy-top, and worth remembering when reading the direction table.

```
x = size * 1.5 * q
z = size * (√3/2 * q + √3 * r)
```

## Walls

A wall is either an **interior ray** of a tile (centre to a corner or an edge midpoint) or a
**half** of a tile's edge. Twenty-four positions touch each tile; a half-edge is shared with the
neighbour, so each has one owner and the map resolves the other side (`TileGeometry.Twin` is the
mirror across the edge, *not* the half turn: the east half of a South edge meets the east half of
the neighbour's North edge).

Rays that block movement cut the tile into **areas**. Each area is a cell; a creature stands in
one; two areas of one tile have no path between them except round the outside, and no bearing
problem, because bearings come from area centres. Half-edge walls never change the areas, which is
why a door should be a half-edge: opening it invalidates nobody's position. A ray changing mid-fight
renumbers the tile, and `AreaLayout.Carry` plus the `WallChanged` event are the seam for moving
creatures with it; nothing calls it yet, because nothing changes walls mid-fight yet.

Sight and movement are independent flags on the same wall. A **low wall** (movement only) is
stepped round and shot over at a cost; a **curtain** (sight only) is walked through and not seen
through; **solid** is both.

## Systems layer

- **`IGridRules` / `GridRules`** — the seam every "where can I go" question goes through. Neighbours
  are the cells across open half-edges onto standable ground; `StepsToEnter` is the terrain's
  price. Read live from the map, nothing baked, so a wall that changes is simply the next answer.
- **`HexPathfinder`** — Dijkstra over cells by `IGridRules`, returning the route and its cost in
  steps. `Reachable` gives the whole frontier for a budget, which is what the reach overlay draws.
- **`LineOfSight`** — the static half of a line: walked tile by tile from one area centre to the
  other, tested against every ray it crosses and the half-edge at every boundary, in the same
  integer frame as bearings. `Clear`, `Obstructed` (a low wall, or impassable ground) or `Blocked`.
  `TilesBetween` is the body pass's walk; the bodies themselves are the game's to count.
- **`ArenaMap`** — the scene seam. Builds a map from a definition, owns it and its `GridRules` for
  the arena's lifetime; everything else asks this component instead of constructing maps of its
  own. Positions go through its transform, so the arena can be moved without the data layer knowing.
- **`HexSpawnPlacement`** — picks evenly spread cells around the rim of the *playable* ground: the
  largest connected piece of the map, so nobody starts in a walled-off pocket. Works outward from
  the map's own bounds rather than assuming a hexagon. `MatchSpawner` calls this instead of using
  scene markers.

## Rendering layer

**`HexMapRenderer`** reacts to the data and owns none of it. One child object per tile, all sharing
a single generated mesh and material, tinted through a `MaterialPropertyBlock` so no per-tile
material instances are created. A tile changing terrain repaints only that tile.

Assign **Tile Prefab** to instantiate a model per tile instead of the generated mesh — the path to
3D tiles with no data-layer change.

**`WallRenderer`** draws a box along every walled ray and owned half-edge: tall where the wall
blocks sight, waist-high where it only blocks feet, so what a wall does is what it looks like. One
object per walled tile, rebuilt on `WallChanged`. Its material is the cutaway one
(`Assets/Shaders/WallCutaway.shader`), which dithers away where a creature is behind it; a plain
lit stone is the fallback.

**`HexMeshFactory.CreateArea`** is the fan for one area of a tile, which is what the hover marker
and the reach overlay draw once a tile is split: the half you can reach, not the tile.

There are **no per-tile colliders**. Picking is a raycast against one ground plane followed by
`HexMap.CellAt`, which finds the tile and then, by the point's bearing off the tile's centre, the
area — cheaper and simpler than hundreds of MeshColliders.

## Tests

`Assets/Tests/EditMode` — run them from **Window → General → Test Runner → EditMode**.

Coverage worth knowing about:

- `FromWorld(ToWorld(h)) == h` across the map, at non-unit tile size and offset origin.
- A dense sweep of the whole bounding box asserting every tile is reachable — this is what catches
  an inverse transform that disagrees with the forward one.
- Walking between two adjacent centres, asserting every sample lands on one of the two (no gaps).
- Every ring member is *exactly* the ring radius away. A wrong starting corner still yields the
  right count, so counting alone would not catch it.
- `Range` matches the centred hexagonal numbers `3r² + 3r + 1`, and equals the union of its rings.
- Neighbour reciprocity: going in a direction then back returns to the start.
- Spawn placement is deterministic, distinct, on the rim, and survives being asked for more spawns
  than the map has tiles.
- Walls (`WallTests`, `ThreatGeometryTests`): what a pattern of rays cuts a tile into and that it is
  never more than four; a walled edge is neither a way in nor a way out; the halves of a split tile
  have no path between them but a way round; a line meets what is in its way from either side; the
  half-edge twin is the mirror across the edge; a point is picked into the area it lies in and a
  sliver is nowhere; nobody spawns in a sealed pocket; a wall a creature cannot see through is not
  one it swings over.

The scratch harness carries the rest, including the Ruins room built from the same records the
editor step writes: the door is the only way in, and sealing it leaves the room unplayable.

## Editor scaffolding

The step that created the terrain, map and material assets and dropped a hex map into the Arena
scene has been deleted: it was spent once it had run and its output is committed. Arena wiring that
is still worth re-running lives in `AuditRewireSetup`, `ArenaVisualsSetup` and `ArenaMapSetup` (the
Ruins map, the wall material, the wall renderer, the cutaway and the reach overlay), all driven by
`ClaudeCode/Set Up Everything`.
