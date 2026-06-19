# CONTEXTMAP — Multi-Room Dungeon Generator

**File:** `Assets/_Game/Environment/DungeonGenerator.cs`
**Type:** `MonoBehaviour` (single class, ~1000 lines, no external dependencies beyond `UnityEngine` / `UnityEditor`)
**Role:** Procedurally builds a complete multi-room dungeon — geometry **and** authored prop dressing — entirely from editor context-menu actions, working both in and out of Play mode.

---

## 1. The Big Idea

The generator's design hinges on **one source of truth: a boolean walkable grid (`_floor[,]`).** Everything else is *derived* from it.

```
BSP partition → rooms placed in leaves → rooms + L-corridors carved into _floor[,]
                                              │
                          ┌───────────────────┴───────────────────┐
                          ▼                                        ▼
              floor meshes (greedy run-merge)        wall meshes (edge detection)
                          │                                        │
                          └───────────────────┬───────────────────┘
                                              ▼
                          decoration pass (occupancy grid Cell[,])
```

Because geometry is derived from the grid rather than authored per-piece:
- Corridor↔room openings appear **automatically** (a doorway is just where two carved regions touch — no wall is emitted there).
- No stray walls land mid-corridor.
- The dressing pass can reason about the same grid to avoid blocking the player.

---

## 2. Pipeline (in execution order)

### Entry points (`[ContextMenu]`)
| Menu | Method | Effect |
|---|---|---|
| **Generate** | `Generate()` | Full rebuild under a single `_dungeonRoot` GameObject |
| **Clear** | `Clear()` | Destroys `_dungeonRoot`, resets all state |
| **Assign Ruins Models (Editor Setup)** | `AssignRuinsModels()` | One-shot: fills every prop group from the *Ultimate Modular Ruins Pack* via `AssetDatabase` (editor-only) |

`Generate()` orchestrates the whole pipeline:
1. `Clear()` — wipe previous output.
2. Seed RNG (`Seed != 0 ? Seed : TickCount`) into `System.Random _rng` — **all** randomness flows through this, so output is deterministic per seed.
3. Create `_dungeonRoot` and a `Decor` child (`_propsRoot`).
4. Allocate `_floor = new bool[MapWidth, MapHeight]`.
5. **BSP**: `Split(root, 0)` → `CollectLeaves` → `CreateRoom` per leaf → `CarveRoom` + capture `RoomInfo`.
6. `CarveCorridors(root)` — connect sibling subtrees with L-shaped corridors.
7. `BuildFloorMeshes()` + `BuildWallMeshes()`.
8. `DecorateDungeon()` (gated by `_decorate`).

### Stage A — BSP partition (`Split`)
- Recursively halves the map down to `MaxDepth` or until a node is too small (`< MinRoomSize * 2`).
- Split direction is **aspect-ratio biased**: wide nodes split vertically, tall nodes horizontally, near-square nodes random.

### Stage B — room placement (`CreateRoom`)
- Each leaf gets a randomly sized room (`MinRoomSize`…`MaxRoomSize`, clamped to leaf bounds minus a 1-tile margin) at a random offset inside the leaf. Stored as `leaf.Room` + `HasRoom`.

### Stage C — grid carving
- `CarveRoom` flips all room tiles to floor.
- `CarveCorridors` walks the BSP tree bottom-up; for each internal node it connects the **representative room centers** of its left/right subtrees (`TryGetRoomCenter`) with an **L-shaped path** (random order of horizontal-then-vertical vs. vertical-then-horizontal).
- `CarveBand` carves `CorridorWidth` tiles perpendicular to the run, so corridors have real width.
- All writes go through `SetFloor` (bounds-checked).

### Stage D — geometry from grid
- **Floors** (`BuildFloorMeshes`): greedy horizontal run-merge — one quad per contiguous run of floor tiles in a row. Fewer meshes than per-tile.
- **Walls** (`BuildWallMeshes`): a wall edge exists wherever a floor tile borders a non-floor neighbour. The four directions are scanned separately (`BuildEdgeRunsAlongX` for N/S, `BuildEdgeRunsAlongZ` for E/W), **merging collinear edges into single boxes**. `WallThickness` is added to length so corners close cleanly.
- Each piece is a fresh GameObject with `MeshFilter` + `MeshRenderer` + `MeshCollider`, built via `BuildQuadMesh` / `BuildBoxMesh`. Materials resolve to serialized `FloorMaterial`/`WallMaterial` or a cached runtime URP-Lit default.

### Stage E — decoration (`DecorateDungeon`)
Only runs if `_decorate` and there are rooms. Sub-steps:

1. **`ClassifyTiles`** — builds a parallel `Cell[,] _cell` occupancy grid (`Wall / Corridor / RoomFree / Reserved / Filled`) and `int[,] _roomIndex` ownership. Also detects **doorways** (a corridor tile orthogonally adjacent to a room tile) into `_doorways`, each recording the room-side tile, direction, and owning room. **This grid never touches `_floor` — walkability is preserved.**
2. **`AssignRoomTypes`** — largest room → `Extraction`; the rest get a size-biased type via `PickRoomType` (big → Hall/Library/Ritual, medium → Bedroom/Library/Storage, small → Storage/Empty).
3. **`ReserveSkeleton`** — the safety net. Marks a 3×3 clearance at each room center plus a straight `ReserveRoute` from every doorway to that center as `Reserved`. Reserved tiles stay walkable but are **off-limits to floor props**, guaranteeing the player can always traverse.
4. **`PlaceArches`** (if `_placeArches`) — one arch per doorway threshold, spacing-deduplicated via `_archSpacing`, pushed off the wall by `_archForwardOffset`.
5. **`DecorateRoom`** per room:
   - Gathers candidate tiles: `RoomFree`, wall-adjacent, corner.
   - Shuffles them, then builds a **wall-biased** furniture order (wall-adjacent tiles first).
   - Places, in order: (1) type-specific **feature furniture** (`FeatureGroupFor`), (2) **wall clutter** (skipped in Extraction), (3) **wall lights** (budget-capped), (4) **corner cobwebs** (raised by `_cobwebHeight`), (5) a rare **horror prop** at `_rareHorrorChancePerRoom`.

---

## 3. Placement primitives

| Method | Lands on | Collider? | Notes |
|---|---|---|---|
| `PlaceFurniture` | `RoomFree` tiles only | optional | Faces inward if wall-adjacent (`TryWallInward`), else random yaw. Marks tile `Filled`. |
| `PlaceWallLights` | wall-adjacent tiles | no | Inset toward wall by `_wallFixtureInset`; runs `ConfigureLights`. |
| `PlaceWallFixtures` | given tiles (corners) | no | Adds `extraHeight` (cobwebs go high). |
| `SpawnPropAt` | — | optional | Core spawn: instantiate, position, apply `RotationOffset`, random scale within `ScaleRange`, optional bounds collider. |

- **`ConfigureLights`** enforces the PS2-cheap light budget: caps enabled real-time lights at `_maxRealtimeLights`, soft-shadow casters at `_maxShadowLights` (the rest get `LightShadows.None`); torch prefabs without an embedded `Light` get a cheap point light when `_attachTorchLight`.
- **`AddBoundsCollider`** adds a single `BoxCollider` sized to the combined local-space mesh bounds — cheap collision for solid furniture instead of per-mesh colliders.
- **`InstantiatePrefab`** uses `PrefabUtility.InstantiatePrefab` in-editor (keeps prefab/model link for hand-tweaking) and plain `Instantiate` at runtime.

---

## 4. Data types (nested)

| Type | Purpose |
|---|---|
| `RoomType` (enum) | Empty, Storage, Bedroom, Library, Ritual, Extraction, Hall |
| `PropEntry` | One weighted prefab + `RotationOffset`, `ScaleRange`, `YOffset` |
| `PropGroup` | Array of `PropEntry`; `IsValid` + weighted `Pick(rng)` |
| `RectInt2D` (struct) | Lightweight integer rect with center/contains helpers |
| `RoomInfo` (class) | Rect + center + area + assigned `RoomType` |
| `Doorway` (struct) | Tile, direction (room→corridor), owning room index |
| `Cell` (enum, byte) | Decoration occupancy: Wall, Corridor, RoomFree, Reserved, Filled |
| `BSPNode` (class) | Binary partition node (bounds, children, room) |

---

## 5. Key invariants & gotchas

- **`_floor` is sacred.** The decoration pass reads it but only ever writes to the separate `_cell` grid. Nothing dressing-related can make a tile non-walkable.
- **`Reserved` skeleton is the anti-softlock guarantee.** If you add new placement logic, respect `Cell.RoomFree` — never spawn floor props on `Reserved`/`Corridor`/`Filled`.
- **Determinism**: every random decision uses `_rng`. Adding `UnityEngine.Random` calls would break seed reproducibility.
- **No `Resources.Load`, no hardcoded asset paths** in the runtime path — all props come from serialized `PropGroup`s assigned in the Inspector (or the one-shot editor `AssignRuinsModels`).
- **Editor/Play parity**: `DestroyImmediateSafe` and `InstantiatePrefab` branch on `Application.isPlaying` so the same code works in both modes.
- **All output is parented under `_dungeonRoot`** (props under its `Decor` child), so `Clear` is a single destroy.

---

## 6. Inspector surface (tunables)

- **Generation**: `Seed`, `MapWidth/Height` (80), `MinRoomSize` (8), `MaxRoomSize` (20), `MaxDepth` (5), `CorridorWidth` (3), `WallHeight` (3), `WallThickness` (0.5), optional `FloorMaterial`/`WallMaterial`.
- **Decoration**: `_decorate` toggle + 10 `PropGroup`s (`_archways`, `_storageFurniture`, `_libraryFurniture`, `_bedroomFurniture`, `_hallFurniture`, `_ritualProps`, `_wallClutter`, `_cornerCobwebs`, `_wallLights`, `_rareHorror`).
- **Density**: `_featureDensity`, `_clutterDensity`, `_cobwebDensity`, `_lightDensity`, `_maxLightsPerRoom`, `_rareHorrorChancePerRoom`, `_placeArches`, `_generatePropColliders`.
- **Light budget**: `_maxRealtimeLights` (24), `_maxShadowLights` (4), `_attachTorchLight`, torch color/intensity/range/height.
- **Placement tuning**: `_wallFixtureInset`, `_cobwebHeight`, `_archSpacing`, `_archForwardOffset`.
- **Debug**: `_debugDrawRooms` → `OnDrawGizmosSelected` draws color-coded room-type wireframes.

---

## 7. Relationship to `ChunkBuilder`

`ChunkBuilder.cs` (in `Assets/Editor/`) is a **separate, complementary** tool: it assembles reusable hand-tweakable *chunk prefabs* from a modular kit. `DungeonGenerator` is the **grid-based procedural** path that builds geometry from scratch. The CLAUDE.md notes a future generator could place `ChunkBuilder` chunks on a grid — i.e. these two are currently parallel approaches, not yet integrated.
