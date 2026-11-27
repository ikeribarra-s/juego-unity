# Skull blink (player skeleton, alive and dead)

Status: implemented (see Deviations at the end). Inputs: `ArtSource/Characters/Animations/Blink/README.md` (asset), `Characters/_Base/FirstPersonBodyCull.cs`, `Characters/_Base/RetroAnimationStepper.cs`, `Characters/_Base/Death/*` (SkeletonDisassembler, BonePiece, LivingSkull), `Docs/Design/DeathFX.md`. Implementer: Sonnet. No Animator layer, no PlayerAnimator change.

## 1. Goal
- Skeleton players blink while alive: random gap 3-7 s, 20 % double blink, on the body's 24 fps retro steps.
- The local first-person player never sees their own lids. Other players and the test dummy see them.
- When the skeleton disassembles, the lids ride on the **skull piece** (rigid child, blend shapes intact) and keep blinking on their own clock.
- Comic extras: double blink when the skull lands, squeeze (both lids shut 0.15 s) on each roll, one-eye wink when the roll cooldown is ready again.

## 2. Assets and import (one-time, done by the editor menu in section 6)
Files are already in `ArtSource/Characters/Animations/Blink/`. Copy into `Assets/Art/Characters/Skeleton/`:
- `Skeleton_Lids.fbx` (the mesh; the only one needed). The two `@Blink` FBX files are NOT used at runtime because weights are driven from key tables (section 3).

Import settings for `Skeleton_Lids.fbx` (ModelImporter):
- Scale Factor **1.201179**, Use File Scale on (same as `Skeleton_Jaw.fbx`).
- Model: Import BlendShapes **on**, Blend Shape Normals **Calculate**, Read/Write **on**, material remap to `M_Skeleton`.
- Rig: **None** (no avatar, no Animator). Animation Type None.
- After import, check the SkinnedMeshRenderer has blend shapes named `Blink_L` and `Blink_R` (the wiring code resolves them by name and logs an error if missing).

## 3. Key tables (no clips at runtime)
Weights are Unity values 0-100, linear between keys, authored at 30 fps (from the README clips):
- `BlinkKeys` (8 frames, 0.233 s): `0, 45, 100, 100, 100, 70, 25, 0`
- `BlinkDoubleKeys` (13 frames, 0.400 s): `0, 50, 100, 100, 55, 35, 70, 100, 100, 70, 35, 10, 0`
- Wink uses `BlinkKeys` on one side only; the other side stays 0.
- Store the tables as `static readonly float[]` in `SkullBlink`. Sample with `frame = t * 30`, linear interpolation, no allocation.
- Verification (editor menu, log only): read `Skeleton_Lids@Blink.fbx` / `@BlinkDouble.fbx` (import them temporarily or read their clips from the Blender export) with `AnimationUtility.GetEditorCurve` on `blendShape.Blink_L` / `Blink_R`, compare to the tables within 1.0. If they differ, the Blender key tables win and the code tables are updated.

## 4. Scene placement (per skeleton model: SampleScene and TestScene, incl. the TestDummy)
Under the model child that has the Humanoid Animator (`Character/Skeleton`, and the TestDummy's skeleton model):
```
Skeleton (Animator, FootIK, CartoonAnimator, RetroAnimationStepper, FirstPersonBodyCull, ...)
 └── mixamorig:Hips ... mixamorig:Head
      ├── JawPivot                 (existing; localPos (0, 0.0114, -0.0281), rot identity)
      │    └── Skeleton_Jaw        (existing, local zero)
      └── LidPivot                 (NEW; sibling of JawPivot, NOT under it)
           └── Skeleton_Lids       (NEW; SkinnedMeshRenderer, local zero, FBX import rotation kept)
                + SkullBlink       (on this GameObject)
```
- `LidPivot` localPosition = `JawPivot` localPosition (0, 0.0114, -0.0281), localRotation identity, scale 1.
- `Skeleton_Lids` SMR: `shadowCastingMode = Off` (4 mm shell, no shadow flicker), `receiveShadows` on, `updateWhenOffscreen` on (closed lids must not cull at screen edges).
- No collider on the lids.

## 5. `SkullBlink` component (`Assets/_Game/Characters/_Base/SkullBlink.cs`, new)
`[DefaultExecutionOrder(70)]`, sits on the lids GameObject. Field defaults:

| Field | Default | Notes |
|---|---|---|
| `_lids` (SkinnedMeshRenderer) | auto: GetComponent in Awake | |
| `_stepper` (RetroAnimationStepper) | auto: `GetComponentInParent<RetroAnimationStepper>()` | null or inactive = own 24 Hz clock |
| `_fps` | 24 | used for the free clock; match the stepper's `Fps` |
| `_interval` (Vector2) | (3, 7) s | converted to ticks: `round(rand * _fps)` |
| `_doubleChance` | 0.2 | |
| `_squeezeTime` | 0.15 s | |
| `_landedDelay` | 0.3 s | delay after the skull lands before its double blink |
| `_enabledBlink` | true | debug toggle |

Internal state: `_tick` (int), `_nextBlinkTick`, `_seq` (float[] running table or null), `_seqTick`, `_seqSide` (0 both, 1 left, 2 right), `_squeezeUntil` (real time), `_idxL`, `_idxR` (blend shape indices, cached in Awake), `_lastL`, `_lastR` (last written; skip writes when unchanged), `_acc` (free clock accumulator), `_living` (LivingSkull), `_skull` (BonePiece), `_wasReady`, `_landedSeen`.

Tick clock (the core rule):
- Living: a tick happens in `LateUpdate` when `_stepper != null && _stepper.isActiveAndEnabled && _stepper.LastStepFrame == Time.frameCount`. One tick per retro step, so the blink advances on the body's steps.
- Detached (dead skull): `_stepper` is null. `_acc += Time.deltaTime; while (_acc >= 1f / _fps) { _acc -= 1f / _fps; Tick(); }` (at most 2 ticks per frame).
- On a tick: if no blink is running and `_tick >= _nextBlinkTick` and `_enabledBlink`, start a blink (`Random.value < _doubleChance` → double table). Sample the running table at `frame = _seqTick * 30f / _fps` and write weights; advance `_seqTick`. When `frame >= table.Length - 1`, finish: write 0 on both lids, `_nextBlinkTick = _tick + round(rand * _fps)`.
- Weights are written **only on ticks** (and once on finish), never per frame, except during squeeze (below). Writes skip when the value is unchanged.

Public API (main thread, no allocation):
- `void Blink()`: starts a single blink now (ignored if one is running).
- `void DoubleBlink()`: starts the double table now (ignored if running).
- `void Squeeze(float duration = 0.15f)`: both lids 100 for `duration` **real** seconds, written every frame in `LateUpdate`. Applied on the call frame, not stepped, because it reacts to a button press. Cancels a running blink.
- `void Wink(int side)`: side 0 = left, 1 = right, -1 = random. Single-lid `BlinkKeys`. Ignored while busy.
- `void Attach(LivingSkull living, BonePiece skull)`: called once by the disassembler (section 7b). Stores refs; `Rolled` subscription in `OnEnable`/`Attach` (see 7c).
- `void DetachForDeath()` and `void AttachToSkull(Transform skullPiece)`: section 7b.

Automatic triggers (polled in `Update`, edge detection):
- Landing: when `_skull.Landed` goes false → true (once, `_landedSeen`), schedule `DoubleBlink()` after `_landedDelay` real seconds.
- Ready: when `_living.RollReady` goes false → true and `_living.RollCount > 0`, call `Wink(-1)`. Note this uses `LivingSkull.RollReady` (public property, no change needed).

Renderer state: `_lids.SetBlendShapeWeight(_idxL, w)` and `_idxR` likewise. Awake sets both to 0 (open).

## 6. Editor menu (`Assets/Editor/SkullBlinkBuilder.cs`, new, re-runnable)
- `Fluffterror > Characters > Skull Blink > Setup Assets`: copy `Skeleton_Lids.fbx` into `Assets/Art/Characters/Skeleton/` if missing, apply the section 2 importer settings, reimport, log the blend shape names and run the table check (section 3).
- `Fluffterror > Characters > Skull Blink > Wire Open Scene`: for each Animator in the open scene whose hierarchy has `mixamorig:Head/JawPivot` and no `LidPivot` yet: create `LidPivot` (section 4 transform), instance the lids (`PrefabUtility.InstantiatePrefab` of the FBX main GameObject so the link survives), set SMR flags, add `SkullBlink` (auto-find refs). Idempotent. Log each model touched. Then `EditorSceneManager.MarkSceneDirty`; do not save unless asked, and log "save the scene".
- Do NOT run `TestSceneBuilder` (it rebuilds TestScene and would wipe hand edits).
- Refuse in Play mode (`EditorApplication.isPlaying`).
- If the `Character` root is a prefab instance, the menu must say which path it took: edit the prefab asset (preferred) or the scene instance with an override. Log it.

## 7. First-person cull and death hand-off

### 7a. `FirstPersonBodyCull.cs` (required change)
`BuildHeadMask()` computes each SMR's `mask` from bone weights. A bone-less SMR (`smr.bones.Length == 0`, which the lids are) gets all zeros, so the local player would see the lids from inside the skull. Change: add a branch before the SMR bone-weight loop: `if (r is SkinnedMeshRenderer s0 && s0.bones.Length == 0)` and `r.transform.IsChildOf(_maskRoot)` → fill the mask with 1 (same as the rigid-jaw branch), else fall through to the existing code. Nothing else changes.

### 7b. Death hand-off (`Characters/_Base/Death/SkeletonDisassembler.cs`, `Disassemble()`)
Two insertion points; the existing order stays.
1. **Before `HideBody(animator)`** (step 4). `HideBody` deactivates the animator GameObject, which would hide the lids, so detach first:
   `foreach (SkullBlink b in animator.GetComponentsInChildren<SkullBlink>(true)) b.DetachForDeath();`
   `DetachForDeath()`: `transform.SetParent(null, true)` (keeps world pose; stays active), `_stepper = null` (switches to the free 24 Hz clock), `_lids.SetPropertyBlock(null)` (clears FirstPersonBodyCull's `_HideHead` / `_CullRadius`, so the dead local player sees their own lids in the orbit camera).
2. **After the Skull piece and the LivingSkull exist** (end of step 5, next to `LivingSkull.Init`): for each detached blink, `blink.Attach(LivingSkull, Skull)` then `blink.AttachToSkull(Skull.transform)`.
   - `AttachToSkull(t)`: `transform.SetParent(t, true)`. Reset both weights to 0 first so the skull lands with open lids. Blink state cleared.
   - If `Skull == null` (rig without a skull group), `Destroy(gameObject)`. Log a warning.
- Why it works: the lid SMR is a rigid, boneless mesh, drawn with its own transform. Under the skull `MeshRenderer` piece it follows the Rigidbody exactly. No bake, so the blend shapes keep working.
- The skull piece pivot is the bounds centre (`BuildPiece`), not the hinge. The lid's local position is computed by `SetParent(…, true)`, so no manual offset.
- `HeldTorchLighting` keeps renderer references in `_saved`, so its restore still reaches the lids after the reparent.
- The lids have no collider, so `BonePiece` clack logic and `DeathFX` (wisp, eye light) are untouched.
- Blink ownership: the lids are on their own GameObject with `SkullBlink` on it, so the component keeps running after the model is deactivated. No timer on `CharacterVitals.Died` is needed; the detach happens in Disassemble.

### 7c. `LivingSkull.cs` (one hook)
Add `public event System.Action Rolled;` and invoke it at the end of `Roll(dir)` after `RollCount++`. `SkullBlink` subscribes in `Attach` (`living.Rolled += OnRolled`) and unsubscribes in `OnDestroy`. `OnRolled` → `Squeeze(_squeezeTime)`.

## 8. Remote players and TestDummy
- Remote living players: `FirstPersonBodyCull.Apply(false)` leaves the lids visible. Nothing extra.
- TestDummy: wired by the same menu (section 6), so it blinks and detaches like any skeleton on death. If its model has no `JawPivot`, log and skip.

## 9. Test plan (Play mode via UnityMCP; `ShotDir` / `ShotTour` for PNGs)
Optional dev probe `Assets/_Game/Testing/SkullBlinkProbe.cs` (not added to scenes). It logs per frame: lid weights L/R, `Time.frameCount`, `_stepper.LastStepFrame`.
1. **Intervals**: 2 min idle in TestScene. Closures every 3-7 s (mean about 5 s), about 1 in 5 is a double blink. Log the frame of each close.
2. **Step rule**: every weight change happens on a frame with `LastStepFrame == frameCount`, except the final write to 0. Changes on non-step frames: must be 0.
3. **Write budget**: `SetBlendShapeWeight` calls per second at most 24 plus about 2 per blink (counter in the probe).
4. **Tables**: one logged blink matches `BlinkKeys` at the tick times (±1).
5. **FP**: stand, look at the lantern and at the floor, take `ShotDir` PNGs. No lid visible over the sockets. Check the lids' mask copy: UV3.x = 1 everywhere.
6. **Third person / other player**: `RemoteLook` or `Apply(false)`; lids visible and closing.
7. **Death**: kill the player with `DeathTestSetup` / `DeathDebugKeys`. After Disassemble: the lids' parent is the `Bone_Skull` piece. Lid position relative to the skull is constant (±1 mm) while the skull rolls (sample 2 s).
8. **Dead skull blinks**: after landing, a double blink within about 0.5 s, then the normal 3-7 s timer. On Space roll: both weights 100 for 0.15 s (±0.02 s), then back to 0.
9. **Dead local orbit view**: lids visible (MPB cleared).
10. **Wink**: wait for cooldown ready; one lid closes for about 0.25 s, side random.
11. **Console**: `read_console` shows no errors or warnings from SkullBlink, FirstPersonBodyCull (mask warnings) or SkeletonDisassembler. Run TestScene and SampleScene.
12. **Allocation**: no GC allocation from the tick path (Profiler).

## 10. Risks and coordination
- **Another agent is editing the death code and both scenes right now.** `SkeletonDisassembler.cs`, `LivingSkull.cs`, `DeathFX*` and the scenes are in flight. Make the edits in 7b and 7c only after their commit, or rebase onto it first. The menu in section 6 writes into the scenes: run it only after their scene save lands. Never run `TestSceneBuilder`.
- Blend shape names and key tables: if the Blender asset changes, regenerate the tables; the check in section 3 catches a mismatch.
- The lids are 4 mm thick and drawn with the ToonOutline inverted hull, so a faint outline may show at distance. The README says they sit flush; accept this unless it reads badly in ShotDir.
- Stepper cadence: the stepper steps on an accumulator, so step frames are about 24 Hz, not exact. Blink timing counts ticks, so the gap jitters by at most one step. Acceptable.
- Rigs with no `JawPivot`: the menu skips them. Do not guess a hinge.
- Squeeze runs outside the stepper on purpose (section 5). If it reads too smooth against the 24 fps body, make it a 4-tick hold instead.

## 11. Deviations (as built)
- `Skeleton_Lids.fbx` with Rig None imports as MeshFilter + MeshRenderer (no SMR), so Wire Open Scene creates the SkinnedMeshRenderer itself (same mesh/materials, FBX root rotation kept, not a prefab instance of the FBX). `rootBone` = the lids GameObject (not LidPivot, which put the bounds in the wrong space and culled the lids).
- Blink gaps and sequence progress use real time (`Time.time`), not tick counts, so a low editor frame rate does not stretch them; writes still happen only on stepper ticks (alive) / the 24 Hz clock (dead).
- The disassembler's bake loop skips the lids (they ride the skull), `SkullBlink` sits on the SMR GameObject (RequireComponent SMR).
- The ready wink is polled from `LivingSkull.RollReady` in `SkullBlink.Update` (works for remote/dummy skulls; `LivingSkull`'s own ready tick is local-only).
- Table check ran in Setup Assets: clip vs code tables max error 0.00.

## 12. Blink sound (added 2026-11-27)
- Asset: `SD_Skull_Blink` (`Assets/_Game/Audio/SFX/Character/`), 3 clips `SFX_Character_SkullBlink_01..03`, about 45 ms, soft low-mid tic (centroid about 600 Hz), measured -36.0 LUFS, peak -24.6 to -24.9 dBFS. Preset `dark_skull_blink` in `sfxr.py`, row in `remaster_live.py` (seeds 29000-29002, relaxed style). SD: volume 0.5, pitch 0.92-1.08, spatial 1, 0.5-6 m, MaxVoices 2, MinInterval 0.08, Actions mixer.
- Plays once when the written weight first reaches >= 95 (stepped, so it matches the visuals). Double blink: tick on each closure, second at x0.8. Wink: one tick. Roll squeeze: none.
- Not played for the local player's own living blink (their lids are hidden in first person). Remote and dummy living skeletons and all dead skulls (including the owner's orbit view) play it.
- Field `_blinkSound` on `SkullBlink`, assigned by Setup Assets / Wire Open Scene (also in already wired models).

