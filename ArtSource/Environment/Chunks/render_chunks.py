# Renders each CH_* chunk (3/4 view without ceiling + an eye-level interior view) and a kit sheet.
# Run in Blender after build_chunks.py:  exec(open(r"<repo>/ArtSource/Environment/Chunks/render_chunks.py").read())
import bpy, math, os
from mathutils import Vector

OUT = os.environ.get("CHUNK_RENDER_DIR",
                     r"C:\Users\redux\AppData\Local\Temp\claude\4684f6bfa92d93dd\1f66dc47-1f2b-49ae-8a28-9bf60f528a76\scratchpad\chunks")

# eye-level interior view per chunk, natural coords (x east, y north, z up): eye, target
EYE = {
    "CH_Corridor_Straight": ((0, -3.6, 1.7), (0, 3, 1.6)),
    "CH_Corridor_Corner": ((-0.5, 3.6, 1.7), (1.5, -1.5, 1.4)),
    "CH_Corridor_T": ((0, 3.6, 1.7), (0, -1, 1.5)),
    "CH_Corridor_Cross": ((0, -3.6, 1.7), (0, 2, 1.6)),
    "CH_Corridor_DeadEnd": ((0, -3.6, 1.7), (0, 2, 1.2)),
    "CH_Room_Storage": ((-0.5, -3.6, 1.7), (1.5, 2, 1.0)),
    "CH_Room_PillarHall": ((-4, -7.4, 1.8), (2, 5, 1.6)),
    "CH_Room_Crypt": ((0, -7.4, 1.7), (0, 3, 1.0)),
    "CH_Room_Ritual": ((-4, -7.4, 1.8), (0, 2, 0.8)),
    "CH_SP_CollapsedCorridor": ((0.5, -3.6, 1.6), (0.5, 1, 0.9)),
    "CH_SP_FloodedCrypt": ((0, -7.4, 1.7), (0, 2, -0.2)),
    "CH_SP_PitBridge": ((0.3, -7.4, 1.8), (0, 0, -1.0)),
    "CH_SP_BellTower": ((-0.5, -3.6, 1.6), (0.5, 1.5, 6.5)),
    "CH_SP_ChuchoDen": ((0, -7.4, 1.7), (-0.5, 4, 0.3)),
    "CH_Generator_Alcove": ((0, -3.6, 1.7), (0, 2, 1.0)),
    "CH_Extraction_Gate": ((-4, -7.4, 1.8), (0, 5, 1.6)),
    "CH_Room_Library": ((1.8, -7.4, 1.7), (-0.5, 0, 1.4)),
    "CH_Room_Larder": ((2.0, -3.6, 1.7), (-1.0, 1.5, 1.4)),
    "CH_SP_Ossuary": ((0.8, -7.4, 1.7), (-0.5, 1.0, 1.4)),
    "CH_Room_Chapel": ((0, -7.4, 1.7), (0, 5, 2.2)),
    "CH_Room_PrisonBlock": ((0, -7.4, 1.7), (0, 3, 1.4)),
    "CH_SP_WellRoom": ((0, -3.6, 1.7), (0, 0.5, 0.6)),
    "CH_Room_Mausoleum": ((0, -7.4, 1.8), (0, 4, 0.9)),
    "CH_SP_SewerChannel": ((-1.5, -7.4, 1.7), (1.5, 2, -0.4)),
    "CH_SP_CaveTransition": ((-1.0, -7.4, 1.7), (0.6, 3, 1.5)),
    "CH_Room_SpiralHall": ((-4, -7.4, 1.8), (4.0, 4.0, 2.5)),
    "CH_Corridor_Chewed": ((0, -3.6, 1.7), (0, 2, 1.0)),
    "CH_SP_TrapCorridor": ((0.5, -3.6, 1.7), (-0.5, 1.5, -0.6)),
    "CH_Junction_BrokenFloor": ((1.5, -3.6, 1.7), (-1.5, 1.0, -0.8)),
    "CH_Corridor_Straight_B": ((0, -3.6, 1.7), (0, 2, 1.2)),
    "CH_Corridor_Corner_B": ((-0.5, 3.6, 1.7), (-1.0, -1.2, 0.8)),
    "CH_Corridor_T_B": ((0, 3.6, 1.7), (-2.5, -0.5, 2.2)),
}
PREFIX = "r2_"


ENGINE = os.environ.get("CHUNK_RENDER_ENGINE", "WORKBENCH")   # "EEVEE": texture x baked vertex colour, lit


def setup_eevee(sc):
    for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
        try:
            sc.render.engine = eng
            break
        except TypeError:
            pass
    sc.eevee.taa_render_samples = 8
    w = sc.world or bpy.data.worlds.new("World")
    sc.world = w
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.16, 0.17, 0.2, 1)
    bg.inputs[1].default_value = 1.0
    sun = bpy.data.objects.get("InspectSun")
    if not sun:
        sun = bpy.data.objects.new("InspectSun", bpy.data.lights.new("InspectSun", 'SUN'))
        sc.collection.objects.link(sun)
    sun.data.energy = 2.2
    sun.data.color = (1.0, 0.85, 0.7)
    sun.rotation_euler = (math.radians(40), math.radians(15), math.radians(150))


def setup():
    sc = bpy.context.scene
    if ENGINE == "EEVEE":
        setup_eevee(sc)
    else:
        try:
            sc.render.engine = 'BLENDER_WORKBENCH'
        except TypeError as e:
            print(e)
    sh = sc.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'TEXTURE'
    sh.show_backface_culling = True
    sh.show_shadows = True
    sh.shadow_intensity = 0.6
    sh.show_cavity = True
    sh.cavity_type = 'WORLD'
    sh.background_type = 'VIEWPORT'
    sc.display.shadow_focus = 0.2
    sc.render.film_transparent = False
    sc.view_settings.view_transform = 'Standard'
    cam = bpy.data.objects.get("InspectCam")
    if not cam:
        cam = bpy.data.objects.new("InspectCam", bpy.data.cameras.new("InspectCam"))
        sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.clip_end = 500
    return sc, cam


def cv(root, p):
    return Vector((-p[0] + root.location.x, -p[1] + root.location.y, p[2] + root.location.z))


def look_at(cam, e, t):
    cam.location = e
    cam.rotation_euler = (t - e).to_track_quat('-Z', 'Y').to_euler()


def set_ceilings(visible):
    for o in bpy.data.objects:
        if o.name.endswith("_Ceiling"):
            o.hide_render = not visible
        if o.name.startswith(("COL_", "VOL_", "SOCKET_")):
            o.hide_render = True


def render(path, w, h):
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.resolution_percentage = 100
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def r_dims(coll):
    xs = [o.dimensions.x for o in coll.objects if o.name.startswith("SM_")]
    ys = [o.dimensions.y for o in coll.objects if o.name.startswith("SM_")]
    return max(xs), max(ys)


def render_all(names=None, skip=("_H36", "_H50")):
    os.makedirs(OUT, exist_ok=True)
    sc, cam = setup()
    roots = [o for o in bpy.data.objects if o.name.startswith("CH_") and o.type == 'EMPTY']
    out = []
    for r in sorted(roots, key=lambda o: o.location.x):
        if names and r.name not in names:
            continue
        if not names and r.name.endswith(skip):
            continue
        coll = bpy.data.collections[r.name]
        W = max(o.dimensions.x for o in coll.objects if o.name.startswith("SM_"))
        Hh = max(o.dimensions.z for o in coll.objects if o.name.startswith("SM_"))
        # isolate this chunk
        for c in bpy.data.collections:
            if c.name.startswith("CH_"):
                c.hide_render = (c != coll)
        # 3/4 overview from the south-west, above, no ceiling
        set_ceilings(False)
        cam.data.lens = 28
        size = max(max(r_dims(coll)), Hh * 0.8)
        e = cv(r, (-0.45 * size, -0.75 * size, 1.25 * size + Hh * 0.5))
        look_at(cam, e, cv(r, (0, 0, -0.5)))
        p1 = os.path.join(OUT, PREFIX + r.name + "_overview.png")
        render(p1, 1000, 750)
        # orthographic plan (north up) for the level-designer
        cam.data.type = 'ORTHO'
        cam.data.ortho_scale = max(r_dims(coll)) + 1.0
        look_at(cam, cv(r, (0, 0, 40)), cv(r, (0, 0.0001, 0)))
        cam.rotation_euler = (0, 0, math.pi)
        p3 = os.path.join(OUT, PREFIX + r.name + "_plan.png")
        render(p3, 800, 800)
        cam.data.type = 'PERSP'
        out.append(p3)
        # eye-level interior, with ceiling
        set_ceilings(True)
        if r.name in EYE:
            cam.data.lens = 16
            e, t = EYE[r.name]
            look_at(cam, cv(r, e), cv(r, t))
            p2 = os.path.join(OUT, PREFIX + r.name + "_interior.png")
            render(p2, 1000, 750)
            out.append(p2)
        out.append(p1)
    for c in bpy.data.collections:
        if c.name.startswith("CH_"):
            c.hide_render = False
    set_ceilings(True)
    return out


def kit_render():
    """One render of every chunk on its grid slot with name labels (ceilings hidden)."""
    sc, cam = setup()
    for c in bpy.data.collections:
        if c.name.startswith("CH_") or c.name == "Labels":
            c.hide_render = False
    set_ceilings(False)
    roots = [o for o in bpy.data.objects if o.name.startswith("CH_") and o.type == 'EMPTY']
    xs = [o.location.x for o in roots]; ys = [o.location.y for o in roots]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    cam.data.type = 'PERSP'
    # orthographic, straight down, north up (Blender -Y), labels under each chunk
    cy_mid = (min(ys) + max(ys)) / 2 + 2.0
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = max(max(xs) - min(xs) + 26, (max(ys) - min(ys) + 34) * 4 / 3)
    cam.location = Vector((cx, cy_mid, 80))
    cam.rotation_euler = (0, 0, math.pi)
    path = os.path.join(OUT, PREFIX + "chunks_kit_sheet.png")
    render(path, 2400, 1800)
    cam.data.type = 'PERSP'
    set_ceilings(True)
    return path


def kit_sheet(cols=4, tile=(500, 375)):
    """Stitch the *_overview.png renders into one labelled-by-order kit sheet."""
    import numpy as np
    files = sorted(f for f in os.listdir(OUT) if f.endswith("_overview.png"))
    order = list(EYE.keys())
    files = sorted(files, key=lambda f: order.index(f.replace("_overview.png", "")) if f.replace("_overview.png", "") in order else 99)
    rows = math.ceil(len(files) / cols)
    tw, th = tile
    sheet = np.zeros((rows * th, cols * tw, 4), dtype=np.float32)
    sheet[..., 3] = 1
    for k, f in enumerate(files):
        img = bpy.data.images.load(os.path.join(OUT, f), check_existing=False)
        iw, ih = img.size
        px = np.array(img.pixels[:], dtype=np.float32).reshape(ih, iw, 4)
        # nearest downsample to the tile
        ys = (np.arange(th) * ih / th).astype(int)
        xs = (np.arange(tw) * iw / tw).astype(int)
        small = px[ys][:, xs]
        r, c = divmod(k, cols)
        y0 = (rows - 1 - r) * th       # blender image origin = bottom-left
        sheet[y0:y0 + th, c * tw:(c + 1) * tw] = small
        bpy.data.images.remove(img)
    out = bpy.data.images.new("KitSheet", cols * tw, rows * th, alpha=True)
    out.pixels = sheet.ravel().tolist()
    path = os.path.join(OUT, "chunks_kit_sheet.png")
    out.filepath_raw = path
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)
    return path, files
