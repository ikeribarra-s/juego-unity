# Fluffterror — AI Workflow

How Claude Code is wired into this project. Everything here lives in the repo.

```
You (terminal / phone via Remote Control)
  │
  ▼
Claude Code ──► subagents (.claude/agents/*.md)   — specialists with their own instructions
  │                 game-designer · ui-designer · 3d-artist · animator · level-designer · optimizer
  ▼
MCP servers (the "hands")
  ├── UnityMCP  (http://127.0.0.1:8080/mcp, user-scope)  → Unity Editor: scenes, prefabs, console
  ├── blender   (.mcp.json, mcp-for-blender 2.3.1)        → Blender 4.5: model, Poly Haven, Sketchfab, Meshy/Tripo/Rodin/Hunyuan3D
  └── comfy     (.mcp.json, comfy-mcp 0.11)               → local ComfyUI: SDXL images, templates, models
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

Typical feature pipeline: `game-designer` writes `Docs/Design/<feature>.md` → you approve →
art agents produce assets listed in its "Assets needed" section → implement → `optimizer` checks.

## Starting a session (checklist)

1. **Unity** open → *Window › MCP for Unity* → make sure the server is **running** (port 8080).
2. **Blender** open → *N-panel › MCP for Blender* → **Start MCP Server**. (Only when doing 3D work.)
3. **ComfyUI** → run `C:\Users\redux\ComfyUI\start-comfyui.bat` (only when doing 2D art; UI at http://127.0.0.1:8188).
4. `claude` in this folder → `/mcp` to confirm everything shows **connected**.

## One-time setup status

| Item | Status |
|---|---|
| Subagents | ✅ `.claude/agents/` |
| Art direction | ⚠️ `Docs/ArtDirection.md` — fill in the **[DECIDE]** items |
| Unity MCP | ✅ installed (`com.coplaydev.unity-mcp`) — Editor bridge must be running |
| Blender MCP | ✅ server in `.mcp.json`; addon copied to Blender 4.5 — **enable it once** (below) |
| Image generation | ✅ local ComfyUI 0.46.2 + SDXL + BiRefNet background removal (below) |
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

## On the go
Unity and Blender run on this PC, so the agents must run here. Use Claude Code **Remote Control**
(or the Claude app connected to this machine) to drive the session from your phone. Pure design
work (`game-designer`) can happen from anywhere.
