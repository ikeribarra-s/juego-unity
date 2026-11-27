# Death FX (bone burst, skull afterlife)

Status: implemented 2026-11-27 (decisions in section 12 applied). Owner: VFX + Audio + Characters. Related: `Characters/_Base/CharacterVitals.cs`, `Characters/_Base/Death/*` (SkeletonDisassembler, BonePiece, BonePickup, LivingSkull), `Rendering/HorrorPostFX.cs`, `Rendering/VFX/AtmosphereFX.cs`, `Rendering/VFX/WaterSplash/` (pool pattern), `Audio/SoundManager.cs`, `Docs/ArtDirection.md` §3 (palette) and §9 (audio mix). Format follows `Docs/Design/WaterSplashFX.md`.

## 0. Pitch

When a skeleton dies it bursts into a clattering heap of bones with a comic "bwomp", and the dead player keeps going as a skull that rolls around the dungeon and yips at the team, which makes the death a running gag rather than a dead end.

## 1. Goals

- The death moment is loud for one beat and then quiet. One layered collapse, one dust puff, one chip spray. No second burst.
- The dead local player gets a clear, short "you died" feel (grain/chroma kick, colour drained), then a stable dead look that stays readable in the dark.
- The skull reads as a character: a faint teal wisp, a tiny eye glow, a yip when it rolls, a ready tick when it can roll again.
- Other players can tell who died and where the skull is by ear and by a glint, at distance, without a UI marker.
- Nothing per frame allocates, every burst goes through a pool, and the existing mix rules (§9) are kept. No death sound may measure louder than its role target (`impact` -20 LUFS for the collapse).

## 2. Player experience and timeline

`t` is seconds from `CharacterVitals.Kill`. Local = the player who died. Remote = everyone else.

| t (s) | Local (dead player) | Remote (team) | Owner class |
|---|---|---|---|
| 0.00 | Kill. Torch and grabbed item drop (existing). `SD_Death_Collapse` plays 3D at pelvis. Dust puff + chip spray at `BurstCenter`. `PulseGrain(0.6, 0.30)` + chroma kick. Dead look begins blending in. | Same collapse + puff + chips, 3D. Hear it through walls at up to 35 m. | `DeathFX.Burst`, `DeathFXLocal` |
| 0.00-0.50 | Pieces launch. Each piece that hits the floor clacks (`SD_BoneClack`, gated, capped). | Same clacks, positional. | `BonePiece` (existing) |
| 0.15 | `SD_Death_Sting` (2D, comic beat, only for the local player). | Nothing. | `DeathFX` |
| 0.60 | Camera orbit starts (existing `LivingSkull`). Dead look fully applied at 0.8 s. Wisp starts emitting. | The skull now glints: teal wisp, faint eye light. | `SkullWisp` |
| 0.60-8.00 | Skull roll cooldown, random 5-8 s (existing). Ready tick at the end. | Sees the skull glint, hears nothing until a roll. | `LivingSkull` |
| roll (Space) | `SD_Skull_Roll` (3D at skull) + `SD_Skull_Yip` 0.06 s later. Wisp puff (+4 particles). Eye light x2 for 0.15 s. | Same, positional. | `LivingSkull` -> `DeathFX.SkullRoll` |
| ready | `SD_Skull_Ready` (2D, once per cooldown). IMGUI line flashes its colour for 0.3 s. | Nothing. | `LivingSkull` |
| pick-up (E on a bone) | `SD_Bone_Pickup` replaces the bone clack for this bone. | Hears it at the bone (3D, 15 m). | `BonePickup` |

Skull-only ending: a skull that is never picked up keeps its wisp and light until the scene reloads.

## 3. Rules and tunables

All tunables are fields on a new `DeathFXProfile` ScriptableObject (`Characters/_Base/Death/DeathFXProfile.asset`), referenced by `SkeletonDisassembler` (one asset shared by all characters). Defaults are given in brackets.

### 3a. Sounds (`SoundDefinition`, see section 4 for recipes)

| Field | SD asset | Mixer | 3D min / max | Volume | MaxVoices / MinInterval |
|---|---|---|---|---|---|
| `_collapse` | `SD_Death_Collapse` | Actions | 2 / 35 m | 0.8 | 1 / 0.5 s |
| `_sting` | `SD_Death_Sting` | SFX (2D) | 2D | 0.5 | 1 / 2 s |
| `_skullRoll` | `SD_Skull_Roll` | Actions | 2 / 25 m | 0.7 | 1 / 0.5 s |
| `_skullYip` | `SD_Skull_Yip` | Actions | 2 / 20 m | 0.5 | 1 / 1.0 s |
| `_skullReady` | `SD_Skull_Ready` | SFX (2D) | 2D | 0.5 | 1 / 1.0 s |
| `_skullHum` (optional, off) | `SD_Skull_Hum` | Actions | 1 / 12 m | 0.4 | 1 per skull (loop) |
| `_bonePickup` | `SD_Bone_Pickup` | Actions | 2 / 15 m | 0.6 | 2 / 0.1 s |
| `_bonePiece` (existing) | `SD_BoneClack` | Actions | 2 / 20 m | 0.6 | **4 / 0.04 s** (new cap) |

Piece clack gating (BonePiece, existing numbers kept): `_impactSpeed` 1.6 m/s relative velocity, `_impactInterval` 0.25 s per piece. New: piece volume scales with impact, `volume = lerp(0.35, 1.0, clamp01((v - 1.6) / 6))`, so a feather-light hand ticks and a skull drop thunks. Requires a volume argument on `SoundManager.Play`; if none exists, add an overload `Play(def, pos, volumeScale)` (check `SoundManager.cs` first).

### 3b. Visual effects (pooled, section 5)

| Effect | Pool | Max alive | Culled beyond |
|---|---|---|---|
| `VFX_DeathDust` (bone-dust puff) | 3 | 3 | 30 m from `Camera.main` |
| `VFX_DeathChips` (bone chips) | 3 | 3 | 30 m |
| `VFX_SkullWisp` (per skull, child of skull piece) | 1 per skull | 12 particles | emission stops > 25 m |
| Skull eye light (`ShadowlessLight` point) | 1 per skull | 1 | off > 25 m (DynamicLightBudget also culls) |

Global budget in `DeathFX`: `_maxBurstsPerSecond` 4 (a burst = dust + chips, counted once; skull roll puff counts 0.5). Over budget: the puff is dropped, the sound never is.

### 3c. Post FX for the local player (`DeathFXLocal`, on the local `CharacterBase`)

Base values are read from `HorrorPostFX` at Awake (grain 0.45, vignette 0.45, saturation -20, post-exposure -0.25, bloom 0.28, chroma 0.18).

Kick (once, at death):
- `HorrorPostFX.PulseGrain(0.6, 0.30)` (existing).
- New `HorrorPostFX.PulseChromatic(0.55, 0.25)`: chroma rises from base to 0.55, back to base over 0.5 s (same coroutine shape as `GrainPulse`).

Dead look (blended by `SetDeathBlend(t)`, t 0 to 1 over `_deadBlendTime` 0.8 s, then held at 1):

| Value | Alive | Dead (t = 1) |
|---|---|---|
| Saturation | -20 | -60 |
| Post-exposure | -0.25 | -0.35 |
| Vignette | 0.45 | 0.60 |
| Bloom | 0.28 | 0.20 |
| Chroma | 0.18 | 0.22 |
| Grain | 0.45 | 0.60 |
| Colour filter | cold blue (existing) | unchanged |

Restore: `DeathFXLocal.Revive(duration)` (default 0.6 s) blends t back to 0. Scene reload (F10) recreates the Volume, so it needs no code. Remote players get no post FX. `OnDisable` sets t = 0 immediately.

Readability rule: the dead look may never drop the image below the alive minimum (vignette <= 0.6, grain <= 0.6). Check the dead screenshot in section 7.

### 3d. Skull rules

- Wisp: `SkullWisp` starts 0.6 s after death (when `LivingSkull` starts), fades in over 0.5 s. Emission 5/s; on roll, +4 particles at once.
- Eye light: `ShadowlessLight` point light at skull local (0, 0.02, 0.08) (front of the skull), colour `#7FD6C2` (pale teal), intensity 0.8, range 1.6 m, shadows off. On roll: intensity x2 for 0.15 s, then eases back over 0.3 s.
- Ready tick: when `LivingSkull.RollReady` becomes true, once. The IMGUI line flashes `#FFD98C` for 0.3 s (existing colour on ready, now also a flash).
- Hum (`_skullHum`, optional, default off): a 3D loop at the skull while the skull is rolling or within 12 m. Off by default because a dead team of 8 would stack 7 hums.

### 3e. Pick-up

`BonePickup.Interact` plays `_bonePickup` instead of the burst clack. Nothing else changes (inventory, glow, prompt).

## 4. Sound recipes

Rules for all: WAV PCM 16-bit mono, `SFX_<Category>_<Name>_<NN>.wav`, 3 takes minimum for repeated sounds (`SD_Skull_Roll` and `SD_BoneClack` get 4), `_pitchRange` 0.94-1.06 (clacks 0.90-1.10). Final pass `Tools/Audio/process_sfx.py process ... --style relaxed --role <role>` (22.05 kHz, LP 4.5 kHz, -3 dBTP). Import as the other `Character`/`Items` clips (Decompress On Load, ADPCM, Preload, Force To Mono). Every new file gets a row in `Tools/Audio/remaster_live.py` so a re-master does not reset its loudness.

| SD | Clip source (seeds) | Role / LUFS | Notes |
|---|---|---|---|
| `SD_Death_Collapse` (`Character/`) | Layer in Python (numpy sum, layer 2 offset 3 ms): (a) `sfxr.py dark_bone_clack --seed 26100/26101/26102 --darkness 0.35` (3 ticks, crunch), (b) new preset `dark_death_bwomp` seed 27000/27001/27002 (see below). Optional (a'): `gen.py sfx "dry skeleton bones collapsing, short cartoon rattle" --seconds 1.5` if the chip crunch is too thin. | `impact` -20 | 1.2 s max; the bwomp is the comic part, the ticks the crunch. |
| `SD_Death_Sting` (`UI/` or `Character/`) | `sfxr.py dark_ui_error --seed 24100 --darkness 0.35` (existing preset), 2 takes | `ui` -24 | 2D, 0.5 s max, quiet. A comic "oops", not an alarm. |
| `SD_Skull_Roll` (`Character/`) | `sfxr.py dark_bone_clack --seed 26200..26203 --darkness 0.35` (4 takes; tick-tumble = 2-3 quick ticks, see bone_clack pattern) | `action` -22 | 0.6 s max. Plays at skull on every roll. |
| `SD_Skull_Yip` (`Character/`) | new preset `dark_skull_yip` seeds 28000/28001/28002 (see below) | `action` -22 | 0.35 s max. Plays 0.06 s after the roll. |
| `SD_Skull_Ready` (`UI/`) | `sfxr.py dark_ui_hover --seed 22100 --darkness 0.35`, 1 take | `ui` -24 | 2D, 0.2 s max. A soft "ding" rather than a click. |
| `SD_Skull_Hum` (`Ambient/`, optional) | Stable Audio `gen.py sfx "faint hollow ghostly hum, dark, soft" --seconds 3` loop-cut to 3 s, LP 2.5 kHz | `ambient` -30 | Only if `_skullHum` is enabled. |
| `SD_Bone_Pickup` (`Items/`) | `sfxr.py dark_bone_clack --seed 26300 --darkness 0.35` (single tick, no tail), 2 takes | `action` -22 | 0.25 s max. |

New presets to add to `Tools/Audio/sfxr.py` (`PRESETS` dict + a `d_*` function; they follow the `dark_*` look: chip voices, dark 8-bit at `--darkness 0.35`, then relaxed):
- `dark_death_bwomp`: a square at 12.5 % duty sliding 220 to 55 Hz over 0.35 s with a 6 Hz vibrato (the air leaving a balloon), plus a 40 ms LFSR crackle at the start, decay 0.45 s. Gives the comic "deflate".
- `dark_skull_yip`: a 4-bit triangle sliding 520 to 880 Hz in 70 ms, two notes 60 ms apart, short LFSR chatter tail (2 ticks). Reads as a small yelp.

Mix sanity: all new clips after `process_sfx` must measure within 1 LU of their role target (AudioLevelMeter, 60 s scripted run). The collapse must stay under the Spike sting (-26) as heard at the listener, so the death is not the loudest event in a run.

Optional later (Phase 5 voices): a real chipmunk "yip" can replace `SD_Skull_Yip` without code change, same SD.

## 5. Visual effects (pixel style)

Sheet recipes follow WaterSplashFX §1: 4x4 grids, row-major, RGBA PNG, Point filter, no mips, uncompressed, Alpha Is Transparency OFF, <= 8 colours plus alpha 0, no pure black (shadow `#0D0F14`). Procedural generation in a sibling script `Tools/ComfyUI/make_death_sheets.py` (no ComfyUI needed; it may import `make_ember_sheets.extract` like `make_water_sheets.py`, with the `sys.path.insert` gotcha). Run: `C:\Users\redux\ComfyUI\.venv\Scripts\python.exe -I Tools/ComfyUI/make_death_sheets.py --out Assets/_Game/Rendering/VFX/Death [--preview <scratchpad>\death_preview.png]`.

Bone colours (3-4 values): `#4A4A44` shadow bone, `#8A8474` bone, `#C9C1A8` bone highlight (under 10 % of pixels), `#0D0F14` outline. Check against ArtDirection §3 before sign-off.

| Sheet | Grid / cell | Depicts | Used by |
|---|---|---|---|
| `BoneDust_Sheet.png` | 4x4, 16 px | Grey-beige puff. f0-3 a 2 px dot grows to a 6 px cloud; f4-10 cloud at peak, 1-2 px flecks drift out; f11-15 dissolves by Bayer dither to nothing | `VFX_DeathDust` |
| `BoneChip_Sheet.png` | 4x4, 8 px | Tiny bone chip (2x2 to 3x2 px, shard edge). f0-15 a tumbling chip: 4 rotation poses repeated, last 4 frames fade by dither | `VFX_DeathChips` |
| `SkullWisp_Sheet.png` | 4x4, 16 px | Pale teal wisp: 3 px bright head with a 2 px tail trailing down-left, slight loop. Additive, HDR x1.4 in material | `VFX_SkullWisp` |

Prefabs in `Assets/_Game/Rendering/VFX/Death/`, all World simulation space (except the wisp, which is local so it follows the skull), no shadows, no lights, no collision:

| Prefab | Emit | Life | Size | Motion | Max | Material |
|---|---|---|---|---|---|---|
| `VFX_DeathDust` | 10 at `BurstCenter` + (0, 0.4, 0), sphere r 0.3 m | 0.6-0.9 s | 0.25-0.40 m | speed 0.8-1.6 m/s outward, drag 2, gravity 0.5 | 16 | `BoneDust_Mat`, URP Particles/Unlit alpha, ZWrite off |
| `VFX_DeathChips` | 8 at `BurstCenter` + (0, 0.5, 0) | 0.5-0.8 s | 0.03-0.06 m | speed 2-4 m/s, cone up 60 deg, gravity 9.81, 0 bounce | 16 | `BoneChip_Mat`, alpha |
| `VFX_SkullWisp` | 5/s; +4 burst on roll | 0.9-1.3 s | 0.08-0.12 m | up 0.25 m/s, noise 0.15 m/s, no gravity | 12 | `SkullWisp_Mat`, URP Particles/Unlit **additive**, HDR x1.4 |

Scaling: `VFX_DeathDust` and `VFX_DeathChips` multiply count x1 on the local view, x0.7 on remote views (less clutter, same beat). Chips are skipped when the burst is over budget.

Pools: `DeathFX` holds 3 dust and 3 chips systems, round-robin takeover when busy (like `AtmosphereFX._splashPool`). Skull wisp: one `ParticleSystem` child per skull, built in `SkullWisp.Awake` (`Instantiate` once, no runtime creation after).

Layering: the dust and chips render after the bone meshes (queue 3000 alpha). The wisp is additive and sits in the transparent queue 3010, after the water particles (same ordering rule as WaterSplash).

## 6. Code changes

Existing files (minimal edits):
1. **`Death/SkeletonDisassembler.cs`**: new `[SerializeField] DeathFXProfile _fx`. In `Disassemble()`: after `BurstCenter` is known, call `DeathFX.Instance?.Burst(BurstCenter, _fx, isLocal: localCam != null)`; replace the `SD_BoneClack` collapse with `_fx._collapse`; pass `_fx._bonePickup` to `BonePickup.Init` and `_fx._skullRoll` / `_fx._skullYip` to `LivingSkull.Init` (new overload); `_boneSound` stays as the piece clack. Add `SkullWisp` to the skull piece (all skulls), `DeathFXLocal` to the local character (when `localCam != null`).
2. **`Death/BonePiece.cs`**: volume scaling on impact (section 3a). Impact sound remains `SD_BoneClack`.
3. **`Death/BonePickup.cs`**: `Init(SoundDefinition pickup)` now receives `_fx._bonePickup`. No other change.
4. **`Death/LivingSkull.cs`**: new `Init` overload with `DeathFXProfile`; in `Roll`: `DeathFX.Instance.SkullRoll(transform.position, yipDelay 0.06f)` (owns the roll, yip, puff, wisp burst, eye-light pulse). Track `_wasReady`; when `RollReady` goes from false to true, `DeathFX.Instance.SkullReady()` (2D, local only: `enabled` owner check) and set a flash timer read by `OnGUI`. Cooldown and roll code unchanged.
5. **`Rendering/HorrorPostFX.cs`**: add `public static HorrorPostFX Instance` (set in Awake, cleared in OnDestroy), `public void PulseChromatic(float peak, float duration)` (mirror of `PulseGrain`), `public void SetDeathBlend(float t)` (lerps the look from cached base values to the dead targets in section 3c through the existing `ApplyLook` path; no per-frame allocation: cache the `Color` and the struct values).
6. **`Audio/SoundManager.cs`**: only if missing, `Play(def, pos, volumeScale)` overload. Verify `MaxVoices` / `MinInterval` are enforced for these SDs (they are, per §9).
7. **`Characters/_Base/CharacterBase.cs`**: no change. `DeathFXLocal` is added by `SkeletonDisassembler` at death (not Awake), so non-dying characters never carry it.

New files:
8. **`Characters/_Base/Death/DeathFXProfile.cs`** (ScriptableObject): the SD fields and tunables of section 3, `[CreateAssetMenu(menuName = "Fluffterror/Death/Death FX Profile")]`. Asset: `Death/DeathFX_Profile.asset`.
9. **`Characters/_Base/Death/DeathFX.cs`** (MonoBehaviour on `GameManager` in SampleScene + TestScene, static `Instance`): pools for dust and chips, `Burst(pos, fx, isLocal)`, `SkullRoll(pos, fx, yipDelay)`, `SkullReady(fx)`, global budget (`_maxBurstsPerSecond` 4), distance cull to `Camera.main`. Allocation-free: `EmitParams` reused, no LINQ, no string building per call.
10. **`Characters/_Base/Death/DeathFXLocal.cs`**: added to the local dead character. In `Update`: advances the blend t toward 1 over `_deadBlendTime` (0.8 s), calls `HorrorPostFX.Instance.SetDeathBlend(t)`; kick via `PulseGrain` + `PulseChromatic` in `Start`. `Revive(duration)`; `OnDisable` sets t = 0.
11. **`Characters/_Base/Death/SkullWisp.cs`**: on the skull piece. Owns the wisp `ParticleSystem` child and the `ShadowlessLight` eye light (`Rendering/ShadowlessLight.cs` marker so DynamicLightBudget never shadows it). Fades in from `LivingSkull` start, stops emission beyond 25 m, `Burst(n)` for rolls, `PulseLight()` for rolls.

Editor:
12. **`Assets/Editor/DeathFXBuilder.cs`**: menu **Fluffterror > VFX > Build Death FX**. Creates the three materials, the three prefabs (section 5), `DeathFX_Profile.asset` wired to the SD assets, adds `DeathFX` to `GameManager` in SampleScene and TestScene. Re-runnable, idempotent. Needs the sheets first (falls back to `AtmoMote_Sheet.png` for dust and chips if the death sheets are missing, as WaterSplash does).

Tools:
13. `Tools/Audio/sfxr.py`: add `d_death_bwomp`, `d_skull_yip` + PRESETS rows (section 4).
14. `Tools/Audio/remaster_live.py`: manifest rows for each new SD clip.
15. `Tools/ComfyUI/make_death_sheets.py`: procedural sheets (section 5).

No changes to `CharacterVitals` (its `Died` event has no arguments; the disassembler already has everything the FX needs).

## 7. Multiplayer notes (Phase 4, NGO)

- **Authority**: the server owns `CharacterVitals.Kill`. The burst, the skull roll and the pick-up are decided on the server and replicated. All SFX, VFX and post FX are **client-only**, triggered by the replicated event.
- **Replicated event**: `DeathEvent(ownerId, burstPos, seed)` (reliable, once per death). Each client runs `SkeletonDisassembler.Disassemble` locally with `Random.InitState(seed)` so bone shapes and launches match; physics is client-side for the cosmetic pieces (about 15 bodies, cheap). The skull and any bone a player picks up are networked objects (NetworkTransform or NetworkRigidbody) because pick-up and roll need one owner.
- **Roll**: the owner's Space sends a ServerRpc; the server applies the impulse to the skull's rigidbody and replies with an unreliable `SkullRolled(dir, serverTime)` event. Every client (owner included) plays `SkullRoll` + yip from that event, so the sound is in step with the visible hop, not with the input.
- **Ready tick**: cooldown is server time. The owner's `LivingSkull` gets `ReadyAt` from the server; the tick is local (2D) and only for the owner. Remote players never hear it.
- **Pick-up**: the server arbitrates ownership (first E wins). The loser's prompt says "taken" and no sound plays. The winner's `SD_Bone_Pickup` is local; others hear the bone disappear through the server event.
- **Distance caps**: client-side; every client culls with its own `Camera.main` (no per-client network traffic). A remote player more than 35 m away hears nothing.
- **Event hooks for other systems** (no behaviour now, see section 8): `DeathFX` raises `Burst` / `SkullRolled` events with position and loudness; Lumi, Pompón and Fotchi subscribe later. Keep them as C# events on `DeathFX`, not in the sound path.
- **Dead player camera**: remains local. A Phase 4 spectator mode can reuse `DeathFXLocal` unchanged.

## 8. Flaw interactions (roster)

- **Mochín (falls asleep idle > 15 s, drops all items)**: a sleeping Mochín who carries bones drops them all at once. Six bones hitting the floor together is the piece clack cascade, capped by `SD_BoneClack` (4 voices, 0.04 s), so it sounds like a bag of bones tipped over, not a wall of noise.
- **Lumi (paralysed by loud sounds)**: `DeathFX.Burst` raises `Loud(pos, 35 m)`. A future Lumi system freezes her when she is within range. The collapse is the loudest thing in a run, so it is the natural trigger for the paralysis gag. Not implemented here.
- **Pompón (hears through walls, false alarms)**: the collapse is heard through walls by design (3D, 35 m, no occlusion). A Pompón hearing a death through a wall can raise a false alarm. Hook only: `DeathFX.Burst` carries `isAlarming` = true.
- **Fotchi (auto-camera on dramatic moments)**: the burst is the dramatic moment. `DeathFX.Burst` raises `DramaticMoment(pos)`; Fotchi's camera snaps the dead body mid-burst. The photo is the best evidence of the run.
- **Coso (30 % chance to break anything he touches)**: Coso who kicks a rolled skull has a 30 % chance to split it. Hook: `SkullRolled` carries the roll direction; the split is a later feature (out of scope).
- **Bobi (stuck in doors)**: a rolled skull that wedges in a doorway is a comic gag for the team. No code; the wisp light makes the stuck skull visible through the door.
- **Sabiucho (cries if warnings are ignored)**: a dead Sabiucho's skull yips on every roll, the yip is the cry. Not extra code; the yip is the same SD.

## 9. Implementation plan order

1. Audio first: write the two presets, render the SDs, add them to the remaster manifest, verify LUFS (section 4). No Unity needed.
2. Sheets: `make_death_sheets.py` (procedural), check the preview.
3. Unity: `DeathFXProfile`, `DeathFX`, `DeathFXLocal`, `SkullWisp`, `HorrorPostFX` additions, then the editor builder.
4. Wire `SkeletonDisassembler`, `BonePiece`, `BonePickup`, `LivingSkull`.
5. Test (section 10).

Estimated size: one implementation session for a mid-tier model. The riskiest part is the post FX blend (must not leave the screen dead after a revive) and the piece-clack cap.

## 10. Assets needed

- Sounds: `SD_Death_Collapse`, `SD_Death_Sting`, `SD_Skull_Roll`, `SD_Skull_Yip`, `SD_Skull_Ready`, `SD_Bone_Pickup`; optional `SD_Skull_Hum`. Clips per section 4.
- Sheets: `BoneDust_Sheet.png`, `BoneChip_Sheet.png`, `SkullWisp_Sheet.png` (4x4), in `Assets/_Game/Rendering/VFX/Death/`. Sources: procedural.
- Materials: `BoneDust_Mat`, `BoneChip_Mat` (URP Particles/Unlit alpha), `SkullWisp_Mat` (URP Particles/Unlit additive, HDR).
- Prefabs: `VFX_DeathDust`, `VFX_DeathChips`; `VFX_SkullWisp` is built at runtime by `SkullWisp` from its material (or as a prefab for consistency, implementer's choice).
- Data: `Death/DeathFX_Profile.asset`.
- Shader: none. Reuses HorrorPostFX and ToonOutline.
- Animation: none. The death uses the existing bake; skull roll uses rigidbody.

## 11. Test plan (Play mode, TestScene)

Use the existing dev keys: K (kill looked-at), F9 (kill self), F10 (reload).

1. **Audio levels**: `AudioLevelMeter` on the listener during a scripted death of 4 characters in 3 s. Collapse within -20 LU ± 1 at the listener; the sting and clacks stay under the collapse; the 4 bodies never produce more than 4 clacks in the same 0.04 s window (log `SoundManager` drops).
2. **Timing**: log `DeathFX.Burst` time and the `SD` start time. Collapse, dust and chips must start in the same frame (+-1 frame). Sting at 0.15 s +-0.02. Roll: yip starts 0.06 s after the roll (+-0.02). Ready tick once per cooldown (5-8 s).
3. **Post FX**: sample `HorrorPostFX` values at t = 0, 0.4, 0.8, 1.6 s after F9. Kick peaks by 0.1 s, dead look reaches the table values at 0.8 s and holds. `Revive(0.6)` returns to alive values within 0.7 s (calls it from DeathDebugKeys in a dev build). F10 leaves no residual look.
4. **Readability**: screenshots: dead local view at 0.3 s (kick) and 3 s (held), skull in the dark TestScene tunnel and in the SampleScene dungeon at 2 m and 10 m. The skull silhouette must be visible; the wisp must read as a glow, not a bloom blob.
5. **Remote view**: a second camera at 15 m and 30 m sees the dust, hears the collapse at 15 m and not at 40 m. Wisp stops emitting beyond 25 m.
6. **Perf**: `PerfSpotSampler` at the TestScene spot with 8 bursts in 4 s (budget 4/s, so 4 bursts render and 4 are dropped) and 8 skull wisps. No growth in draws or particle count over 60 s. Profiler: zero GC allocations per frame after the first burst (Burst and SkullRoll use pools; no LINQ, no string formatting per frame).
7. **Pool takeover**: 5 deaths in 1 s: the 3 dust and 3 chip systems are reused round-robin, no `Instantiate` after `Start`.
8. **Pick-up**: E on a bone plays `SD_Bone_Pickup` once, the bone clack does not play on pick-up.

## 12. Open questions (human decision)

1. Is the comic sting (`SD_Death_Sting`, local 2D, 0.15 s after the collapse) wanted, or is the collapse enough?
2. Dead look: is a permanently desaturated, darker screen acceptable for a dead player who may spend 10+ minutes as a skull? Alternative: keep the kick and a mild dead look (saturation -35, vignette 0.5).
3. Skull hum (`_skullHum`): on or off? It is the most "ghostly" element but stacks with a team of 8.
4. Yip versus Phase 5 chipmunk voices: keep the chip yip as the final skull voice, or wait for voice work?
5. Bone-clack cap (4 voices) may hide a dramatic pile-up. Is a louder single "heap" thud (one SD, fired once per burst at the pile centre) preferred over many small clacks?
6. Should the pick-up glow and the ready tick be audible to the whole team (3D) rather than local only?

### Decisions (2026-11-27)

1. Keep the sting. 2. MILD dead look (saturation -35, vignette 0.5, exposure -0.3, bloom 0.2, chroma 0.22, grain 0.5) + the short kick. 3. Skull hum implemented, default OFF (no clip yet). 4. Keep the chip yip. 5. Keep the per-bone clacks with the 4-voice cap, no heap thud. 6. Bone pick-up sound 3D; ready tick local 2D only.
Deviations: `DeathFX.BurstAt` / `SkullRoll(pos, dir, wisp, fx)` instead of `Burst`; the budget is a 1 s sliding window; `SetDeadLook` added to `HorrorPostFX` so values come from the profile; `SoundManager.Play(def, pos, volumeScale)` added.
