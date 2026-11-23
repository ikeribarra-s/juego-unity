# Fluffterror — AI Workflow

How Claude Code is wired into this project. Everything here lives in the repo.

```
You (terminal / phone via Remote Control)
  │
  ▼
Claude Code ──► subagents (.claude/agents/*.md)   — specialists with their own instructions
  │                 game-designer · ui-designer · 3d-artist · animator · level-designer · optimizer · sound-designer
  ▼
MCP servers (the "hands")
  ├── UnityMCP  (http://127.0.0.1:8080/mcp, user-scope)  → Unity Editor: scenes, prefabs, console
  ├── blender   (.mcp.json, mcp-for-blender 2.3.1)        → Blender 4.5: model, Poly Haven, Sketchfab, Meshy/Tripo/Rodin/Hunyuan3D
  └── comfy     (.mcp.json, comfy-mcp 0.11)               → local ComfyUI: SDXL images, Stable Audio SFX, templates, models
  │
  ▼
Shared context: CLAUDE.md (code) · Docs/ArtDirection.md (look) · Docs/Design/*.md (feature specs)
```

## Using the agents

Just ask — Claude picks the right agent from its description — or name it explicitly:

- "Use the **game-designer** to spec a trap that interacts with Coso's flaw"
- "Have the **ui-designer** build the main menu"
- "**3d-artist**: make a cursed music-box evidence item"
- "**animator**: add squash & stretch when Trotín lands"
- "**level-designer**: generators are too close together on seed 1234, fix it"
- "**optimizer**: audit the project for per-frame allocations"
- "**sound-designer**: make wet-stone footsteps for the sewer level and wire them"

Typical feature pipeline: `game-designer` writes `Docs/Design/<feature>.md` → you approve →
art agents produce assets listed in its "Assets needed" section → implement → `optimizer` checks.

## Starting a session (checklist)

1. **Unity** open → *Window › MCP for Unity* → make sure the server is **running** (port 8080).
2. **Blender** open → *N-panel › MCP for Blender* → **Start MCP Server**. (Only when doing 3D work.)
3. **ComfyUI** → run `C:\Users\redux\ComfyUI\start-comfyui.bat` (only when doing 2D art or AI sound; UI at http://127.0.0.1:8188).
4. `claude` in this folder → `/mcp` to confirm everything shows **connected**.

## One-time setup status

| Item | Status |
|---|---|
| Subagents | ✅ `.claude/agents/` |
| Art direction | ⚠️ `Docs/ArtDirection.md` — fill in the **[DECIDE]** items |
| Unity MCP | ✅ installed (`com.coplaydev.unity-mcp`) — Editor bridge must be running |
| Blender MCP | ✅ server in `.mcp.json`; addon copied to Blender 4.5 — **enable it once** (below) |
| Image generation | ✅ local ComfyUI 0.46.2 + SDXL + BiRefNet background removal (below) |
| Sound generation | ✅ Stable Audio Open 1.0 in the same ComfyUI + procedural `sfxr.py` + `process_sfx.py` (below) |
| Skills (repeatable recipes) | ⬜ add as patterns emerge (`.claude/skills/<name>/SKILL.md`) |

### Enable the Blender addon (once)
Blender › Edit › Preferences › Add-ons → search "MCP" → enable **MCP for Blender**.
If two entries show up (old `addon.py` and new `blender_mcp.py`), enable only one.
For AI 3D generation (Meshy / Tripo / Hyper3D Rodin / Hunyuan3D), enable it in the addon panel and paste
that service's API key there — keys stay in Blender, not in this repo.

### Image generation — local ComfyUI
| | |
|---|---|
| Install | `C:\Users\redux\ComfyUI` (git checkout v0.46.2, `.venv` Python 3.12, PyTorch 2.14 cu130) — **outside** the repo |
| Start | `C:\Users\redux\ComfyUI\start-comfyui.bat` → http://127.0.0.1:8188 |
| Model | `models/checkpoints/sd_xl_base_1.0.safetensors` (SDXL base) |
| Custom nodes | `ComfyUI-RMBG` (BiRefNet background removal — use BiRefNet models; RMBG-2.0 is non-commercial) |
| Speed (RTX 4060) | ~25 s per 1024² image once warm; first run ~80 s |
| Claude access | `Tools/ComfyUI/gen.py` (simple, used by agents) + `comfy` MCP server (templates, models, node search, launch/stop) |
| Workflows | `Tools/ComfyUI/workflows/*.json` (API format, `SUBJECT` placeholder in the prompt) |

```
python Tools/ComfyUI/gen.py icon    "rusty key with a pink ribbon" --out Assets/Art/UI/Icons --count 4
python Tools/ComfyUI/gen.py concept "main menu, dungeon entrance at night" --out Assets/Art/UI/_mockups
```

Next upgrades, when the base model isn't enough: a stylised SDXL fine-tune checkpoint, and later
**a Fluffterror style LoRA** trained on ~20 approved images so everything matches.

### Sound generation — chip synth (lead) + Stable Audio Open
House style is **dark 8-bit** (`Docs/ArtDirection.md` §9): procedural `sfxr.py dark_*` presets lead, AI textures are optional and always crushed with `--style dark8bit`.

| | |
|---|---|
| AI model | Stable Audio Open 1.0 in the local ComfyUI: `models/checkpoints/stable-audio-open-1.0.safetensors` (Comfy-Org's official repackage) + `models/text_encoders/t5-base.safetensors` (google-t5, Apache-2.0). Re-download: `python Tools/ComfyUI/download_stable_audio.py` |
| Licence | **Stability AI Community License** — free incl. commercial use under USD 1M annual revenue; above that an enterprise licence from Stability is needed |
| Speed (RTX 4060) | ~10 s for a 3 s clip once warm, ~25–40 s for 6 s |
| Workflow | `Tools/ComfyUI/workflows/stable_audio_sfx.json` (API format, `SUBJECT` placeholder, duration set by `--seconds`) |
| Procedural | `Tools/Audio/sfxr.py` — chip synth with NES-style channels (square, 4-bit triangle `tri4`, LFSR noise `lfsr`/`lfsr_short`, 60 Hz stepping). **`dark_*` presets** (house style): dark_step, dark_step_sprint, dark_step_crouch, dark_land, dark_jump, dark_creak, dark_bone_clack, dark_whoosh, dark_torch_pickup, dark_pickup, dark_drop, dark_punch, dark_ui_click, dark_ui_hover, dark_ui_error, dark_powerup. Old bright presets kept for reference. Seeded variations; no GPU |
| A/B + spec | `Tools/Audio/build_ab.py [--source-dir <CC0 packs>]` → `Assets/_Game/Audio/SFX/_AB/` (A real + relaxed / B soft synth / C current) for the user to pick; format spec in `Docs/ArtDirection.md` §9; sources in `Tools/Audio/SOURCES.md` |
| Post-processing | `Tools/Audio/process_sfx.py` — `process` (trim/fade/mono, **`--style relaxed`** (default candidate: 22.05 kHz, 12-bit dither, LP 4.5 kHz, no saturation) + **`--role`** loudness targets (-3 dBTP), or `--style dark8bit` = dark EQ + optional square fold + 11 kHz sample-and-hold (no anti-alias) + 8-bit + low-pass; also `lofi`, `--room`), `split` (onsets), `vary`, `layer`, `inspect` (stats + waveform/spectrogram PNG) |
| Python | the ComfyUI venv: `C:\Users\redux\ComfyUI\.venv\Scripts\python.exe -I …` (numpy, scipy, soundfile, matplotlib). Extra packages: `uv pip install --python <that exe> <pkg>` (the venv has no pip) |
| Agent | `sound-designer` — generate → inspect PNG → process → import → SoundDefinition → wire |
| Style | `Docs/ArtDirection.md` §9 (palette, loudness, naming `SFX_<Category>_<Name>_<NN>.wav`, folders `Assets/_Game/Audio/SFX/<Category>/`, prompt table) |

```
set PY=C:\Users\redux\ComfyUI\.venv\Scripts\python.exe
:: Procedural chip sounds (house style) — e.g. 8 dark stone footsteps
%PY% -I Tools/Audio/sfxr.py --list
%PY% -I Tools/Audio/sfxr.py dark_step --out %TEMP%\sfx --count 8 --seed 11000
%PY% -I Tools/Audio/process_sfx.py process %TEMP%\sfx\SFX_Footsteps_StoneWalk_*.wav --out Assets/_Game/Audio/SFX/Footsteps --category Footsteps --name StoneWalk --mono --style dark8bit --fade 0.015 --lowpass 4000
:: Optional AI texture: a long take of several steps, cut into one-shots, crushed to 8-bit
python Tools/ComfyUI/gen.py sfx "single footstep on cold stone floor, hollow bony heel" --out %TEMP%\sfx --name step --seconds 1.5 --count 4
%PY% -I Tools/Audio/process_sfx.py process %TEMP%\sfx\step_*.flac --out %TEMP%\sfx\p --category Footsteps --name StoneTexture --mono --style dark8bit --max-len 0.35
:: Variations, layering, checking
%PY% -I Tools/Audio/process_sfx.py vary Assets/_Game/Audio/SFX/Character/SFX_Character_Jump_01.wav --out %TEMP%\sfx --count 4
%PY% -I Tools/Audio/process_sfx.py layer click.wav texture.flac --out layered.wav --gain-b -3
%PY% -I Tools/Audio/process_sfx.py inspect Assets/_Game/Audio/SFX/Character/*.wav --png %TEMP%\sfx\sheet.png --sheet
```

## On the go
Unity and Blender run on this PC, so the agents must run here. Use Claude Code **Remote Control**
(or the Claude app connected to this machine) to drive the session from your phone. Pure design
work (`game-designer`) can happen from anywhere.
