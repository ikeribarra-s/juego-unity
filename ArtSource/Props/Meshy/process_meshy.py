# Fluffterror — PS2 processing for Meshy-generated hero props (Blender 4.5, run inside MeshyLibrary.blend).
#
#   g = {}; exec(open(r"<repo>/ArtSource/Props/Meshy/process_meshy.py").read(), g)
#   g["process"]("Meshy_Altar", **g["PROPS"]["Altar"])
#
# Steps per prop (hi-poly Meshy import -> SM_Meshy<Name>):
#   1. scale to fit the target box (uniform, or per-axis when stretch=True), origin at bottom/top centre
#   2. decimate to the budget (weld + collapse; voxel remesh first when collapse stalls on non-manifold seams)
#   3. Smart UV project, bake the Meshy albedo onto it (selected-to-active, Cycles)
#   4. palette-snap the bake: nearest colour from the prop's ArtDirection ramps with a 4x4 ordered dither,
#      Point / no mips in Unity, 64-256 px
#   5. Cycles AO -> corner vertex colour "Col" (0.5..1, linear), flat shading
#   6. export Assets/Art/Environment/Props/Meshy/SM_Meshy<Name>.fbx (+ T_Meshy<Name>_D.png)
# Orientation: Meshy fronts face Blender -Y, which the FBX settings map to Unity +Z, i.e. the HERO_* socket's
# forward in the chunk prefabs. Hi-poly originals are archived outside the repo (../meshy_sources/*.glb).
import bpy, bmesh, os, math
import numpy as np
from mathutils import Vector, Matrix

REPO = r"C:\Users\redux\Desktop\proyectos\juego-unity\juego"
OUTD = os.path.join(REPO, r"Assets\Art\Environment\Props\Meshy")
ARCH = os.path.join(os.path.dirname(REPO), "meshy_sources")


def hx(h):
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


RAMPS = {
    "stone": ["5E5142", "6C5C49", "7A6B57", "90816E", "A39280"],
    "stone_dk": ["2B241E", "463B30", "5E5142"],
    "rock": ["3A342E", "4A433B", "5B5248", "6A6156"],
    "moss": ["3D4A3A", "4A5A3E"],
    "wood": ["2E1F14", "47311F", "5A3F2A", "6E4E33", "7E5B3C"],
    "iron": ["1E1F22", "2E2F33", "3C3E44", "50525A"],
    "bronze": ["4A3818", "5C4520", "74582A", "8A6A2E", "A88440", "C49A5A"],
    "bone": ["5E5648", "8A8070", "B5AA8E", "CFC6AE", "E8E2D0"],
    "straw": ["5E5030", "7E6C3E", "9C8650", "B39B5C"],
    "dirt": ["3B3027", "4A3D30", "54463A"],
    "meat": ["5A2A24", "8C4A3E", "B0705A"],
    "wax": ["B8AE92", "D9D0B4"],
    "red": ["4A1216", "7A1E24", "B3202A"],
    "amber": ["C2502A", "FFB347"],
    "rope": ["6A5A3A", "8A7650"],
    "rock_grey": ["2E2E32", "3E3D42", "504F55", "64626A", "7A7880"],   # cold cave limestone (stalagnate)
    "rust": ["3A2418", "5C3A2A", "7A4A30"],
    "water": ["1E3A40", "2C5560"],
    "cloth": ["4A4238", "5E5648", "786E5C"],
}

# target box (x = width, y = depth front-back, z = height) in metres, budget, texture px, pivot, palette
PROPS = {
    # yaw: rotate the Meshy model about Z before fitting so its front faces Blender -Y (Unity +Z)
    # cut_below: drop everything under this fraction of the raw height (winch: keep posts + beam, not the well)
    "Effigy":     dict(box=(1.3, 2.6, 1.2), tris=1500, px=256, pivot="bottom", pal=["stone", "stone_dk", "iron"], yaw=-90),
    "Angel":      dict(box=(0.9, 0.7, 1.6), tris=1500, px=128, pivot="bottom", pal=["stone", "stone_dk"], dither=0.015),
    "Lectern":    dict(box=(1.3, 0.75, 1.3), tris=800, px=128, pivot="bottom", pal=["wood", "bone", "stone_dk", "red"]),
    "Nest":       dict(box=(3.4, 3.4, 0.55), tris=2000, px=256, pivot="bottom", pal=["straw", "bone", "wood", "dirt"], stretch=True),
    "Altar":      dict(box=(2.5, 1.45, 1.2), tris=1200, px=256, pivot="bottom", pal=["stone_dk", "stone", "iron", "wax", "red", "amber"]),
    "Butcher":    dict(box=(2.4, 1.0, 1.1), tris=1500, px=256, pivot="bottom", pal=["wood", "iron", "meat", "straw", "bone"], stretch=True),
    "Winch":      dict(box=(2.4, 0.8, 1.45), tris=900, px=128, pivot="bottom", pal=["wood", "iron", "rope"], cut_below=0.46),
    "Chandelier": dict(box=(1.5, 1.5, 1.6), tris=900, px=128, pivot="top", pal=["iron", "wax", "amber"], voxel=0.02),
    "Stalagnate": dict(box=(1.5, 1.5, 5.0), tris=1000, px=256, pivot="bottom", pal=["rock_grey"], stretch=True, dither=0.02),
    # batch 2 (library, not placed in chunks yet)
    "Coffin":      dict(box=(0.75, 2.0, 0.6), tris=900, px=256, pivot="bottom", pal=["wood", "iron"], yaw=90, stretch=True),
    "CoffinAjar":  dict(box=(0.8, 2.0, 1.0), tris=1200, px=256, pivot="bottom", pal=["wood", "iron", "bone"], yaw=90, stretch=True),
    "Tombstones":  dict(box=(2.2, 1.2, 1.3), tris=1200, px=256, pivot="bottom", pal=["stone", "stone_dk", "moss", "dirt"]),
    "Candelabra":  dict(box=(0.7, 0.7, 1.7), tris=700, px=128, pivot="bottom", pal=["iron", "wax", "amber"]),
    "Cage":        dict(box=(1.0, 1.0, 2.2), tris=900, px=128, pivot="top", pal=["iron", "rust", "bone"]),
    "Shackles":    dict(box=(1.2, 0.6, 1.4), tris=600, px=128, pivot="bottom", pal=["iron", "rust", "stone_dk"], voxel=0.012),
    "Stocks":      dict(box=(1.7, 0.8, 1.3), tris=900, px=128, pivot="bottom", pal=["wood", "iron"]),
    "BonePile":    dict(box=(1.5, 1.5, 0.6), tris=1200, px=256, pivot="bottom", pal=["bone", "dirt"], voxel=0.012),
    "Urns":        dict(box=(1.1, 0.8, 0.9), tris=900, px=128, pivot="bottom", pal=["dirt", "stone", "bone", "meat"]),
    "Sacks":       dict(box=(1.4, 1.1, 1.0), tris=900, px=128, pivot="bottom", pal=["cloth", "straw", "rope", "dirt"]),
    "DogBowl":     dict(box=(1.3, 1.3, 0.85), tris=800, px=128, pivot="bottom", pal=["iron", "bone", "red"], voxel=0.012),
    "Mushrooms":   dict(box=(0.9, 0.9, 0.7), tris=600, px=128, pivot="bottom", pal=["bone", "moss", "rock_grey"]),
    "Font":        dict(box=(1.0, 1.0, 1.1), tris=900, px=128, pivot="bottom", pal=["stone", "stone_dk", "water"]),
    "Brazier":     dict(box=(0.9, 0.9, 1.2), tris=900, px=128, pivot="bottom", pal=["iron", "amber", "bone", "red"]),
    "SewerValve":  dict(box=(1.1, 0.8, 1.3), tris=700, px=128, pivot="bottom", pal=["iron", "rust", "moss"]),
}

# Blender asset library: catalogue + suggested chunk per prop (tags on the asset)
LIB = {
    "Bell": ("Structural", "CH_SP_BellTower"), "Skull": ("Funerary", "CH_SP_Ossuary"),
    "Angel": ("Funerary", "CH_Room_Mausoleum"), "Effigy": ("Funerary", "CH_Room_Mausoleum"),
    "Lectern": ("Furniture", "CH_Room_Library"), "Nest": ("Debris", "CH_SP_ChuchoDen"),
    "Altar": ("Ritual", "CH_Room_Ritual"), "Butcher": ("Furniture", "CH_Room_Larder"),
    "Winch": ("Structural", "CH_SP_WellRoom"), "Chandelier": ("Lighting", "CH_Room_Chapel"),
    "Stalagnate": ("Structural", "CH_SP_CaveTransition"),
    "Coffin": ("Funerary", "CH_Room_Crypt"), "CoffinAjar": ("Funerary", "CH_Room_Crypt"),
    "Tombstones": ("Funerary", "CH_Room_Mausoleum"), "Candelabra": ("Lighting", "CH_Room_Chapel"),
    "Cage": ("Structural", "CH_Room_PrisonBlock"), "Shackles": ("Structural", "CH_Room_PrisonBlock"),
    "Stocks": ("Furniture", "CH_Room_PrisonBlock"), "BonePile": ("Debris", "CH_SP_Ossuary"),
    "Urns": ("Containers", "CH_SP_Ossuary"), "Sacks": ("Containers", "CH_Room_Storage"),
    "DogBowl": ("Containers", "CH_SP_ChuchoDen"), "Mushrooms": ("Debris", "CH_SP_CaveTransition"),
    "Font": ("Ritual", "CH_Room_Chapel"), "Brazier": ("Lighting", "CH_Room_Ritual"),
    "SewerValve": ("Structural", "CH_SP_SewerChannel"),
}
CATEGORIES = ["Furniture", "Containers", "Lighting", "Funerary", "Ritual", "Debris", "Structural"]

BAYER = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) + 0.5) / 16 - 0.5


def _tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def _sel(objs, active):
    sc = bpy.context.scene
    for o in sc.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = active


def fit(hi, box, pivot, stretch=False, yaw=0.0):
    """scale + rotate the hi-poly in place, origin at bottom (or top) centre of the target box."""
    hi.rotation_mode = 'XYZ'                 # Meshy imports come in QUATERNION mode
    hi.rotation_euler = (0, 0, math.radians(yaw))
    _sel([hi], hi)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    d = hi.dimensions
    sx, sy, sz = box[0] / max(d.x, 1e-4), box[1] / max(d.y, 1e-4), box[2] / max(d.z, 1e-4)
    if stretch:
        s = (sx, sy, sz)
    else:
        u = min(sx, sy, sz)
        s = (u, u, u)
    hi.scale = s
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    ws = [hi.matrix_world @ v.co for v in hi.data.vertices]
    cx = (min(p.x for p in ws) + max(p.x for p in ws)) / 2
    cy = (min(p.y for p in ws) + max(p.y for p in ws)) / 2
    cz = max(p.z for p in ws) if pivot == "top" else min(p.z for p in ws)
    off = hi.matrix_world.inverted() @ Vector((cx, cy, cz))
    hi.data.transform(Matrix.Translation(-off))
    hi.location = (0, 0, 0)


def decimate(lo, target, voxel=None):
    _sel([lo], lo)
    if voxel:   # chain links etc.: fuse tiny closed islands first, collapse can't reduce them
        m = lo.modifiers.new("rm", 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = voxel; m.use_smooth_shade = False
        bpy.ops.object.modifier_apply(modifier=m.name)
    bm = bmesh.new(); bm.from_mesh(lo.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005)
    bm.to_mesh(lo.data); bm.free()
    for _ in range(5):
        t = _tris(lo)
        if t <= target:
            return "collapse"
        m = lo.modifiers.new("dec", 'DECIMATE'); m.ratio = max(0.0005, target * 0.97 / t)
        bpy.ops.object.modifier_apply(modifier=m.name)
    if _tris(lo) <= target * 1.05:
        return "collapse"
    # stalled on non-manifold seams: planar dissolve opens them up for another round of collapse
    bm = bmesh.new(); bm.from_mesh(lo.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=max(lo.dimensions) * 0.004)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(6), verts=bm.verts, edges=bm.edges)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(lo.data); bm.free()
    for _ in range(5):
        t = _tris(lo)
        if t <= target:
            return "collapse+dissolve"
        m = lo.modifiers.new("dec", 'DECIMATE'); m.ratio = max(0.0005, target * 0.97 / t)
        bpy.ops.object.modifier_apply(modifier=m.name)
    if _tris(lo) <= target * 1.1:
        return "collapse+dissolve"
    # still stalled (non-manifold seams): voxel remesh at ~1/120 of the size, then collapse
    vs = max(lo.dimensions) / 250.0
    m = lo.modifiers.new("rm", 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = vs; m.use_smooth_shade = False
    bpy.ops.object.modifier_apply(modifier=m.name)
    for _ in range(6):
        t = _tris(lo)
        if t <= target:
            break
        m = lo.modifiers.new("dec", 'DECIMATE'); m.ratio = max(0.01, target * 0.97 / t)
        bpy.ops.object.modifier_apply(modifier=m.name)
    return "remesh %.3f" % vs


def bake_albedo(hi, lo, name, px):
    sc = bpy.context.scene
    try:
        sc.render.engine = 'CYCLES'
    except TypeError:
        pass
    sc.cycles.device = 'CPU'; sc.cycles.samples = 16
    _sel([lo], lo)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.03)
    bpy.ops.object.mode_set(mode='OBJECT')
    imgname = "T_Meshy%s_D" % name
    old = bpy.data.images.get(imgname)
    if old:
        bpy.data.images.remove(old)
    img = bpy.data.images.new(imgname, px, px)
    mat = bpy.data.materials.get("M_Meshy" + name) or bpy.data.materials.new("M_Meshy" + name)
    mat.use_nodes = True; nt = mat.node_tree
    tn = next((n for n in nt.nodes if n.type == 'TEX_IMAGE'), None) or nt.nodes.new("ShaderNodeTexImage")
    tn.image = img; tn.interpolation = 'Closest'; nt.nodes.active = tn
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    lo.data.materials.clear(); lo.data.materials.append(mat)
    _sel([hi, lo], lo)
    size = max(lo.dimensions)
    b = sc.render.bake
    b.use_selected_to_active = True; b.cage_extrusion = size * 0.015; b.max_ray_distance = size * 0.04
    b.use_pass_direct = False; b.use_pass_indirect = False; b.use_pass_color = True
    b.target = 'IMAGE_TEXTURES'; b.margin = 2
    bpy.ops.object.bake(type='DIFFUSE')
    return img


def palette_snap(img, ramps, path, dither=0.045):
    w, h = img.size
    a = np.array(img.pixels[:]).reshape(h, w, 4)
    rgb = a[..., :3]
    used = rgb.sum(-1) > 0.01
    pal = np.array([hx(c) for r in ramps for c in RAMPS[r]])
    d = np.tile(BAYER, (h // 4, w // 4))[..., None] * dither
    q = rgb + d
    # nearest palette colour, luminance-weighted distance (keeps value structure)
    wgt = np.array([0.35, 0.5, 0.15])
    dist = (((q[..., None, :] - pal[None, None, :, :]) ** 2) * wgt).sum(-1)
    out = pal[np.argmin(dist, axis=-1)]
    out[~used] = out[used].mean(axis=0) if used.any() else out[~used]
    a[..., :3] = out; a[..., 3] = 1
    img.pixels = a.ravel().tolist()
    img.filepath_raw = path; img.file_format = 'PNG'; img.save()


def ao_vertex(lo, lo_min=0.5):
    sc = bpy.context.scene
    me = lo.data
    for at in list(me.color_attributes):
        me.color_attributes.remove(at)
    ca = me.color_attributes.new("Col", 'FLOAT_COLOR', 'CORNER'); me.color_attributes.active_color = ca
    _sel([lo], lo)
    sc.render.bake.use_selected_to_active = False; sc.render.bake.target = 'VERTEX_COLORS'
    sc.cycles.samples = 64
    bpy.ops.object.bake(type='AO')
    ca = me.color_attributes["Col"]
    for dd in ca.data:
        v = lo_min + (1 - lo_min) * dd.color[0]
        dd.color = (v, v * 0.985, v * 0.96, 1)
    for p in me.polygons:
        p.use_smooth = False


def export(lo, name):
    loc = lo.location.copy(); lo.location = (0, 0, 0)
    _sel([lo], lo)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUTD, "SM_Meshy%s.fbx" % name), use_selection=True, object_types={'MESH'},
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             mesh_smooth_type='FACE', add_leaf_bones=False, path_mode='STRIP', embed_textures=False,
                             colors_type='LINEAR', prioritize_active_color=True)
    lo.location = loc


def archive(hi):
    os.makedirs(ARCH, exist_ok=True)
    _sel([hi], hi)
    bpy.ops.export_scene.gltf(filepath=os.path.join(ARCH, hi.name + ".glb"), use_selection=True, export_format='GLB')


def cut(hi, frac):
    """delete geometry below frac of the object's height (world Z), keep the rest."""
    zs = [(hi.matrix_world @ v.co).z for v in hi.data.vertices]
    z = min(zs) + (max(zs) - min(zs)) * frac
    bm = bmesh.new(); bm.from_mesh(hi.data)
    inv = hi.matrix_world.inverted()
    co = inv @ Vector((0, 0, z)); no = (inv.to_3x3().transposed() @ Vector((0, 0, 1))).normalized()
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, clear_inner=True)
    bm.to_mesh(hi.data); bm.free()


def process(hi_name, box, tris, px, pivot, pal, stretch=False, yaw=0.0, slot=0.0, cut_below=None, dither=0.045, voxel=None):
    name = hi_name.replace("Meshy_", "")
    hi = bpy.data.objects[hi_name]
    hi.hide_set(False)
    archive(hi)
    if cut_below:
        cut(hi, cut_below)
    fit(hi, box, pivot, stretch, yaw)
    hi.location = (slot, 0, 0)
    lo = bpy.data.objects.get("SM_Meshy" + name)
    if lo:
        bpy.data.objects.remove(lo, do_unlink=True)
    lo = hi.copy(); lo.data = hi.data.copy(); lo.name = "SM_Meshy" + name; lo.data.name = lo.name
    bpy.context.scene.collection.objects.link(lo)
    lo.data.materials.clear()
    raw = _tris(hi)
    how = decimate(lo, tris, voxel)
    img = bake_albedo(hi, lo, name, px)
    palette_snap(img, pal, os.path.join(OUTD, "T_Meshy%s_D.png" % name), dither)
    hi.hide_set(True)
    ao_vertex(lo)
    export(lo, name)
    dims = tuple(round(x, 3) for x in lo.dimensions)
    return {"name": name, "raw_tris": raw, "tris": _tris(lo), "verts": len(lo.data.vertices), "how": how, "dims": dims, "px": px}


def purge_hi():
    for o in list(bpy.data.objects):
        if o.name.startswith("Meshy_"):
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.outliner.orphans_purge(do_recursive=True)


def import_archive(name):
    """re-import an archived hi-poly (meshy_sources/Meshy_<Name>.glb) as Meshy_<Name> for re-processing."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(ARCH, "Meshy_%s.glb" % name))
    new = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    o = new[0]
    o.name = "Meshy_" + name
    o.parent = None
    return o


# ───────────────────────────── asset library ─────────────────────────────
import uuid as _uuid

LIB_DIR = os.path.dirname(os.path.join(REPO, r"ArtSource\Props\Meshy\x"))


def catalogs():
    """blender_assets.cats.txt next to the .blend (deterministic UUIDs, so re-running keeps the same ids)."""
    ids = {c: str(_uuid.uuid5(_uuid.NAMESPACE_URL, "fluffterror/meshy/" + c)) for c in CATEGORIES}
    lines = ["# Blender asset catalogue for the Fluffterror Meshy prop library (written by process_meshy.py)",
             "VERSION 1", ""]
    lines += ["%s:Meshy/%s:Meshy-%s" % (ids[c], c, c) for c in CATEGORIES]
    with open(os.path.join(LIB_DIR, "blender_assets.cats.txt"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return ids


def mark_library(lay_out=True):
    """mark every SM_Meshy<Name> as an asset: catalogue, tags (tris, chunk, px), description, preview."""
    ids = catalogs()
    objs = sorted([o for o in bpy.data.objects if o.name.startswith("SM_Meshy") and o.type == 'MESH'], key=lambda o: o.name)
    out = []
    for i, o in enumerate(objs):
        name = o.name[len("SM_Meshy"):]
        cat, chunk = LIB.get(name, ("Debris", ""))
        if lay_out:
            o.location = ((i % 7) * 3.5, -(i // 7) * 4.0, 0.0 if PROPS.get(name, {}).get("pivot") != "top" else 2.4)
        if o.asset_data is None:
            o.asset_mark()
        ad = o.asset_data
        ad.catalog_id = ids[cat]
        for t in list(ad.tags):
            ad.tags.remove(t)
        tris = _tris(o)
        img = None
        for m in o.data.materials:
            for n in (m.node_tree.nodes if m and m.node_tree else []):
                if n.type == 'TEX_IMAGE' and n.image:
                    img = n.image
        px = img.size[0] if img else 0
        for t in ("tris:%d" % tris, "chunk:%s" % chunk, "tex:%dpx" % px, "meshy", "ps2"):
            ad.tags.new(t)
        ad.description = "%s - %d tris, %d px Point texture, suggested %s. Unity: Assets/Art/Environment/Props/Meshy/PROP_Meshy%s.prefab" % (name, tris, px, chunk, name)
        ad.author = "Meshy 6 + process_meshy.py"
        out.append((name, cat, tris, px, chunk))
    return out
