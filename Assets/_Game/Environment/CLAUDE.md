# CONTEXTMAP — Multi-Section Dungeon Generator

**File:** `Assets/_Game/Environment/MultiSectionDungeonGenerator.cs`
**Type:** `MonoBehaviour` (single class, ~2000 lines, no dependencies beyond `UnityEngine` / `UnityEditor`)
**Role:** Procedurally builds a **closed, fully-connected multi-biome level** — geometry, themed dressing, lighting, *and* repairable generator machinery — entirely from editor context-menu actions, in or out of Play mode, using **primitive meshes only** (no external models, no `Resources.Load`).

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
| 7 | `ConnectSections()` | L-corridors between every right/down grid neighbour → looped lattice. |
| 8 | **`CarveGeneratorRooms()`** | Carve dedicated machine chambers + connecting corridors (see §7). |
| 9 | `PruneFloorIslands()` | Keep only the largest connected floor region; everything else → wall. |
| 10 | `AssignSectionOwnership()` | Fill `_section[,]` from section bounds; non-section floor = `Transition`. |
| 11 | **`SpawnGenerators()`** | Instantiate one `PowerGenerator` per carved room (see §7). |
| 11b | **`PlaceMissionObjectsOnFloor()`** | Move the scene's extraction zone, player and evidence onto the new floor (see §7). |
| 12 | `BuildFloors / BuildCeilings / BuildWalls / BuildCeilingSkirts` | Geometry from the grid. |
| 13 | `DecorateGothic/Graveyard/Hell/Catacombs/Cavern` | Themed primitive dressing. |
| 14 | fog + ambient/moon | Skipped if a `DungeonLightingController` is in the scene (it owns RenderSettings). |
| 15 | *(editor)* occlusion / lightmap bake | Gated by `ComputeOcclusionOnGenerate` / `BakeLightingOnGenerate`. |

Output hierarchy: `transform/_multiSectionRoot/{Floors, Walls, Ceilings, Details, Decor, Lights, Generators}` — `Clear` is a single destroy.

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
- **Player start**: clear tile `PlayerStartDistance` (8–18) tiles' **BFS walking distance** from the extraction, outside its footprint, facing it.
- **Evidence**: themed-section tiles off reserved corridors, `EvidenceMinDistance`–`EvidenceMaxDistance` walk tiles from the start, ≥ `EvidenceSpacing` apart, ≥ `EvidenceGeneratorGap` from generator rooms, one per section type first. Constraints relax stepwise if the map can't fit them (logs a warning if items are left unplaced).
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
- **No external assets in the runtime path** — primitives + runtime URP/Lit materials (`MakeMatte`/`MakeEmissive`); optional Inspector materials override the fallbacks.
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
- **Pickup/carry:** `Assets/_Game/Environment/Torch.cs` (`IInteractable`, on the prefab root + a collider) routes `Interact` → `CharacterBase.TorchCarrier`. `Assets/_Game/Characters/_Base/TorchCarrier.cs` (auto-added by `CharacterBase`, like Grabber/Vitals) carries **one torch at a time**: parents it to the player camera at a front-left offset (`_holdLocalPosition`), disables its colliders while held, keeps it upright (yaw-only, `Torch.UprightRotation` = the FBX's -90° X). **G** throws it (`Torch.Launch` adds a Rigidbody) unless the grabber holds evidence; the HUD shows it as an inventory slot. `BuildTorch` composes `WallFacing(e) * TorchPrefab.transform.localRotation` so the prefab's upright correction survives.

## 11. Related files

- `PowerGenerator.cs` — the machine (§7); `EngineAnimParams` (param/handle constants) lives at the bottom of this file.
- `Editor/EngineAnimatorBuilder.cs` — code-generates `engineAnimatorController` + handle-spin clips (menu **Fluffterror > Animation > Build Engine Animator**).
- `Core/IHoldInteractable.cs`, `Characters/_Base/InteractionSystem.cs` — hold-to-charge interaction + look-at highlight dispatch.
- `Core/IHighlightable.cs`, `Rendering/InteractableOutline.cs` (+ `.shader`) — look-at border. `UI/InteractionPromptUI.cs` — gothic on-screen prompt.
- `DungeonLightingController.cs` — optional external owner of scene lighting/fog.
- `DungeonGenerator.cs`, `DressedDungeonGenerator.cs` — **inactive** older generators (kept for reference, not in the scene).
