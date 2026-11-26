# Fluffterror — Authored Map Chunks (CH_*)

Hand-authored, scripted-in-Blender map pieces for the procedural generator to stamp into the
level (LivingMap S2 "set-piece rooms" + basic connectors). **Not wired into the generator yet** —
the level-designer integrates them next (see *Integration notes*).

| | |
|---|---|
| Source scene | `ArtSource/Environment/Chunks/Chunks.blend` (all chunks laid out side by side; regenerate it with the build script — the script is the source of truth) |
| Build script | `ArtSource/Environment/Chunks/build_chunks.py` (+ `chunks_extra.py`: round-2 chunks, corridor variants, `TAGS`, `THEME_HEIGHTS`; `render_chunks.py`: renders + kit sheet) — rebuilds every chunk, the palette texture, the FBX files and `chunks_manifest.json`. Run inside Blender: `exec(open(r"<repo>/ArtSource/Environment/Chunks/build_chunks.py").read())` (or `blender --background --python build_chunks.py`) |
| Manifest | `ArtSource/Environment/Chunks/chunks_manifest.json` (+ copy next to the prefabs) — per chunk: footprint, doors, walkable tile mask, sockets in Unity coordinates, tri counts |
| Unity | FBX: `Assets/Art/Environment/Chunks/Models/CH_<Name>.fbx` · prefabs: `Assets/Art/Environment/Chunks/CH_<Name>.prefab` · material `M_Chunks` (+ `M_ChunksWater`, `M_ChunksShaft`) · texture `T_Chunks_D.png` · runtime component `Assets/_Game/Environment/Chunks/AuthoredChunk.cs` |
| Prefab builder | `Assets/Editor/ChunkPrefabBuilder.cs` — menu **Fluffterror › Chunks › Build Authored Chunk Prefabs** (re-run after re-exporting) |

## 1. Grid standard

| Rule | Value | Why |
|---|---|---|
| Units / axes | metres, **Y-up in Unity**, 1 Blender unit = 1 m = 1 Unity unit | project rule |
| Generator tile | **1 m** | `MultiSectionDungeonGenerator` `_floor[,]` is 1 tile = 1 unit |
| **Cell** (chunk module) | **8 × 8 m = 8 × 8 tiles** | = 4 × 4 kit tiles (kit floor/wall modules are 2 m), holds the generator's 4-tile `CorridorWidth` with a 1-tile wall ring on each side, and two cells make a 16 m hall that still fits inside a 50 × 46 section. Small enough that corridors stay ≤ 3k tris. |
| Chunk footprints | 1×1 cell (8×8 m), 1×2 (8×16 m), 2×2 (16×16 m) | rotate in 90° steps |
| **Pivot** | **centre of the footprint, floor level** (Y = 0) | 90° rotations keep the footprint on the tile grid. Footprint with lower-left tile `(tx, tz)`: position = `(tx + W/2, 0, tz + D/2)` (W, D in metres, always integers) |
| Facing | prefab +Z = **North**, +X = **East** (unrotated) | doors named by side |
| Floor | top of walkable floor at **Y = 0** (except the authored pits / flooded basins / balconies) | matches generator floors |
| Wall ring | the **outer 1-tile ring** of every footprint is wall/rock; walls are **0.5 m thick on the inner half** of the ring (outer half empty) | chunks can sit edge-to-edge with no z-fighting (wall + 1 m gap + wall) |
| Wall height | **parametric** (`Chunk(H=…)`): base pieces 4 m; halls 4.5–7 m; Bell tower 12 m; the 5 base corridors also ship as **`_H36`** (Catacombs 3.6 m) and **`_H50`** (Gothic / Transition / Hell 5 m) variants. Every chunk has a door **collar** (see §8) so any theme height closes cleanly | generator `WallHeightFor`: Gothic 5, Transition corridors 5 (Gothic default), Catacombs 3.6, Hell 6.5, Cavern 7.5, Graveyard 1.4 (no ceiling) |
| Corridors | **4 m** clear (= `CorridorWidth` 4) | 2×2 nav flood fill passes |
| **Door socket** | opening **3 m wide × 3 m high**, centred on each **8 m edge segment**; the 1-tile ring at the socket is walkable floor with 0.5 m stone jambs on both sides + lintel + faceted (chamfered) top corners | any two sockets meet face to face: two chunks form a 2 m deep arched passage |
| Door sockets per edge | one per 8 m segment; named `SOCKET_Door_<Side>` when the edge has one segment, `SOCKET_Door_<Side><i>` (i = 0,1 from West→East / South→North) when it has two | |
| Navigation | steps ≤ 0.25 m (CharacterController stepOffset 0.3), ramps/stairs ≤ 35° (collider = ramp), gaps ≥ 1.2 m, crawl gaps **exactly 1.3 m** high | `slopeLimit` 45, crouch tunnel 1.3 m in TestScene |
| Ceiling | separate object `SM_<Chunk>_Ceiling` (faces down) — enable *Two Sided* shadows like the generator's roofs, or drop it for open-sky biomes | generator §9 ceiling rule |

## 2. Naming & object conventions (inside each FBX)

| Object | Meaning |
|---|---|
| `SM_<Chunk>` | **structure** (walls, trims, big props, the boldest wall stones): LOD0 + LOD1, casts shadows (one-sided), occluder |
| `SM_<Chunk>_Detail` | **detail** (small jutting stones, chamfered flagstones, bones, candles, books, bars…): LOD0 only, no shadows, occludee only |
| `SM_<Chunk>_FloorLOD1` | flat merged floor quads: LOD1 only, no shadows |
| `SM_<Chunk>_Ceiling` | ceiling + ribs, both LODs, **two-sided** shadows (blocks the moon — generator §9) |
| `SM_<Chunk>_Collar` / `SM_<Chunk>_Roof` | inactive by default; `AuthoredChunk.FitToTheme` scales the collar (pivot at the wall top, 1 m tall → scale Y = corridorHeight − H) and enables the roof for open sky |
| `SM_<Chunk>_Shaft` | additive light shaft (`M_ChunksShaft`, Chapel) |
| `SM_<Chunk>_Lid_NN` | sliding sarcophagus lids (Mausoleum), each with its `COL_Lid_NN` child |
| `SM_<Chunk>_Water` | water surface, material `M_ChunksWater` (flooded room only) |
| `SM_<Chunk>_Bell` / `SM_<Chunk>_Gate` | movable parts, pivot = hinge (bell top) / gate bottom centre (lift along +Y to open) |
| `COL_Wall_NN` | box collider, **layer `Walls`** (EnvironmentReverb rays, creature sight blockers) |
| `COL_Block_NN` | box collider for solid props that should also block sight (pillars, rubble, stairs mass) → layer `Walls` |
| `COL_Floor_NN` / `COL_Ramp_NN` | walkable box colliders, layer `Default` (ramps are rotated boxes under stairs) |
| `COL_Rail_NN` | low railings / small solids, layer `Default` |
| `VOL_Water` / `VOL_Trap` | trigger volumes (flooded basin + sewer channel / spike pit) |
| `SOCKET_*` | empties: position + orientation. **Socket +Z (forward) = the facing direction** (Unity), +Y = up |

Collision meshes are axis-aligned boxes in their own local space; the prefab builder replaces each
`COL_*`/`VOL_*` mesh with a `BoxCollider` of the same bounds and deletes the mesh renderer.

### Sockets

| Socket | Placed at | Forward points |
|---|---|---|
| `SOCKET_Door_<S>[i]` | floor level, exactly on the footprint edge, centre of the opening | **outward** (out of the chunk) |
| `SOCKET_Torch_NN` | on the inner wall surface, 2.0 m high | away from the wall, into the room (use `WallMount.PushOffWall` like `BuildTorch`) |
| `SOCKET_Prop_NN` / `SOCKET_Prop_<Hint>_NN` | floor (or shelf) spot for a kit prop; hint = Crate, Barrel, Candle, Limb, Shelf, Bones… | suggested prop front |
| `SOCKET_Evidence_NN` | a spot for an `EvidencePickup` (drop it from ~0.2 m above) | — |
| `SOCKET_Generator` | floor centre of the machine plinth | towards the entrance (machine +Z faces the door, add `GeneratorYawOffset`) |
| `SOCKET_Landmark` | landmark anchor (also where to hang the landmark `ShadowlessLight`) | — |
| `SOCKET_Spawn_NN` | player / creature spawn candidate, floor | suggested look direction |
| `SOCKET_Drip_NN` | just under the ceiling, for `DungeonLayout` drip points | down |
| `SOCKET_Den` | Chucho's den drop point (`CreatureDen`) | towards the entrance |
| `SOCKET_Extraction` | centre of the extraction zone, floor (suggested BoxCollider size in the manifest) | towards the exit gate |
| `SOCKET_Beacon` | above the extraction zone, for `ExtractionBeacon`'s light | — |
| `SOCKET_BellRope` | bottom of the bell rope (future interactable: ring = noise 1.0) | — |

## 3. Chunk set (starter kit)

| Chunk | Cells | Doors | Role |
|---|---|---|---|
| `CH_Corridor_Straight` | 1×1 | N, S | connector, ribbed vault |
| `CH_Corridor_Corner` | 1×1 | N, E | connector |
| `CH_Corridor_T` | 1×1 | N, E, W | connector |
| `CH_Corridor_Cross` | 1×1 | N, E, S, W | connector + floor drain |
| `CH_Corridor_DeadEnd` | 1×1 | S | shrine niche, evidence reward |
| `CH_Room_Storage` | 1×1 | S | small room, built-in shelving |
| `CH_Room_PillarHall` | 2×2 | N1, S0, E0, W1 | medium hall, 6 pillars, landmark spot, 5 m |
| `CH_Room_Crypt` | 1×2 | N, S | burial niches + open sarcophagus (evidence) |
| `CH_Room_Ritual` | 2×2 | N1, S0 | stepped dais, altar, painted circle, 5 m |
| `CH_SP_CollapsedCorridor` | 1×1 | N, S | rubble choke with a 1.3 m crawl gap |
| `CH_SP_FloodedCrypt` | 1×2 | N, S | sunken basin (−0.5 m) under 0.35 m of water |
| `CH_SP_PitBridge` | 1×2 | N, S | 3 m pit, plank bridge, escape stairs, evidence at the bottom |
| `CH_SP_BellTower` | 1×1 | S, W | 12 m tower, stairs to a 3 m balcony, the bell (landmark) |
| `CH_SP_ChuchoDen` | 1×2 | S | Chucho's den: nest, bone pile, free floor for limbs |
| `CH_Generator_Alcove` | 1×1 | S | machine plinth, pipes, fuse box |
| `CH_Extraction_Gate` | 2×2 | S0 | portcullis exit gate (`_Gate` object), rune circle zone |

Tri counts / sockets per chunk: see `chunks_manifest.json` (and the final table below, filled by the build).

## 4. Style

- **PS2 look (2026-11-24, see §9)**: chunky low-poly silhouettes (plinth + cornice courses, a few bold
  jutting stones, chamfered arches, ribbed vaults), flat floors, and the small-scale detail in **low-res
  point-filtered textures + baked vertex-colour AO/grime**. (Round 1–2 were flat-colour PS1-ish; superseded.)
- **One shared atlas** `T_Chunks_D.png` (512 × 512, 8 × 8 cells of 64 px, one tileable pattern per
  palette entry) + tangent-space normal atlas `T_Chunks_N.png` (same cells, from the same height fields via `make_ps2_normal_atlas`; `PS2Lit._BumpMap`, fades out 12-28 m); one floor key (`floor0`) and one wall key (`wall0`) per material, wood frames/wainscot behind `WOOD_FRAMES` (env `CHUNKS_WOOD_FRAMES=0` disables) on **one material** `M_Chunks` (`Fluffterror/PS2Lit`, Point filter, no mips). Palette = ArtDirection §3 (blue-grey stone,
  near-black shadow, amber/ember accents, sick red ritual, ghost green exit, bone white) + wood, iron,
  straw, dirt, brass. The kits are flat-colour URP Lit too, so kit props sit in the chunks naturally.
- No baked lighting; light comes from the torch sockets (≤ 4 per set piece, landmark lights unshadowed).

## 5. Budgets

≤ 3k tris per corridor chunk, ≤ 8k per room / set piece (visual meshes; colliders are boxes). Shared materials only.
PS2 targets (§9.3) are tighter.

## 6. Build results (2026-11-24)

Visual tris (main + ceiling + water/bell/gate). All prefabs: root rotation identity, `M_Chunks` only (water `M_ChunksWater`).

| Chunk | Size | Tris | Box colliders (Walls layer) | Sockets |
|---|---|---|---|---|
| Corridor_Straight | 8×8 | 1 246 | 9 (8) | Door N/S, Torch 1, Prop 2, Drip 1 |
| Corridor_Corner | 8×8 | 1 340 | 12 (10) | Door N/E, Torch 1, Prop 1, Drip 1 |
| Corridor_T | 8×8 | 1 298 | 16 (14) | Door N/E/W, Torch 1, Prop 2, Drip 1 |
| Corridor_Cross | 8×8 | 1 628 | 23 (20) | Door N/E/S/W, Drip 1, Prop 2 |
| Corridor_DeadEnd | 8×8 | 1 354 | 8 (7) | Door S, Evidence 1 (shrine shelf), Candle 2, Torch 1, Prop 1, Drip 1 |
| Room_Storage | 8×8 | 1 728 | 15 (12) | Door S, Torch 1, Prop_Barrel/Crate/Prop, Evidence 2 (one on a shelf), Spawn 1, Drip 1 |
| Room_PillarHall | 16×16 | 4 938 | 42 (36) | Door N1/S0/E0/W1, Torch 4, Landmark, Prop 6, Evidence 2, Spawn 1, Drip 2 |
| Room_Crypt | 8×16 | 3 062 | 22 (19) | Door N/S, Torch 2, Candle 4 (niche shelves), Evidence 2 (one inside the sarcophagus), Spawn 1, Drip 2 |
| Room_Ritual | 16×16 | 5 300 | 27 (21) | Door N1/S0, Torch 2, Candle 8, Landmark (altar), Evidence 2, Prop 3, Spawn 1 |
| SP_CollapsedCorridor | 8×8 | 3 006 | 11 (10) | Door N/S, Torch 1, Prop_Warning, Drip 1 — crawl gap x 0.6..2.0 (1.4 m) × 1.3 m × 3 m |
| SP_FloodedCrypt | 8×16 | 2 260 | 28 (19) + `VOL_Water` trigger | Door N/S, Torch 2, Drip 3, Evidence 2 (one submerged), Prop 1 — basin −0.5 m, water −0.15, ramps 18° |
| SP_PitBridge | 8×16 | 3 388 | 29 (19) | Door N/S, Torch 2, Evidence 1 (pit bottom, −3 m), Prop 2, Drip 1 — bridge 2 m wide, west rail broken, escape stairs 31° |
| SP_BellTower | 8×8 (12 m) | 2 854 | 24 (17) | Door S/W, Torch 2 (one on the balcony), Landmark, BellRope, Evidence 1 (balcony), Prop 2, Spawn 1 — balcony 3 m, stairs 33.7° |
| SP_ChuchoDen | 8×16 | 4 971 | 14 (10) | Door S, Den, Landmark (bone pile), Prop_Limb 4, Evidence 2 (burrow + nest), Spawn 1 (creature), Torch 1, Drip 1 |
| Generator_Alcove | 8×8 | 1 638 | 13 (10) | Door S, Generator (faces the door), Torch 1, Prop_Crate/Barrel, Evidence 1 |
| Extraction_Gate | 16×16 | 5 322 | 27 (20) | Door S0, Extraction, Beacon, ExitGate, Torch 4 (2 wall + 2 braziers), Prop 3, Spawn 1 |

Verified in Unity (throwaway additive scene, closed unsaved): every door socket sits on the footprint
edge at a segment centre, faces outward, Y = 0; a `CH_Corridor_Straight` snapped to **each of the 32
doors** (chunk rotated 0/90/180/270°) gives a clear 0.4 m-radius / 1.8 m capsule path through the
doorway with floor under it, jambs + lintel present (the only "blocked" results are intended features
2+ m inside: a hall pillar facing two doors, the collapsed rubble, the flooded ramp); all 26 torch
sockets have a wall collider ≤ 0.6 m behind them; the crawl gap passes a 1.25 m crouched capsule and
blocks a standing one; stair ramps 31° / 34°, flooded ramps 18°, balcony at 3.00 m.

## 7. Integration notes (level-designer)

1. **Stamping into the grid**: footprint W×D tiles, lower-left tile `(tx, tz)` → prefab position
   `(tx + W/2, 0, tz + D/2)`, yaw in 90° steps. Use `mask_north_up` in `chunks_manifest.json`
   (`#` = rock/wall, `.` = walkable, `o` = walkable footprint but occupied by a solid prop, `D` = door
   ring) — rotate it with the prefab. Mark `.`/`D`/`o` tiles as `_floor = true` *before*
   `PruneFloorIslands` and keep the chunk's own geometry: **skip the generator's floor/wall/ceiling
   build for tiles owned by a chunk** (it would otherwise double the walls — the chunk ring is already
   wall). Register `o` tiles with `RegisterBlocker` so props/evidence don't land on them.
2. **Connecting**: two chunks connect when door sockets coincide with opposite forwards. To attach a
   generator corridor, carve from the tile just outside a door socket (`socket.position + forward*0.5`);
   corridors are 4 tiles wide, the door clear opening is 3 m (jambs hide the step).
3. **Wall height**: chunks are 4 m (Hall/Ritual/Extraction 5 m, Bell tower 12 m). Catacomb sections are
   3.6 m — where a generator corridor meets a door, the lintel covers 3–4 m, so a 3.6 m corridor ceiling
   butts against it without a gap; for Gothic (5 m) corridors add a skirt or use a 5 m chunk.
4. **Ceilings**: `SM_*_Ceiling` is separate and already set to two-sided shadows. Drop it in open-sky
   biomes (Graveyard) if wanted.
5. **Sockets → systems**: `SOCKET_Torch_*` → `TorchPrefab` with `WallMount.PushOffWall` (forward = away
   from the wall; Extraction `Torch_01/02` are free-standing braziers). `SOCKET_Generator` → `PowerGenerator`
   (+`GeneratorYawOffset`). `SOCKET_Extraction` → move the `ExtractionZone` there; its current trigger box
   is 9.5 × 7.07 m, which fits the Extraction_Gate floor (14 × 14 m) — the rune circle is 7.7 m across.
   `SOCKET_Landmark` → landmark anchor/`ShadowlessLight`. `SOCKET_Drip_*` → `DungeonLayout` drip points.
   `SOCKET_Den` → `CreatureDen` (Chucho); `SOCKET_Prop_Limb_*` stay free for offered limbs.
   `SOCKET_Evidence_*` → evidence spawn candidates (drop from +0.2 m). `SOCKET_Spawn_*` → player /
   creature spawn candidates.
6. **Moving parts**: `SM_Extraction_Gate_Gate` (with its `COL_Gate_01` child) lifts along +Y ~4 m to
   open — hook it to `MissionManager.GeneratorsCompleted`. `SM_SP_BellTower_Bell` pivots at the beam
   (swing about local X/Z); `SOCKET_BellRope` is where an interactable rope trigger goes (bell = loud noise,
   the director's real bell).
7. **Flooded crypt**: `VOL_Water` is a trigger (Default layer) covering the basin — hook speed ×0.7 /
   splash steps / item buoyancy (LivingMap R1 "Flooded") to it.
8. **NavMesh**: colliders are plain boxes (stairs/ramps are rotated boxes, under-stair masses are
   blocked), so `NavMeshSurface` with `PhysicsColliders` bakes them. The pit bottom is reachable by the
   escape stairs; the crawl gap is 1.3 m high, so set the creature agent height ≤ 1.2 m or add a
   NavMeshLink if Chucho must not pass.
9. **Perf**: ≤ 5.4k tris per chunk, one material, static-batching flags set on everything except the
   bell, gate and water. No lights are baked into the prefabs — every light comes from the sockets.
10. **Rebuild** (also writes `T_Chunks_N.png`; `MB.pick()` must keep its rng draw or colliders/rocks move; palette keys are APPEND-only): edit `build_chunks.py` → run it in Blender (re-exports FBX + manifest) → Unity
    **Fluffterror › Chunks › Build Authored Chunk Prefabs**. Renders: `render_chunks.py`
    (`render_all()`, `kit_render()`).

---

## 8. Round 2 (2026-11-24): warm palette, theme heights, 16 new chunks, built-in performance

> The round-1 numbers in §6 are superseded by the table in §8.5 (walls were rebuilt with the warm palette and LOD split).

### 8.1 Palette
Walls now use the measured **Modular Dungeon kit** colours (Unity `_BaseColor`): Wall_Dark `7A6B57`, Wall_Medium `77634E`,
Wall_Highlights `90816E` (+ trim `A39280`, shadow `5E5142`). Floors use the kit's cool floor greys `4D4C5A` / `3C3A46`, so
floor and wall stay separate in value and hue; ceilings are a dark warm `463B30`. Torch amber `FFB347` is still the most saturated
warm colour on screen. New cells: rock ×4, crystal, stained glass ×4, meat ×2 (56 of 64 cells used). `Docs/ArtDirection.md` §3/§5 updated.

### 8.2 Theme heights
| Theme (SectionType) | Generator wall height | Chunk handling |
|---|---|---|
| Catacombs | 3.6 m | use `_H36` corridor variants; 4 m chunks: the door lintel (3–4 m) already closes a 3.6 m corridor |
| GothicRuins / **Transition** (all generator corridors) | 5.0 m | `_H50` variants, or any chunk + `FitToTheme(5)` → collar 1 m |
| HellPillars | 6.5 m | `FitToTheme(6.5)` → collar scaled to 6.5 m |
| Cavern | 7.5 m | `FitToTheme(7.5)` → collar to 7.5 m |
| Graveyard | 1.4 m, open sky | `FitToTheme(1.4, openSky: true)` → collar off, roof cap on (the chunk reads as a small building) |

`AuthoredChunk` (root of every prefab): `FootprintTiles`, `WallHeight`, `FitToTheme(corridorHeight, openSky)`, `GetSockets(kind)`.
Verified: the collar top lands exactly on 5.0 / 6.5 / 7.5 m, and is off for 3.6 m and for open sky (roof on).

### 8.3 New chunks
| Chunk | Size | Doors | Identity / landmark | Gameplay reason |
|---|---|---|---|---|
| `CH_Room_Library` | 8×16, 5 m | N, S | wall-to-wall bookcases, giant open book on a lectern, library ladder | two freestanding rows break line of sight; **hidden crawl route** (1.4 × 1.3 m) under a toppled bookcase is a shortcut, with evidence inside it |
| `CH_Room_Larder` | 8×8 | S, E | curtain of hanging hams and sacks, butcher table with a cleaver, cheese, jar shelves | **hiding aisle** behind the meat curtain (2 Hide sockets); a loop room (2 doors) |
| `CH_SP_Ossuary` | 8×16, 4.5 m | N, S | walls made of skulls, skull pyramid, a skeleton-shaped **"VACANCY"** niche (comedy: the hero fits) | landmark; pyramid breaks line of sight; evidence at the niche |
| `CH_Room_Chapel` | 8×16, 6 m | S, E0 | pews, altar, broken stained-glass window with an additive **moonlight shaft**, chandelier | crouch cover behind the pews (Hide ×2, evidence under a pew); `SOCKET_LightShaft` for an optional spot light |
| `CH_Room_PrisonBlock` | 8×16 | N, S | 8 barred cells (4 open with swung doors, 4 locked), tally marks, a prisoner who never left | **hiding spots** in open cells (Hide ×4); bars are see-through (Default layer) but block movement |
| `CH_SP_WellRoom` | 8×8 | S, W, E | well with a 6 m shaft, winch, bucket | 3-door **loop junction**; the well breaks line of sight; evidence in the bucket (within the 2.5 m interaction range from the rim) |
| `CH_Room_Mausoleum` | 8×16, 5 m | S | 3 sarcophagi with **sliding lids** (separate objects), knight effigy on a dais, angel statues | a dead-end reward room: evidence inside each sarcophagus |
| `CH_SP_SewerChannel` | 8×16 | N, S | sunken channel with water, culvert mouth, big outflow **grate** (`SOCKET_Grate`), footbridge | risk/reward evidence on the grate platform across the water, another in the channel (side stairs down); water volume to slow players |
| `CH_SP_CaveTransition` | 8×16, 5 m | N, S | masonry turns into faceted natural rock, a stalagnate column, stalactites, crystals, puddle | breaks up the masonry between Catacombs and Cavern; the column breaks line of sight |
| `CH_Room_SpiralHall` | 16×16, 7 m | S0, S1, W0 | **tight spiral stair** (14 steps × 0.25 m) up to a 3.5 m balcony overlooking the hall, long table, chandelier, banners | verticality and a vantage point; evidence at the far end of the balcony |
| `CH_Corridor_Chewed` | 8×8 | N, S | Chucho's territory: bitten door jambs, claw marks, a chewed door, dug holes, a junk pile with a boot | tells players the den is near (tag `near: CH_SP_ChuchoDen`); evidence in the junk pile |
| `CH_SP_TrapCorridor` | 8×8 | N, S | spike pit (1.2 m deep) with an unlucky skeleton, a warning skull on a stick | safe 1.2 m ledge detour vs a 0.5 m **plank shortcut** with evidence halfway along; `VOL_Trap` + `SOCKET_Trap` for a kill/damage hook |
| `CH_Junction_BrokenFloor` | 8×8 cross | N, E, S, W | collapsed floor revealing a cellar and a broken grate over the deeper dark | 4-way junction; evidence down in the cellar (0.24 m steps back up) |
| `CH_Corridor_Straight_B` | 8×8 | N, S | wall breach with spilled rubble, fallen rib, hanging roots, moss | variety for repeats; evidence tucked behind the rubble |
| `CH_Corridor_Corner_B` | 8×8 | N, E | corner skull shelf with candle, broken barrel, puddle under a drip | variety for repeats |
| `CH_Corridor_T_B` | 8×8 | N, E, W | half-raised portcullis (bottom at 2.5 m), banners, weapon rack | variety; you walk under the gate |
| `CH_Corridor_{Straight,Corner,T,Cross,DeadEnd}_H36` / `_H50` | 8×8 | as base | — | theme-height variants |

### 8.4 Manifest tags (`chunks_manifest.json` → `chunks[].tags`)
`themes` (SectionType names or `Any`), `role` (corridor / room / setpiece / objective), `weight` (relative pick chance),
`min` / `max` per map, `torches_suggested` (how many torch sockets to light; corridors 1, rooms ≤ 3, extraction 2 + braziers),
optional `near` (e.g. the chewed corridor near the den). Source: `TAGS` in `chunks_extra.py`. Required: Chucho's den (1),
Generator alcove (3), Extraction gate (1), Dead end (≥ 1).

### 8.5 Performance (measured on the prefabs)
- **LODs**: one `LODGroup` per chunk. LOD0 = structure + detail; LOD1 = structure + flat floor (the boldest wall stones stay).
  The switch is at screen height 0.30 and the cull at 0.05 (with lodBias 2 and fov 60: an 8 m chunk switches at about 47 m and is culled at about 280 m; a 16 m chunk at about 93 m and 560 m).
  Checked by rendering: at 20 and 40 m the detail mesh draws and the LOD1 floor does not; at 55 and 120 m it is the reverse; at 320 m nothing draws.
- **Static flags**: structure has Batching + Occluder + Occludee; detail, LOD1 floor, ceiling, water and shaft have Batching + Occludee; the bell, gate and lids are not static.
- **Materials**: one shared atlas material `M_Chunks` (URP Lit, SRP Batcher compatible, instancing enabled), plus water or shaft only where they exist.
- **Shadows**: only the structure (one-sided) and the ceiling (two-sided) cast shadows, plus the moving parts. Detail, floors, water and the shaft cast none.
- **Colliders**: boxes only, no MeshColliders. Floors are greedy-merged and walls are one box per wall run. Triggers exist only for water and traps.
- **Lights**: the prefabs contain no lights. Light only `torches_suggested` torch sockets so `DynamicLightBudget` keeps its ≤ 24 visible additional lights.

| Chunk | LOD0 tris | LOD1 tris | Renderers (active) | Materials | Colliders | Shadow casters | Torch sockets |
|---|---|---|---|---|---|---|---|
| Corridor_Straight | 1246 | 580 | 4 | 1 | 9 | 2 | 1 |
| Corridor_Straight_B | 1456 | 832 | 4 | 1 | 10 | 2 | 1 |
| Corridor_Straight_H36 / H50 | 1042 / 1330 | 472 / 640 | 4 | 1 | 9 | 2 | 1 |
| Corridor_Corner | 1340 | 688 | 4 | 1 | 12 | 2 | 1 |
| Corridor_Corner_B | 1348 | 583 | 5 | 2 | 14 | 2 | 1 |
| Corridor_Corner_H36 / H50 | 1196 / 1472 | 628 / 808 | 4 | 1 | 12 | 2 | 1 |
| Corridor_T | 1298 | 734 | 4 | 1 | 16 | 2 | 1 |
| Corridor_T_B | 1618 | 934 | 4 | 1 | 18 | 2 | 1 |
| Corridor_T_H36 / H50 | 1178 / 1406 | 530 / 746 | 4 | 1 | 16 | 2 | 1 |
| Corridor_Cross (+H36 / H50) | 1628 (1604 / 1676) | 902 (902 / 986) | 4 | 1 | 23 | 2 | 0 |
| Corridor_DeadEnd (+H36 / H50) | 1354 (1330 / 1642) | 518 (542 / 806) | 4 | 1 | 8 | 2 | 1 |
| Corridor_Chewed | 1782 | 596 | 4 | 1 | 10 | 2 | 1 |
| Junction_BrokenFloor | 1906 | 1022 | 4 | 1 | 34 | 2 | 1 |
| Room_Storage | 1728 | 998 | 4 | 1 | 15 | 2 | 1 |
| Room_Larder | 1887 | 1080 | 4 | 1 | 28 | 2 | 1 |
| Room_Crypt | 3062 | 1332 | 4 | 1 | 22 | 2 | 2 |
| Room_Library | 4712 | 1792 | 4 | 1 | 29 | 2 | 2 |
| Room_Chapel | 3652 | 2072 | 5 | 2 | 30 | 2 | 2 |
| Room_PrisonBlock | 4438 | 1496 | 4 | 1 | 47 | 2 | 2 |
| Room_Mausoleum | 3284 | 1640 | 7 | 1 | 22 | 5 | 2 |
| Room_PillarHall | 4938 | 1930 | 4 | 1 | 42 | 2 | 4 |
| Room_Ritual | 5300 | 1992 | 4 | 1 | 27 | 2 | 2 |
| Room_SpiralHall | 6232 | 2484 | 4 | 1 | 71 | 2 | 3 |
| SP_CollapsedCorridor | 3006 | 864 | 4 | 1 | 11 | 2 | 1 |
| SP_FloodedCrypt | 2260 | 1254 | 5 | 2 | 28 (1 trigger) | 2 | 2 |
| SP_PitBridge | 3388 | 1734 | 4 | 1 | 29 | 2 | 2 |
| SP_BellTower | 2844 | 1644 | 5 | 1 | 24 | 3 | 2 |
| SP_ChuchoDen | 4971 | 1995 | 4 | 1 | 14 | 2 | 1 |
| SP_Ossuary | 6806 | 322 | 4 | 1 | 20 | 2 | 2 |
| SP_WellRoom | 1393 | 756 | 4 | 1 | 33 | 2 | 2 |
| SP_SewerChannel | 2531 | 1145 | 5 | 2 | 31 (1 trigger) | 2 | 2 |
| SP_CaveTransition | 3478 | 848 | 5 | 2 | 23 | 2 | 1 |
| SP_TrapCorridor | 1568 | 830 | 4 | 1 | 15 (1 trigger) | 2 | 1 |
| Generator_Alcove | 1638 | 1004 | 4 | 1 | 13 | 2 | 1 |
| Extraction_Gate | 5322 | 2126 | 5 | 1 | 27 | 3 | 4 (2 are braziers) |

"Renderers (active)" counts SM_* meshes that are on by default (the collar and roof are off). Most LOD1s are 40–60 % of LOD0.
The Ossuary's LOD1 is only 5 %: its skull walls become flat bone panels. The Cave's is 24 %: its rocks are detail.
The SpiralHall has 71 colliders because each spiral step is two boxes; all of them are cheap static boxes.

### 8.6 Verification (Unity, throwaway additive scenes, closed unsaved)
- **Doors**: all 92 door sockets across the 42 prefabs sit on the footprint edge at a segment centre and face outward. A straight corridor snapped to each door, at all four rotations, gives a clear 0.8 m capsule path through the door, with floor under it and jambs and lintel present.
- **Torch sockets**: all 59 have a wall within 0.6 m behind them (the Extraction braziers are free-standing).
- **Walkability**: a multi-level flood fill per chunk (0.25 m grid, a 0.33 m capsule that steps 0.3 m like the CharacterController, crouching to 1.25 m). Every door, evidence, spawn, hide, den, generator and extraction socket is reachable. The only exception is the well bucket, 1.63 m from the rim, which is inside the 2.5 m interaction range.
- **Layout bugs this test found and fixed**:
  - The flooded crypt's log plus a column walled off the room.
  - The bell tower stair foot was flush against a wall: the balcony is now at 2.65 m and the stair foot is 0.6 m from the wall.
  - The spiral step colliders overlapped each other, and the railings blocked the top landing.
  - The sewer channel was split by its footbridge: a side stair now goes down from the walkway, and the evidence moved.
  - The trap evidence sat inside the kill volume: it is now on the plank.

### 8.7 Integration additions (level-designer)
- After placing a chunk, call `AuthoredChunk.FitToTheme(WallHeightFor(theme of the tiles outside its doors), theme == Graveyard)`.
  Use the `_H36` variants in Catacombs and the `_H50` variants for corridors in Gothic/Transition.
- Pick chunks with `tags` (theme, weight, min/max). The den must exist once, and chewed corridors should go near it.
- `SOCKET_Hide_*` → future hiding logic / creature search points. `SOCKET_Trap` + `VOL_Trap` → damage/kill hook.
  `SOCKET_Grate` → secret or vent route. `SOCKET_LightShaft` → optional unshadowed spot light (counts toward the light budget).
  `SM_*_Lid_NN` → slide them along local X to open.
- Static batching of LOD-grouped objects works in builds. If the generator instantiates chunks at runtime, call `StaticBatchingUtility.Combine` on the chunk roots after generation.

---

## 9. PS2 surface pass (2026-11-24)

Source: `ps2_style.py` (exec'd by `build_chunks.py`). Everything is reproducible from the script; nothing was hand-edited.

### 9.1 What changed
| | Before | After |
|---|---|---|
| Atlas | 64 × 64, 8 px flat-colour cells | **512 × 512, 64 px tileable cells**, one per palette key (same index/order): masonry courses (walls, 2 m period = 32 px/m), dressed ashlar (trim), slab ceiling, irregular flagstones (2 m), planks, riveted plates, rust, bone, straw, weave, rock strata, organic blotches, `skulls` (ossuary wall), `books` (shelf row). Each cell quantised to 6–7 shades of its colour with a 4×4 ordered dither, mean re-centred on the §3 palette value |
| UVs | each face stretched over one colour cell | **world-anchored**: projected on the dominant axis, `u,v = world position mod period`. Faces larger than one period are cut on the period grid (+ every metre below 2 m on the vertical, for vertex lighting); small faces keep a local origin; thin faces (< 0.2 m: bars, trims) stretch along their length |
| Vertex colour | none | `Col` (corner, linear float → FBX colour): 14-ray AO against the chunk itself (1.3 m, strength 0.55) × floor-contact grime (warm-dark, bottom 0.9 m of non-floor faces) × low-frequency mottling |
| Shader | URP Lit | **`Fluffterror/PS2Lit`** (`Assets/Materials/Shaders/PS2Lit.shader`): albedo × vertex colour, Lambert, main + all additional lights (Forward+ cluster loop, Light Layers), shadows, SH ambient, SSAO, fog, optional 32-level colour depth with ordered dither (`_ColorDepth`, 0 = off) |
| Floors | 5-poly chamfered flagstone per tile | one flat quad per tile (seams/chips are in the texture) |
| Walls | all 6 box faces | bottom, back (inside the rock ring) and top (under the ceiling) faces dropped; jutting stones 0.45 → 0.16 chance, bolder (0.09–0.15 m) |
| Ossuary | ~1700 skull quads + 48 modelled skulls | `skulls` panels on both walls and on the pyramid tiers, 4 modelled corner skulls |
| Library | one quad per book | one `books` panel per shelf row |

Blender renders use the same maths (material = texture × `Col`, backface culling on). `render_chunks.py` takes
`CHUNK_RENDER_ENGINE=EEVEE` for lit renders. Partial rebuilds: set `CHUNKS_ONLY = ["CH_…"]` in the exec globals
(the manifest keeps every other entry); `CHUNKS_NO_AUTORUN = True` to just load the functions.

### 9.2 Layout untouched
Colliders, triggers, sockets, moving parts, collars/roofs and `AuthoredChunk` data of all 42 prefabs were diffed
before/after in Unity: 1 400 / 1 400 entries identical. Prefab GUIDs kept (overwritten in place).

### 9.3 Budgets (LOD0 visual tris) and results
Targets: corridor 1×1 ≤ 1.6k · room / set piece 1×1 ≤ 2k · 1×2 ≤ 4k · 2×2 ≤ 5k · bell tower ≤ 3k.
LOD1 is now 80–95 % of LOD0 on most chunks (the structure mesh carries the tessellation); it only removes small detail.
See `chunks_manifest.json` for per-object counts.

### 9.4 Meshy hero props (2026-11-24)
`HERO_PROPS = True` (top of `build_chunks.py`): the procedural feature a Meshy prop supersedes is still generated into a
throw-away `c.scratch_mb()` (same RNG draws, so every other piece is unchanged) and a `HERO_<Prop>[_NN][__<Part>]` empty
marks the spot (+Z = prop front, uniform scale in the empty). `ChunkPrefabBuilder.NestHeroProps` nests
`Assets/Art/Environment/Props/Meshy/PROP_Meshy<Prop>.prefab` there, strips the prop's own collider (the chunk's `COL_*`
boxes already cover it, so colliders/sockets stay identical: 1 400/1 400 entries), adds it to both LODs and re-parents
`__<Part>` anchors under the moving object (`SM_SP_BellTower_Bell`, `SM_Room_Mausoleum_Lid_03`) so they swing/slide with it.
Do not parent empties under meshes in Blender: the FBX `bake_space_transform` then exports them Y/Z-swapped.

| Chunk | Prop | Replaces | Tris |
|---|---|---|---|
| SP_BellTower | Bell (under `SM_*_Bell`, rope kept) | procedural bell + clapper | 1 163 |
| SP_Ossuary | Skull | the 0.45 m top skull | 584 |
| Room_Mausoleum | Effigy (on lid 3), Angel x2 (on the pedestals) | effigy boxes, statue boxes | 1 454, 1 455 |
| Room_Library | Lectern | lectern box + book | 780 |
| SP_ChuchoDen | Nest | straw mound + loose straws | 1 940 |
| Room_Ritual | Altar (on the dais) | altar block + drips | 1 164 |
| Room_Larder | Butcher | table, cleaver, cheese | 1 455 |
| SP_WellRoom | Winch (posts, beam, rope; well + bucket stay procedural) | posts, roller, crank | 873 |
| Room_Chapel / Room_SpiralHall (x1.45) | Chandelier | ring + candles | 880 |
| SP_CaveTransition | Stalagnate | 3-cylinder column | 970 |

Processing (`ArtSource/Props/Meshy/process_meshy.py`, file `MeshyLibrary.blend`): fit to a target box, weld + collapse
decimate (planar dissolve, then fine voxel remesh when collapse stalls), Smart UV, bake Meshy albedo, nearest-colour
palette snap to ArtDirection ramps with a 4x4 dither (128-256 px, Point, no mips), Cycles AO -> vertex colour, PS2Lit.

### 9.5 Meshy prop library (`ArtSource/Props/Meshy/MeshyLibrary.blend`)
One file is both the working file for `process_meshy.py` and a Blender **asset library**: add the folder
`ArtSource/Props/Meshy` under Preferences > File Paths > Asset Libraries. Every `SM_Meshy<Name>` (low-poly only, 26 so far)
is a marked asset in a `Meshy/<Category>` catalogue (Furniture, Containers, Lighting, Funerary, Ritual, Debris, Structural;
`blender_assets.cats.txt`, deterministic UUIDs) with tags `tris:N`, `chunk:CH_...`, `tex:Npx`, a description naming its Unity
prefab, and a preview. Unity counterparts: `Assets/Art/Environment/Props/Meshy/PROP_Meshy<Name>.prefab` (PS2Lit, BoxCollider).
To use one in a chunk: add `c.hero("<Name>", pos, face)` in the chunk definition (see 9.4) and rebuild.
New props: generate (Meshy 6, prompt prefix in `PROPS`/report), add a `PROPS` + `LIB` entry, `process(...)`, `mark_library()`.
Hi-poly originals stay outside the repo in `juego-unity/meshy_sources/*.glb` (`import_archive(name)` re-imports one for a re-bake).

### 9.6 Rocks, rubble and bones heroes + batch-2 placements (2026-11-25)
Batch 3 (Meshy 6, 8 new + DogBowl regenerated): RockBoulderA/B (391/291 tris), RockCluster (387), RubblePile (582), BoneScatter (387, 64 px),
SkullPile (435), Ribcage (339), Remains (474), DogBowl (484). Placed with `c.hero(...)` / `c.hero_rand(props, pos, key, scale)`;
`c.scratch()` (context manager) builds the superseded procedural piece into a throw-away MB with the same `c.rng` draws, and
`c.hero_site(key)` is an independent `random.Random(crc32(name:key))` for yaw / scale / variant, so `c.rng` and every collider,
socket, VOL_* and footprint are untouched (verified: per-chunk `c.rng` state hash identical before/after on all 42 chunks; manifest
differs only in tri counts / notes; collider count per chunk prefab identical).
Replaced: CaveTransition 3 boulders (A,A,B), CollapsedCorridor slab rubble + 2 spills, 8 floor rubble spots, 11 bone scatters,
shrine / alcove skulls (SkullPile), prison + trap skeletons (Remains), Chucho den bone mound (BonePile) + dog bowl (DogBowl).
Placed from batch 2: CoffinAjar (Crypt sarcophagus) + Coffin (W niche bay) + Ribcage, Candelabra x2 + Font (Chapel), Brazier (Ritual),
Cage (hangs, scale 1.2, bottom ~1.9 m) + Shackles (PrisonBlock closed cells), Sacks (Storage), Urns (Larder), Mushrooms (Cave).
`ChunkPrefabBuilder.NestHeroProps`: BoneScatter, RubblePile, RockCluster, Remains, Ribcage cast no shadows.
