# Fluffterror — Master Development Plan
**Engine:** Unity 6000.4.0f1 · **Pipeline:** URP 17.4 · **Target:** 1–8 players co-op PC

---

## 1. Game Concept (Reference)

Co-op horror/comedy. 8 playable characters, each with a **special ability** that makes them essential and a **comic flaw** that creates chaos. The goal is not to win — it is to generate a story to tell afterward. The chain reaction of flaws (Coso breaks something → noise freezes Lumi → Sabiucho cries → Trotín sprints in and triggers another trap) is the core product, not a side effect.

**Core loop:** Enter a location → collect evidence/complete objectives → extract → score based on evidence quality and survival. Voice chat is a first-class mechanic (real player screams freeze Lumi in-game).

---

## 2. Tech Stack Decisions

| Concern | Choice | Reason |
|---|---|---|
| Networking | Unity Netcode for GameObjects (NGO) | Already guided by Multiplayer Center in project; free; Unity 6 native |
| Voice chat | Unity Vivox SDK | Free tier; integrates with NGO; needed early for Lumi mechanic |
| Input | New Input System (already installed) | Rebindable, multiplayer-ready |
| Creature AI | Unity AI Navigation / NavMesh (already installed) | Sufficient for 3D patrol + chase |
| Physics | Rigidbody for Trotín slide; CharacterController for others | Per-character needs |
| Character data | ScriptableObjects for stats | Easy to tweak without code changes |
| Abilities | Component-based (one MonoBehaviour per ability) | Composable, testable |
| Night vision | URP custom Renderer Feature + post-process volume | Lumi's POV shader |
| Flash effect | Full-screen white blit shader with fade | Fotchi's flash blindness |

---

## 3. Repository & Folder Structure

```
juego-unity/
├── PLANIFICACION.md          ← this file
├── juego/                    ← Unity project root
│   ├── Assets/
│   │   ├── _Game/            ← all project code lives here
│   │   │   ├── Characters/
│   │   │   │   ├── _Base/        ← shared character systems
│   │   │   │   ├── Bobi/
│   │   │   │   ├── Lumi/
│   │   │   │   ├── Pompon/
│   │   │   │   ├── Trotin/
│   │   │   │   ├── Fotchi/
│   │   │   │   ├── Sabiucho/
│   │   │   │   ├── Coso/
│   │   │   │   └── Mochin/
│   │   │   ├── Creatures/
│   │   │   ├── Environment/
│   │   │   │   ├── Traps/
│   │   │   │   ├── Interactables/
│   │   │   │   └── Events/       ← Blackout, etc.
│   │   │   ├── Evidence/
│   │   │   ├── Mission/
│   │   │   ├── Networking/
│   │   │   ├── Audio/
│   │   │   ├── UI/
│   │   │   └── Core/
│   │   ├── Art/
│   │   │   ├── Characters/
│   │   │   ├── Environment/
│   │   │   └── VFX/
│   │   ├── Audio/
│   │   ├── Scenes/
│   │   │   ├── MainMenu
│   │   │   ├── Lobby
│   │   │   └── Maps/
│   │   └── Settings/             ← URP assets (already exist)
│   ├── Packages/
│   └── ProjectSettings/
```

**Rule:** Never put scripts in a folder named after the scene. Code belongs in `_Game/`, art in `Art/`, audio in `Audio/`.

---

## 4. Core Systems Map

These are all the systems that need to exist, with their dependencies.

```
GameManager
 └── MissionManager
      ├── EvidenceSystem
      └── ObjectiveSystem

CharacterBase (NetworkBehaviour)
 ├── CharacterStats (ScriptableObject)
 ├── CharacterMovement
 ├── AbilitySystem
 │    ├── ActiveAbility (one per character)
 │    └── PassiveAbility (one per character)
 ├── InventorySystem
 ├── InteractionSystem
 └── CharacterAudio

DetectionSystem (global singleton)
 ├── SmellDetector        ← Bobi
 ├── SoundDetector        ← Pompón, Lumi trigger
 └── LightDetector        ← Fotchi flash, Apagón

CreatureBase (NetworkBehaviour)
 ├── CreatureAI (NavMesh)
 ├── CreatureSenses
 └── CreatureBehaviourTree

EventSystem (global)
 ├── BlackoutEvent
 └── TrapTrigger

VoiceChatManager (Vivox)
 └── MicrophoneAnalyzer   ← feeds into Lumi's passive

NetworkManager (NGO)
 ├── PlayerSpawner
 ├── LobbyManager
 └── CharacterSelector

UIManager
 ├── HUD
 │    ├── AbilityCooldownUI
 │    ├── InventoryUI
 │    ├── SabiuchoWarningBanner
 │    └── MochinSleepTimer
 └── ScoreScreen
```

---

## 5. Development Phases

---

### Phase 0 — Foundation
**Duration:** 1–2 weeks  
**Goal:** Everything compiles, the team can run and move around a test room. No content, no fun yet.

#### 0.1 — Project Setup
- [ ] Add Unity Gitignore (Library/, Temp/, Builds/, *.csproj excluded)
- [ ] Create `_Game/` folder hierarchy as defined above
- [ ] Install **Unity Netcode for GameObjects** via Package Manager
- [ ] Install **Unity Vivox SDK** via Package Manager
- [ ] Set up two Build Profiles in Unity 6: PC Development and PC Release

#### 0.2 — Input System
- [ ] Rename existing `InputSystem_Actions.inputactions` → keep it, extend it
- [ ] Define action maps:
  - `Player`: Move (Vector2), Look (Vector2), Interact (Button), UseAbility (Button), DropItem (Button), Crouch (Button)
  - `UI`: Navigate, Submit, Cancel
- [ ] Create `InputReader.cs` (ScriptableObject) that broadcasts C# events from the Input System — characters subscribe to events, never poll Input directly
- [ ] Bind keyboard defaults: WASD move, mouse look, E interact, Q ability, G drop

#### 0.3 — Base Character Controller
Create `CharacterBase.cs` (NetworkBehaviour) with:
- [ ] `CharacterStats` ScriptableObject reference (speed, inventory slots, detection radius, panic multiplier)
- [ ] `CharacterMovement.cs`: handles move + look using CharacterController component, reads from InputReader
- [ ] Camera rig: per-player first-person camera, controlled by this script
- [ ] Basic animations: idle, walk, run states in Animator (placeholder cubes acceptable)
- [ ] Network sync: position and rotation via `NetworkTransform`

#### 0.4 — Test Scene
- [ ] Build a simple greybox indoor map: 5–6 rooms connected by hallways, one exit point
- [ ] Place a `NetworkManager` prefab in scene
- [ ] Confirm: two instances of the game running locally, both can move around independently

**Phase 0 exit criteria:** Two players in the same greybox room, both moving, no errors in console.

---

### Phase 1 — Core Game Loop (Local, No Networking)
**Duration:** 2–3 weeks  
**Goal:** A complete (ugly) game loop that can be won or lost. One character, one creature, evidence items.

#### 1.1 — Evidence System
- [ ] `EvidenceItem.cs`: ScriptableObject definition (name, baseValue, qualityMultiplier, isHeld, carriedBy)
- [ ] `EvidencePickup.cs`: MonoBehaviour on world objects; has `Interact()` method; adds to carrier's inventory
- [ ] Place 8–10 evidence items in test map
- [ ] `EvidenceCollector.cs` on ExtractionPoint: counts value of all evidence on players who reach it

#### 1.2 — Inventory System
- [ ] `InventorySystem.cs` on CharacterBase: `List<EvidenceItem> items`, `int maxSlots`
- [ ] `AddItem(item)`, `RemoveItem(item)`, `DropAll()` methods
- [ ] Default max: 2 slots for all characters (Mochín will override to 6 in Phase 3)
- [ ] Drop mechanic: press G to drop held item, it spawns a world object at feet

#### 1.3 — Interaction System
- [ ] `InteractionSystem.cs`: raycast forward 2.5m, check for `IInteractable` interface
- [ ] `IInteractable`: `string GetPrompt()`, `void Interact(CharacterBase interactor)`
- [ ] Objects that implement it: EvidencePickup, Doors, ExtractionPoint
- [ ] UI prompt appears on screen when looking at an interactable

#### 1.4 — First Character: Trotín (Recommended)
Trotín is first because his movement quirk forces the physics system to work correctly.

- [ ] Create `CharacterStats_Trotin.asset` (speed: max, inventory: 2)
- [ ] Override `CharacterMovement.cs` with `TrotinMovement.cs`:
  - Uses **Rigidbody** (not CharacterController) to allow physics-based sliding
  - On input released: apply drag gradually over 0.5–1s, not instantly
  - Slide distance depends on velocity at release (3–6m walk, 6–8m sprint)
  - On collision with objects during slide: `ISlideTarget` interface → objects respond (fall, break, etc.)
- [ ] `SprintAbility.cs` (Active): multiplies velocity by 3 for 5s, sets cooldown 25s
- [ ] `LobbyWiggle.cs` (Passive): in lobby scene, applies random small velocity impulses every 0.3–0.8s
- [ ] `TrotinAudio.cs`: skid sound on slide start, thud sound on collision, victory chirp on arrival

#### 1.5 — Creature (Basic)
- [ ] `CreatureBase.cs` (NetworkBehaviour)
- [ ] `CreatureAI.cs` using NavMesh:
  - States: Patrol → Alert → Chase → Retreat
  - Patrol: random waypoints in navmesh area
  - Alert: triggered when player enters detection radius OR makes sound above threshold
  - Chase: pursue player, end chase after losing sight for 8s
- [ ] On catch: screen shake + stun player 5s, drop one item
- [ ] Place one creature in test map

#### 1.6 — Mission & Win/Lose
- [ ] `MissionManager.cs`: tracks time (5min countdown), extraction status
- [ ] Win: player reaches ExtractionPoint with at least 1 evidence item
- [ ] Lose: timer expires
- [ ] End screen shows score (sum of evidence values × quality multipliers)

**Phase 1 exit criteria:** Run into map as Trotín, pick up evidence, survive or avoid the creature, reach extraction, see score. Trotín's slide should cause at least one accidental disaster in every run.

---

### Phase 2 — Ability & Passive Framework
**Duration:** 2–3 weeks  
**Goal:** A reusable system for all 8 characters' abilities and flaws, so characters 2–8 are implementations, not reinventions.

#### 2.1 — AbilitySystem
- [ ] `AbilitySystem.cs` on CharacterBase: manages one active + one passive
- [ ] `ActiveAbilityBase.cs` (abstract MonoBehaviour):
  - `float Cooldown`, `float Duration`, `bool IsActive`, `float CooldownRemaining`
  - `abstract void OnActivate()`, `abstract void OnDeactivate()`
  - `TryActivate()`: checks cooldown, calls OnActivate, starts Duration timer, starts Cooldown timer
  - Broadcasts `AbilityActivated` / `AbilityDeactivated` events (for UI and sound)
- [ ] `PassiveAbilityBase.cs` (abstract MonoBehaviour):
  - Always running; subscribes to global events or polls conditions
  - `abstract void OnPassiveTrigger()` called when condition met
- [ ] UI binding: `AbilityCooldownUI.cs` reads from AbilitySystem and shows cooldown ring

#### 2.2 — Global Event Bus
`GameEventBus.cs` (static class): a simple type-safe event bus. All game events flow through here. Examples:
- `LoudNoiseMade(Vector3 position, float volume)` — Lumi's paralysis trigger
- `ShinyObjectSpotted(Vector3 position)` — Bobi's distraction trigger
- `PlayerIgnoredWarning()` — Sabiucho's cry trigger
- `PlayerFellAsleep(CharacterBase player)` — Mochín's drop trigger
- `FlashFired(Vector3 position)` — Fotchi's flash trigger
- `ObjectBroken(Vector3 position)` — Coso's destruction event

Pattern: `GameEventBus.Raise<LoudNoiseMadeEvent>(new LoudNoiseMadeEvent { position = pos, volume = vol });`

#### 2.3 — Detection Framework
- [ ] `SmellSource.cs`: placed on creatures, traps, key items. Has `SmellIntensity` and `SmellType` enum
- [ ] `SoundSource.cs`: any object that makes noise. Has `Volume` (0–1), `SoundType`, emits `LoudNoiseMadeEvent` via bus if volume > threshold
- [ ] `DetectionZone.cs`: configurable sphere trigger. Used for creature's sight cone and Bobi's smell radius

#### 2.4 — Second Character: Bobi

- [ ] `CharacterStats_Bobi.asset` (speed: medium, detection: very high)
- [ ] `SmellAbility.cs` (Active, 40s CD, 8s duration):
  - Activates `SmellDetectionZone` (radius 15m)
  - All `SmellSource` objects in radius: ping on map, draw marker in world space
  - After 8s: markers fade
- [ ] `BobiHitboxPassive.cs` (Passive):
  - CharacterController capsule is 10% wider than base
  - On entering a narrow trigger (`NarrowPassage.cs` component on door frames): check if Bobi can fit
  - If not: play chipmunk scream SFX, Bobi is frozen 2s ("stuck"), then pops through
- [ ] `ShinyObjectDistraction.cs`:
  - Listens to `ShinyObjectSpottedEvent` on GameEventBus (raised by glowing objects in range)
  - If Bobi is not in ability use: overrides movement toward the object for 2–4s
  - Can be interrupted by player input after 1s
- [ ] Voice lines: 3 barks for "found something", 1 for "stuck"

#### 2.5 — Third Character: Lumi

- [ ] `CharacterStats_Lumi.asset` (speed: medium-low, vision: max)
- [ ] `NightVisionAbility.cs` (Passive, always active — treated as active for sharing):
  - Attaches a URP `RendererFeature` that applies a green-tint luminance boost post-process to Lumi's camera
  - During `BlackoutEvent`: all other players' cameras go dark; Lumi's does not
  - Share mechanic: `UseAbility` key for 5s shares Lumi's camera feed to one nearby player's secondary viewport. 60s cooldown.
- [ ] `AudioShockPassive.cs` (Passive):
  - Subscribes to `LoudNoiseMadeEvent` on GameEventBus
  - If volume above threshold AND event position within 20m: trigger paralysis
  - Paralysis: disable CharacterMovement, disable AbilitySystem, play freeze animation, 3s timer, re-enable
  - **Microphone trigger:** `MicrophoneAnalyzer.cs` (singleton) reads mic every frame; if RMS > threshold, fires `LoudNoiseMadeEvent` at Lumi's position with volume = RMS. This means real screams freeze Lumi.
- [ ] URP Night Vision Renderer Feature: custom ScriptableRendererFeature + blit shader. Green gamma boost, increased exposure. Only active on Lumi's camera layer.

**Phase 2 exit criteria:** Bobi smells things and gets stuck in doors. Lumi navigates blackouts and freezes when you yell into the mic. AbilitySystem cooldown shows in UI.

---

### Phase 3 — Remaining 5 Characters
**Duration:** 4–5 weeks (roughly one character per week, with synergy testing sessions)

Each character follows the same implementation pattern:
1. `CharacterStats_{Name}.asset`
2. Active ability script (extends `ActiveAbilityBase`)
3. Passive ability script (extends `PassiveAbilityBase`)
4. Comic flaw logic (event bus subscription or condition polling)
5. Voice lines wired to key moments
6. Synergy test session with previously implemented characters

---

#### 3.1 — Pompón

**Active — `WallListenAbility.cs`** (10s listen, no cooldown while not active):
- Requires player to be within 1m of a wall (check with SphereCast)
- Activates a `SoundPermeationZone` on the other side of the wall (same position, 10m radius)
- Captures all `SoundSource` events in that zone; routes their audio to Pompón's AudioSource at reduced volume
- Does NOT auto-share: player must describe what they hear on voice chat (by design)

**Passive — `HypersensitiveHearingPassive.cs`**:
- Pompón's `SoundDetector` has +40% radius over CharacterBase default
- He hears `SoundSource` events earlier; this fires an anxiety UI pulse (jittery crosshair for 0.5s)
- Counter-intuitively, hearing threats early increases his reaction window but also triggers more false-alarm responses

**Flaw — `DistractionListenFlaw.cs`**:
- If `WallListenAbility` is active and the zone contains only ambient/non-threat sounds, there is a 20% chance per second that Pompón's player gets a "WAIT I HEAR SOMETHING" screen notification and the ability auto-extends 3s
- The distraction text cycles through the voice line set ("hay algo que hace tic-tac...")

**Synergy tag:** `SynergyWith: Sabiucho` (Pompón detects, Sabiucho interprets)

---

#### 3.2 — Fotchi

**Active — `EvidencePhotoAbility.cs`** (CD: 15s after blindness clears):
- On activation: freeze frame (0.2s camera still), then spawn `FlashEffect` (full-screen white → fade 1s)
- Screenshot the camera render texture → encode as `EvidencePhoto` ScriptableObject
- If a creature is in frame: evidence value ×2. If invisible creature: first reveal it (disable invisibility material override)
- Fotchi's camera goes black for 3s (`CameraBlackout` shader param)
- All players within 5m radius: `LoudNoiseMadeEvent` with volume 0.9 (triggers Lumi)
- All players within 5m radius: 1s white screen overlay

**Passive — `FlashSharedPassive.cs`**:
- Wraps the flash effect logic above so it always affects nearby players even when ability is on cooldown
- Note: the flash IS the ability — there is no passive that fires separately. The shared effect is built into the active.

**Flaw — `PhotographicInstinctFlaw.cs`**:
- Subscribes to: `PlayerFell`, `TrapTriggered`, `CreatureSpotted`, `PlayerSlid` (Trotín) events
- When one fires within Fotchi's sight (dot product check): 25% chance to auto-fire `EvidencePhotoAbility`
- Auto-fire plays "instinto fotográfico" voice line before triggering

**Synergy tag:** `SynergyWith: Bobi` (Bobi detects, Fotchi photographs)

---

#### 3.3 — Sabiucho

**Active — `FieldAnalysisAbility.cs`** (CD: 15s):
- Player aims at any object, creature, or interactable (raycast 5m)
- 4s channel animation (Sabiucho points and scribbles)
- On complete: opens `AnalysisPanel` UI showing: object type, behavior description, weakness, interaction tip
- Data comes from `AnalysisDatabase.cs` (ScriptableObject lookup table keyed by object tag/type)

**Passive — `LivingLibraryPassive.cs`**:
- Subscribes to: any `TrapTrigger`, `CreatureAggro`, `ObjectBroken` event
- 2s before these events (requires predictive check — use trigger volumes with a "PreWarning" area before the danger zone), show `SabiuchoWarningBanner` in HUD for all players: "¡No hagan eso!" with Sabiucho's face
- If the event fires anyway (players ignored it): trigger `PlayerIgnoredWarningEvent` on event bus

**Flaw — `DramaticCryFlaw.cs`**:
- Subscribes to `PlayerIgnoredWarningEvent`
- On trigger: 20s cry mode — disable AbilitySystem, play cry SFX loop, show cry particle effect (huge tears), log to score screen "Sabiucho lloró X veces"
- Cry intensity scales with "how right he was": if the ignored warning led to a creature aggro, cry is 1.5× louder

**Synergy tag:** `SynergyWith: Pompon` / `WorstWith: Trotín, Coso`

---

#### 3.4 — Coso

**Active — `BruteForceAbility.cs`** (no cooldown, instantaneous):
- Player must face an `IBreakable` object (door, shelf, wall segment, machine)
- Coso runs into it: applies `AddForce` impulse 10× larger than any other character
- `IBreakable.Break(float force)`: each object has a `breakThreshold`; Coso exceeds it on everything
- Break spawns debris particles, plays loud crash SFX → fires `LoudNoiseMadeEvent` (volume 1.0) and `ObjectBrokenEvent`
- Unintended radius: collider is slightly larger than visuals — Coso breaks adjacent objects he "didn't mean to touch"

**Passive — `ClumzyHandsPassive.cs`** (30% break chance on interaction):
- Overrides `InteractionSystem.Interact()`: before calling base interact, roll RNG
- On 30% roll: call `IBreakable.Break()` on the target if it implements that interface (mechanisms, evidence containers)
- On 70%: normal interaction
- If a breakable was broken: fire `ObjectBrokenEvent`, Coso plays "ups" voice line

**Flaw — `ThrowInsteadOfHelpFlaw.cs`**:
- When Coso presses interact on a stunned/downed player (future mechanic: players can be temporarily downed):
  - 40% chance: normal revive
  - 60% chance: apply large upward + forward force to downed player (launches them), fire `PlayerLaunchedEvent`

**Synergy tag:** `WorstWith: Sabiucho, Lumi`

---

#### 3.5 — Mochín

**Active — `BigPackAbility.cs`** (passive inventory trait, no activation):
- `InventorySystem` max slots = 6 (overrides default 2)
- Items 1–3: no speed penalty
- Item 4: −15% speed. Item 5: −30%. Item 6: −45%
- Speed penalty computed in `CharacterMovement.UpdateSpeed()` based on inventory count

**Passive — `NarcolepsyPassive.cs`**:
- Timer counts seconds without player input (no move, no look, no interact)
- At 15s: trigger sleep sequence (gradual head-bob animation → eyes close → snore SFX loop)
- Snore SFX: played via `SoundSource` component (volume 0.7) → this fires `LoudNoiseMadeEvent` every 3s → creatures in range are alerted
- While asleep: disable CharacterMovement, disable AbilitySystem
- Wake condition: any `LoudNoiseMadeEvent` within 3m OR `PlayerLaunchedEvent` (Coso's flaw) → instant wake, jump 0.5m, play shocked SFX

**Flaw — `SpillOnSleepFlaw.cs`**:
- When sleep triggers: all items in inventory drop to world positions around Mochín (0.5m scatter radius)
- 10s timer starts: items glow red
- If a creature is within 5m when items drop, it picks one up and runs (item is lost)
- If players collect all items before timer: no loss

**Synergy tag:** `SynergyWith: everyone (when awake)` / `Crisis: Coso, Trotín`

---

### Phase 4 — Networking
**Duration:** 3–4 weeks  
**Goal:** 2–8 players in the same session, all mechanics synced.

#### 4.1 — NGO Setup
- [ ] `NetworkManager` GameObject with `UnityTransport` (default: UDP, port 7777)
- [ ] Configure `NetworkManager.NetworkPrefabs`: CharacterBase prefab (one per character skin, or one prefab with a skin index)
- [ ] Host/Client model: one player hosts (no dedicated server for now). Can upgrade to relay later via Unity Gaming Services

#### 4.2 — Player Spawning & Character Selection
- [ ] `LobbyManager.cs`: before map load, all players select a character (no duplicates allowed)
- [ ] `CharacterSelector.cs`: networked — when player selects, server confirms, broadcasts to all
- [ ] On scene load: `PlayerSpawner.cs` (server-side) spawns the correct character prefab at spawn point for each connected client

#### 4.3 — State Sync Per System

| System | Sync method | Notes |
|---|---|---|
| Position/Rotation | `NetworkTransform` | On CharacterBase |
| Inventory | `NetworkVariable<NetworkList<EvidenceItemNetworkData>>` | Compact struct per item |
| Ability cooldowns | `NetworkVariable<float>` | One per character, server authoritative |
| Passive states | `NetworkVariable<bool>` (e.g., isParalyzed, isSleeping, isCrying) | Drives local animations on all clients |
| Active flaw triggers | `ClientRpc` | Server detects condition, tells all clients to play effect |
| Evidence value | `NetworkVariable<int>` | Computed server-side, displayed client-side |
| Creature state | `NetworkBehaviour` on creature | Server runs AI, clients see results |

#### 4.4 — Lumi Voice Trigger (Special Case)
The microphone detection is intentionally **local-only** by design. The rule:
- `MicrophoneAnalyzer` runs on every client
- If it detects a loud sound on Lumi's client: it calls `LumiPassive.TriggerParalysis()` locally
- It also calls a `ServerRpc` → `PlayerParalyzedClientRpc` to sync the paralysis state to all clients (so they see Lumi freeze)
- This means: only Lumi's own player's microphone triggers her paralysis. If another player screams IRL, it doesn't affect Lumi on that client (the RPC call from their local mic detector only targets their own Lumi if they are playing Lumi)

#### 4.5 — Fotchi Flash Sync
- Flash visual is a `ClientRpc` broadcast when Fotchi fires the ability
- Server validates line-of-sight for each player in radius → sends `ApplyFlashBlindnessClientRpc` only to those players

#### 4.6 — Lobby Scene
- [ ] Main Menu → "Host Game" or "Join Game" (IP or Unity Relay code)
- [ ] Lobby scene: character grid (8 slots), player list, Ready button
- [ ] Character locked when selected; shown as taken to others
- [ ] Start button enabled when all players ready

---

### Phase 5 — Audio & Feel
**Duration:** 2 weeks  
**Goal:** The game sounds as funny as it plays.

#### 5.1 — Chipmunk Voice Processing
- [ ] `ChipmunkVoiceProcessor.cs`: real-time audio filter on each character's AudioSource
- [ ] Implementation: `AudioSource` + `AudioClip` for voice lines; apply pitch shift via `AudioSource.pitch` (1.6–2.2 range per character — Bobi is highest)
- [ ] Each character has a `VoiceLineLibrary.asset` (ScriptableObject): Dictionary<VoiceLineTrigger, AudioClip[]> (array for random pick)
- [ ] `CharacterAudio.cs` on each character: subscribes to events and plays correct voice line

#### 5.2 — Spatial Audio for Pompón
- [ ] Pompón's wall-listen audio: use `AudioMixer` with a "muffled" snapshot (low-pass filter + reverb) for sounds heard through walls
- [ ] When `WallListenAbility` active: routed through muffled mixer group
- [ ] When inactive: normal mix

#### 5.3 — Creature Audio
- [ ] Idle ambient (low, looping, spatial)
- [ ] Alert bark (short, directional cue for players)
- [ ] Chase music sting (creature-specific)
- [ ] Retreat sound
- [ ] All creature sounds feed into the global `SoundSource` system (so Pompón can hear them through walls)

#### 5.4 — Vivox Integration
- [ ] Add Vivox SDK; initialize on lobby join
- [ ] Positional audio mode: voice attenuates with distance
- [ ] Volume threshold hook: `MicrophoneAnalyzer` uses `Microphone.Start()` native Unity API, not Vivox audio buffer — they are independent pipes

#### 5.5 — UI Audio
- [ ] Cooldown pop sound when ability ready
- [ ] Inventory full sound
- [ ] Sabiucho warning banner plays a small "ahem" clip when appearing
- [ ] Score screen: fanfare or failure music based on score threshold

---

### Phase 6 — Content & Polish
**Duration:** 3–4 weeks

#### 6.1 — Map 1: "El Edificio Abandonado" (priority)
- 3 floors, stairwells, basement, exit on ground floor
- Narrow hallways (Bobi gets stuck), dark basement (Lumi shines), machinery room (Coso breaks things)
- Place traps: bear traps (visible, avoidable), pressure plates (invisible until stepped), falling shelf (triggered by Coso's radius)

#### 6.2 — Creature Variety (3 types minimum)
- **Sombrero** (default): patrols, chases on sight
- **El Dormilón**: sleeps unless disturbed by sound > 0.5 volume (irony: woken by Mochín's snores)
- **El Invisible**: can only be spotted by Bobi's smell or Fotchi's flash

#### 6.3 — Trap System Expansion
- Each trap: `TrapBase.cs` with `Arm()`, `Trigger(CharacterBase victim)`, `Reset()` methods
- Trap types: stun (3s), item-drop (drops one item), noise (fires loud `SoundSource`), launch (like Coso's throw — irony: Trotín can accidentally slide through and trigger it for everyone)

#### 6.4 — Mission Variety
Define 3 mission types that can be selected randomly per run:
1. **Evidence Hunt**: collect 5 specific items, reach extraction
2. **Creature Photo**: Fotchi must photograph a specific creature type (uses `EvidencePhotoAbility` tagging)
3. **Survival**: stay alive for 8 minutes, one creature actively hunts

#### 6.5 — UI Polish
- [ ] Health/status bar (Bobi's stuck indicator, Lumi's paralysis bar)
- [ ] Mini-map that shows Bobi's smell pings
- [ ] Inventory slots visible in HUD bottom-right (highlight when full)
- [ ] Sabiucho warning banner: slides in from left with character portrait
- [ ] Mochín sleep timer: small ZZZ counter in corner when stationary > 10s

#### 6.6 — VFX
- [ ] Trotín: skid marks on slide, smoke from feet when braking
- [ ] Bobi: smell particles (floating squiggly lines) radiating from him when ability active
- [ ] Lumi: eye-glow particle, green aura on camera during night vision
- [ ] Fotchi: camera flash light (Point Light burst), blinding screen overlay
- [ ] Sabiucho: oversized tears as particle system when crying
- [ ] Coso: debris particles on break, innocent shrug animation with star particles
- [ ] Mochín: Z particles floating over head during sleep

---

## 6. Character Implementation Order

```
Trotín   → tests movement physics (slide system)
Bobi     → tests detection framework + smell
Lumi     → tests URP renderer feature + mic detection
Mochín   → tests inventory extension + sleep mechanic
Pompón   → tests wall audio system
Fotchi   → tests flash effect + photo evidence
Sabiucho → tests warning system + analysis DB
Coso     → tests physics interactions + break system
```

**Synergy test checkpoints:**
- After Trotín + Bobi: test "Bobi finds something, Trotín sprints over and activates trap"
- After adding Lumi: test the Coso→Lumi paralysis chain (Coso breaks something loud, Lumi freezes)
- After Sabiucho: test the full chaos chain end-to-end
- After all 8: play a full 8-player session and log every funny incident

---

## 7. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Networking complexity delays polish | High | High | Build local-first; add net layer in Phase 4 so core mechanics are validated before sync |
| Microphone detection latency (Lumi) | Medium | Medium | Use `Microphone.Start()` with small buffer (256 samples); test on weak hardware early |
| 8 simultaneous characters is too many to balance | High | Medium | Implement in pairs, playtest every addition; ship with 4 if necessary |
| Trotín's slide interacts badly with networked physics | Medium | High | Test NGO + Rigidbody sync early in Phase 4 with Trotín first |
| Vivox licensing / cost at scale | Low | Medium | Use Unity Personal tier; monitor MAU as playerbase grows |
| Sabiucho's warning system fires too often → annoying | Medium | High | Add minimum 20s cooldown between `LivingLibraryPassive` banner appearances |
| Fotchi auto-photo fires constantly in large groups | Medium | Medium | Rate-limit `PhotographicInstinctFlaw` to max 1 auto-fire per 30s |

---

## 8. Milestone Summary

| Milestone | Phase End | Deliverable |
|---|---|---|
| M0 | End of week 2 | Two players in a room, both moving |
| M1 | End of week 5 | Completable game loop with Trotín, one creature, evidence |
| M2 | End of week 8 | Bobi + Lumi added, ability framework proven |
| M3 | End of week 13 | All 8 characters implemented, local co-op |
| M4 | End of week 17 | Online 8-player session works, lobby functional |
| M5 | End of week 19 | Audio complete, chipmunk voices, Vivox |
| M6 | End of week 23 | Map 1 finished, 3 creature types, 3 mission types |
| Ship candidate | ~Week 26 | Full polish pass, 2 maps, build profiles |

---

## 9. First Session Checklist (Do This Monday)

1. `git init` if not done → add Unity `.gitignore` → first commit
2. Install **Unity Netcode for GameObjects** (via Package Manager → Unity Registry → Netcode for GameObjects)
3. Create the `_Game/` folder structure inside `Assets/`
4. Set up `InputSystem_Actions.inputactions` with the Player action map
5. Write `InputReader.cs` (ScriptableObject, exposes C# events)
6. Write `CharacterBase.cs` + `CharacterMovement.cs` with a capsule in the sample scene
7. Confirm: capsule moves with keyboard, look with mouse, no errors
