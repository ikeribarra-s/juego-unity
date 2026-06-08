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
| `CharacterStats_Base.asset` | `CharacterStats` | Per-character movement/inventory/detection values |
| `TrotinStats.asset` | `CharacterStats` | Trotín-specific stat overrides |
| `EvidenceItem.asset` | `EvidenceItem` | Evidence definition (name, type, value) |
| `MissionConfig.asset` | `MissionConfig` | Mission parameters (evidence count formula) |

### Component Layout (every character)
```
GameObject
 ├── CharacterBase          (or subclass, e.g. TrotinCharacter)
 ├── CharacterController
 ├── CharacterMovement      (or subclass, e.g. TrotinMovement)
 ├── InventorySystem
 ├── InteractionSystem
 └── CameraRoot (child Transform, Y=1.6)
     └── Camera (child)
         └── CameraEffects
```

---

## File Map

### Core — `Assets/_Game/Core/`
- **`InputReader.cs`** — ScriptableObject. Exposes events: `MoveEvent`, `LookEvent`, `InteractStartedEvent`, `InteractCanceledEvent`, `UseAbilityEvent`, `DropItemEvent`, `CrouchEvent(bool)`, `SprintEvent(bool)`, `JumpEvent`. Guards: `if (_actions == null) return` in OnEnable; `if (_move == null) return` in OnDisable; null-conditional on FindActionMap in OnDisable.
- **`IInteractable.cs`** — Interface: `string GetPrompt()` + `void Interact(CharacterBase interactor)`.
- **`DebugHUD.cs`** — IMGUI overlay: interaction target, inventory contents, mission state.

### Characters Base — `Assets/_Game/Characters/_Base/`
- **`CharacterStats.cs`** — Fields: `MoveSpeed=5`, `SprintMultiplier=1.8`, `Gravity=-20`, `JumpForce=6`, `CrouchSpeedMultiplier=0.5`, `LookSensitivity=0.15`, `InventorySlots=2`, `DetectionRadius=5`.
- **`CharacterBase.cs`** — RequireComponent: CharacterController, CharacterMovement, InventorySystem, InteractionSystem. Exposes: `Stats`, `Input`, `Controller`, `Movement`, `Inventory`, `Interaction`. protected virtual Awake().
- **`CharacterMovement.cs`** — All fields `protected`, all methods `protected virtual`. Serialized: `_cameraRoot`, `_crouchHeight=0.9`, `_crouchCameraY=0.5`, `_crouchTransitionSpeed=12`. Update() → ApplyLook() → ApplyCrouch() → ApplyMovement(). Protected helper `ApplyGravityAndJump()` shared with subclasses. `CanStand()` SphereCast prevents uncrouch under ceilings.
- **`InventorySystem.cs`** — MaxSlots from `_character.Stats.InventorySlots`. Methods: `TryAdd(EvidencePickup)`, `Drop()`, `DropAll()`, `DropLastItem()`. Subscribes to `DropItemEvent`. Events: `ItemAdded`, `ItemRemoved`.
- **`InteractionSystem.cs`** — Raycast from camera forward (cyan debug ray). Finds Camera via `GetComponentInChildren` in Start. Subscribes to `InteractStartedEvent`. Stores `CurrentPrompt` for DebugHUD.
- **`CameraEffects.cs`** — On the **Camera child** (not CameraRoot). Auto-discovers `CharacterBase` via `GetComponentInParent` in Awake. Head bob (sin wave on localPosition XY, scales with speed ratio). Z-axis tilt based on lateral velocity. Does not conflict with CharacterMovement because it lives one level lower in the hierarchy.

### Trotín — `Assets/_Game/Characters/Trotin/`
- **`TrotinCharacter.cs`** — Inherits CharacterBase. RequireComponent(TrotinMovement). Exposes `TrotinMovement` property.
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

---

## Key Design Decisions

- **CharacterController, not Rigidbody** for all characters except Trotín's flying state (simulated via velocity vector).
- **Protected virtual methods** on CharacterMovement so subclasses override only what they need.
- **`ApplyGravityAndJump()`** is a protected shared helper — TrotinMovement calls it explicitly at the top of its ApplyMovement override to avoid duplicating gravity code.
- **Carried evidence is SetActive(false)** (kinematic, hidden), so ExtractionZone counts carried items by inspecting player inventories directly, not by trigger detection.
- **CameraEffects on Camera child** (not CameraRoot) so bob/tilt offsets don't fight with pitch or crouch-Y managed by CharacterMovement on CameraRoot.
- **InputReader null guards**: Unity can call OnDisable before OnEnable during asset import; guard `if (_move == null) return` prevents NullReferenceException.

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
| 1 — Core Loop | 🔄 In progress | Evidence, Inventory, Interaction, Trotín, Jump/Crouch, Camera FX |
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
- Camera moving the capsule: CameraRoot must be a **child** of the character root, not at the root level. `_cameraRoot` field on CharacterMovement must point to the CameraRoot child Transform.
- Trotín A/D rotation also rotated the camera (camera is a child): removed A/D entirely. Mouse-only steering.
- Crouch camera conflict: CharacterMovement sets CameraRoot.localPosition.y; CameraEffects sets Camera.localPosition. Different transforms, no conflict.
