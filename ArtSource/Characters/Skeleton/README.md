# Skeleton_LowPoly (player character)

Low-poly remake of the Meshy AI T-pose skeleton for Fluffterror. 1 Blender unit = 1 m, feet at Z=0, faces -Y, origin at the feet.
One material (`M_Skeleton`), one texture `T_Skeleton_D.png` (1024), shared by body and jaw.

## v3 (current)
| File | Contents | Tris |
|---|---|---|
| `Skeleton_LowPoly.fbx` | body **without** the lower jaw (closed skull, dark palate). Upload this one to Mixamo. | 5,470 |
| `Skeleton_Jaw.fbx` | lower jaw (mandible + lower teeth), separate closed mesh, **pivot = hinge** at the file origin | 320 |
| `Skeleton_LowPoly.blend` | both objects (`Skeleton_LowPoly`, `Skeleton_Jaw` with origin at the hinge) | |

Breakdown: skull 1,466 · jaw 320 · torso 1,154 · pelvis ≈480 · hands 1,448 · arms/legs/feet ≈920. Total 5,790.

### Jaw hinge (temporomandibular joint)
- Blender: **(0, 0.030, 1.645)** m (Z-up, the model faces -Y)
- Unity model space (Y-up, faces +Z): **(0, 1.645, -0.030)**. Measured from the feet, at FBX scale 1.
- Opening = rotation around the hinge's X axis. In Blender, +X rotation opens it. Tested at 20–22°: there are no holes, the cut faces and the palate are dark, and the neck shows through the mouth.

### Jaw setup in Unity (jaw is not part of the Mixamo rig)
1. Under the rigged character, find `mixamorig:Head`. Create an empty child `JawPivot` and set its **world** position to the hinge: character root + (0, 1.645, -0.030) in T-pose (multiply by the model's scale factor if it isn't 1).
2. Drop `Skeleton_Jaw` (prefab from `Skeleton_Jaw.fbx`) under `JawPivot` at local position 0. Keep whatever rotation the FBX import gives the mesh, and use the same material as the body.
3. A script drives `JawPivot.localRotation = Quaternion.Euler(openAngle, 0, 0)` from the voice amplitude (0–22°). Check the sign once in the Editor, because it depends on the Head bone's local axes.
   Alternative, if a real bone is wanted later: in Blender, add a `Jaw` bone (child of Head) at the hinge to the Mixamo rig, join the jaw mesh with 100% weight to it, and map it to the Humanoid **Jaw** slot.

## Pipeline (scripts in this folder)
- `derive_core.py`: head / torso / pelvis + jaw from the Meshy source (`Downloads/Meshy_AI_T-Pose Skeleton_.../model.obj`, imported as `REF_Full` and moved +0.998 m in Z).
  1. Region cut: drops the arms and the leg/femur tops.
  2. Octree remesh, drop small islands, mirror +X.
  3. Carve the eye and nose sockets.
  4. Voxel remesh (3.5 mm), symmetrize.
  5. Exact boolean with a jaw cutter: side profile below the mouth line z=1.600, rising to the hinge, minus the neck column. This gives a closed skull and a closed jaw.
  6. Decimate with X symmetry, weighted by region, then clean up non-manifold geometry.
- `build_skel.py`: procedural arms, 5-finger hands, legs, feet (`LEGACY_UPPER` / `LEGACY_PELVIS` = True rebuilds the v1/v2 parts).
- `assemble.py`: runs both, joins them, Smart UV, then bakes. The reference base colour is baked Selected-to-Active onto the derived parts. The kept parts get the painted local-AO palette, tinted to the reference colour. Dark masks go on the sockets, the mouth cut faces and the palate / inner jaw. Last, it splits off the jaw and puts its origin at the hinge.
  Run in Blender: `g={'__file__': r'...\assemble.py'}; exec(open(g['__file__']).read(), g); g['main']()` (about 90 s).

## Export settings
FBX: selected mesh only, Apply Scale = FBX All, forward -Z, up Y, no leaf bones, no embedded textures. The body re-imports at 1.994 m.
The jaw is exported with its object location zeroed, so the hinge is at the file origin.

## History
- v1 (`*_v1`): fully procedural, 4,804 tris.
- v2 (`*_v2`): head and torso from the reference, procedural pelvis, jaw fused to the skull, 5,492 tris.
