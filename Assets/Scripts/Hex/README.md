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
- **`HexTile`** — coordinates plus terrain. No geometry, no GameObject, and deliberately **no
  neighbour pointers**: neighbours come from `Hex` math plus a map lookup, so topology cannot drift
  out of sync with coordinates.
- **`HexMap`** — tiles keyed by coordinate, plus the layout. Sparse, so shape and size are properties
  of the *data*, not of the class. Raises `TileChanged` so views react to exactly what changed.
- **`TerrainType`** — a ScriptableObject, not an enum, so terrain can be added and retuned without
  recompiling.
- **`HexMapDefinition`** — abstract ScriptableObject with `Build(int seed)`. This is the seam that
  keeps arena shape out of the upper layers: they hold a definition reference and never learn which
  subclass it is. `GeneratedMapDefinition` (hexagon or rectangle) is the first concrete one; a
  hand-authored map would be another subclass and nothing downstream would change.

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

## Systems layer

- **`ArenaMap`** — the scene seam. Builds a map from a definition and owns it for the arena's
  lifetime; everything else asks this component instead of constructing maps of its own. Positions
  go through its transform, so the arena can be moved without the data layer knowing.
- **`HexSpawnPlacement`** — picks evenly spread walkable tiles around the map's rim. Works outward
  from the map's own bounds rather than assuming a hexagon, so a rectangle or hand-authored arena
  gets sensible spawns with no code change. `MatchFlow` calls this instead of using scene markers.

## Rendering layer

**`HexMapRenderer`** reacts to the data and owns none of it. One child object per tile, all sharing
a single generated mesh and material, tinted through a `MaterialPropertyBlock` so no per-tile
material instances are created. A tile changing terrain repaints only that tile.

Assign **Tile Prefab** to instantiate a model per tile instead of the generated mesh — the path to
3D tiles with no data-layer change.

There are **no per-tile colliders**. Picking should be a raycast against one ground plane followed by
`HexLayout.FromWorld`, which is cheaper and simpler than hundreds of MeshColliders.

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

## Editor scaffolding

The step that created the terrain, map and material assets and dropped a hex map into the Arena
scene has been deleted: it was spent once it had run and its output is committed. Arena wiring that
is still worth re-running lives in `AuditRewireSetup` and `ArenaVisualsSetup`, both driven by
`ClaudeCode/Set Up Everything`.
