# Water Splash FX (pixel-art water particles)

Status: spec, not implemented. Owner: VFX. Related: `Environment/Chunks/WaterVolume.cs`, `Audio/CharacterAudio.cs`, `Rendering/VFX/AtmosphereFX.cs`, `Docs/ArtDirection.md` §3 (palette) and §9 (water audio).

## 0. Scope and decisions

- Water exists only as shallow `VOL_Water*` boxes in `CH_SP_FloodedCrypt` / `CH_SP_SewerChannel` (top = surface). There is **no swimming**, so "submerge" means *falling or jumping into a volume*, not deep-water physics.
- **Blend = Alpha (SrcAlpha / OneMinusSrcAlpha), Transparent queue, ZWrite off, HDR off.** Reason: water is not emissive. Additive on a dark scene reads as light/fire and HorrorPostFX bloom would make spray glow. Alpha keeps spray murky and opaque on the dark background. Dark outline pixels stay opaque, so the sprite keeps its silhouette.
- **Pixel rules** match the torch embers: Point filter, no mips, uncompressed, Alpha Is Transparency **OFF** (real straight alpha). Dissolve by dropping pixels with a Bayer ordered pattern, never by partial alpha.
- **Palette** (from ArtDirection §3, shadow `#0D0F14` is the darkest value, never pure black):
  - `#0D0F14` outline / shadow
  - `#1E3A3F` deep teal
  - `#3B5A5E` murky water
  - `#6E8A8C` grey-blue
  - `#B9CBCB` foam highlight (under 10 % of pixels)
- Splash VFX is independent of audio. Only the foot-contact timing is read from the existing animation event.

## 1. Art (work package A)

Grid is the interface with Unity: 4x4 cells, 16 frames, row-major, frame 0 first. Frame size is the pixel size.

| File | Grid | Cell | Depicts | Used by |
|---|---|---|---|---|
| `WaterSplash_Sheet.png` | 4x4 | 16 px (64x64) | Crown splash. f0-3 small dome of droplets rising; f4-9 crown peaks, 1-2 px droplets arc out; f10-15 droplets fall, shrink to 1 px specks | VFX_WaterStep, VFX_WaterEnter |
| `WaterRipple_Sheet.png` | 4x4 | 16 px (64x64) | Flat ring on water. Radius 2 px to 7 px over 16 frames, outline 2 px thins to 1 px, foam arc on the upper-left, last 4 frames dither out | VFX_WaterRipple |
| `WaterDrop_Sheet.png` | 4x4 | 8 px (32x32) | Droplet. f0-8 teardrop 2x3 px falling/stretching; f9-12 splat 3x1 px; f13-15 gone. One foam pixel top-left | VFX_WaterDrips |

Generation (sources kept in `Tools/ComfyUI/sources/water_splash/`):

1. Only the splash silhouette uses ComfyUI. Ripple and drop are procedural (same as `make_atmo_sheets.py`), so they do not need SDXL.
2. Splash source (cwd `Tools/ComfyUI/`, server running per CLAUDE.md):
   `python gen.py workflows/sdxl_fx_black.json "a single crown-shaped splash of murky water, droplets flung upward, ring of spray, flat dark background" --out sources/water_splash --name splash --seed 7100 --count 3`
   Keep the best of 7100-7102. If `gen.py` does not resolve the workflow path from its folder, use `Tools/ComfyUI/workflows/sdxl_fx_black.json`.
3. Optional droplet source: `... "a single falling water droplet, glossy, small, isolated, flat dark background" --name drop --seed 7300 --count 2`. Not required.
4. **Sheet script: a sibling `Tools/ComfyUI/make_water_sheets.py`, not a palette flag on `make_ember_sheets.py`.** The ember script builds additive sheets (black = transparent, luminance = light). Water needs alpha from coverage plus an opaque outline colour. The sibling reuses `make_ember_sheets.extract()` (connected-component harvest) and its own ramp and alpha rule.
   - Gotcha: the sibling must `sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))` before `import make_ember_sheets`, because `-I` strips the script directory from `sys.path`.
   - Run: `C:\Users\redux\ComfyUI\.venv\Scripts\python.exe -I Tools/ComfyUI/make_water_sheets.py --out Assets/_Game/Rendering/VFX/WaterSplash [--splash-src Tools/ComfyUI/sources/water_splash/<kept>.png ...] [--preview <scratchpad>\water_preview.png] [--seed 7]`
   - Without `--splash-src` it draws the crown procedurally (like `AtmoSplash`), so A is never blocked on ComfyUI. Passing the source later swaps the silhouette and keeps the file names and grids.
5. Acceptance: three RGBA PNGs at the grids above, at most 8 distinct colours including alpha 0, no `#000000`, preview PNG checked by eye (enlarged x8).

Fallback placeholders for B (same grids): `AtmoSplash_Sheet.png` (4x4, 16 px) for splash and ripple, `AtmoMote_Sheet.png` (4x4, 8 px) for drops.

## 2. Particle systems (prefabs in `Assets/_Game/Rendering/VFX/WaterSplash/`)

All: Simulation Space **World** unless noted, Renderer material `WaterSplash_Mat` / `WaterRipple_Mat` / `WaterDrop_Mat` (URP Particles/Unlit, Surface Transparent, Alpha blend, ZWrite off), no shadows, no lights, no collision, Texture Sheet Animation 4x4 over lifetime. Spawn point is **the water surface** (`WaterVolume.SurfaceY + 0.02`), never the foot.

| Prefab | Sheet | Emission (driven from script) | Life | Size | Motion | Max | Render |
|---|---|---|---|---|---|---|---|
| `VFX_WaterStep` | WaterSplash | `Emit(n)` per foot contact: walk 5, sprint 9, crouch 3 | 0.35-0.55 s | 0.05-0.09 m | Circle shape r 0.12 m, emit from edge, speed 0.6-1.0 m/s outward, gravity 1.2, +0.6-1.4 m/s up | 48 | Billboard, View align |
| `VFX_WaterEnter` | WaterSplash | `Emit(n)` once per entry, n = lerp(14, 60, k) | 0.7-1.1 s | 0.14-0.22 m | Circle r 0.35 m, speed lerp(2, 3.5) x lerp(0.6, 1.6, k), gravity 2.2 | 96 | Billboard, View align |
| `VFX_WaterRipple` | WaterRipple | `Emit(1)` with `EmitParams.rotation3D = (90,0,0)` (lies flat), size per call | 0.6 s | 0.45 m walk / 1.6 m entry (set per emit) | No speed, no gravity; alpha 0.85 to 0 over last 30 % (Color over Lifetime) | 12 | **Mesh Quad, Render Alignment World**, `+0.02` Y lift |
| `VFX_WaterDrips` | WaterDrop | `Emit(1)` every 0.14 s for 1.2 s after leaving water | 0.45-0.7 s | 0.035-0.05 m | Spawn at body, random 0.3-1.2 m above feet, radius 0.25 m; speed 0, gravity 9.81 | 16 | Billboard, View align |

Scaling rules (applied through `main.startSizeMultiplier`, `Emit` counts, and speed multipliers):
- Sprint: counts x1.6, size x1.25, speed x1.3. Crouch: counts x0.5, size x0.7.
- Entry intensity `k` (see section 3): 0.15 base for walking in, up to 1.0 at about 9 m/s impact.

## 3. Code changes

Files:
1. **`Assets/_Game/Audio/CharacterAudio.cs`** (minimal): add `public event System.Action FootContact;`. Raise it at the top of `AnimationFootstep()` before the grounded/speed early-returns, and in the fallback `TriggerStep()`. This is the only audio change.
2. **New `Assets/_Game/Rendering/VFX/WaterSplash/WaterSplashEmitter.cs`**: per character, auto-added by `CharacterBase.Awake` (same pattern as `TorchCarrier`/`PhysicsGrabber`). Responsibilities:
   - Subscribes to `CharacterAudio.FootContact`. On contact, if grounded, horizontal speed >= 0.3 m/s, and `WaterVolume.TryFind(transform.position)` is non-null, it requests a step splash (min 0.18 s between splashes per character, matching CharacterAudio's cooldown).
   - Every frame: tracks `_inWater` with `WaterVolume.TryFind`. **Enter** (false to true): `k = clamp01((impact - 2) / 7)` where `impact = max(0, -prevVelocity.y)` from last frame's `CharacterController.velocity` (walking in gives k = 0.15). Request the entry splash plus a 1.6 m ripple at the surface. **Exit** (true to false): start a 1.2 s drip timer.
   - Wading ripples: while `_inWater` and moving at >= 0.3 m/s, emit a 0.45 m ripple at each foot's XZ every 0.4 s (walk) or 0.25 s (sprint). Idle = no ripples.
   - Skips if dead (`CharacterVitals`) and if the character is more than 30 m from `Camera.main` (distance cull).
   - Works on remote players too: it only reads `transform` and `CharacterAudio` events, so Phase 4 replication needs no change.
3. **New `WaterSplashFX.cs`** (MonoBehaviour on `GameManager` in SampleScene + TestScene; static `Instance` like `AtmosphereFX`). Holds pools of prefab instances: step 8, enter 2, ripple 12, drips 4. Round-robin takeover when busy (same as `AtmosphereFX._splashPool`). API: `Step(surfacePos, mulCount, mulSize, mulSpeed)`, `Enter(surfacePos, k)`, `Ripple(pos, size)`, `Drips(origin)`.
   - **Budget**: global cap of 10 new bursts per second. Requests above the cap are dropped (not queued).
   - Why not extend `AtmosphereFX`: it is anchored to the listener and culls by its distance. Water needs per-character anchors. Same pooling pattern, separate class.
4. **`CharacterBase.Awake`**: add `WaterSplashEmitter` if missing.
5. **No changes** to `WaterVolume`, `AnimationAudioSync`, or the existing `VFX_DripSplash` (atmosphere drips stay separate).

Rules summary: only characters inside a `WaterVolume` produce water FX; per-character cooldowns (step 0.18 s, ripple 0.25-0.4 s); global cap 10 bursts/s; no FX for remote characters beyond the distance cull and budget.

## 4. Unity work (work package B) and builder

Builder: `Assets/Editor/WaterSplashFXBuilder.cs`, menu **Fluffterror > VFX > Build Water Splash**. Re-runnable, idempotent. It does not need UnityMCP (the server is currently down with ECONNREFUSED), so it can run from the Editor menu.

The builder:
1. Sets sheet import settings on the three PNGs: Point, no mips, uncompressed, Alpha Is Transparency off, clamp, `Read/Write` off. Placeholders are copied from the Atmo sheets if the water sheets are missing.
2. Creates `WaterSplash_Mat`, `WaterRipple_Mat`, `WaterDrop_Mat` from `Universal Render Pipeline/Particles/Unlit` with `_Surface=1`, `_Blend=0`, `_SrcBlend=5`, `_DstBlend=10`, `_ZWrite=0`, render queue 3000.
3. Builds the four prefabs with the parameters in section 2, one ParticleSystem each, and sets `scalingMode = Hierarchy`.
4. Adds a `WaterSplashFX` component to `GameManager` in SampleScene and TestScene, wires the four prefabs, and sets pool sizes.
5. **TestScene water pit**: `TestSceneBuilder` gets a 3 x 3 m pit (`VOL_Water` box, 0.4 m deep, `WaterVolume` added, top at the surface) on the South side of TestArea, plus a 2 m raised ledge for the jump-in test.

Prefabs and sheets live in `Assets/_Game/Rendering/VFX/WaterSplash/`. Generated sources stay in `Tools/ComfyUI/sources/water_splash/`.

## 5. Verification

Scene: **TestScene** (after the water pit exists). SampleScene for the real chunks: generate a seed with `CH_SP_FloodedCrypt` / `CH_SP_SewerChannel`, or use `LivingMapDebugTools > TopDown` to find one.

1. Walk: from the dry edge, walk into the pit at walk, sprint (Shift), and crouch (C). Expect a crown per foot, sprint bigger and more droplets, crouch smaller. Ripples visible at the feet.
2. Enter: drop in from the 2 m ledge (jump) and from a standing step. Expect a larger splash and a 1.6 m ring for the jump, and a small splash for the step.
3. Exit: walk out. Droplets fall for about 1.2 s from the body.
4. Screenshots (ShotTour or Game view): side view of a step splash at 3 frames; entry at +0.1, +0.3, +0.6 s; top-down ripples (`TopDown`); drips at +0.3 and +0.8 s after exit. Check: no pure black, pixels read as chunky, no bloom glow.
5. Perf: `PerfSpotSampler` at the pit: draws and particle counts must stay flat over 60 s of walking; no per-frame allocations (Profiler). Check the budget log, which should show no more than 10 bursts/s.
6. Audio check: the existing water step and enter sounds still fire, once each, with the new event added.

## 6. Risks

- **Water surface may be invisible.** VOL_Water might be only a trigger box with no rendered plane. Check the chunk prefab renderers before B. If no plane exists, ripples float on nothing; the fix is a flat translucent water quad from the builder, not a change to the splash.
- **Sorting** of alpha particles against the transparent water plane. Ripples lift +0.02 m and ZWrite is off, so z-fighting is unlikely; check in the first screenshot.
- **`-I` strips the script directory** from `sys.path`. Handled by the sys.path insert in `make_water_sheets.py`.
- **SDXL silhouettes on black** may come out soft. The procedural path is the fallback and is not blocked by ComfyUI.
- **Pool/culling in 8-player co-op**: 8 characters x 2 ripples/s could hit the cap. Ripples are the first thing to drop when the budget is full.
- **Event order**: the emitter reads `CharacterController.velocity` from the previous frame for the impact estimate. Test with a long fall (above 9 m/s) and a short hop; the impact must not be underestimated.

Model needs: nothing here needs the strongest model. A is Python plus ComfyUI, with a sheet script that follows the existing pattern. B is C# plus an editor builder following `TorchCarrier`, `AtmosphereFX` and `CharacterBase`. The riskiest part is the event wiring in `CharacterAudio` and the emitter; a careful mid-tier model is enough for it.

## 7. Work packages (parallel)

**A - Art (ComfyUI + Python, no Unity)**
- Deliverables: `Assets/_Game/Rendering/VFX/WaterSplash/WaterSplash_Sheet.png`, `WaterRipple_Sheet.png`, `WaterDrop_Sheet.png`; `Tools/ComfyUI/make_water_sheets.py`; sources in `Tools/ComfyUI/sources/water_splash/`.
- Step 1 (no ComfyUI needed, first deliverable): run `make_water_sheets.py` procedurally so B gets real-format sheets early.
- Step 2: splash silhouette via `gen.py` (section 1), re-run the script with `--splash-src`.

**B - Unity (C# + builder + prefabs + wiring)**
- Start immediately with the builder, prefabs and materials, using the placeholders in section 1 (Atmo sheets, same grids).
- Then `WaterSplashEmitter`, `WaterSplashFX`, the `CharacterAudio.FootContact` event, `CharacterBase` auto-add, TestScene water pit.
- Swap placeholders for A's sheets without any code change: the grids are the interface.

**Interface contract (both packages):** three sheets, 4x4 grids, 16 frames row-major; splash and ripple 16 px cells, drip 8 px cells; file names as in section 1. Changing pixel size is free; changing the grid needs a B change.
