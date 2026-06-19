# Fluffterror — Claude Context

## Project Overview

Co-op comedic horror game for 1–8 players. Players enter a location, collect evidence, and extract.
Each character has a **special ability** and a **comic flaw** that interacts with other characters.

**Engine:** Unity 6000.4.0f1 | **Pipeline:** URP 17.4 | **Platform:** PC  
**Networking:** Unity Netcode for GameObjects (NGO) — Phase 4, not yet implemented  
**Input:** New Input System (InputActionAsset)

---

## Unity Setup Notes

- **CharacterController** height = 1.8, center Y = 0.9
- **CameraRoot** localPosition Y = 1.6 (eye level), child of the character root
- **Camera** is a child of CameraRoot
- Cursor is locked in `CharacterMovement.OnEnable`, unlocked in `OnDisable`
- Git repo root is `juego/` (the Unity project folder), `.gitignore` lives there

---

## Architecture

### ScriptableObject Architecture
Data lives in ScriptableObjects, not MonoBehaviours.

| Asset | Class | Purpose |
|---|---|---|
| `InputReader.asset` | `InputReader` | Wraps InputActionAsset, fires C# events |
| `BaseDefinition.asset` | `CharacterDefinition` | Base character type (MovementClass = Base) |
| `TrotinDefinition.asset` | `CharacterDefinition` | Trotín character type (MovementClass = Trotin) |
| `CharacterStats_Base.asset` | `CharacterStats` | Per-character movement/inventory/detection values |
| `TrotinStats.asset` | `CharacterStats` | Trotín-specific stat overrides |
| `EvidenceItem.asset` | `EvidenceItem` | Evidence definition (name, type, value) |
| `MissionConfig.asset` | `MissionConfig` | Mission parameters (evidence count formula) |

### Component Layout (every character)
```
GameObject
 ├── CharacterBase
 ├── CharacterController
 ├── CharacterMovement      (or TrotinMovement — auto-swapped by CharacterBase.OnValidate)
 ├── InventorySystem
 ├── InteractionSystem
 ├── PhysicsGrabber         (auto-added at runtime by CharacterBase.Awake if missing)
 │    └── GrabBeam (child GO with LineRenderer, created at runtime)
 ├── CharacterAudio
 └── CameraRoot (child Transform, Y=1.6)
     └── Camera (child)
         ├── AudioListener
         ├── AudioReverbFilter
         ├── EnvironmentReverb
         └── CameraEffects
```

---

## File Map

### Core — `Assets/_Game/Core/`
- **`InputReader.cs`** — ScriptableObject. Exposes events: `MoveEvent`, `LookEvent`, `InteractStartedEvent`, `InteractCanceledEvent`, `UseAbilityEvent`, `DropItemEvent`, `CrouchEvent(bool)`, `SprintEvent(bool)`, `JumpEvent`. Guards: `if (_actions == null) return` in OnEnable; `if (_move == null) return` in OnDisable; null-conditional on FindActionMap in OnDisable.
- **`IInteractable.cs`** — Interface: `string GetPrompt()` + `void Interact(CharacterBase interactor)`.
- **`DebugHUD.cs`** — IMGUI overlay: interaction target, grabber held item, inventory contents, mission state.

### Characters Base — `Assets/_Game/Characters/_Base/`
- **`CharacterDefinition.cs`** — ScriptableObject. Enum `MovementClass { Base, Trotin }`. Fields: `DisplayName`, `Movement`, `Stats`. Setting `Movement` in the Inspector triggers `OnValidate` → `SyncMovementComponent` which swaps the movement component automatically via `Undo.DestroyObjectImmediate` + `Undo.AddComponent`.
- **`CharacterStats.cs`** — Fields: `MoveSpeed=5`, `SprintMultiplier=1.8`, `Gravity=-20`, `JumpForce=6`, `CrouchSpeedMultiplier=0.5`, `LookSensitivity=0.15`, `InventorySlots=2`, `DetectionRadius=5`.
- **`CharacterBase.cs`** — RequireComponent: CharacterController, InventorySystem, InteractionSystem (NOT CharacterMovement — that is auto-managed). Serialized: `_definition (CharacterDefinition)`, `_input (InputReader)`, `_cameraRoot (Transform)`. Exposes: `Definition`, `Stats`, `Input`, `CameraRoot`, `Controller`, `Movement`, `Inventory`, `Interaction`, `Grabber`. Awake auto-adds `PhysicsGrabber` if the GameObject doesn't have one. Editor-only `OnValidate` calls `SyncMovementComponent` via `EditorApplication.delayCall`.
- **`CharacterMovement.cs`** — All fields `protected`, all methods `protected virtual`. `_cameraRoot` removed — reads `_character.CameraRoot` instead. Public properties: `IsSprinting`, `IsCrouching`. Update() → ApplyLook() → ApplyCrouch() → ApplyMovement(). Protected helper `ApplyGravityAndJump()` shared with subclasses. **Crouch** is hold-to-crouch (`CrouchEvent` true/false). `ApplyCrouch` lerps `_controller.height` toward target at `_crouchTransitionSpeed` **in lockstep with the camera Y**, recomputing `center = height*0.5` each frame so feet stay planted (no snap/pop). `CanStand()` SphereCast starts at the **top of the current capsule** (not mid-body) sweeping only the reclaimed gap — avoids the self-overlap that let players stand through low ceilings. `_debugCrouch` toggle logs input/state/blocked events and draws the stand-up cast ray (green=clear, red=blocked).
- **`InventorySystem.cs`** — **Dormant**: evidence is now carried physically via PhysicsGrabber, nothing flows through here. Kept for Phase 2 abilities (Mochín). Exposes `Items` (always empty for now), `MaxSlots` from `_character.Stats.InventorySlots`, `IsFull`.
- **`InteractionSystem.cs`** — Raycast from camera forward (cyan debug ray). Finds Camera via `GetComponentInChildren` in Start. Subscribes to `InteractStartedEvent`. Stores `CurrentPrompt` for DebugHUD.
- **`PhysicsGrabber.cs`** — R.E.P.O.-style physics carry. `EvidencePickup.Interact` routes here via `interactor.Grabber.TryGrab(pickup)` (hold **E** to grip — release fires `InteractCanceledEvent` → `Release()`; **G** = `DropItemEvent` → throw with `_throwSpeed` impulse). FixedUpdate applies spring-damper acceleration toward `camera + forward × _holdDistance` (wall raycast pulls the hold point in front of geometry). Heavy items: `_liftFactor = clamp01(_maxLiftMass / mass)` weakens the grip and blends gravity back in, so they sag. Grip breaks beyond `_breakDistance`. Gravity disabled while held, restored on release. Beam: runtime LineRenderer child "GrabBeam", 12 segments, Perlin wobble, updated in LateUpdate; optional `_beamMaterial` (defaults to Sprites/Default). Events: `ItemGrabbed`, `ItemReleased` (consumed by CharacterAudio). Tunables: `_holdDistance=1.8`, `_springStrength=140`, `_damping=11`, `_angularDamping=6`, `_breakDistance=3.5`, `_maxLiftMass=8`, `_throwSpeed=8`.
- **`CameraEffects.cs`** — On the **Camera child** (not CameraRoot). Auto-discovers `CharacterBase` via `GetComponentInParent` in Awake. Head bob (sin wave on localPosition XY, scales with speed ratio). Z-axis tilt based on lateral velocity. Does not conflict with CharacterMovement because it lives one level lower in the hierarchy.

### Trotín — `Assets/_Game/Characters/Trotin/`
- **`TrotinMovement.cs`** — Inherits CharacterMovement. States: Moving / Sprinting / Flying.
  - Always moves forward (no A/D, steered by mouse only — rotating transform also rotates the camera child).
  - **Q key** (`UseAbilityEvent`) → TryActivateSprint if cooldown clear.
  - Sprinting: 3× speed, if S pressed or timer expires → EnterFlying.
  - Flying: burst forward, exponential drag, exits when horizontal speed ≤ MoveSpeed or timer expires.
  - Blocks `OnSprint()` (Shift does nothing).
  - Calls `ApplyGravityAndJump()` at top of ApplyMovement override.
  - Tunables: `_sprintMultiplier=3`, `_sprintDuration=5`, `_sprintCooldown=25`, `_flyingBurstMultiplier=2`, `_flyingDrag=1.5`, `_flyingUpward=2`, `_maxFlyingDuration=3`.

### Evidence — `Assets/_Game/Evidence/`
- **`EvidenceType.cs`** — Enum: `Physical`, `Photo`, `ScannerReading`.
- **`EvidenceItem.cs`** — ScriptableObject: `DisplayName`, `Type`, `Icon(Sprite)`, `BaseValue=10`, `QualityMultiplier=1`, `IsBreakable`. Property: `FinalValue = BaseValue * QualityMultiplier`.
- **`EvidencePickup.cs`** — RequireComponent: Collider, Rigidbody. Implements IInteractable. States: `InWorld / Carried / AtExtractionZone / Destroyed`. `Interact()` routes to `interactor.Grabber.TryGrab(this)` — item stays an active, non-kinematic rigidbody while Carried (beam-held). `OnGrabbed(carrier)` / `OnReleased()` only flip state + Carrier. Exposes `Body` (Rigidbody) for the grabber. `Break()`: only if IsBreakable, calls MissionManager. **Impact breaking**: `OnCollisionEnter` calls `Break()` when a breakable item hits anything faster than `_breakImpactSpeed` (7 m/s) — throwing fragile evidence can destroy it.

### Mission — `Assets/_Game/Mission/`
- **`MissionConfig.cs`** — ScriptableObject: `MissionName`, `LevelIndex=1`, `BaseEvidenceCount=2`, `PlayerMultiplier=1`, `MissionDuration=300`. Formula: `(BaseEvidenceCount × LevelIndex) + (playerCount × PlayerMultiplier)`.
- **`MissionManager.cs`** — Static Instance singleton. `[SerializeField] int _playerCount=1` (Phase 4 placeholder). Registers all scene evidence via `FindObjectsByType`. Events: `MissionWon`, `MissionLost`, `EvidenceDestroyed`. `RegisterNewEvidence()` for runtime additions (Fotchi photos).

### Environment — `Assets/_Game/Environment/`
- **`ExtractionZone.cs`** — Trigger collider (set isTrigger in Awake). Tracks `HashSet<CharacterBase>` in zone (OnTriggerEnter/Exit) and `HashSet<EvidencePickup>` dropped in zone via **OnTriggerStay** — Stay, not Enter, so beam-carried items register when released while already inside the zone. `CheckWinConditions` prunes entries whose state is no longer `AtExtractionZone` (grabbed back out / destroyed). Carried items do NOT count — they must be released in the zone, R.E.P.O.-style. Win requires: no destroyed evidence blocking, all evidence in zone, `majorityPresent = count > aliveCount * 0.5f`. 3-second countdown.
- **`DungeonGenerator.cs`** — Procedural BSP dungeon (walls + floors) **+ authored prop-dressing pass**. `[ContextMenu]` **Generate** / **Clear** (work outside Play mode; everything parented under a single `_dungeonRoot`, props under a `Decor` child). **Grid-based**: BSP `Split` → rooms placed in leaves → rooms + L-corridors carved into a `bool[,] _floor` (1 tile = 1 world unit). Geometry is **derived from the grid**: floor quads via greedy horizontal run-merge; a wall is placed only on a floor tile's edge where the neighbour is empty (collinear edges merged into runs). This makes corridor↔room openings appear automatically and prevents stray walls mid-corridor. Procedural `MeshFilter`+`MeshRenderer`+`MeshCollider` per piece. Default materials cached (`_runtimeFloorMat`/`_runtimeWallMat`).
  - **Room metadata**: each carved room is stored as `RoomInfo` (rect, center, area, `RoomType` enum: Empty/Storage/Bedroom/Library/Ritual/Extraction/Hall). `AssignRoomTypes` makes the largest room Extraction, others size-biased (big→Hall/Library/Ritual, small→Storage/Empty).
  - **Decoration** (`DecorateDungeon`, runs after floor/wall build, gated by `_decorate`): builds a `Cell[,]` occupancy grid (Wall/Corridor/RoomFree/Reserved/Filled) + `_roomIndex` ownership + `_doorways` (corridor tiles touching a room). `ReserveSkeleton` marks a center clearance + a straight route from every doorway to the room center as **Reserved** (stays walkable, off-limits to floor props) — this is what guarantees CharacterController navigation is never blocked. Floor props only land on `RoomFree` tiles (never corridors/reserved), wall fixtures only on wall-adjacent tiles.
  - **Prop groups** are all serialized `PropGroup`s (arrays of weighted `PropEntry{Prefab, Weight, RotationOffset, ScaleRange, YOffset}`) assigned in the Inspector — **no `Resources.Load`, no hardcoded paths**. Groups: `_archways` (placed at doorways), `_storageFurniture`/`_libraryFurniture`/`_bedroomFurniture`/`_hallFurniture`/`_ritualProps` (room-type features), `_wallClutter` (wall/corner), `_cornerCobwebs` (corners, high), `_wallLights` (torches/candles), `_rareHorror` (sparse). Furniture is wall-biased (lists wall-adjacent tiles first), faces inward; fixtures inset toward the wall by `_wallFixtureInset`.
  - **Light budget**: `ConfigureLights` caps total real-time lights at `_maxRealtimeLights` (extras disabled) and shadow-casters at `_maxShadowLights` (rest `LightShadows.None`); torches without an embedded `Light` get a cheap point light when `_attachTorchLight`.
  - **Furniture colliders**: `AddBoundsCollider` adds one `BoxCollider` sized to combined local mesh bounds (cheap; only for solid furniture/clutter, not fixtures), gated by `_generatePropColliders`. Prop instantiation uses `PrefabUtility.InstantiatePrefab` in-editor (keeps prefab/model link) and `Instantiate` at runtime. All placement is deterministic from `Seed`. `_debugDrawRooms` draws color-coded room-type gizmos.
  - Tunables: `Seed` (0=random), `MapWidth/Height=80`, `MinRoomSize=8`, `MaxRoomSize=20`, `MaxDepth=5`, `CorridorWidth=3`, `WallHeight=3`, `WallThickness=0.5`, density sliders (`_featureDensity`/`_clutterDensity`/`_cobwebDensity`/`_lightDensity`/`_maxLightsPerRoom`/`_rareHorrorChancePerRoom`), optional `FloorMaterial`/`WallMaterial`.

### UI — `Assets/_Game/UI/`
- **`InventoryHUD.cs`** — IMGUI inventory at bottom-center. Slot boxes: filled (dark) vs empty (dimmer), item name + value, count label.

### Rendering — `Assets/_Game/Rendering/`
- **`HorrorPostFX.cs`** — Creates a global URP Volume at runtime with FilmGrain, Vignette, ColorAdjustments, Bloom, ChromaticAberration. Public `PulseGrain(intensity, duration)` coroutine for jump-scare spikes. `[Range(1,32)] _pixelSize` drives `PixelateFeature.Instance.BlockSize`; `OnValidate` applies changes live in the Inspector. Public `PixelSize` property for runtime changes.
- **`PixelateFeature.cs`** — `ScriptableRendererFeature`. Add to **`Assets/Settings/PC_Renderer`** once; no Inspector wiring needed after that. Uses URP 17 **RenderGraph** API (`RecordRenderGraph` / `AddRasterRenderPass`). Two-pass ping-pong blit: Pass A pixelates source → temp, Pass B copies temp → source. Static `Instance` property lets `HorrorPostFX` drive `BlockSize` directly. Skips when `BlockSize ≤ 1` or `isActiveTargetBackBuffer`.
- **`Pixelate.shader`** — `Hidden/Pixelate`. Fullscreen blit shader using `Blit.hlsl`. Quantizes UV to block-center: `(floor(uv × blockGrid) + 0.5) / blockGrid`. Samples with `sampler_PointClamp` for sharp edges. `_BlockSize` xy = `(screenW / blockSize, screenH / blockSize)`.
- **`LightFlicker.cs`** — Collects all `Light` children in Awake. Drives intensity via Fractal Brownian Motion (FBM) over 1-D Perlin noise each Update. Tunables: `_flickerIntensity`, `_flickerSpeed`, `_octaves`, `_lacunarity`, `_gain`. Each light gets a random `noiseOffset` so they desynchronise naturally.
- **`EvidenceGlow.cs`** — RequireComponent(Renderer). Finds nearest `CharacterBase` via `FindAnyObjectByType`. SmoothStep distance falloff, Lerp fade, sin-wave pulse. Writes `_EmissionColor` and `_EmissionIntensity` per-instance via `MaterialPropertyBlock`. Skips if pickup state is Destroyed. Place on the **mesh child** of an evidence GameObject, not the root.

### Shaders — `Assets/Materials/Shaders/`
- **`BlinnPhong_Textured.shader`** — URP HLSL. Inputs: `_k_d_tex` (diffuse), `_k_s_tex` (specular/roughness), `_N_tex` (normal), `_n` (shininess), `[HDR] _EmissionColor`, `_EmissionIntensity`. Roughness conversion: `pow(IsRoughness + (-2*IsRoughness+1)*k_s, 2)`. Three passes: ForwardLit (`Blend One Zero`), ShadowCaster, DepthOnly. LIGHT_LOOP_BEGIN/END for Forward+ additional lights. SRP Batcher compatible (identical CBUFFER across all passes).
- **`BlinnPhong.shader`** — Simpler variant at `Assets/_Game/Rendering/` with Albedo, Normal, Specular, Shininess, Emission.

### Audio — `Assets/_Game/Audio/`
- **`SoundDefinition.cs`** — ScriptableObject: `AudioClip[]`, `Volume`, `PitchRange (Vector2)`, `SpatialBlend`, `MixerGroup`. Methods: `GetClip()` (random), `GetPitch()` (random range). `IsValid` guards against empty clip arrays.
- **`SoundManager.cs`** — Singleton, DontDestroyOnLoad. Pool of 16 `AudioSource`s (round-robin). `Play(def, worldPos)` for 3D, `Play2D(def)` for UI/global.
- **`CharacterAudio.cs`** — RequireComponent(CharacterBase). Footsteps use a **dedicated `AudioSource`** (`_footstepSource`, created via `AddComponent` in Awake) to prevent pool overlap. `MinStepInterval = 0.18s` cooldown prevents double-triggers. Velocity-aware: decelerating resets the distance counter (no late steps); stopped pre-loads to threshold (first step on next frame is instant). Landing sound has **`_landSoundOffset`** (default 0.12 s): while falling, a downward raycast estimates `hit.distance / |vy|`; when ≤ offset the sound pre-fires so the impact aligns on contact. `_landSoundFired` flag prevents double-play. **Footstep fade-out**: the footstep source fades to silence over `_sprintFadeOutTime` (0.25 s) instead of ringing to the end of the clip when (a) the last step was the sprint clip and sprint ends (Shift released or player stops), or (b) the player goes airborne (jump/fall) — any walk or sprint clip is silenced. A fresh step cancels the fade and plays at full volume. `_muteWalkWhileSprinting` keeps sprinting silent (never falls back to walk clip) if no sprint clip is assigned. Item grab/release sounds (`_itemPickup` / `_itemDrop`) play via `SoundManager` pool on `PhysicsGrabber.ItemGrabbed` / `ItemReleased`. Three footstep SoundDefinition slots: `_walkSteps`, `_sprintSteps`, `_crouchSteps`.
- **`AmbientAudio.cs`** — Looping 2D ambient track with fade-in on Start. Creates its own `AudioSource` (spatialBlend=0). Public `FadeIn(duration)` / `FadeOut(duration)` for scripted transitions. Place on any persistent GameObject and assign a `SoundDefinition`.
- **`EnvironmentReverb.cs`** — RequireComponent(AudioReverbFilter). Place on the **Camera** (same GO as AudioListener). Casts 4 world-axis rays (`Vector3.forward/back/left/right`) on the `Walls` layer every `_updateInterval` (0.1 s). Average hit distance → `t ∈ [0,1]`. Two-segment blend: `t ∈ [0,0.5]` = small room → hall; `t ∈ [0.5,1]` = hall → open. Smoothly drives `decayTime`, `reverbLevel`, `reflectionsLevel`, `diffusion`, `density` each frame. `reverbLevel` must be **positive** to be audible (range −10000 to +2000 mB; Unity default is −10000 = dry).

### Editor — `Assets/Editor/`
- **`WallsLayerSetup.cs`** — Menu item **Fluffterror > Setup > Add Walls Layer**. Writes "Walls" into the first free slot (index 8–31) in `ProjectSettings/TagManager.asset`.
- **`ChunkBuilder.cs`** — Menu item **Fluffterror > Chunks > Build Dungeon Chunks**. Assembles reusable chunk prefabs from the *Updated Modular Dungeon - May 2019* kit into `Assets/_Game/Environment/Chunks/`. Each chunk is an empty root with `Floor`/`Walls`/`Props` child groups of **nested model-prefab instances** (instantiated via `PrefabUtility.InstantiatePrefab`, so links survive for hand-tweaking). Chunks are square (`CHUNK_TILES`=4 floor tiles); piece sizes are **measured from renderer bounds** at build time so floors tile seamlessly and walls/props rest on the floor surface at any kit scale. Each open side gets a centered opening (`OPEN_WIDTH`=2 tiles) + an `Arch` so chunks align edge-to-edge. Builds a curated set keyed by an open-side mask (`Side` flags) + a `Dressing` type (Pillars/Storage/Ritual/Cozy/None): Cross, T_N, Corner_NE, Straight_NS, DeadEnd_S, Room_Storage, Room_Ritual, Closed. Re-running overwrites the prefabs. Intended as a starting point to refine by hand; a future generator can place these chunks on a grid.

---

## Key Design Decisions

- **CharacterController, not Rigidbody** for all characters except Trotín's flying state (simulated via velocity vector).
- **Protected virtual methods** on CharacterMovement so subclasses override only what they need.
- **`ApplyGravityAndJump()`** is a protected shared helper — TrotinMovement calls it explicitly at the top of its ApplyMovement override to avoid duplicating gravity code.
- **Evidence is carried physically (R.E.P.O.-style)**, not pocketed: `PhysicsGrabber` suspends the live rigidbody in front of the camera with a spring-damper force while the player holds E. The item keeps colliding with the world, can be stolen by gravity (grip breaks past `_breakDistance`), and fragile items shatter on hard impacts. `InventorySystem` is dormant until Phase 2 (Mochín).
- **ExtractionZone uses OnTriggerStay for evidence** because a beam-held item is already inside the trigger when the player releases it — OnTriggerEnter would never re-fire, so the drop would be missed.
- **CameraEffects on Camera child** (not CameraRoot) so bob/tilt offsets don't fight with pitch or crouch-Y managed by CharacterMovement on CameraRoot.
- **InputReader null guards**: Unity can call OnDisable before OnEnable during asset import; guard `if (_move == null) return` prevents NullReferenceException.
- **CharacterDefinition drives MovementClass**: assigning a `CharacterDefinition` SO in the Inspector auto-swaps the movement component (Base ↔ Trotin) via editor `OnValidate`. `_cameraRoot` lives on `CharacterBase` (not CharacterMovement) so it survives component swaps.
- **Dedicated `AudioSource` for footsteps**: `CharacterAudio` creates its own `AudioSource` in Awake instead of using the shared `SoundManager` pool. This prevents simultaneous overlapping steps from multiple pool slots firing at once.
- **`AudioReverbFilter` on the AudioListener (Camera)**: affects all sounds heard by the player globally. `reverbLevel` must be set to a **positive value** (e.g. +1000) to be audible — the Unity default of −10000 is completely dry.
- **`PixelateFeature` uses a static `Instance`**: `ScriptableRendererFeature.Create()` runs at app start and sets `Instance = this`. `HorrorPostFX` reads it in `Awake` and `OnValidate` — no serialized reference needed. This pattern works for any renderer feature that needs to be driven at runtime without a drag-and-drop reference.

---

## Input Bindings (InputSystem_Actions.inputactions — Player map)

| Action | Keyboard | Gamepad |
|---|---|---|
| Move | WASD | Left Stick |
| Look | Mouse Delta | Right Stick |
| Interact (hold = grab beam) | E | South Button |
| UseAbility | Q | West Button |
| DropItem (throw held item) | G | DPad Down |
| Sprint | Shift | Left Shoulder |
| Crouch | C / Left Ctrl | Left Stick Press |
| Jump | Space | South Button |

---

## Development Phases (from PLANIFICACION.md)

| Phase | Status | Scope |
|---|---|---|
| 0 — Foundation | ✅ Done | Folders, Input, Base Character Controller |
| 1 — Core Loop | 🔄 In progress | Evidence, Inventory, Interaction, Trotín, Jump/Crouch, Camera FX, Shaders, Audio base, Dynamic reverb |
| 1.5 — Creature | ⬜ Next | NavMesh patrol + chase |
| 1.6 — Mission Polish | ⬜ | Timer display, score screen |
| 2 — Ability Framework | ⬜ | AbilitySystem, Bobi, Lumi |
| 3 — Remaining Characters | ⬜ | Pompón, Fotchi, Sabiucho, Coso, Mochín |
| 4 — Networking | ⬜ | NGO, player spawning, state sync |
| 5 — Audio | ⬜ | Chipmunk voices, spatial audio, Vivox |
| 6 — Content & Polish | ⬜ | Maps, creatures, traps, mission types |

---

## Character Roster

| # | Name | Ability | Comic Flaw |
|---|---|---|---|
| 1 | Trotín | Sprint → slide → fly | Can't stop moving |
| 2 | Bobi | Smell detection | Gets stuck in doors |
| 3 | Lumi | Night vision | Paralyzed by loud sounds (including real screams via Vivox) |
| 4 | Mochín | Large inventory | Falls asleep if idle >15s, drops all items |
| 5 | Pompón | Hear through walls | False-alarm distractions |
| 6 | Fotchi | Photo evidence | Auto-triggers camera on dramatic moments |
| 7 | Sabiucho | Field analysis / danger prediction | Cries if team ignores warnings |
| 8 | Coso | Break things open | 30% chance to accidentally break anything he touches |

---

## Known Bugs Fixed (do not reintroduce)

- `InputReader.OnDisable` NullReferenceException: guard `if (_move == null) return` at top, and `_actions?.FindActionMap(...)` null-conditional.
- Camera moving the capsule: CameraRoot must be a **child** of the character root, not at the root level. `_cameraRoot` field on CharacterBase must point to the CameraRoot child Transform.
- Trotín A/D rotation also rotated the camera (camera is a child): removed A/D entirely. Mouse-only steering.
- Crouch camera conflict: CharacterMovement sets CameraRoot.localPosition.y; CameraEffects sets Camera.localPosition. Different transforms, no conflict.
- Footsteps overlapping: `SoundManager` pool is round-robin — each step grabbed a fresh `AudioSource`, so rapid triggers stacked. Fixed by giving `CharacterAudio` its own dedicated `AudioSource` + a 0.18 s cooldown.
- `EnvironmentReverb` inaudible: `AudioReverbFilter.reverbLevel` defaults to −10000 (dry). Preset values must use **positive** mB for wet reverb (e.g. +1000 for a small room, +1500 for a hall). Setting it to −600 is still near-silent.
- `FindAnyObjectOfType` does not exist in Unity 2022.2+: correct API is `FindAnyObjectByType<T>()` (note "ByType", not "OfType").
- URP 17 (Unity 6) removed `ScriptableRenderPass.Execute(ScriptableRenderContext, ref RenderingData)` and `OnCameraSetup(CommandBuffer, ref RenderingData)`: must use `RecordRenderGraph(RenderGraph, ContextContainer)` with `AddRasterRenderPass` / `AddUnsafePass`. `RenderingData` no longer exists — use `frameData.Get<UniversalResourceData>()` and `frameData.Get<UniversalCameraData>()`.
- Crouch "not working": the Crouch action was only bound to `<Keyboard>/c` + gamepad East. Added `<Keyboard>/leftCtrl` as a second binding. If a key seems dead, check the actual binding in `InputSystem_Actions.inputactions` before touching the movement code — the logic was fine.
- Crouch stand-up through ceilings / snap-pop: `CanStand()` SphereCast must start at the capsule **top** (Unity ignores colliders the cast already overlaps), and `ApplyCrouch` must lerp `height` + `center` together with the camera rather than snapping the collider instantly.
