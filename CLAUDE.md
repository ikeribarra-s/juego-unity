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
- **`DebugHUD.cs`** — IMGUI overlay: interaction target, inventory contents, mission state.

### Characters Base — `Assets/_Game/Characters/_Base/`
- **`CharacterDefinition.cs`** — ScriptableObject. Enum `MovementClass { Base, Trotin }`. Fields: `DisplayName`, `Movement`, `Stats`. Setting `Movement` in the Inspector triggers `OnValidate` → `SyncMovementComponent` which swaps the movement component automatically via `Undo.DestroyObjectImmediate` + `Undo.AddComponent`.
- **`CharacterStats.cs`** — Fields: `MoveSpeed=5`, `SprintMultiplier=1.8`, `Gravity=-20`, `JumpForce=6`, `CrouchSpeedMultiplier=0.5`, `LookSensitivity=0.15`, `InventorySlots=2`, `DetectionRadius=5`.
- **`CharacterBase.cs`** — RequireComponent: CharacterController, InventorySystem, InteractionSystem (NOT CharacterMovement — that is auto-managed). Serialized: `_definition (CharacterDefinition)`, `_input (InputReader)`, `_cameraRoot (Transform)`. Exposes: `Definition`, `Stats`, `Input`, `CameraRoot`, `Controller`, `Movement`, `Inventory`, `Interaction`. Editor-only `OnValidate` calls `SyncMovementComponent` via `EditorApplication.delayCall`.
- **`CharacterMovement.cs`** — All fields `protected`, all methods `protected virtual`. `_cameraRoot` removed — reads `_character.CameraRoot` instead. Public properties: `IsSprinting`, `IsCrouching`. Update() → ApplyLook() → ApplyCrouch() → ApplyMovement(). Protected helper `ApplyGravityAndJump()` shared with subclasses. `CanStand()` SphereCast prevents uncrouch under ceilings.
- **`InventorySystem.cs`** — MaxSlots from `_character.Stats.InventorySlots`. Methods: `TryAdd(EvidencePickup)`, `Drop()`, `DropAll()`, `DropLastItem()`. Subscribes to `DropItemEvent`. Events: `ItemAdded`, `ItemRemoved`.
- **`InteractionSystem.cs`** — Raycast from camera forward (cyan debug ray). Finds Camera via `GetComponentInChildren` in Start. Subscribes to `InteractStartedEvent`. Stores `CurrentPrompt` for DebugHUD.
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
- **`EvidencePickup.cs`** — RequireComponent: Collider, Rigidbody. Implements IInteractable. States: `InWorld / Carried / AtExtractionZone / Destroyed`. `OnPickedUp()`: kinematic=true, SetActive(false). `OnDropped(pos)`: kinematic=false, SetActive(true). `Break()`: only if IsBreakable, calls MissionManager.

### Mission — `Assets/_Game/Mission/`
- **`MissionConfig.cs`** — ScriptableObject: `MissionName`, `LevelIndex=1`, `BaseEvidenceCount=2`, `PlayerMultiplier=1`, `MissionDuration=300`. Formula: `(BaseEvidenceCount × LevelIndex) + (playerCount × PlayerMultiplier)`.
- **`MissionManager.cs`** — Static Instance singleton. `[SerializeField] int _playerCount=1` (Phase 4 placeholder). Registers all scene evidence via `FindObjectsByType`. Events: `MissionWon`, `MissionLost`, `EvidenceDestroyed`. `RegisterNewEvidence()` for runtime additions (Fotchi photos).

### Environment — `Assets/_Game/Environment/`
- **`ExtractionZone.cs`** — Trigger collider (set isTrigger in Awake). Tracks `HashSet<CharacterBase>` in zone and `HashSet<EvidencePickup>` dropped in zone. Win requires: no destroyed evidence blocking, `majorityPresent = count > aliveCount * 0.5f`. 3-second countdown.

### UI — `Assets/_Game/UI/`
- **`InventoryHUD.cs`** — IMGUI inventory at bottom-center. Slot boxes: filled (dark) vs empty (dimmer), item name + value, count label.

### Rendering — `Assets/_Game/Rendering/`
- **`HorrorPostFX.cs`** — Creates a global URP Volume at runtime with FilmGrain, Vignette, ColorAdjustments, Bloom, ChromaticAberration. Public `PulseGrain(intensity, duration)` coroutine for jump-scare spikes.
- **`EvidenceGlow.cs`** — RequireComponent(Renderer). Finds nearest `CharacterBase` via `FindAnyObjectByType`. SmoothStep distance falloff, Lerp fade, sin-wave pulse. Writes `_EmissionColor` and `_EmissionIntensity` per-instance via `MaterialPropertyBlock`. Skips if pickup state is Destroyed. Place on the **mesh child** of an evidence GameObject, not the root.

### Shaders — `Assets/Materials/Shaders/`
- **`BlinnPhong_Textured.shader`** — URP HLSL. Inputs: `_k_d_tex` (diffuse), `_k_s_tex` (specular/roughness), `_N_tex` (normal), `_n` (shininess), `[HDR] _EmissionColor`, `_EmissionIntensity`. Roughness conversion: `pow(IsRoughness + (-2*IsRoughness+1)*k_s, 2)`. Three passes: ForwardLit (`Blend One Zero`), ShadowCaster, DepthOnly. LIGHT_LOOP_BEGIN/END for Forward+ additional lights. SRP Batcher compatible (identical CBUFFER across all passes).
- **`BlinnPhong.shader`** — Simpler variant at `Assets/_Game/Rendering/` with Albedo, Normal, Specular, Shininess, Emission.

### Audio — `Assets/_Game/Audio/`
- **`SoundDefinition.cs`** — ScriptableObject: `AudioClip[]`, `Volume`, `PitchRange (Vector2)`, `SpatialBlend`, `MixerGroup`. Methods: `GetClip()` (random), `GetPitch()` (random range). `IsValid` guards against empty clip arrays.
- **`SoundManager.cs`** — Singleton, DontDestroyOnLoad. Pool of 16 `AudioSource`s (round-robin). `Play(def, worldPos)` for 3D, `Play2D(def)` for UI/global.
- **`CharacterAudio.cs`** — RequireComponent(CharacterBase). Footsteps use a **dedicated `AudioSource`** (`_footstepSource`, created via `AddComponent` in Awake) to prevent pool overlap. `MinStepInterval = 0.18s` cooldown prevents double-triggers. Velocity-aware: decelerating resets the distance counter (no late steps); stopped pre-loads to threshold (first step on next frame is instant). Landing, item pickup/drop go through `SoundManager` pool. Three SoundDefinition slots: `_walkSteps`, `_sprintSteps`, `_crouchSteps`.
- **`EnvironmentReverb.cs`** — RequireComponent(AudioReverbFilter). Place on the **Camera** (same GO as AudioListener). Casts 4 world-axis rays (`Vector3.forward/back/left/right`) on the `Walls` layer every `_updateInterval` (0.1 s). Average hit distance → `t ∈ [0,1]`. Two-segment blend: `t ∈ [0,0.5]` = small room → hall; `t ∈ [0.5,1]` = hall → open. Smoothly drives `decayTime`, `reverbLevel`, `reflectionsLevel`, `diffusion`, `density` each frame. `reverbLevel` must be **positive** to be audible (range −10000 to +2000 mB; Unity default is −10000 = dry).

### Editor — `Assets/Editor/`
- **`WallsLayerSetup.cs`** — Menu item **Fluffterror > Setup > Add Walls Layer**. Writes "Walls" into the first free slot (index 8–31) in `ProjectSettings/TagManager.asset`.

---

## Key Design Decisions

- **CharacterController, not Rigidbody** for all characters except Trotín's flying state (simulated via velocity vector).
- **Protected virtual methods** on CharacterMovement so subclasses override only what they need.
- **`ApplyGravityAndJump()`** is a protected shared helper — TrotinMovement calls it explicitly at the top of its ApplyMovement override to avoid duplicating gravity code.
- **Carried evidence is SetActive(false)** (kinematic, hidden), so ExtractionZone counts carried items by inspecting player inventories directly, not by trigger detection.
- **CameraEffects on Camera child** (not CameraRoot) so bob/tilt offsets don't fight with pitch or crouch-Y managed by CharacterMovement on CameraRoot.
- **InputReader null guards**: Unity can call OnDisable before OnEnable during asset import; guard `if (_move == null) return` prevents NullReferenceException.
- **CharacterDefinition drives MovementClass**: assigning a `CharacterDefinition` SO in the Inspector auto-swaps the movement component (Base ↔ Trotin) via editor `OnValidate`. `_cameraRoot` lives on `CharacterBase` (not CharacterMovement) so it survives component swaps.
- **Dedicated `AudioSource` for footsteps**: `CharacterAudio` creates its own `AudioSource` in Awake instead of using the shared `SoundManager` pool. This prevents simultaneous overlapping steps from multiple pool slots firing at once.
- **`AudioReverbFilter` on the AudioListener (Camera)**: affects all sounds heard by the player globally. `reverbLevel` must be set to a **positive value** (e.g. +1000) to be audible — the Unity default of −10000 is completely dry.

---

## Input Bindings (InputSystem_Actions.inputactions — Player map)

| Action | Keyboard | Gamepad |
|---|---|---|
| Move | WASD | Left Stick |
| Look | Mouse Delta | Right Stick |
| Interact | E | South Button |
| UseAbility | Q | West Button |
| DropItem | G | DPad Down |
| Sprint | Shift | Left Shoulder |
| Crouch | C | Left Stick Press |
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
