# Chucho (Creature 01, the bone hound)

A procedural low-poly model of the giant mangy stray from `Docs/Design/Creature_01.md` §3 and §9. Everything was built by
`build_chucho.py`: no third-party or AI-generated geometry, so it needs no `CREDITS.md` entry.
The reference is a mix of the concepts in `concepts/`:
- the faceted body and clean side silhouette of 01,
- the dark palette and menace of 02,
- the scrawny stray look and collar of 04.

## Files
| File | Contents |
|---|---|
| `Chucho.blend` | `SK_Chucho` (skinned mesh) + `Chucho_Rig` (armature), plus a render stage (`ChuchoCam`, `ChuchoSun`, `ChuchoGround`) |
| `Chucho.fbx` | Exported mesh and armature: bind pose, no animation |
| `T_Chucho_D.png` | 512² diffuse, flat per-facet palette colours. Use Point filtering, no mips |
| `T_Chucho_E.png` | 512² emission mask: white = eyes, black elsewhere |
| `build_chucho.py` | The whole pipeline: mesh, UVs, texture, rig, skin, test poses, renders, export |

Unity copies are in `Assets/Art/Creatures/Chucho/`: `Chucho.fbx`, the two textures, `M_Chucho.mat`, and `Chucho.prefab` (model only).

## Numbers
- **2,672 tris** (1,560 verts). Budget ≤ 4,000, hard cap 5k. One material, flat-shaded.
- **Size:** about 3.0 m nose to tail. The bounding box is 3.10 m including the tail tufts and tongue tip.
  - Withers 1.52 m, top of the skull ~1.95 m, pricked ear tip 2.23 m.
  - 0.65 m wide.
- **Conventions:**
  - Blender is Z-up, the dog faces **-Y**, and 1 BU = 1 m.
  - The origin is on the ground between the four paws.
  - The FBX is exported with forward -Z / up Y and Apply Scale = FBX All. Unity imports it at scale 1 facing **+Z**, with its feet at Y = 0.
- **Unity import:**
  - Generic rig, avatar created from this model, Root node = `root`. Animation import is off; the clips come later from the animator.
  - Normals are imported (flat).
  - The FBX material `M_Chucho` is remapped to `M_Chucho.mat`.

## Material (`M_Chucho.mat`, `Fluffterror/ToonOutline`)
- Settings are copied from `M_Skeleton`. `_CullRadius` is 0.
- `_BaseMap` = `T_Chucho_D`. `_EmissionMap` = `T_Chucho_E`.
- `_EmissionColor` = `#B3202A` and `_EmissionIntensity` = 1.5. For the Chase state the brief wants ×2, so set it to 3.
- `_EmissionMap` is a new optional mask on ToonOutline. Its default is white, so existing materials look the same as before.

## Palette
| Use | Colour |
|---|---|
| Fur | `#4A4340` (back) / `#6B5F55` (sides) / `#2F2A28` (underside, paws) / `#3A3432` (lower legs) |
| Ribs | `#7E7062` |
| Bony spine | `#A09280` |
| Snout | `#C9BCA2` (pale bone) |
| Mouth | gums `#A8404A`, tongue `#C25664` |
| Eyes | `#B3202A` with an `#FF6A48` core (emissive) |
| Collar | `#5B1F1F` |
| Bell | brass `#B08A3A` / `#E0BC66` |
| Tag | `#D4B060` with "CHUCHO" in `#3A2A12` (5x7 pixel font) |

The mesh is built from separate closed parts that intersect, the usual low-poly approach:
- **Body:** torso with rib ridges and grooves, neck, head, jaw, tongue, ears (left pricked, right flopped).
- **Limbs:** 4 legs, 4 huge paws, 16 claws, tail.
- **Bony details:** 14 vertebra bumps, hip crests, shoulder blades.
- **Fur:** jagged tufts on the neck, chest, belly, shoulders, thighs, cheeks and tail.
- **Accessories:** a thin collar, an oversized bell with a clapper, and a bone-shaped tag on a strap.

Each face has a `part` int attribute, and the part list is stored as a custom property on the object. The painter and the skinning use both.

## Rig (`Chucho_Rig`): 41 bones, 37 deforming
```
root (non-deform, on the ground)
└ Hips ─ Spine1 ─ Spine2 ─ Spine3 ─ Chest ─ Neck1 ─ Neck2 ─ Head ─ Jaw ─ Tongue1 ─ Tongue2
  │                                   │              │       │     └ MouthSocket (non-deform)
  │                                   │              │       ├ EyeL, EyeR (non-deform, at the eyes, +Y = gaze)
  │                                   │              │       └ EarL1 ─ EarL2, EarR1 ─ EarR2
  │                                   │              └ Collar ─ Bell   (the tag is weighted to Bell, so it swings too)
  │                                   └ LegFL_/LegFR_ Upper ─ Lower ─ Foot ─ Toe
  ├ LegHL_/LegHR_ Upper ─ Lower ─ Foot ─ Toe
  └ Tail1 ─ Tail2 ─ Tail3 ─ Tail4
```
L = the dog's left = Blender +X.

**Roll convention** (this is what the animator needs):
- Spine, neck, head, jaw and tongue: local Z = up.
  - **+X rotation lifts the tip**. Negative X lowers the head or opens the jaw.
  - Z rotation is yaw.
- Tail: local Z = dorsal, so +X lifts the tail and Z wags it.
- Legs, ears, collar and bell: local Z = forward.
  - **+X swings the tip forward**.
  - Z swings it sideways; for the bell, that is the side-to-side swing.

**Skinning:**
- Rigid parts are weighted 100 % to one bone: head, jaw, teeth, eyes, collar, bell, tag, claws.
- Chain parts blend between at most 3 allowed bones, using a distance falloff that favours the bone a vertex projects onto.
- The leg tops blend into Chest or Hips.

**Test poses** are in `POSES` in the script: walk, sniff, jaw_open, bell_swing, tail_wag. Apply one with `set_pose('<name>')` and reset with `set_pose(None)`. No tears were found in these poses.

## Rebuild
Run this in Blender 4.5:
```python
g = {'__file__': r'<repo>\ArtSource\Creatures\Chucho\build_chucho.py'}
exec(open(g['__file__']).read(), g); g['main'](); g['export']()
```
It runs in a few seconds:
- `main()` builds the mesh, UVs, texture (saved next to the script), rig and skin.
- `export()` writes `Chucho.fbx`.

Then copy the FBX and PNGs over the files in `Assets/Art/Creatures/Chucho/`. The GUIDs stay the same, so the import settings, material and prefab survive.

Things you can tune at the top of the script:
- `J`, `FL`/`HL`, `EAR_*`, `EYE`, `COLLAR_*`: joint positions.
- `TORSO_KEYS`: torso profile.
- `HEAD_SCALE`, `SY`: overall proportions.
- `UV_SCALE`: texel density per part.
