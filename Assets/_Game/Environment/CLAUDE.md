# CONTEXTMAP — Multi-Section Dungeon Generator

**Files:** `Assets/_Game/Environment/MultiSectionDungeonGenerator.cs` + partial `MultiSectionDungeonGenerator.LivingMap.cs` (Living Map Milestone 1, §12)
**Type:** `MonoBehaviour` (partial class, no `Resources.Load`)
**Role:** Procedurally builds a **closed, fully-connected multi-biome level** — geometry, themed dressing, lighting, landmarks, *and* repairable generator machinery — entirely from editor context-menu actions, in or out of Play mode. Geometry is primitive; dressing uses the **kit models via Inspector-assigned `PropPalette` assets** (primitives are the fallback when a palette is empty).

> ⚠️ **This is the live generator.** It is the component on the `DungeonGenerator` GameObject in `Assets/Scenes/SampleScene.unity`. The sibling files `DungeonGenerator.cs` and `DressedDungeonGenerator.cs` are **older, parallel generators NOT in the scene** — do not edit them expecting in-game changes. (The root `CLAUDE.md` still documents `DungeonGenerator.cs`; treat it as stale for level-gen work.)

---

## 1. The Big Idea

One source of truth: a global **`bool[,] _floor`** grid (1 tile = 1 world unit). A grid of themed *sections* each carve into it, adjacency corridors stitch them together, an island-prune guarantees one connected region, then **all geometry is derived from the grid** (floors by run-merge, walls by edge detection). Parallel grids carry metadata without ever touching walkability:

| Grid | Meaning |
|---|---|
| `_floor[,]` | **Sacred.** Walkable or not. Source of truth for all geometry. |
| `_section[,]` | `SectionType` owning each floor tile (`Transition` if outside every section). Drives wall height, ceiling, materials, dressing. |
| `_reserved[,]` | Tiles kept clear of obstacles (corridors, generator clearance) so navigation never blocks. |
| `_hasSection[,]` | Whether a tile was claimed during ownership assignment. |

Determinism: **all** randomness flows through one `System.Random _rng` seeded from `Seed` (0 = `Environment.TickCount`). Adding `UnityEngine.Random` calls would break seed reproducibility.

---

## 2. Pipeline — `Generate()` (execution order)

| Step | Method | Effect |
|---|---|---|
| 1 | `Clear()` | Destroy `_multiSectionRoot`, reset all state/material caches/lists. |
| 2 | seed `_rng`, clear `_sections` | Deterministic RNG. |
| 3 | `LayoutSections()` | Place themed sections on a `GridColumns × GridRows` grid; sets `_width/_height`. |
| 4 | allocate `_floor/_reserved/_section/_hasSection` | |
| 5 | `CreateRoots()` + `EnsureMaterials()` | Build the `_multiSectionRoot` child hierarchy + runtime fallback materials. |
| 6 | per-section **carve** | `CarveGothic` (Gothic + Catacombs), `CarveOpenArea` (Graveyard, Hell, Cavern). |
| 7 | `ConnectSectionsTree()` (or `ConnectSections()` if `UseSpanningTree` off) | Random spanning tree of grid neighbours + `ExtraLoopChance` loops (min `MinExtraLoops`, max `MaxExtraLoops`) → main routes, far wings, a few circuits (§12). |
| 8 | **`CarveGeneratorRooms()`** + `CarveDeadEndRooms()` | Machine chambers (§7) + 2–3 single-entrance side rooms (evidence prefers them). |
| 9 | `PruneFloorIslands()` + `ValidateLayoutConnectivity()` | Keep the largest region; then check every section / side room survived and is reachable (repairs with a corridor + re-prune, logs the link/loop count). |
| 10 | `AssignSectionOwnership()` | Fill `_section[,]` from section bounds; non-section floor = `Transition`. |
| 11 | **`SpawnGenerators()`** | Instantiate one `PowerGenerator` per carved room (see §7). |
| 11b | **`PlaceMissionObjectsOnFloor()`** | Move the scene's extraction zone, player and evidence onto the new floor (see §7). |
| 12 | `BuildFloors / BuildCeilings / BuildWalls / BuildCeilingSkirts` | Geometry from the grid. |
| 12b | `PlaceSectionLandmarks()` | One landmark per section (§12). |
| 13 | `DecorateGothic/Graveyard/Hell/Catacombs/Cavern` + `DressWithKit()` | Themed dressing (kit swaps inside the builders) + per-theme kit palettes. |
| 13b | `CollectDripPoints()`, `ValidateNavigationAndFix()`, `WriteLayout()` | Drip emitters, the 2×2 flood-fill navigation fix, the `DungeonLayout` summary. |
| 14 | fog + ambient/moon | Skipped if a `DungeonLightingController` is in the scene (it owns RenderSettings). |
| 15 | *(editor)* occlusion / lightmap bake | Gated by `ComputeOcclusionOnGenerate` / `BakeLightingOnGenerate`. |

Output hierarchy: `transform/_multiSectionRoot/{Floors, Walls, Ceilings, Details, Decor, Lights, Generators, Landmarks, Kit}` + a `DungeonLayout` component on `_multiSectionRoot` — `Clear` is a single destroy.

---

## 3. Sections (themes) & layout

- `enum SectionType { GothicRuins, Graveyard, HellPillars, Catacombs, Cavern, Transition }`. `Transition` is reserved for corridors / anything outside a section.
- `LayoutSections` fills a fixed-pitch grid; `BuildThemeBag` distributes themes evenly then shuffles (`RandomizeLayout`). `MakeSection` jitters each section's size (`SizeJitter`, floored at `MinSectionDimension`) and offset **inside its slot** so neighbours never touch — the gaps are where corridors and generator rooms live.
- `SectionInfo` (nested class): `Type`, `Bounds (RectInt)`, `Col/Row`, `Entry/Exit`, `Center`.

---

## 4. Carving

- **`CarveOpenArea`** (Graveyard/Hell/Cavern): organic footprint — erode the boundary with edge-weighted noise, then 3 cellular-automata smoothing passes; the interior core is never eroded so the section stays solid.
- **`CarveGothic`** (Gothic/Catacombs): `SplitBSP` (aspect-ratio-biased, depth `GothicMaxDepth`) → `CollectLeaves` → a clamped room per leaf → chain rooms + entry/exit with `CarveCorridorAStar` confined to the section bounds (maze-like spine).
- **Corridor A\*** (`FindCorridorPath`): deterministic A* over a bounding rect. Cost model: fresh rock `CorridorStepFresh(10)`, reused floor `CorridorReuseCost` (merge bias), `+CorridorTurnPenalty` per direction change (straightness). Uses a nested `MinHeap`. `CarveCorridorTile` carves a `CorridorWidth` block per path tile. Falls back to an L-shape if search fails.
- **`ConnectSections`**: links every section to its right & down grid neighbour via `CarveCorridorL` (`reserve: true`) → many loops, not a single line.
- **`PruneFloorIslands`**: flood-fill components; keep the biggest, revert the rest to wall (clears their `_reserved` too). The anti-disconnection safety net — **anything carved must connect to the main region before this runs** (that's why generator rooms carve at step 8).
- **`AssignSectionOwnership`**: for each floor tile, `_section = ` the containing section's type, else `Transition`.

---

## 5. Geometry from the grid

- **Floors** (`BuildFloors` → `BuildHorizontalMesh`, faceUp, y=0): one merged mesh per `SectionType` (incl. `Transition`), greedy run-merge of tiles, with `MeshCollider`.
- **Ceilings** (`BuildCeilings` → `TryCeiling`): one mesh per type at `WallHeightFor(type)`, gated by per-theme toggles (`CeilingEnabledFor`). **Graveyard has no ceiling** (open sky).
- **Walls** (`BuildWalls`): a wall exists wherever a floor tile borders non-floor (`IsWallEdge`). Scans N/S (`BuildWallRunX`) and E/W (`BuildWallRunZ`), merging collinear runs of the same section type; `CapBefore/CapAfter` add half-`WallThickness` only at true corners (butt-joins at section boundaries to avoid z-fighting). Height/material per owning tile via `WallHeightFor` / `WallMatFor`. Walls get the `Walls` layer (`ApplyWallLayer`) so `EnvironmentReverb` raycasts hit them.
- **Ceiling skirts** (`BuildCeilingSkirts` → `TrySkirt`): vertical fillers where two roofed tiles have different ceiling heights; emitted once from the taller tile, skipped beside open-sky tiles.

---

## 6. Decoration & lighting

- Each theme has its own pass over `_sections` of that type. Two placement styles:
  - **Wall-edge props** (`CollectWallEdges` → `PlaceAlongEdges`): collects inner wall faces (`WallEdge`: surface point, inward normal, owning tile+side), shuffles, places by density with min-spacing dedup and a `used` edge-key set. Gothic = torches/windows/cell-bars/panels; Catacombs = sconces/bone-piles/skull-niches.
  - **Floor props** (per-section random tiles, **skipping `_reserved`**): Graveyard = gravestones/crosses/tombs/dead-trees; Hell = pillar grid; Cavern = rocks/stalagmites/stalactites/crystals.
- **Lights** (`SpawnLight`): point lights parented under `Lights`, **budget-capped at `MaxRealtimeLights`** (returns null past the cap); only the first `HeroShadowLightCount` `heroEligible` lights get soft shadows, the rest `LightShadows.None`; `flicker` adds a `LightFlicker`. `ApplyFog` (ExpSq) and `ApplyAmbientAndSky` (flat ambient + a directional "moon") drive RenderSettings unless a `DungeonLightingController` owns them.
- **One directional light only**: `ApplyAmbientAndSky` now reuses an existing scene directional instead of adding a `Moonlight` (a second directional is unshadowed in URP and lights every roofed floor). Prefab torches bring one range-10 light; shadows/culling are handled at runtime by `Rendering/DynamicLightBudget` (nearest 4 shadowed), so `HeroShadowLightCount` only matters without it.
- *(Editor)* `MarkStatic` flags geometry static for occlusion/GI; `ComputeOcclusion` / `BakeLighting` are opt-in and slow on the large map.

---

## 7. Generator machinery (the gameplay objective)

Three repairable machines, **each in its own dedicated room**, carved as an extension of generation.

### Carving the rooms — `CarveGeneratorRooms()` (pipeline step 8)
Runs **after `ConnectSections`** (a section exists to attach to) and **before `PruneFloorIslands`** (the connecting corridor saves the room from the cull). For each of `GeneratorCount`:
1. **`TryFindEmptyRoom`** — find a rectangle whose footprint **plus a `GeneratorRoomGap` ring** is entirely non-floor, so the chamber sits apart from every theme. Uses a **floor prefix-sum** (`BuildFloorPrefixSum` + `RectFloorCount`) for O(1) emptiness queries; `CollectEmptySpots` gathers candidates, relaxing gap then size if the map is dense. `TooCloseToExistingRoom` enforces `GeneratorRoomSpacing`.
2. **`CarveRoomAndConnect`** — `SetFloor` the rect, then `CarveCorridorAStar` from its centre to `NearestSectionCenter` (bounds limited via `EncapsulateBounds`). Returns the **yaw** that makes the machine's +Z face that entrance, snapped to a cardinal.
3. Store `GeneratorRoom { Rect, Yaw }`.

Because the room tiles fall outside every section, ownership leaves them `Transition` → they auto-get walls + a ceiling and **no theme décor pass touches them** → clean machinery chambers.

### Placing the machines — `SpawnGenerators()` (pipeline step 11)
One per room: reserve a clearance box (`ReserveArea`, `GeneratorClearance`) and `PlaceGenerator(tile, yaw)`. Uses `GeneratorPrefab` if assigned (via `InstantiatePrefab` — `PrefabUtility.InstantiatePrefab` in-editor, `Instantiate` at runtime), else `BuildPrimitiveGenerator` (box body using `GeneratorMaterial` or default metal, an emissive `Indicator` child, a `StatusLight` point light, a solid root `BoxCollider`, and the `PowerGenerator` component). Final rotation = `yaw + GeneratorYawOffset`.

### Mission objects placement — `PlaceMissionObjectsOnFloor()` (pipeline step 11b)

The extraction zone, the player and the evidence are **hand-configured scene objects** (beacon, glow, …), so Generate **moves** them instead of spawning them — regenerating with any `Seed` never leaves them floating over the void. Gated by `PlaceMissionObjects`; refs `Extraction` / `PlayerStart` / `Evidence[]` auto-find if empty (evidence: every `EvidencePickup` not under the generator, sorted by name).
- Uses its **own RNG** (`seed ^ 0x5EED`) so the floor layout for a given `Seed` is identical with or without this pass (décor shifts slightly because the chosen tiles are reserved).
- **Extraction**: centre of a clear all-floor square big enough for its `BoxCollider` (`ExtractionClearance`), preferring `ExtractionPreferredSection` (Graveyard = open sky, beacon visible), away from generator rooms. The **trigger box centre** (not the pivot) lands on the tile. Whole footprint reserved → no graves inside the zone.
- **Extraction (Living Map R4, `VariedPlacement` on)**: `ExtractionCandidates` (3) sections are drawn (Graveyard weighted ×`PreferredSectionWeight`), one candidate spot is picked — the exit can be in any theme. Off → the old "prefer Graveyard" rule.
- **Player start**: with `VariedPlacement`, a clear themed-section tile whose BFS walk from the extraction is `StartDistanceBand` (0.35–0.6) × the farthest walkable tile, not in a generator/dead-end room; else the old `PlayerStartDistance` (8–18) rule.
- **Evidence**: up to `EvidenceInDeadEnds` (1) items first go into dead-end rooms, then themed-section tiles off reserved corridors, `EvidenceMinDistance`–`EvidenceMaxDistance` walk tiles from the start, ≥ `EvidenceSpacing` apart, ≥ `EvidenceGeneratorGap` from generator rooms, one per section type first. Constraints relax stepwise if the map can't fit them (logs a warning if items are left unplaced).
- `MoveObject`: `Undo.RecordObject` in the editor; at runtime toggles the `CharacterController` and zeroes rigidbody velocity, so the pass also works if Generate is ever called in Play mode.

### The machine — `PowerGenerator.cs` (`Assets/_Game/Environment/`)
Self-contained `MonoBehaviour, IHoldInteractable`. States **Broken → Repaired → Running**: hold E to fill a repair charge (`_repairTime`, drains at `_chargeDrain` when released), then press E to start (`_runDuration`). Recolours its `Indicator` renderer (instanced emissive material) + `StatusLight` per state (red→amber→green pulse). Auto-finds a child renderer named `Indicator` and any child `Light` if the fields are empty. Static hooks for a future win condition: `All`, `RepairedCount`, `AnyStateChanged`. See memory `[[generator-machinery]]`.

### Hold interaction plumbing
`Assets/_Game/Core/IHoldInteractable.cs` (extends `IInteractable`). `InteractionSystem` now also subscribes to `InteractCanceledEvent`; on press it calls `UseHold(interactor)` — true → `OnHoldStart/OnHoldTick/OnHoldStop` while aiming + holding E, false → normal `Interact()`. Reuse for any future hold-to-charge object.

### Animation (the spinning handle) — `engineAnimatorController`
**Speed-driven, single state — no snap-back.** The controller is **generated by code** (menu **Fluffterror > Animation > Build Engine Animator**, `Assets/Editor/EngineAnimatorBuilder.cs`): one looping **Spin** state whose playback speed is multiplied by a float param **`SpinSpeed`** (`EngineAnimParams.SpinSpeed`, the shared source of truth at the bottom of `PowerGenerator.cs`). Writes `engineAnimatorController.controller` + one clip `EngineClips/EngineSpin.anim`. `PowerGenerator` auto-finds the `Animator` and each `Update` eases `SpinSpeed` (`Mathf.MoveTowards`, rate `_spinAccel`) toward a target: `_crankSpinSpeed` while repairing, `_runSpinSpeed` while Running, **0** otherwise. At 0 the clip just holds its current frame, so the handle **coasts to a stop in place** instead of blending back to a start pose (the old Idle/Crank/Run states did snap back). The clip rotates a child named **`Handle`** (`EngineAnimParams.HandlePath`) around `EngineAnimParams.SpinProperty` (default `localEulerAnglesRaw.x`); all three euler channels are authored, so **`Handle` must be an identity-rotation pivot on the axle** (offset the mesh as its child). Re-aim by changing `SpinProperty` (x/y/z) and rebuilding. Prefab wiring: name the spinning child `Handle`, add an `Animator` to the **root**, assign the controller.

### Audio (repair/run)
`PowerGenerator` adds its own looping `AudioSource` in `Awake` (R.E.P.O.-style dedicated source, like `CharacterAudio`). `UpdateLoopAudio` plays `_repairLoop` while repairing, `_runLoop` while Running, silent otherwise (only restarts on def change). One-shots via `SoundManager.Instance.Play`: `_repairComplete` on `CompleteRepair`, `_engineStart` on `StartRunning`, plus `_skillCheckGood`/`_skillCheckGreat`/`_skillCheckMiss` on skill-check results. All `SoundDefinition` slots optional/null-safe.

### Skill checks (Dead by Daylight–style) — `SkillCheckController`
While hold-repairing, `PowerGenerator.DriveSkillChecks` (called from `OnHoldTick`) feeds the repair bar and randomly pops a skill check (`_skillCheckChancePerSecond` × dt, gated by `_skillCheckMinGap`; also the grace delay before the first one). The minigame lives in **`Assets/_Game/UI/SkillCheckController.cs`** (singleton `Instance`, like a HUD): a circular IMGUI gauge with a needle sweeping clockwise from the top and a random success arc (white) holding a thin **red "great"** slice at its leading edge. Hit key = the **Jump binding (Space)**; `CharacterMovement.OnJump` early-returns while `SkillCheckController.Active` so the player doesn't jump. Results via callback to `PowerGenerator.OnSkillCheckResult`: **Great** → `+_skillCheckGreatBonus` charge, **Good** → nothing, **Miss** (pressed outside / needle swept past) → `−_skillCheckMissPenalty` charge (the regression bite). Releasing E (or looking away) → `OnHoldStop` → `SkillCheckController.Cancel()` (no penalty); the existing `_chargeDrain` then handles "progress saved but slowly going back". Setup: add `SkillCheckController` to a HUD/manager object and assign the player's `InputReader`. To make red the *only* pass zone, set `_greatArc` ≈ `_successArc`. The gauge's `UnityEngine.Random` use is fine — it's gameplay, not the seeded dungeon RNG.

### Look-at border + on-screen prompt
- `Assets/_Game/Core/IHighlightable.cs` + `Assets/_Game/Rendering/InteractableOutline.cs` (+ `InteractableOutline.shader`, `Hidden/InteractableOutline`): an **inverted-hull outline** that `InteractionSystem` toggles via `GetComponentInChildren<IHighlightable>()` as the reticle target changes. Clones the **MeshFilters under wherever the component sits** (so putting it on the `Handle` outlines only the handle; on the root outlines the whole machine) with a Cull-Front clip-space-extruded hull (constant screen-px border regardless of the model's ~9× scale); just enabled/disabled per highlight. Auto-added to the primitive generator fallback root.
- `Assets/_Game/UI/InteractionPromptUI.cs`: IMGUI prompt centred low on screen, rendered with a gothic `Font` (serialized; falls back to default). Reads `InteractionSystem.CurrentPrompt`, so it shows "Hold E to repair" / "Repairing… 45%" / "Press E to start" / "Running" for free.

---

## 8. Data types (nested)

| Type | Purpose |
|---|---|
| `SectionType` (enum) | Theme of a tile/section (+ `Transition`). |
| `SectionInfo` (class) | A placed section: bounds, grid cell, entry/exit, center. |
| `WallEdge` (struct) | An inner wall face for edge-decoration: surface, inward normal, owning tile/side, section. |
| `GeneratorRoom` (struct) | A carved machine chamber: `Rect` + facing `Yaw`. |
| `BSPNode` (class) | Gothic binary-partition node. |
| `MinHeap` (class) | Deterministic binary heap for corridor A*. |

---

## 9. Key invariants & gotchas

- **`_floor` is sacred** — décor/metadata only ever writes `_section` / `_reserved`, never makes a tile non-walkable.
- **Carve-before-prune** — any new floor (rooms, corridors) must connect to the main region *before* `PruneFloorIslands`, or it gets culled.
- **Determinism** — everything random goes through `_rng`. No `UnityEngine.Random`.
- **No `Resources.Load` / hardcoded paths in the runtime path** — kit models only arrive through Inspector-assigned `PropPalette`s (built by `Assets/Editor/LivingMapAssetBuilder.cs`); primitives + runtime URP/Lit materials are the fallback.
- **Navigation is checked, not assumed** — every solid prop registers its tile footprint (`RegisterBlocker` / `TryPlaceFloorKit`), and `ValidateNavigationAndFix` floods a 2×2-tile body (≥ 2 m gaps; the CharacterController is 0.8 m wide) from the start and deletes the props that cut off ≥ 10 free tiles or an objective. Landmarks are never removed (their placement already passed the same flood fill). New solid props must call `RegisterBlocker`.
- **Interaction collider must be on the same GameObject as `PowerGenerator`** — `InteractionSystem` does `hit.collider.GetComponent<IInteractable>()`. Keep child meshes collider-free; the solid `BoxCollider` lives on the root.
- **Editor/Play parity** — `DestroyImmediateSafe` and `InstantiatePrefab` branch on `Application.isPlaying`. `PowerGenerator` visuals only animate in Play (its `Awake` doesn't run in edit mode).
- **Light budget** — past `MaxRealtimeLights`, `SpawnLight` returns null; raise it for the large grid.
- **Roof must occlude overhead light (two-sided ceiling shadows)** — ceiling quads are wound facing **down** (`faceUp:false`), and URP's shadow-caster pass back-face-culls, so a downward roof would cast **no shadow** against the directional moon → it shines through and the interior reads "roofless". `TryCeiling` therefore passes `ShadowCastingMode.TwoSided` to `BuildHorizontalMesh`. For an enclosed-castle look keep **ambient low** (torches do the work) and the **moon dim + shadows ON** (`DungeonLightingController` → "Preset: Castle Interior", or its `_ambientIntensity`/`_moonIntensity`/`_moonShadows`). Walls are solid cubes, so they occlude fine. Beyond the URP main-light shadow distance the dim moon can still bleed — fog + low intensity hide it; raise the URP asset's shadow distance for sharper far interiors.

---

## 10. Inspector surface (tunables)

- **Layout/Grid**: `Seed`, `MapWidth/Height`, `SectionMargin/Padding`, `CorridorWidth`, `CorridorTurnPenalty`, `CorridorReuseCost`, `GridColumns/Rows`, `CellSize`, `RandomizeLayout`, `SizeJitter`, `MinSectionDimension`, Gothic BSP (`GothicMinRoom/MaxRoom/MaxDepth`).
- **Geometry**: `WallThickness`, per-theme wall heights, per-theme ceiling toggles, `WallLayerName`.
- **Decoration density**: `TorchDensity`, `GothicDetailDensity`, `GraveDensity`, `HellPillarDensity`, `PillarSpacing`, `LavaCrackDensity`, `CatacombBoneDensity`, `CavernRockDensity`.
- **Lighting**: `MaxRealtimeLights`, per-theme light densities, `AllowHeroShadows`, `HeroShadowLightCount`, color arrays; **Atmosphere/Ambient**: fog, ambient, moonlight, `ExteriorFillIntensity`.
- **Materials**: optional per-theme overrides (fallbacks auto-created).
- **Generators (machinery)**: `GeneratorCount`, `GeneratorPrefab`, `GeneratorClearance`, `GeneratorRoomSize`, `GeneratorRoomGap`, `GeneratorRoomSpacing`, `GeneratorMaterial`, `GeneratorYawOffset`.
- **Baking/Debug**: `MarkStaticGeometry`, `ComputeOcclusionOnGenerate`, `BakeLightingOnGenerate`, `DebugDrawSectionBounds`.

---

## 10b. Torches (prefab + pickup)

- **Reduced count + prefab:** `TorchDensity` default is now **0.1** (was 0.22). `BuildTorch` uses the optional **`TorchPrefab`** if assigned (instantiated via the editor/runtime-safe `InstantiatePrefab`), skipping the primitive flame + `SpawnLight` — the prefab brings its own flame + light. Prefab torches are **not** marked static (they can move).
- **Pickup/carry:** `Assets/_Game/Environment/Torch.cs` (`IInteractable`, on the prefab root + a collider) routes `Interact` → `CharacterBase.TorchCarrier`. `Assets/_Game/Characters/_Base/TorchCarrier.cs` (auto-added by `CharacterBase`, like Grabber/Vitals) carries **one torch at a time**: parents it to the player camera at a front-left offset (`_holdLocalPosition`), disables its colliders while held, keeps it upright (yaw-only, `Torch.UprightRotation` = the FBX's -90° X). **G** throws it (`Torch.Launch` adds a Rigidbody) unless the grabber holds evidence; the HUD shows it as an inventory slot. `BuildTorch` composes `WallFacing(e) * TorchPrefab.transform.localRotation` so the prefab's upright correction survives. It then calls `WallMount.PushOffWall` (gap `TorchWallGap` 0.04 m) because the lantern pivot is its centre: without it half the lantern and its flame light sat inside the wall. The prefab's point light is a child of `EmbersFX/FlameFlipbook` (always inside the flame).

## 11. Related files

- `PowerGenerator.cs` — the machine (§7); `EngineAnimParams` (param/handle constants) lives at the bottom of this file.
- `Editor/EngineAnimatorBuilder.cs` — code-generates `engineAnimatorController` + handle-spin clips (menu **Fluffterror > Animation > Build Engine Animator**).
- `Core/IHoldInteractable.cs`, `Characters/_Base/InteractionSystem.cs` — hold-to-charge interaction + look-at highlight dispatch.
- `Core/IHighlightable.cs`, `Rendering/InteractableOutline.cs` (+ `.shader`) — look-at border. `UI/InteractionPromptUI.cs` — gothic on-screen prompt.
- `DungeonLightingController.cs` — optional external owner of scene lighting/fog.
- `DungeonGenerator.cs`, `DressedDungeonGenerator.cs` — **inactive** older generators (kept for reference, not in the scene).

## 12. Living Map — Milestone 1 (Docs/Design/LivingMap.md)

Code: `MultiSectionDungeonGenerator.LivingMap.cs` (generator side) + `Environment/LivingMap/` (runtime). Data: `Environment/Data/{Palettes,Director}/`, `Environment/Landmarks/`, `Mission/Data/RunModifiers/`, `Audio/SFX/Ambient/`. **Rebuild all data + wire the open scene: Fluffterror › Living Map › Build Assets + Wire Scene** (`Assets/Editor/LivingMapAssetBuilder.cs`, kit paths live only there), then Generate.

| System | What | Tunables |
|---|---|---|
| **Layout (S5)** | Kruskal spanning tree over the section grid + loops; dead-end rooms carved like generator rooms (`CarveRoomAndConnect`). `ValidateLayoutConnectivity` after the prune. Gothic/Catacomb room lanes are now `reserve: true` and also marked in `_lane[,]` (a landmark may sit on a lane if the flood fill still passes). | `UseSpanningTree`, `ExtraLoopChance` 0.3, `MinExtraLoops` 2, `MaxExtraLoops` 4, `DeadEndRoomCount` 2–3, `DeadEndRoomSize` 6–9, `DeadEndRoomSpacing` 28 |
| **Kit dressing (S3)** | `ThemeDressing[] Dressing` (per `SectionType`: `FloorProps`, `WallProps`, `Landmarks` palettes, densities, landmark light colour). `PropPalette.Entry`: Prefab, Weight, `Mount` (Floor / AgainstWall / Wall / Ceiling), RotationOffset, ScaleRange, YOffset, Solid, RandomYaw, CastShadows, DisplayName. `SpawnKit` composes yaw × offset × the FBX's own root rotation (kit FBX are Z-up, root −90° X ×100 — never overwrite it); `SnapToFloor` places by renderer bounds (pivots vary); AgainstWall/Wall use `WallMount.PushOffWall`; wall props taller than the wall are lowered/shrunk. Solid props get **one** `BoxCollider` from their bounds (`AddKitCollider`); imported colliders are stripped. Kit swaps in the old builders: `KitHellPillars`, `KitDeadTrees`, `KitWindows`, `KitCellBars`, `KitWallPanels`, `KitBonePiles`, `KitCandles`. Kit +Z = front (checked for bookcases, closets, chests, flags). | `UseKitDressing`, `KitColliders`, `Dressing[].FloorDensity` (props / 100 free tiles), `WallDensity` (per 100 wall faces), `BlockerInflate` 0.2 |
| **Landmarks (S6)** | One per section: least-used prefab of the theme's landmark palette, clearest spot near the section centre (prefix-sum square search, relaxes to r = 3), kept only if the 2×2 flood fill from the start loses ≤ footprint + 6 tiles and every objective stays reachable. Own point light centred above the prop, colour per theme, marked `ShadowlessLight` (`Rendering/ShadowlessLight.cs`: `DynamicLightBudget` never shadows it — a shadowed top light blacked out the Bell). Prefabs `LM_*` (kit pieces as nested prefab instances + a few primitives for the bell/well/beam). | `PlaceLandmarks`, `LandmarkLightIntensity` 6, `LandmarkLightRange` 9 |
| **Drips (E5)** | `DripDensity` points / 100 floor tiles in Catacombs, Cavern (×1) and Gothic (×0.4), stored in `DungeonLayout`. | `DripDensity` 0.5 |
| **`DungeonLayout`** | Serialized summary on `_multiSectionRoot`: seed, sections (type, rect, landmark name + position), dead-end rooms, drip points, extraction, start, start→extraction walk, link/loop counts. `DungeonLayout.Current`, `SectionAt(world)`, `NearestLandmark(world)`. | — |
| **`TorchStateController`** | On the Torch prefab. Lit / Guttering / Out. `Gutter(sec, thenDie, relightAfter)`, `Extinguish(relightAfter, silent)`, `Relight()`. Gutter = choppy multiplier via `LightFlicker.IntensityScale` + flame scale; Out = embers stop, `FlameFlipbook` (flame quad + light) deactivated → `DynamicLightBudget` skips it. `Torch.Interact` on an Out torch relights it when the player carries a lit torch ("Press E to relight"), otherwise "Burnt out" (not pickable). Static registry `All`. Sounds `SD_Torch_Gutter/Out/Relight`. | `_gutterRange`, `_gutterStep`, `_fadeOutTime`, `_relightTime` |
| **`TensionDirector`** (GameManager) | T ∈ [0,1], phases Calm → Build-up (T ≥ 0.35) → Spike (T ≥ 0.8 or a creature in Chase) → Relief (forced decay) → Calm. Moods: Normal / Tense / Chase / Normal (Extraction after unlock). 2 Hz tick, per-phase event budget, `MinEventGap`, per-event cooldowns. Events (`AmbientEventDefinition.EventKind`): TorchGutter, TorchDie, LightStutter, DistantSound (NavMesh spot 15–35 m away, preferably behind a wall), CreatureCue (asks registered creatures first, else plays the fake bell), Sting (spike entry). Inputs: `AddTension(amount, reason)`, `ReportCreatureAlert(source, level)` (Investigate +0.15, Point +0.3, Chase → Spike), `RegisterCreature`, generator running (+0.1 floor each), extraction open (+0.3 floor), `SetModifierFloor`. Debug: `_debugLog`, `_debugOverlay`, context menu Force Calm/Build-up/Spike, Fire Random Event. Uses gameplay `Random`, never the seed. | `DP_Default`: CalmRise 0.005, BuildUpRise 0.0065, RiseJitter 0.003, SpikeDuration 8–14 s, ReliefDuration 20–35 s, EventsPerMinute (2, 5, 0, 1), MinEventGap 6 s, StartGrace 12 s |
| **`ICreatureTensionSource`** | Creature hook (Position, AlertLevel, `TryPlayCue(CreatureCue)`). `PatrolEnemy` implements it (registers, reports Chase, returns false for cues → the director fakes them). Chucho should return true and play its real bell. | — |
| **`SoundscapeController`** (SoundManager) | Area bed per section theme from `DungeonLayout.SectionAt(listener)`, crossfaded on 2 dedicated 2D sources; drips one at a time from points within `DripRadius` via the `SoundManager` pool. The old `AmbientAudio` music bed still plays. | `SSP_Default`: BedVolume 0.45, CrossfadeTime 4 s, DripRadius 22 m, DripInterval 2.5–6 s; beds duck ×0.6 during Spike (`_spikeDuck`) |
| **Run modifiers** | `RunSetup` (GameManager, order −20): seed from `DungeonLayout`, hashed (`RunSetup.Mix`, System.Random's first draws correlate for nearby seeds), one weighted pick from `_pool`, `Apply(RunContext)`, run card through `MissionMessageUI.Show(title, sub, colour, sticky, hold)` (new hold overload). `RM_Blackout` (`_torchesOut` 0.5, keeps torches within 10 m of the start lit, ambient ×0.45 via `LightingMoodController.SetScales`, director floor 0.08), `RM_Fog` (fog ×2.5 via `SetScales` + `SetFogTint` mist (0.13, 0.14, 0.16) × 0.75, `EnemySpawner.SightRangeScale` 0.7 → `PatrolEnemy.SightRangeScale`), `RM_ExtraCreature` (`EnemySpawner.EnemyCount` +1). Debug: `_forceModifier` / `_forceIndex`. Seeds that hit each: 7 → Fog, 12 → Blackout, 30 → Extra creature. | per asset |

**Mix (2026-11-24): levels, distances and anti-spam caps are now set by the sound-designer — see `Docs/ArtDirection.md` §9 *Mix levels*; files re-mastered by `Tools/Audio/remaster_live.py`.** **Audio set** (`Audio/SFX/Ambient/`, Stable Audio Open → `process_sfx.py --style relaxed --role …`, workflow `Tools/ComfyUI/workflows/stable_audio_ambience.json` for beds): `SD_Amb_Drip` (6, role footstep −26, 3D min 2 m), `SD_Amb_Distant` (6 chains/creaks/stone, action −22, min 10 m), `SD_Amb_Bell` (3, action −22, min 10 m), `SD_Torch_Gutter/Out/Relight` (footstep −26, min 3 m), `SD_Amb_Sting` (3, ui −24, 2D), `SD_Bed*` (5 seamless 20 s loops, ambient −30, Vorbis streaming, 2D). `SoundDefinition` gained `_minDistance`/`_maxDistance` (defaults 1/500 = Unity's), applied by `SoundManager`. Seeds: drip 31000, chain 32000, stone 33000, creak 34000, bell 35000, gutter 36000, out 37000/37100, relight 38000/38100, sting 39000/39100, beds 41000–45000 (catacomb 42100). **Needs an ear check** (generated + picked by measurements, not by listening).

**Performance** (SampleScene, seed 30, editor Play mode, same three camera positions; before = old map single frames, after = median of 32 frames): main thread 6.0–7.1 ms → 6.3–6.7 ms, render thread 2.8–3.5 → 2.9–3.1 ms, GPU 3.0–4.2 → 3.0–3.3 ms; draw calls 1.9k–6.2k → 2.5k–4.7k; enabled point lights 29–59 → 29–58 (182 total, Blackout runs ≈ 90 fewer); shadowed ≤ 4. The 30k-tri torch lantern (`Details`) is the biggest triangle cost when several shadowed point lights see many torches (up to 32 M tris/frame at one spot) — a LOD / shadows-off on the lantern mesh is the next optimizer win.



## 13. Chunk grid — the map built from authored prefabs (2026-11-24)

Goal (user): *"the entire map generated from the prefabs, or at least the corridor zones, so later we can refine the prefabs one by one."* Every walkable cell of the map is now an authored chunk prefab (`Assets/Art/Environment/Chunks/CH_*.prefab`, built by the 3d-artist from `ArtSource/Environment/Chunks/`, README §7/§8). Refining a prefab refines every map that uses it.

**Files:** `MultiSectionDungeonGenerator.ChunkGrid.cs` (grid mode), `MultiSectionDungeonGenerator.Chunks.cs` (stamping, socket wiring, combine — shared; also the older *embed* mode), `Chunks/ChunkLibrary.cs`, `Chunks/ExtractionGateLift.cs`, `Chunks/TrapKillVolume.cs`, `Rendering/ChunkDistanceCuller.cs`. Data: `Data/ChunkLibrary.asset` (rebuild: **Fluffterror › Living Map › Steps › Build Chunk Library**, also part of Build Assets + Wire Scene).

**Pipeline (`ChunkGridMode` on and `Chunks` assigned):**
1. `LayoutChunkGrid` — 4×4 sections of `SectionCells` (5) × 5 cells, 8 m per cell (= 8 tiles), with 1-cell link lanes → 23×23 cells (184 m). Themes from `BuildThemeBag`.
2. `LinkSectionsOnGrid` — Kruskal tree + loops (`MinExtraLoops`/`MaxExtraLoops`/`ExtraLoopChance`), one lane cell per link.
3. `PlaceGridRooms` — required rooms first, each must keep the section's door/link cells connected (`SectionStaysConnected`): Extraction_Gate (weighted section), SP_ChuchoDen far from the gate + Corridor_Chewed glued to its door, 3 Generator_Alcoves spread farthest-first, one landmark room per section (a landmark room may repeat up to Max+2, far apart), `ExtraRoomsPerSection`.
4. `GrowSectionMaze` — random Prim to `CellFill` (0.68) of each section, BFS joins required cells, `CellLoopChance` loops.
5. `SelectAndStampCells` — per cell a N/E/S/W door mask (bits `DN/DE/DS/DW`); 1×2 merges of straight cells for 8×16 N/S chunks (`MergeRoomChance`), else 1×1 match with rotation (`RotBits`), theme filter, Min/Max, ≥ 40 tiles between equal non-corridor chunks, **no repeated BaseName next to each other**, theme height variant (`_H36` Catacombs, `_H50` Gothic/Hell/Transition) via `Variant()`. Dead-end leaves (DeadEnd / Storage) become `_deadEndRooms`. A cell with no matching chunk falls back to a procedural 4 m corridor and is logged in `_gridGaps` (0 on the 10 test seeds).
6. `FitGridCollars` — each chunk's `FitToTheme` height = tallest neighbour across its doors (no gaps at height changes). No open sky in grid mode.
7. `ValidateChunkGrid` — every chunk reachable from the start (orphans are removed), then the usual 2×2 nav flood (1×1 allowed on chunk tiles) + objective check.
8. `FinishChunks` — sockets → systems: `SOCKET_Torch*` → kit torch + `TorchStateController` (manifest `torches_suggested`; plain corridors only with `GridCorridorTorchChance` 0.3), `SOCKET_Evidence*` (evidence prefers sockets, one per chunk, never in the gate/alcove), `SOCKET_Landmark` + DisplayName (landmark name for the run card / `DungeonLayout`), `SOCKET_Drip` → drip points, `SOCKET_Hide*` / `VOL_Water` → `DungeonLayout` (for future hide/Flooded mechanics), `VOL_Trap` → `TrapKillVolume`, `SOCKET_Generator_01` → `PowerGenerator`, `SOCKET_Spawn` → player start band, `_Gate` → `ExtractionGateLift`. `MarkStaticDeep`.
9. `CombineChunksAtRuntime` — `StaticBatchingUtility.Combine` of every chunk renderer (except `_Gate`, `_Bell`, `_Lid_`, `_Water`) in Play mode.

Hierarchy: `_multiSectionRoot/Map/Section_NN_Theme/Cell_x_z_CH_Name` (+ `Map/Links`), prefab links kept (editor `InstantiatePrefab`). Procedural floors/walls/ceilings and the old decorate/kit passes are skipped in grid mode.

**Tunables:** `ChunkGridMode` (true), `SectionCells` 5, `CellFill` 0.68, `CellLoopChance` 0.12, `MergeRoomChance` 0.55, `ExtraRoomsPerSection` (0,1), `GridCorridorTorchChance` 0.3. Embed mode (grid off): `UseChunks`, `ChunksPerSection`, `CorridorChunks`, `CorridorChunkSpacing`, `ChunkCentreKeepOut`, `SameChunkSpacing`.

**Seeds (all 0 fallback cells, 0 unreachable objectives, ~0.9–1.4 s):** 30, 7, 12, 3, 5, 11, 21, 42, 77, 101 → 306–325 chunks, 20–23 distinct rooms/set pieces, landmarks 13–16/16. Scene saved at seed **7**.

**Coverage (authoring to-do):** `gen.ChunkCoverageReport()` lists distinct 1×1 chunks per mask × theme. Thin: Cross in Graveyard/Cavern (1), Cross Hell/Transition (2), Corner/T in Graveyard/Cavern/Hell (2). Graveyard, Cavern and Hell have **no themed corridor set** (they reuse masonry corridors) — the biggest visual gap.

**Perf (seed 30 Play, editor, 5 spots, median):** draws 547–4564 (old procedural map) → 74–937 (grid) → 73–671 with `ChunkDistanceCuller`; GPU 0.9–5.5 ms → 0.9–3.2 ms; main thread 5.4–8.7 → 6.8–10.3 ms (editor IMGUI/SceneView dominates the main-thread sample; profile a player build before acting). Shadowed lights ≤ 4.

**Walk tests (real CharacterController):** spiral stair → balcony, bell tower ramp → balcony (after the torch-trigger fix) + under the balcony, pit escape stairs, sewer side stair down + up, trap plank, library N→S — all REACHED; trap volume kills.
