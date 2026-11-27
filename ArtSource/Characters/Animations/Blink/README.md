# Skull blink (player skeleton)

The skull has no eyelids or eyeballs, only shallow dark sockets painted on `T_Skeleton_D`. To blink
it, each socket gets a small bone-coloured **eyelid shell**. It sits rolled up inside the brow,
where it can't be seen. When it blinks it drops over the socket and goes back up.

## Files
| File | Contents |
|---|---|
| `SkullBlink.blend` | Scene `SkullBlink`: bind-pose `skeleton_nojaw.fbx` (Mixamo take removed), the jaw at its hinge, `Skeleton_Lids`, actions `Blink` / `BlinkDouble`, preview camera `BlinkCam` |
| `Skeleton_Lids.fbx` | Lid mesh only, no animation. Import this one as the model |
| `Skeleton_Lids@Blink.fbx` | Same mesh + take `Blink` (frames 0-7 @ 30 fps = 0.233 s) |
| `Skeleton_Lids@BlinkDouble.fbx` | Same mesh + take `BlinkDouble` (frames 0-12 @ 30 fps = 0.400 s) |
| `build_blink.py` | Rebuilds everything (lids fitted to the real socket rims, shape keys, actions, FBX export) |

Rebuild: in Blender 4.5 import `ArtSource/Characters/Final/skeleton_nojaw.fbx` into an empty scene, then
`g = {'__file__': r'<this folder>\build_blink.py'}; exec(open(g['__file__']).read(), g); g['main']()`.
The tunables are at the top of the script: socket centre and size, `DOME` bulge, `EDGE_SINK`, `TUCK_*`, the clip key tables in `CLIPS`, and `LASH_LINE`.

## What was built
- **Object / mesh**: `Skeleton_Lids`, 196 verts and 384 tris for both lids. Each lid is a closed shell 4 mm thick: a 6x6 front grid, a back grid and a border strip. It is closed so the `ToonOutline` inverted-hull outline works on it.
  - The lid border follows the real bone surface and sits 4 mm inside it, so the lid comes out of the rim with no visible lip.
  - The middle of the lid is a 1.6 cm dome, so it reads like a lid over an eyeball.
- **Material**: `M_Skeleton`, the same material and UV layout as the body.
  - The lid UVs are a squeezed forehead patch, so the lids are plain bone colour.
  - The bottom vertex row samples the dark socket texel, which gives a soft dark lower edge, like a lash line.
- **Pivot**: the file origin is the **jaw hinge**, the same point as `Skeleton_Jaw.fbx`: Blender (0, 0.030, 1.645), Unity model space (0, 1.645, -0.030) at FBX scale 1.
- **Shape keys**, imported as Unity blend shapes:
  - `Basis` = **open**. The lids are rolled into a thin strip 12 mm behind the brow surface, so they are invisible even if the blend shapes fail to import.
  - `Blink_L` = character's **left** lid closed (+X in Blender).
  - `Blink_R` = character's **right** lid closed.
  - Each key is a weight from 0 to 1 in Blender, which Unity reads as 0-100. Winks are possible with one key alone.
- **Clips**: both keys move together, with linear keys at 30 fps.
  - `Blink`: 0, 45, 100, 100 (hold), 100, 70, 25, 0. It closes in 2 frames, holds for 2 frames, then opens over 3 frames.
  - `BlinkDouble`: 0, 50, 100, 100, 55, 35, 70, 100, 100, 70, 35, 10, 0. It half-opens, then closes again.

Checked in Blender from the front, from 3/4, from the side, and from below and above. Open, the lids are fully hidden. Closed, the edges are flush with the rims.

Why this setup and not lid bones or a shape key on the skull:
- Blend shapes on a **separate rigid object under the Head** don't depend on the Humanoid avatar. Humanoid retargeting drops the animation of extra bones, but it doesn't touch child objects.
- `Skeleton.fbx`, its avatar, FootIK and the torch arm tuning stay untouched. Re-exporting the body with a skull shape key would mean re-importing the 65-bone rig.
- It is the same pattern the jaw already uses (`JawPivot`).

## Unity integration (later — nothing is wired yet)
1. **Import**: copy the three FBXs to `Assets/Art/Characters/Skeleton/`. Import settings:
   - All three: Scale Factor **1.201179**, Use File Scale on (the same as `Skeleton_Jaw.fbx`).
   - Model tab: Import BlendShapes on, **Blend Shape Normals = Calculate**. The exporter writes no shape normals, so without Calculate the closed lid gets the open strip's normals.
   - **Read/Write Enabled** on, which `FirstPersonBodyCull` needs. Materials: remap to `M_Skeleton`.
   - `Skeleton_Lids.fbx`: Rig = **None**, so no extra Animator is added.
   - `Skeleton_Lids@Blink.fbx` / `@BlinkDouble.fbx`: Rig = **Generic**, no avatar. Animation tab: Loop Time off. The clips animate `blendShape.Blink_L` / `blendShape.Blink_R` on the root, path "". Check the exact blend shape names on the imported SkinnedMeshRenderer.
2. **Placement**, on the `Skeleton` model child in SampleScene and TestScene, or on the prefab:
   - Under `mixamorig:Head`, create `LidPivot` as a sibling of `JawPivot`. Give it the **same local position as JawPivot** (0, 0.0114, -0.0281), rotation identity and scale 1. Do not parent it under JawPivot, which rotates when the jaw talks.
   - Drop a `Skeleton_Lids` instance under `LidPivot` at local position 0, and keep the FBX import rotation (like the jaw). It imports as a `SkinnedMeshRenderer` because it has blend shapes.
   - On the SkinnedMeshRenderer, turn on **Update When Offscreen**. The mesh bounds are the open strip, so without it the closed lid can be culled at screen edges. It only costs 196 verts.
3. **First person (needs one code change)**: `FirstPersonBodyCull` masks SkinnedMeshRenderers by their bone weights.
   - The lid SMR has **no bones**, so its mask would be 0 and the local player would see the lids from inside the skull.
   - Fix: in the `SkinnedMeshRenderer` branch, when `smr.bones.Length == 0 && r.transform.IsChildOf(_maskRoot)`, fill the mask with 1, the same as the rigid jaw branch.
   - `HeldTorchLighting` and the ToonOutline shader need nothing extra.
4. **Trigger**: use a small script on the model child, not an Animator layer. `PlayerAnimator` is rebuilt by `LocomotionControllerBuilder`, and a Humanoid layer would need the clip curves re-pathed to `.../mixamorig:Head/LidPivot/Skeleton_Lids`. Sketch:
   ```csharp
   [DefaultExecutionOrder(70)]
   public class SkullBlink : MonoBehaviour
   {
       [SerializeField] SkinnedMeshRenderer _lids;
       [SerializeField] AnimationClip _blink, _blinkDouble;
       [SerializeField] Vector2 _interval = new(3f, 7f);      // seconds between blinks
       [SerializeField, Range(0, 1)] float _doubleChance = 0.2f;
       [SerializeField] RetroAnimationStepper _stepper;        // optional: blink on the body's 24 fps steps
       AnimationClip _clip; float _t, _next;

       void OnEnable() => _next = Time.time + Random.Range(_interval.x, _interval.y);

       void LateUpdate()
       {
           if (_clip == null)
           {
               if (Time.time < _next) return;
               _clip = Random.value < _doubleChance ? _blinkDouble : _blink; _t = 0f;
           }
           _t += Time.deltaTime;
           bool stepFrame = _stepper == null || _stepper.LastStepFrame == Time.frameCount;
           if (stepFrame || _t >= _clip.length) _clip.SampleAnimation(_lids.gameObject, Mathf.Min(_t, _clip.length));
           if (_t >= _clip.length) { _clip = null; _next = Time.time + Random.Range(_interval.x, _interval.y); }
       }
   }
   ```
   - The last sample at `clip.length` makes sure the lids always end fully open.
   - Stop blinking or close the lids when dead (`CharacterVitals.Died`).
   - Optionally blink right after landing, a scare or a torch throw, so the blinks react to what happens in the game.
   - If `SampleAnimation` ever misbehaves, drive the lids with `SetBlendShapeWeight(index, w)` from the key tables above (weight x100).
