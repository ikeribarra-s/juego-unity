# Fluffterror — Art Direction

> Single source of truth for the game's look. **Every creative agent reads this before producing
> anything** (UI art, models, animation, levels). Change the look here, not in prompts.
> Items marked **[DECIDE]** are open — fill them in and the agents will follow.

---

## 1. Tone in one line

**Cute things in a place that wants them dead.** Soft, round, fluffy characters against cold,
grainy, crumbling dungeons. The horror is real; the characters are comedic. Contrast is the brand.

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
| Environment base | Desaturated stone greys / blue-greys `#2B2F36` `#3E4450` `#5A6270` | Walls, floors |
| Shadow | Near-black blue `#0D0F14` | Never pure black |
| Warm light | Torch amber `#FFB347` → ember `#C2502A` | The only warm tones in the world |
| Characters | **[DECIDE]** Saturated pastels (pink, mint, butter yellow, lilac)? | Must pop against the cold environment |
| Danger | Sick red `#B3202A` | Enemies' eyes, loss states, locked extraction |
| Safe / success | Ghost green `#7CFFB2` | Unlocked extraction beacon, objective complete |
| UI ink | Bone white `#E8E2D0` on translucent black | Text, prompts |

## 4. Characters  **[DECIDE most of this]**

- Body: round, plush/stuffed-toy feel — stitched seams, button eyes? **[DECIDE]**
- Proportions: big head, short limbs (chibi ~2–2.5 heads tall) **[DECIDE]**
- Each of the 8 characters needs one **instantly readable silhouette feature** tied to their ability
  (e.g. Bobi = big nose, Lumi = huge eyes, Pompón = big ears, Fotchi = camera, Mochín = backpack/belly).
- Poly budget: ≤ 5k tris each; one 512–1024 texture; Mixamo-compatible humanoid rig if bipedal.

## 5. Environment

- Base kits in use: *Updated Modular Dungeon (May 2019)*, *Ultimate Modular Ruins (Aug 2021)*, *Furniture Pack (Mar 2019)*.
- New environment assets must **match those kits' scale and low-poly style** — measure against them.
- Grid: 1 tile = 1 world unit (`DungeonGenerator`), wall height 3.
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
