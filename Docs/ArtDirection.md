# Fluffterror — Art Direction

> Single source of truth for the game's look. **Every creative agent reads this before producing
> anything** (UI art, models, animation, levels). Change the look here, not in prompts.
> Items marked **[DECIDE]** are open — fill them in and the agents will follow.

---

## 1. Tone in one line

**Goofy bones in a place that wants them dead.** Clumsy, expressive low-poly skeletons against cold,
grainy, crumbling dungeons. The horror is real; the characters are comedic. Contrast is the brand.
(Earlier drafts said "soft, fluffy plush characters": superseded by the skeleton protagonist, 2026-11-23.)

## 2. Rendering look (already implemented — don't fight it)

| Element | Current setting | Source |
|---|---|---|
| Pixelation | `PixelateFeature` block size (1–32) driven by `HorrorPostFX._pixelSize` | `Assets/_Game/Rendering/` |
| Film grain + vignette | Always on, spikes on scares (`PulseGrain`) | `HorrorPostFX.cs` |
| Colour grade | Cold blue cast, `colorFilter = (0.82, 0.88, 1.0)` | `HorrorPostFX.cs` |
| Light | Warm flickering torches (FBM flicker) as the only warmth in the frame | `LightFlicker.cs` |
| Fire / VFX | Flipbook sprite-sheets on billboards, additive, HDR so they bloom | `Flipbook.shader` |

Consequence for assets: **fine detail is lost to pixelation and grain.** Favour strong silhouettes,
big shapes, and readable value contrast over texture detail.

## 3. Palette

| Role | Colour | Use |
|---|---|---|
| Environment walls | Muted warm-brown stone, matched to the Modular Dungeon kit: `#7A6B57` `#77634E` `#90816E` (trim `#A39280`, shadowed `#5E5142`) | Walls, arches, pillars, trims (2026-11-24, was blue-grey) |
| Environment floors | Cool dark grey-violet flagstone, matched to the kit floor: `#4D4C5A` `#3C3A46` `#45444F` | Floors; keeps floor/wall contrast |
| Natural rock | `#5B5248` `#4A433B` `#6A6156` `#3A342E` | Caves (`CH_SP_CaveTransition`) |
| Shadow | Near-black blue `#0D0F14` | Never pure black |
| Warm light | Torch amber `#FFB347` → ember `#C2502A` | The only warm tones in the world |
| Characters | **[DECIDE]** Saturated pastels (pink, mint, butter yellow, lilac)? | Must pop against the cold environment |
| Danger | Sick red `#B3202A` | Enemies' eyes, loss states, locked extraction |
| Safe / success | Ghost green `#7CFFB2` | Unlocked extraction beacon, objective complete |
| UI ink | Bone white `#E8E2D0` on translucent black | Text, prompts |

## 4. Characters

- **Body: a low-poly skeleton** (approved 2026-11-23). Current model: `Assets/Art/Characters/Skeleton/Skeleton.fbx`
  — 5-finger Mixamo skeleton, 65 bones, Humanoid, 2.39 m in game, separate lower jaw (`Head/JawPivot`),
  `Fluffterror/ToonOutline` material, bone-white base, one 512² texture (Point, no mips).
- The skeleton is **funny, not creepy**: big readable skull, loose expressive posing, chattering jaw for talk
  (Phase 5). Never gore, never rot — clean bones. Body sounds carry the "bone tick" (§9).
- **Bones are gameplay:** limbs can detach (Chucho's bone offering, `Docs/Design/Creature_01.md` §15), so
  every limb must read as a separate prop when cut off (upper arm + forearm + hand, thigh + shin + foot).
- Each of the 8 characters keeps one **instantly readable silhouette feature** tied to their ability, worn
  on the skeleton as an accessory/prop (e.g. Bobi = big dog-nose snout, Lumi = huge glowing goggles,
  Pompón = oversized ear-trumpet/ears, Fotchi = camera, Mochín = backpack, Trotín = running shoes,
  Sabiucho = glasses + notebook, Coso = big gloves/hammer) plus a character colour on that accessory —
  saturated pastels (pink, mint, butter yellow, lilac…) so they pop against the cold environment. **[DECIDE]**
  exact accessory per character.
- Poly budget: ≤ 5k tris each including the accessory; one 512–1024 texture; Mixamo-compatible humanoid rig
  (reuse the skeleton rig for all eight).

## 5. Environment

- Base kits in use: *Updated Modular Dungeon (May 2019)*, *Ultimate Modular Ruins (Aug 2021)*, *Furniture Pack (Mar 2019)*.
- New environment assets must **match those kits' scale and low-poly style** — measure against them.
- Grid: 1 tile = 1 world unit (`DungeonGenerator`), wall height 3.
- **Authored map chunks** (`ArtSource/Environment/Chunks/README.md`, prefabs in `Assets/Art/Environment/Chunks/`):
  8 m cells, one atlas `T_Chunks_D` (512², Point, no mips) on one material (`Fluffterror/PS2Lit`). Walls use the
  kit's warm brown, floors the kit's cool grey, so the torch amber `#FFB347` stays the warmest thing on screen
  (wall browns are low-saturation, value ≈ 0.45).
- **PS2 look (2026-11-24):** big shapes and silhouettes are geometry (courses, arches, ribs, pillars); small-scale
  detail is **texture**: tileable 64 px cells per material at ~32 px/m (walls, floors) / 64 px/m (props), Point
  filtered, 6–7 shades per material with ordered dither, mean on the §3 palette colour. Lighting depth comes from
  **baked vertex-colour AO + floor grime** (1 m vertex grid), multiplied in by `PS2Lit`. No bevels or chamfers
  smaller than ~0.3 m; no mips. Normal maps only for the chunk atlas `T_Chunks_N` (same 64 px cell grid, Point, no mips, generated from the same height fields as the albedo; Meshy props stay flat). Architectural wood (door frames, wainscot, planks) is now 32 px/m like the stone. New environment pieces follow `ArtSource/Environment/Chunks/ps2_style.py`.
- Prop budget: ≤ 1.5k tris for small props, ≤ 3k for furniture.

## 6. UI

- Font: gothic display font for headings/prompts (**[DECIDE]** pick one, e.g. a free blackletter from Google Fonts), clean sans for numbers.
- Style: diegetic-feeling where possible — torn paper, chalk, scratched metal plates. No glossy modern UI.
- Comedy lives in the UI copy and icons (wobbly hand-drawn icons, silly character portraits).
- Must stay readable **after** pixelation if rendered in world space; screen-space UI is not pixelated.
- Tech: Unity **UI Toolkit** (UXML/USS) for new menus. Existing HUD is IMGUI (debug-grade, to be replaced).

## 7. Audio-visual comedy rules

- Scares are real; the payoff is a character's flaw making it worse (see roster in `CLAUDE.md`).
- Never make the characters grotesque. Make the world grotesque.

## 8. Image-generation prompts (local ComfyUI / SDXL)

**Put the subject first.** SDXL follows the first words most strongly. A style block that
starts with "plush creatures" turns *every* image into a sheet of creatures (tested: asking for a
key produced 40 critters).

**Objects / icons:** `gen.py icon` already wraps your subject in this:
```
a single <SUBJECT>, one object only, game inventory icon, centered, isolated on plain flat light
grey background, thick dark outline, flat cel shading, chunky readable silhouette, slightly crude
hand-drawn feel, muted cold palette with one warm accent, dark comedic horror game item art
```

**Scenes / characters / concept art:** `gen.py concept` appends the subject to this:
```
Fluffterror style: cute round plush-toy creatures in a dark cold medieval dungeon,
comedic horror, chunky readable silhouettes, low detail, desaturated blue-grey stone,
warm amber torchlight as the only warm colour, soft pastel character colours,
film grain, slightly crude hand-made feel, <SUBJECT>
```
For concept art of a specific thing, put the key noun early in `<SUBJECT>` and keep it short.

## 9. Audio

Pipeline and commands: `Docs/AI-Workflow.md` → *Sound generation*. Agent: **sound-designer**.

### Audio format spec (current — read this first)
The game is low-poly 3D under a pixel post-filter (PS1-ish). **Preferred sound:** real recordings (CC0)
through a soft retro filter — `relaxed`. Chip synth only for UI and stings. The user rejected sounds that
were "too loud and saturated". Every new sound family goes through an **A/B round** before batching.

**Files**
| | |
|---|---|
| Format | WAV PCM 16-bit, 44.1 kHz container; content band-limited by the style (relaxed: 22.05 kHz resample + 4.5 kHz LP) |
| Channels | **mono** for every 3D/world sound; UI may be mono too (2D via `SpatialBlend = 0`) |
| Naming | `SFX_<Category>_<Name>_<NN>.wav` (`_placeholder` before `_NN` for stand-ins); A/B files `AB_<Sound>_<A\|B\|C>.wav` |
| Folders | `Assets/_Game/Audio/SFX/<Category>/` (Footsteps, Character, Items, Combat, UI, Environment, Creatures); A/B rounds in `SFX/_AB/` with a `README.md` table + `AB_compare_sheet.png` |
| Definitions | one `SD_<Name>.asset` (SoundDefinition) per group next to its clips; ≥ 3 clips per repeated sound (footsteps ≥ 6); `_pitchRange` 0.94–1.06 |

**Styles** (`Tools/Audio/process_sfx.py process … --style <s>`)
| Style | Use | Exact chain |
|---|---|---|
| **`relaxed`** (default candidate) | real recordings, softened synth | HP 60 Hz → anti-aliased resample to 22.05 kHz → 12-bit quantise with ±1 LSB TPDF dither → LP 4.5 kHz 12 dB/oct (`--lowpass`) → back to 44.1 kHz. **No** fold, saturation or clipping. 4 ms fade-in, exponential tail (`--fade` 0.08 s). Optional `--room` (dark 0.25 s tail, `--room-wet` ≤ 0.10, default 0.08) — 2D/UI only, never on 3D sounds (EnvironmentReverb adds space) |
| `dark8bit` (legacy / stings) | the current in-game set; UI blips; stings | `--darkness 0.5`: HP 45 Hz, +1 dB @160 Hz, -2 dB @2.8 kHz, optional `--fold`, sample-and-hold to ≈15.6 kHz (no anti-alias), ≈9-bit, LP 6.5 kHz |
| `clean` | loudness-only re-master | trim + fade + normalise |

**Loudness by role** (`--role`, max-momentary LUFS with the short-clip window; **true-peak ceiling -3 dBFS**;
a clean look-ahead peak limiter, gain riding only, ≤ `--limit` 6 dB, helps spiky recordings reach the target)
| Role | Target | Sounds | Unity import | Mixer group | SD volume |
|---|---|---|---|---|---|
| `footstep` | **-26 LUFS** | walk, sprint | Decompress On Load, ADPCM, Preload, Force To Mono | `FootSteps.mixer` | 1.0 |
| `crouch` | **-30** | crouch steps | same | `FootSteps.mixer` | 1.0 |
| `action` | **-22** | pickup, drop, jump, whoosh, torch pickup | same | `Actions.mixer` | 1.0 |
| `impact` | **-20** | land, punch/hit | same | `Actions.mixer` | 1.0 |
| `ui` | **-24** | click, hover, error | Decompress On Load, ADPCM, Preload, mono OK | `SFX.mixer` | 1.0 |
| `idle` / `ambient` | **-30** | idle creak, beds | one-shots as above; long beds Streaming/Vorbis | `Actions.mixer` / `SFX.mixer` | 1.0 |
With role targets the **files carry the balance**; SoundDefinition volumes then stay near 0.6–0.8 and the mixer Masters at -6 dB give
headroom — see *Mix levels* below for the live values. (The current in-game dark8bit set still uses the older -16 LUFS + per-SD volumes until the
A/B pick is batched.)

### Mix levels (live game — set 2026-11-24 after "too loud and noisy")
Every live clip is re-mastered with `Tools/Audio/remaster_live.py` (manifest of every sound → role/LUFS,
low-pass, max length; re-run after replacing a source). Player/item/UI sounds are re-rendered from the same
`dark_*` presets + seeds at `--darkness 0.35` through `relaxed` (same character, no 8-bit crunch); ambient
one-shots go through `relaxed` with darker low-passes; beds get a loop-safe 24 dB/oct low-pass at 3 kHz.

| Layer | File (LUFS) | SD volume | Mixer (Master) | 3D min / max (log) | Anti-spam (`MaxVoices` / `MinInterval`) |
|---|---|---|---|---|---|
| Footsteps walk / sprint | -26 (LP 4–4.2 kHz) | 0.8 | FootSteps **-6 dB** | 2 / 30 m | — (0.18 s floor in CharacterAudio) |
| Footsteps crouch | -30 (LP 3.2 kHz) | 0.8 | FootSteps -6 | 2 / 20 | — |
| Jump · pickup · drop · torch pickup · whoosh | -22 | 0.7 | Actions **-6** | 2 / 25–30 | 1–2 voices |
| Land · punch | -20 | 0.7 | Actions -6 | 2 / 30 | 2 |
| Idle creak · bone clack | -30 / -24 | 0.6 | Actions -6 | 2 / 15–20 | 1–2 |
| UI click / hover / error · powerup | -24 / -28 / -24 · -24 | 0.7 · 0.6 | SFX **-6** | 2D | 1–2, 0.03–0.5 s |
| Area beds (loops) | -32 (LP 3 kHz, 24 dB/oct) | 1.0 × `SSP_Default.BedVolume` **0.45** | SFX -6 | 2D; **duck ×0.6** during Spike | crossfade 2 max |
| Drips | -30 (LP 3.5 kHz, ≤ 0.6 s) | 0.8 | Actions -6 | 1.5 / 25 | 2 voices, 0.8 s; `DripInterval` **2.5–6 s** |
| Water footsteps (walk / sprint) | -26 (LP 4.5 kHz, ≤ 0.45 s, 7 takes each) | 0.8 | FootSteps | 2 / 30 | only inside a `WaterVolume` |
| Water splash (enter) | -22 (≤ 0.9 s, 3 takes) | 0.7 | Actions -6 | 2 / 25 | once when the feet enter the water |
| Water drips | -30 (LP 3.5 kHz, ≤ 0.6 s, 6 takes) | 0.8 | Actions -6 | 1.5 / 25 | 2 voices, 0.8 s; `WaterDripInterval` 1.5–4 s, only near water |
| Water bed (3D loop) | -32 (LP 3 kHz, 18 s loop) | 1.0 × `BedVolume` | SFX -6 | 3 / 18 | at the nearest `WaterVolume`, fades out by `WaterRadius` 16 m |
| Distant sounds · fake bell | -26 (LP 2.5 / 3 kHz) | 0.8 | Actions -6 | 5 / 50 | 1 voice, 4 s |
| Torch gutter / out / relight | -30 (LP 3.5 kHz, gutter ≤ 1.2 s) | 0.7 | Actions -6 | 1.5 / 20 | 2 voices, 0.3 s (spike stutter can't stack) |
| Spike sting | -26 (LP 4 kHz) | 0.7 | SFX -6 | 2D | 1, 3 s |
| Music bed `SuspenseAmbient` (AmbientAudio) | -11 | 0.05 | Music **-6** | 2D | — |
| Generator run loop / engine start | -27 / -24 (mp3, user's) | 0.35 / 0.45 | Actions -6 (was none) | 2 / 25 | start: 1 |

The four mixers are separate (no shared master), so each mixer's Master at -6 dB **is** the global headroom.
The user's old definitions (`SFX_Walk` 0.10, `SFX_Sprint` 0.08, `SFX_JumpImpact` 0.10) show how quiet they
wanted footsteps: about -48 LUFS effective; the dark8bit set was running at about -20.

**Measured at the AudioListener** (`AudioLevelMeter`, 60 s scripted run in SampleScene: idle, walk, sprint,
crouch, 3 jumps, torch carry + throw, evidence grab/drop, forced Spike): see the table in the 2026-11-24
mix report in `CLAUDE.md` → SFX library. Targets for future checks: **integrated ≈ -35 LUFS, max
momentary ≤ -26 LUFS, sample peak ≤ -15 dBFS**, footsteps ≈ 5–8 dB above the bed, ambience well under actions.

**Sources & licences** — CC0 / public domain or Sonniss GDC bundles only (plus our own Stable Audio /
`sfxr.py` output). Verify the licence on the source page, download each pack into its own folder outside
the repo, use only the audio files, and log pack + URL + licence + file → asset in `Tools/Audio/SOURCES.md`.
No CC-BY/-SA/-NC without asking.

**Workflow: A/B → pick → batch**
1. For a new sound family build an A/B set: **A** real recording + `relaxed`, **B** softened synth
   (`sfxr.py soft_*`, `--darkness 0.3`) + `relaxed`, **C** the current sound re-mastered to the role target
   (`Tools/Audio/build_ab.py [--source-dir <packs>]` does this for the core set). Same role loudness for all
   three so the choice isn't biased by volume.
2. The user listens in the Unity Project window (`SFX/_AB/README.md`) and picks per sound (A/B/C).
3. Batch the family in the picked style (≥ 6 variations for footsteps), overwrite the live `SFX_*` files in
   place (same names → same GUIDs → wiring survives), update the SoundDefinitions to the role table, then
   delete or archive `_AB`.

### Direction: DARK 8-BIT (darkness 0.5) — legacy / UI & stings
> Superseded as the default by the format spec above (pending the A/B pick); still the style of the current
> in-game set and the choice for UI blips and stings.

> **Current setting: `--darkness 0.5`** (both `sfxr.py` and `process_sfx.py --style dark8bit`). The first pass at
> 1.0 felt too oppressive; 0.5 keeps the chip palette but is brighter: ≈ 9-bit / 15.6 kHz hold, low-pass 6.5 kHz,
> -2 dB top shelf, fundamentals of thuds/steps ≈ +3.6 semitones, minor thirds instead of tritones, louder
> transients. 1.0 = heavy (8-bit / 11 kHz / LP 5 kHz / tritones), 0 = light (10-bit / 22 kHz / LP 8 kHz).

Think **NES / C64-era horror** — the sound of a cursed cartridge, not a cartoon. It matches the
pixelated, grainy picture: limited, crunchy, cold, with the occasional sound that is *slightly wrong*.

| Do | Don't |
|---|---|
| Console channels only: **square** (12.5 / 25 / 50 % duty), **4-bit triangle**, **LFSR noise** (long = hiss/scuff, short = metallic tick) | Sine pads, hi-fi samples, stereo width, glossy reverb |
| **Limited polyphony**: ≤ 3–4 voices per sound | Big layered "cinematic" stacks |
| **Low bit depth / sample rate feel**: 8-bit, 11–16 kHz, aliasing allowed (`--style dark8bit`) | Clean 44 kHz brightness |
| **Short decays**, low **thuds**, gritty **noise bursts** | Long ringing tails (the game adds space via `EnvironmentReverb`) |
| Pitch moves **down** or by small **minor** steps — minor 2nd, minor 3rd (tritones only at darkness ≥ 0.75); lightly **detuned** square pairs | Rising boings, slide whistles, cartoon pitch-bends, bright major arpeggios, "coin" blips |
| Pitch + volume stepped at **60 Hz frames**, 16 volume levels (`chip=True`) | Smooth analogue glides |

- **The player is a skeleton:** body sounds carry a **bone tick** = a short burst of *short-mode* LFSR
  noise (metallic, hollow). Footsteps = long-LFSR scuff + low triangle thud + bone tick.
- **Torches** are the only warm element: noise whoosh with crackle ticks, low tritone dyad on pickup.
- UI is low, short and muted (low 12.5 % square ticks, 4-bit triangle hovers, falling-tritone error).
- Comedy comes from the characters' flaws and voices (Phase 5), **not** from silly SFX.
- **Source priority:** the procedural chip synth (`Tools/Audio/sfxr.py dark_*`) **leads**. Stable
  Audio is optional, only for dark grounded textures (stone, wind, wet drips, creature breath), and
  **always** goes through `--style dark8bit` so it lands in the same palette.

### Loudness & format
| Rule | Value |
|---|---|
| Delivery format | WAV, 44.1 kHz, 16-bit container (the 8-bit/11 kHz character is baked in by the style pass) |
| Loudness target (one-shots) | **-16 LUFS** max-momentary (400 ms window or the clip length if shorter), peaks ≤ **-1 dBFS** — `process_sfx.py` default |
| Balance | All files sit at the same level; balance in `SoundDefinition.Volume` by **role**, not per file: footsteps 0.6 (crouch 0.35), actions/items 0.6, impacts 0.7, idle 0.3, UI 0.4–0.5 |
| Ambience / loops | -24 LUFS integrated |
| Silence | none at the head (≤ 5 ms), short faded tail (15 ms for chip sounds) |
| Pitch variation | `SoundDefinition._pitchRange` ±5–8 % (0.94–1.06) + ≥ 3 clips per repeated sound (footsteps ≥ 6) |

### Mono vs stereo
- **Mono** for every sound that comes from a place in the world (`SpatialBlend = 1`): footsteps,
  body, items, torches, creatures, generators. Unity import: **Force To Mono** on.
- **2D** (`SpatialBlend = 0`): UI, stingers, music, global beds (8-bit sounds are mono anyway).
- Unity import for one-shots: **Decompress On Load**, ADPCM, Preload on. Streaming for music.

### Naming & folders
- Files: `SFX_<Category>_<Name>_<NN>.wav` — e.g. `SFX_Footsteps_StoneWalk_03.wav`. Temporary
  stand-ins add `_placeholder` before the number.
- Folders: `Assets/_Game/Audio/SFX/<Category>/` — `Footsteps`, `Character`, `Items`, `Combat`, `UI`,
  `Environment`, `Creatures`. One `SD_<Name>.asset` (SoundDefinition) per group next to its clips.
- Remakes **overwrite files in place** (same name → same GUID → SoundDefinitions keep working).

### Recipes (procedural — preferred)
| Sound | Preset | Process extras |
|---|---|---|
| Footsteps walk / sprint / crouch | `dark_step` / `dark_step_sprint` / `dark_step_crouch` (×8, ×8, ×6) | crouch `--lowpass 4500` |
| Jump / land | `dark_jump` / `dark_land` | jump `--fold 0.1` |
| Idle bone creak / bone clack | `dark_creak` / `dark_bone_clack` | creak `--fold 0.1` |
| Torch throw / pickup | `dark_whoosh` / `dark_torch_pickup` | pickup `--fold 0.1` |
| Evidence grab / release | `dark_pickup` / `dark_drop` | grab `--fold 0.1` |
| Hit | `dark_punch` | — |
| UI click / hover / error, objective sting | `dark_ui_click` / `dark_ui_hover` / `dark_ui_error`, `dark_powerup` | error/sting `--fold 0.1` |

Generate with `sfxr.py <preset> --darkness 0.5`, process with `process_sfx.py process … --mono --style dark8bit --darkness 0.5 --fade 0.015 --thresh -50` (fold 0.1 on tonal sounds; crouch `--lowpass 4500`).

### Stable Audio Open prompt rules (optional textures)
`gen.py sfx` wraps the subject: `<SUBJECT>, single sound effect, isolated, close-miked, dry, no music,
no reverb tail, game sfx`, with music/voice/ambience/reverb in the negative prompt.
- **Subject first, short** (≤ ~12 words), **dark and grounded**: material + action + one mood word
  (stone, bone, iron, wet, hollow, low). Never "cartoon", "funny", "magic", "sparkle".
- One sound per generation (`--seconds 1.5–3`), or "several separate …" over 4–6 s → `split`.
- Generate 3–4 seeds, inspect, keep the best, record the seed, then **`--style dark8bit`**.

| Category | Template subject (`gen.py sfx "<…>"`) | Seconds |
|---|---|---|
| Footsteps (texture layer) | `single footstep on cold stone floor, hollow bony heel` | 1.5 |
| Environment | `heavy iron door grinding open in a stone dungeon` / `water dripping in a dark stone cellar` | 2–4 |
| Creatures | `low wet creature breath, slow, close` / `small creature growl, raspy throat` | 2–3 |
| Torch / fire bed | `small torch fire crackling, close, dry` | 3 |
| Impacts (texture layer) | `heavy body hitting stone floor, dull thud` | 1.5 |

Layer a texture under a chip transient with `process_sfx.py layer <chip.wav> <texture.wav> --gain-b -6`,
then run the result through `--style dark8bit` again so both share the same crunch.
