"""
reduce_fbx.py — Headless polygon reduction for FBX files using Blender.

Runs inside Blender (its bundled Python). Imports an FBX, applies a Decimate
modifier to every mesh, and re-exports an FBX. UVs, normals and the object
hierarchy are preserved.

USAGE (from a terminal)
-----------------------
    blender --background --python reduce_fbx.py -- <input> [output] [options]

  <input>   An .fbx file, OR a directory (batch mode: every *.fbx inside).
  [output]  Output .fbx (single file) or output directory (batch). Optional —
            if omitted a sibling file/dir suffixed with "_reduced" is used.

OPTIONS
-------
  --ratio R          COLLAPSE ratio in (0,1]. 0.5 = keep ~50% of faces. Default 0.5
  --target-faces N   Reduce each mesh to ~N triangles (overrides --ratio).
  --mode MODE        collapse (default) | unsubdiv | planar
  --iterations N     UNSUBDIV iterations (mode=unsubdiv). Default 2
  --angle DEG        PLANAR angle limit in degrees (mode=planar). Default 5
  --triangulate      Triangulate before decimating (more predictable collapse).
  --keep-normals     Recalculate custom split normals after decimation.
  --scale S          Apply FBX export global scale. Default 1.0
  --dry-run          Report before/after triangle counts without writing output.

EXAMPLES
--------
  blender -b --python reduce_fbx.py -- model.fbx --ratio 0.4
  blender -b --python reduce_fbx.py -- model.fbx model_lod1.fbx --target-faces 5000
  blender -b --python reduce_fbx.py -- ./Assets/3DModels --ratio 0.5
  blender -b --python reduce_fbx.py -- model.fbx --mode planar --angle 8
"""

import sys
import os
import argparse

import bpy


# --------------------------------------------------------------------------- #
# Argument parsing — everything after the literal "--" belongs to this script. #
# --------------------------------------------------------------------------- #
def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []

    p = argparse.ArgumentParser(
        prog="reduce_fbx.py",
        description="Reduce FBX polygons with Blender's Decimate modifier.",
    )
    p.add_argument("input", help="Input .fbx file or directory (batch).")
    p.add_argument("output", nargs="?", default=None,
                   help="Output .fbx file or directory. Default: <name>_reduced.")
    p.add_argument("--ratio", type=float, default=0.5,
                   help="COLLAPSE keep-ratio in (0,1]. Default 0.5.")
    p.add_argument("--target-faces", type=int, default=0,
                   help="Target triangle count per mesh (overrides --ratio).")
    p.add_argument("--mode", choices=["collapse", "unsubdiv", "planar"],
                   default="collapse", help="Decimation mode. Default collapse.")
    p.add_argument("--iterations", type=int, default=2,
                   help="UNSUBDIV iterations. Default 2.")
    p.add_argument("--angle", type=float, default=5.0,
                   help="PLANAR angle limit in degrees. Default 5.")
    p.add_argument("--triangulate", action="store_true",
                   help="Triangulate meshes before decimating.")
    p.add_argument("--keep-normals", action="store_true",
                   help="Recalculate split normals after decimation.")
    p.add_argument("--scale", type=float, default=1.0,
                   help="FBX export global scale. Default 1.0.")
    p.add_argument("--dry-run", action="store_true",
                   help="Report tri counts only; do not write output.")
    return p.parse_args(argv)


# --------------------------------------------------------------------------- #
# Helpers                                                                      #
# --------------------------------------------------------------------------- #
def reset_scene():
    """Start from an empty scene so batch runs don't accumulate objects."""
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mesh_tri_count():
    total = 0
    for obj in bpy.data.objects:
        if obj.type == "MESH":
            total += sum(p.loop_total - 2 for p in obj.data.polygons)
    return total


def decimate_object(obj, args):
    """Apply a Decimate modifier (and optional triangulate) to one mesh."""
    bpy.context.view_layer.objects.active = obj

    if args.triangulate and args.mode == "collapse":
        tri = obj.modifiers.new(name="LOD_Triangulate", type="TRIANGULATE")
        tri.quad_method = "BEAUTY"
        tri.ngon_method = "BEAUTY"

    mod = obj.modifiers.new(name="LOD_Decimate", type="DECIMATE")

    if args.mode == "collapse":
        mod.decimate_type = "COLLAPSE"
        if args.target_faces > 0:
            current = sum(p.loop_total - 2 for p in obj.data.polygons)
            mod.ratio = min(1.0, max(0.0, args.target_faces / max(1, current)))
        else:
            mod.ratio = min(1.0, max(0.0, args.ratio))
        mod.use_collapse_triangulate = True
    elif args.mode == "unsubdiv":
        mod.decimate_type = "UNSUBDIV"
        mod.iterations = args.iterations
    else:  # planar
        mod.decimate_type = "DISSOLVE"
        mod.angle_limit = args.angle * 3.14159265358979 / 180.0

    # Apply every modifier so the reduction is baked into the mesh data.
    for m in list(obj.modifiers):
        try:
            bpy.ops.object.modifier_apply(modifier=m.name)
        except RuntimeError as exc:
            print(f"  [warn] could not apply {m.name} on {obj.name}: {exc}")

    if args.keep_normals:
        try:
            obj.data.calc_normals_split()
        except Exception:
            pass


def process_file(in_path, out_path, args):
    reset_scene()

    ext = os.path.splitext(in_path)[1].lower()
    if ext != ".fbx":
        print(f"[skip] not an FBX: {in_path}")
        return False

    bpy.ops.import_scene.fbx(filepath=in_path)

    before = mesh_tri_count()
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if not meshes:
        print(f"[skip] no meshes found in {in_path}")
        return False

    for obj in meshes:
        decimate_object(obj, args)

    after = mesh_tri_count()
    pct = (after / before * 100.0) if before else 0.0
    name = os.path.basename(in_path)
    print(f"[{name}] {before:>8,} -> {after:>8,} tris  ({pct:5.1f}% kept)")

    if args.dry_run:
        return True

    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    # Select everything so the exporter writes the full hierarchy.
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=out_path,
        use_selection=False,
        global_scale=args.scale,
        apply_unit_scale=True,
        bake_space_transform=False,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        path_mode="COPY",
        embed_textures=False,
    )
    print(f"        written -> {out_path}")
    return True


def default_output(in_path):
    base, ext = os.path.splitext(in_path)
    return f"{base}_reduced{ext}"


def main():
    args = parse_args()
    in_path = os.path.abspath(args.input)

    if not os.path.exists(in_path):
        print(f"[error] input does not exist: {in_path}")
        sys.exit(1)

    # Batch mode: input is a directory.
    if os.path.isdir(in_path):
        out_dir = args.output or (in_path.rstrip("/\\") + "_reduced")
        out_dir = os.path.abspath(out_dir)
        fbxs = [f for f in os.listdir(in_path) if f.lower().endswith(".fbx")]
        if not fbxs:
            print(f"[error] no .fbx files in {in_path}")
            sys.exit(1)
        print(f"Batch: {len(fbxs)} file(s) from {in_path} -> {out_dir}")
        ok = 0
        for f in sorted(fbxs):
            if process_file(os.path.join(in_path, f),
                            os.path.join(out_dir, f), args):
                ok += 1
        print(f"Done. {ok}/{len(fbxs)} processed.")
        return

    # Single file.
    out_path = os.path.abspath(args.output) if args.output else default_output(in_path)
    if process_file(in_path, out_path, args):
        print("Done.")
    else:
        sys.exit(1)


if __name__ == "__main__":
    main()
