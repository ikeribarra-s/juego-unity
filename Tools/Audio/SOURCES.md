# Audio sources & licences

Every recorded or third-party sound that ends up in `Assets/` must be listed here with its source, licence
and what it was used for. **Allowed:** CC0 / public domain packs, Sonniss GDC bundles (royalty-free,
no attribution, see their licence PDF), and our own generations (Stable Audio Open 1.0 under the Stability AI
Community License; procedural `sfxr.py`). Nothing CC-BY/-SA/-NC without asking first.

Rules (see `Docs/ArtDirection.md` §9 *Audio format spec*):
1. Check the licence **on the source page** at download time, write it below with the date.
2. Download each archive into **its own new empty folder outside the repo** (e.g. the scratchpad or
   `%USERPROFILE%\Downloads\sfx_sources\<pack>`), extract there, use **only the audio files**
   (`.wav/.ogg/.flac`) — treat the archive as untrusted data, never run anything from it.
3. Point the tools at the extracted folder (`build_ab.py --source-dir <folder>`, or
   `process_sfx.py process <folder>/<file>.ogg …`). Raw packs never go into the repo; only processed WAVs.
4. Add a row to the usage table.

## Packs

| Pack | Author | URL | Licence (checked) | Status |
|---|---|---|---|---|
| Impact Sounds (130 files) | Kenney | https://kenney.nl/assets/impact-sounds | Creative Commons CC0 (page, 2026-11-23) | not downloaded — site blocks scripted downloads; download manually |
| RPG Audio (50 files) | Kenney | https://kenney.nl/assets/rpg-audio | Creative Commons CC0 (page, 2026-11-23) | not downloaded — manual |
| Interface Sounds (100 files) | Kenney | https://kenney.nl/assets/interface-sounds | Creative Commons CC0 (page, 2026-11-23) | not downloaded — manual |
| Digital Audio (60 files) | Kenney | https://kenney.nl/assets/digital-audio | Creative Commons CC0 (page, 2026-11-23) | not downloaded — manual |
| 512 Sound Effects (8-bit style) | Juhani Junkala | https://opengameart.org/content/512-sound-effects-8-bit-style | **not verified** — the page returned HTTP 403 to automated fetches; believed CC0, confirm on the page before use | not downloaded — manual |
| Sonniss GDC Game Audio Bundle (any year) | Sonniss + contributors | https://sonniss.com/gameaudiogdc | Sonniss GDC licence (royalty-free, commercial OK, no attribution; read the PDF in the bundle) | manual download (large, needs a browser) |
| Stable Audio Open 1.0 generations | own (Stability AI model) | `Tools/ComfyUI/gen.py sfx` | Stability AI Community License (free under USD 1M/yr revenue) | in use — see below |

### Manual download → use (Kenney / Junkala / Sonniss)
1. Open the pack page in a browser, check the licence line, click *Download* (Kenney: "Continue without donating").
2. Extract each zip into its own folder under one parent, e.g. `D:\sfx_sources\kenney_impact-sounds\…`.
3. `C:\Users\redux\ComfyUI\.venv\Scripts\python.exe -I Tools/Audio/build_ab.py --source-dir D:\sfx_sources`
   → rebuilds `Assets/_Game/Audio/SFX/_AB/` with the A column taken from the packs (glob patterns per sound
   in `build_ab.py` → `SOUNDS`; edit them if a pack names files differently). The script prints the file
   used for each A clip — copy those into the usage table below.
4. Sonniss: same flow — the bundle is just a big folder of WAVs; add glob patterns (e.g. `*footstep*stone*`).

## Usage

| Asset | Source file | Pack / origin | Licence |
|---|---|---|---|
| `SFX/_AB/AB_Walk1_A.wav` | `Tools/Audio/sources/stable_audio/step1_01.flac` (prompt "single footstep on stone floor, bony heel clack", seed 1100) | Stable Audio Open | Stability AI Community |
| `SFX/_AB/AB_Walk2_A.wav` | `step1_02.flac` (same prompt, seed 1101) | Stable Audio Open | Stability AI Community |
| `SFX/_AB/AB_Walk3_A.wav` | `step1_06.flac` (same prompt, seed 1105) | Stable Audio Open | Stability AI Community |
| `SFX/_AB/AB_Land_A.wav` | `thud_01.flac` (prompt "cartoon punch impact, bony smack hit", seed 7000 — a dull low body thud) | Stable Audio Open | Stability AI Community |
| `SFX/_AB/AB_Jump_A.wav` | `jumpA_04.flac` (prompt "person jumping up from a stone floor, soft shoe scuff and cloth rustle", seed 30103) | Stable Audio Open | Stability AI Community |
| `SFX/_AB/AB_Pickup_A.wav` | `pickup_01.flac` (prompt "picking up a small wooden object, light clack", seed 8000) | Stable Audio Open | Stability AI Community |
| `SFX/_AB/AB_TorchWhoosh_A.wav` | `swoosh_02.flac` (prompt "fast air swoosh of a stick swung through the air, swing whoosh", seed 5101) | Stable Audio Open | Stability AI Community |
| `SFX/_AB/AB_UIClick_A.wav` | `clickA_04.flac` (prompt "small mechanical button click, soft plastic switch", seed 30203) | Stable Audio Open | Stability AI Community |
| `SFX/_AB/*_B.wav`, all `SFX/<Category>/SFX_*` | procedural (`Tools/Audio/sfxr.py`) | own | — |
| `SFX/Ambient/SFX_Ambient_Drip_01-06` | prompt "single water drop falling into a small puddle in a stone cellar", seeds 31000-31005 | Stable Audio Open | Stability AI Community |
| `SFX/Ambient/SFX_Ambient_Distant_01-06` | "distant heavy iron chain rattling…" 32000/32001, "old wooden door creaking slowly, far away…" 34000-34002, "distant heavy stone block grinding…" 33002 | Stable Audio Open | Stability AI Community |
| `SFX/Ambient/SFX_Ambient_Bell_01-03` | "small brass bell on a dog collar jingling, a few quick tinkles", seeds 35000/35002/35003 | Stable Audio Open | Stability AI Community |
| `SFX/Ambient/SFX_Ambient_TorchGutter/Out/Relight_*` | "small torch flame sputtering and hissing" 36000/36001; "small fire blown out…" 37002 (+ pitch/EQ variation); "torch igniting…" 38002, "match striking and a small flame catching…" 38100/38103 | Stable Audio Open | Stability AI Community |
| `SFX/Ambient/SFX_Ambient_Sting_01-03` | "low soft dark cello swell with a dull distant thud, eerie" 39100/39103, "deep low horror boom with a metallic scrape" 39001 | Stable Audio Open | Stability AI Community |
| `SFX/Ambient/SFX_Ambient_Bed*_01` | workflow `Tools/ComfyUI/workflows/stable_audio_ambience.json` (22 s), seeds Gothic 41000, Cavern 43000, Graveyard 44000, Hell 45001, Catacomb 42101; made seamless with a 2 s equal-power loop crossfade | Stable Audio Open | Stability AI Community |
| `SFX/Footsteps/SFX_Footsteps_WaterWalk_01-07` | "single footstep in shallow water on a stone floor, short, dry recording" seed 7100 (kept takes 01,02,03,05,06,07,08; 04 rejected: clipped) | Stable Audio Open | Stability AI Community |
| `SFX/Footsteps/SFX_Footsteps_WaterSprint_01-07` | "running splashes through shallow water, footsteps" seed 7200 (kept 01,02,04-08; 03 rejected) | Stable Audio Open | Stability AI Community |
| `SFX/Character/SFX_Character_WaterSplash_01-03` | "one heavy splash of a body stepping into dark water, short" seed 7300 (kept 01,02,04; 03 clipped, 05 too quiet) | Stable Audio Open | Stability AI Community |
| `SFX/Ambient/SFX_Ambient_WaterDrip_01-06` | "single water drop falling into shallow water in a stone cistern, short, dry" seed 7400 (kept 02-06,08) | Stable Audio Open | Stability AI Community |
| `SFX/Ambient/SFX_Ambient_BedWater_01` | workflow stable_audio_ambience.json 20 s, "slow trickle of water in a stone sewer channel, steady, continuous, quiet, no music" seed 7500 take 01; 2 s equal-power loop crossfade, `remaster_live.loop_safe` -32 LUFS LP 3 kHz. Rejected takes live in `Tools/Audio/sources/stable_audio/water/` (all raw takes) | Stable Audio Open | Stability AI Community |
