# Fluffterror — Authored Map Chunks (CH_*)

Hand-authored, scripted-in-Blender map pieces for the procedural generator to stamp into the
level (LivingMap S2 "set-piece rooms" + basic connectors). **Not wired into the generator yet** —
the level-designer integrates them next (see *Integration notes*).

| | |
|---|---|
| Source scene | `ArtSource/Environment/Chunks/Chunks.blend` (all chunks laid out side by side) |
| Build script | `ArtSource/Environment/Chunks/build_chunks.py` — rebuilds every chunk, the palette texture, the FBX files and `chunks_manifest.json`. Run inside Blender: `exec(open(r"<repo>/ArtSource/Environment/Chunks/build_chunks.py").read())` (or `blender --background --python build_chunks.py`) |
| Manifest | `ArtSource/Environment/Chunks/chunks_manifest.json` (+ copy next to the prefabs) — per chunk: footprint, doors, walkable tile mask, sockets in Unity coordinates, tri counts |
| Unity | FBX: `Assets/Art/Environment/Chunks/Models/CH_<Name>.fbx` · prefabs: `Assets/Art/Environment/Chunks/CH_<Name>.prefab` · material `M_Chunks` (+ `M_ChunksWater`) · texture `T_Chunks_D.png` |
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
| Wall height | **4 m** (ceiling at 4 m); Pillar hall & Ritual room **5 m**; Bell tower **12 m** | between the generator's Catacomb 3.6 m and Gothic 5 m; 2 kit wall modules; clears the 2.39 m skeleton + held lantern |
| Corridors | **4 m** clear (= `CorridorWidth` 4) | 2×2 nav flood fill passes |
| **Door socket** | opening **3 m wide × 3 m high**, centred on each **8 m edge segment**; the 1-tile ring at the socket is walkable floor with 0.5 m stone jambs on both sides + lintel + faceted (chamfered) top corners | any two sockets meet face to face: two chunks form a 2 m deep arched passage |
| Door sockets per edge | one per 8 m segment; named `SOCKET_Door_<Side>` when the edge has one segment, `SOCKET_Door_<Side><i>` (i = 0,1 from West→East / South→North) when it has two | |
| Navigation | steps ≤ 0.25 m (CharacterController stepOffset 0.3), ramps/stairs ≤ 35° (collider = ramp), gaps ≥ 1.2 m, crawl gaps **exactly 1.3 m** high | `slopeLimit` 45, crouch tunnel 1.3 m in TestScene |
| Ceiling | separate object `SM_<Chunk>_Ceiling` (faces down) — enable *Two Sided* shadows like the generator's roofs, or drop it for open-sky biomes | generator §9 ceiling rule |

## 2. Naming & object conventions (inside each FBX)

| Object | Meaning |
|---|---|
| `SM_<Chunk>` | all static visual geometry (walls, floor, dressing) — **one mesh, one material `M_Chunks`** |
| `SM_<Chunk>_Ceiling` | ceiling + ribs (toggleable) |
| `SM_<Chunk>_Water` | water surface, material `M_ChunksWater` (flooded room only) |
| `SM_<Chunk>_Bell` / `SM_<Chunk>_Gate` | movable parts, pivot = hinge (bell top) / gate bottom centre (lift along +Y to open) |
| `COL_Wall_NN` | box collider, **layer `Walls`** (EnvironmentReverb rays, creature sight blockers) |
| `COL_Block_NN` | box collider for solid props that should also block sight (pillars, rubble, stairs mass) → layer `Walls` |
| `COL_Floor_NN` / `COL_Ramp_NN` | walkable box colliders, layer `Default` (ramps are rotated boxes under stairs) |
| `COL_Rail_NN` | low railings / small solids, layer `Default` |
| `VOL_Water` | trigger volume (BoxCollider, isTrigger) of the flooded basin |
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

- Chunky faceted low-poly, flat-shaded, PS1-ish: big stone blocks, plinth + cornice courses, jutting
  stones, chamfered arches, ribbed vaults. Detail is geometry, not texture (pixelation eats texture).
- **One shared palette atlas** `T_Chunks_D.png` (64 × 64, 8 × 8 cells of 8 px, flat colour + faint
  2-tone dither) on **one material** `M_Chunks` (URP Lit, Point filter, no mips). Each face's UVs sit
  inside one colour cell, so Point filtering never bleeds. Palette = ArtDirection §3 (blue-grey stone,
  near-black shadow, amber/ember accents, sick red ritual, ghost green exit, bone white) + wood, iron,
  straw, dirt, brass. The kits are flat-colour URP Lit too, so kit props sit in the chunks naturally.
- No baked lighting; light comes from the torch sockets (≤ 4 per set piece, landmark lights unshadowed).

## 5. Budgets

≤ 3k tris per corridor chunk, ≤ 8k per room / set piece (visual meshes; colliders are boxes). Shared materials only.

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
10. **Rebuild**: edit `build_chunks.py` → run it in Blender (re-exports FBX + manifest) → Unity
    **Fluffterror › Chunks › Build Authored Chunk Prefabs**. Renders: `render_chunks.py`
    (`render_all()`, `kit_render()`).
