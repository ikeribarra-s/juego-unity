# Fluffterror - hand-modelled pickup props: Map (folded parchment) and SaltBag (burlap sack), Blender 4.5.
#
#   g = {}; exec(open(r"<repo>/ArtSource/Props/build_map_saltbag.py").read(), g)
#   g["build_all"]()            # builds both, saves Props_Map.blend / Props_SaltBag.blend (copies), exports FBX + PNG
#
# Same conventions as ArtSource/Props/Meshy/process_meshy.py: 1 unit = 1 m, pivot at the base centre of the bounding box,
# front = Blender -Y (Unity +Z), flat shading, 128 px Point texture painted from a fixed palette with a 4x4 Bayer dither,
# Cycles AO -> corner colour attribute "Col" (multiplied by PS2Lit), same FBX export settings.
# Everything is deterministic (seeded) so re-running reproduces the same files.
import bpy, bmesh, os, math, random
import numpy as np
from mathutils import Vector, Matrix

REPO = r"C:\Users\redux\Desktop\proyectos\juego-unity\juego"
SRC = os.path.join(REPO, "ArtSource", "Props")
OUT = {"Map": os.path.join(REPO, "Assets", "Art", "Props", "Map"),
       "SaltBag": os.path.join(REPO, "Assets", "Art", "Props", "SaltBag")}

BAYER = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) + 0.5) / 16 - 0.5
TEX = 128


def hx(h):
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32) / 255.0


# ------------------------------------------------------------------ helpers
def clean_scene():
    for o in list(bpy.data.objects):
        if o.type in {'MESH', 'CURVE', 'EMPTY'}:
            bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for d in list(coll):
            if d.users == 0 or True:
                coll.remove(d)


def newell(pts):
    n = Vector((0, 0, 0))
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    return n


class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")

    def face(self, pts, uvs, away=None, hint=None):
        pts = [Vector(p) for p in pts]
        uvs = list(uvs)
        n = newell(pts)
        c = sum(pts, Vector()) / len(pts)
        flip = False
        if away is not None:
            flip = n.dot(c - Vector(away)) < 0
        elif hint is not None:
            flip = n.dot(Vector(hint)) < 0
        if flip:
            pts.reverse(); uvs.reverse()
        vs = [self.bm.verts.new(p) for p in pts]
        try:
            f = self.bm.faces.new(vs)
        except ValueError:
            return None
        for l, uv in zip(f.loops, uvs):
            l[self.uv].uv = uv
        return f

    def box(self, center, size, yaw=0.0, uvrect=(0, 0, 0.1, 0.1), pitch=0.0):
        """closed box, all faces mapped to one solid uv rect (u0, v0, u1, v1)."""
        c = Vector(center)
        sx, sy, sz = (s / 2 for s in size)
        R = Matrix.Rotation(yaw, 3, 'Z') @ Matrix.Rotation(pitch, 3, 'X')
        P = [c + R @ Vector((x * sx, y * sy, z * sz)) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
        idx = {(x, y, z): P[i] for i, (x, y, z) in enumerate((x, y, z) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1))}
        u0, v0, u1, v1 = uvrect
        q = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
        faces = [
            [(-1, -1, -1), (-1, 1, -1), (-1, 1, 1), (-1, -1, 1)], [(1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1)],
            [(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)], [(-1, 1, -1), (1, 1, -1), (1, 1, 1), (-1, 1, 1)],
            [(-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1)], [(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)],
        ]
        for f in faces:
            self.face([idx[k] for k in f], q, away=c)

    def finish(self, name):
        bm = self.bm
        bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def sel(o):
    for x in bpy.context.scene.objects:
        x.select_set(x == o)
    bpy.context.view_layer.objects.active = o


def ground(ob):
    """pivot at the bottom centre of the bounding box."""
    vs = [v.co for v in ob.data.vertices]
    cx = (min(v.x for v in vs) + max(v.x for v in vs)) / 2
    cy = (min(v.y for v in vs) + max(v.y for v in vs)) / 2
    cz = min(v.z for v in vs)
    ob.data.transform(Matrix.Translation((-cx, -cy, -cz)))


def make_material(name, arr):
    """arr: (TEX, TEX, 3) top-down float image -> bpy image + Blender material (Closest)."""
    img = bpy.data.images.new("T_%s_D" % name, TEX, TEX, alpha=False)
    rgba = np.ones((TEX, TEX, 4), dtype=np.float32)
    rgba[..., :3] = arr[::-1]
    img.pixels = rgba.ravel().tolist()
    mat = bpy.data.materials.new("M_" + name)
    mat.use_nodes = True
    nt = mat.node_tree
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = img; tn.interpolation = 'Closest'
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 1.0
    return img, mat


def ao_vertex(lo, lo_min=0.55):
    sc = bpy.context.scene
    try:
        sc.render.engine = 'CYCLES'
    except TypeError:
        pass
    sc.cycles.device = 'CPU'; sc.cycles.samples = 64
    me = lo.data
    for at in list(me.color_attributes):
        me.color_attributes.remove(at)
    ca = me.color_attributes.new("Col", 'FLOAT_COLOR', 'CORNER'); me.color_attributes.active_color = ca
    sel(lo)
    b = sc.render.bake
    b.use_selected_to_active = False; b.target = 'VERTEX_COLORS'
    bpy.ops.object.bake(type='AO')
    ca = me.color_attributes["Col"]
    for d in ca.data:
        v = lo_min + (1 - lo_min) * d.color[0]
        d.color = (v, v * 0.985, v * 0.96, 1)
    for p in me.polygons:
        p.use_smooth = False


def export(lo, name):
    os.makedirs(OUT[name], exist_ok=True)
    lo.location = (0, 0, 0)
    sel(lo)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT[name], "SM_%s.fbx" % name), use_selection=True, object_types={'MESH'},
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             mesh_smooth_type='FACE', add_leaf_bones=False, path_mode='STRIP', embed_textures=False,
                             colors_type='LINEAR', prioritize_active_color=True)


def save_png(img, name):
    os.makedirs(OUT[name], exist_ok=True)
    img.filepath_raw = os.path.join(OUT[name], "T_%s_D.png" % name)
    img.file_format = 'PNG'
    img.save()


def snap(rgb, pal, dither):
    """nearest palette colour with ordered dither; rgb (h,w,3), pal (n,3)."""
    h, w = rgb.shape[:2]
    d = np.tile(BAYER, (h // 4 + 1, w // 4 + 1))[:h, :w, None] * dither
    q = rgb + d
    wgt = np.array([0.35, 0.5, 0.15])
    dist = (((q[..., None, :] - pal[None, None]) ** 2) * wgt).sum(-1)
    return pal[np.argmin(dist, -1)]


def vnoise(h, w, cell, rng):
    """smooth value noise in 0..1 (bilinear upsample of random grid)."""
    g = rng.random((h // cell + 2, w // cell + 2))
    ys, xs = np.mgrid[0:h, 0:w]
    fy, fx = ys / cell, xs / cell
    y0, x0 = fy.astype(int), fx.astype(int)
    ty, tx = fy - y0, fx - x0
    ty, tx = ty * ty * (3 - 2 * ty), tx * tx * (3 - 2 * tx)
    a = g[y0, x0] * (1 - tx) + g[y0, x0 + 1] * tx
    b = g[y0 + 1, x0] * (1 - tx) + g[y0 + 1, x0 + 1] * tx
    return a * (1 - ty) + b * ty


GLYPH = {"S": ["111", "100", "111", "001", "111"], "A": ["010", "101", "111", "101", "101"], "L": ["100", "100", "100", "100", "111"]}


# ------------------------------------------------------------------ MAP
MAP_W, MAP_D = 0.30, 0.20      # x width, y depth (m)
# atlas regions (top-down pixels): top face 96x64 at (0,0); paper edge 96x16 at (0,64); underside 32x16 at (96,0); string 32x16 at (96,16)


def map_texture():
    rng = np.random.default_rng(7)
    parch = np.array([hx(c) for c in ["8A7248", "A68C5C", "BCA06A", "CDB57C", "DCC690"]])
    ink = hx("3A2A18"); ink2 = hx("5A3E22"); red = hx("8E2229"); red2 = hx("B3202A")
    sea = np.array([hx("7C8A76"), hx("6A7C6C")])
    A = np.zeros((TEX, TEX, 3), dtype=np.float32) + hx("8A7248")
    W, H = 96, 64
    # parchment base: low-freq stains + burnt edges
    n = vnoise(H, W, 8, rng) * 0.6 + vnoise(H, W, 3, rng) * 0.4
    yy, xx = np.mgrid[0:H, 0:W]
    edge = np.minimum.reduce([xx, W - 1 - xx, yy, H - 1 - yy]).astype(np.float32)
    burn = np.clip(1 - edge / 7.0, 0, 1) * (0.55 + 0.6 * vnoise(H, W, 4, rng))
    v = np.clip(0.62 + (n - 0.5) * 0.55 - burn * 0.55, 0, 1)
    idx = np.clip(np.floor((v + np.tile(BAYER, (H // 4, W // 4)) * 0.18) * len(parch)).astype(int), 0, len(parch) - 1)
    img = parch[idx].copy()
    # land mass: union of noisy blobs
    land = np.zeros((H, W), bool)
    cn = vnoise(H, W, 7, rng) * 0.55 + vnoise(H, W, 3, rng) * 0.45
    for cx, cy, rx, ry in [(30, 31, 18, 11), (47, 23, 9, 8), (69, 40, 14, 9), (19, 47, 9, 6), (79, 17, 7, 6)]:
        d = ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2
        land |= d < (0.15 + 1.25 * cn) * 0.95
    land &= (edge > 7)
    # sea wash: faded blue-green dither everywhere inside the frame that is not land
    inner = edge > 6
    wash = inner & ~land & (((xx + yy) % 2 == 0) & (vnoise(H, W, 4, rng) > 0.3))
    img[wash] = img[wash] * 0.5 + sea[(xx[wash] // 2 + yy[wash]) % 2] * 0.5
    # land a touch lighter
    lf = land & (vnoise(H, W, 6, rng) > 0.3)
    img[lf] = parch[min(len(parch) - 1, 3)]
    # coast outline
    nb = np.zeros_like(land)
    nb[1:, :] |= land[:-1, :]; nb[:-1, :] |= land[1:, :]; nb[:, 1:] |= land[:, :-1]; nb[:, :-1] |= land[:, 1:]
    coast = nb & ~land
    img[coast] = ink
    # sea wave dashes
    for _ in range(26):
        x, y = int(rng.integers(8, W - 12)), int(rng.integers(8, H - 8))
        if land[y, x] or coast[y, x] or land[y, min(W - 1, x + 4)]:
            continue
        for k, dy in enumerate((0, -1, 0, -1)):
            if not land[y + dy, x + k]:
                img[y + dy, x + k] = ink2
    # hills (^) on land
    for _ in range(14):
        x, y = int(rng.integers(8, W - 10)), int(rng.integers(8, H - 8))
        if land[y, x] and land[y, x + 3] and land[y - 2, x + 1]:
            img[y, x] = img[y, x + 2] = ink2; img[y - 1, x + 1] = ink2
    # towers / crypts
    for x, y in [(60, 36), (24, 36)]:
        if land[y, x]:
            img[y - 3:y + 1, x:x + 3] = ink
            img[y - 4, x] = img[y - 4, x + 2] = ink
    # frame: double ink line
    for off, col in ((4, ink), (6, ink2)):
        img[off, off:W - off] = col; img[H - 1 - off, off:W - off] = col
        img[off:H - off, off] = col; img[off:H - off, W - 1 - off] = col
    # dotted red route to the X
    pts = [(12, 52), (22, 47), (31, 40), (37, 33), (47, 30), (55, 27), (64, 31), (70, 36), (74, 42)]
    dots = []
    for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
        L = max(abs(x1 - x0), abs(y1 - y0))
        for i in range(0, L, 3):
            t = i / L
            dots.append((int(round(x0 + (x1 - x0) * t)), int(round(y0 + (y1 - y0) * t))))
    for x, y in dots:
        img[y, x] = red2; img[y, x + 1] = red
    ex, ey = pts[-1]
    for d in range(-3, 4):
        for t in (0, 1):
            img[ey + d, ex + d + t] = red2
            img[ey + d, ex - d + t] = red2
    img[ey, ex] = red
    # compass rose bottom right
    cx, cy = 82, 18
    for d in range(-6, 7):
        img[cy + d, cx] = ink; img[cy, cx + d] = ink
    for d in range(-2, 3):
        img[cy + d, cx + abs(d) - 2 + 1] = ink2
    img[cy - 7, cx] = ink; img[cy - 8, cx] = ink
    g = ["101", "111", "111", "101", "101"]   # N
    for r, row in enumerate(g):
        for c, ch in enumerate(row):
            if ch == "1":
                img[cy - 15 + r, cx - 1 + c] = ink
    A[0:H, 0:W] = img
    # paper edge band 96x16: thin alternating layers
    band = np.zeros((16, 96, 3), np.float32)
    for r in range(16):
        base = parch[3] if r % 3 else parch[1]
        band[r] = base
    nn = vnoise(16, 96, 3, rng)
    band[nn > 0.72] = parch[2]; band[nn < 0.22] = parch[1]
    A[64:80, 0:96] = band
    # underside
    A[0:16, 96:128] = parch[1]
    # string: twisted rope
    rope = np.array([hx("6A5A3A"), hx("8A7650"), hx("5A4A2E")])
    s = np.zeros((16, 32, 3), np.float32)
    for yv in range(16):
        for xv in range(32):
            s[yv, xv] = rope[((xv + yv) // 2) % 2]
    A[16:32, 96:128] = s
    return A


def build_map():
    clean_scene()
    rng = random.Random(11)
    B = Builder()
    uvu = lambda x: (x + MAP_W / 2) / MAP_W * 96 / TEX                 # u of the top face
    uvv = lambda y: 1 - ((MAP_D / 2 - y) / MAP_D * 64) / TEX            # v: +Y at the image top (row 0)
    xs = [-0.15, -0.10, -0.05, 0.0, 0.05, 0.10, 0.15]
    ys = [-0.10, -0.05, 0.0, 0.05, 0.10]
    fold = {-0.15: 0.012, -0.05: 0.024, 0.05: 0.012, 0.15: 0.024}       # accordion panel heights at the creases
    def zprof(x):
        ks = sorted(fold)
        for a, b in zip(ks[:-1], ks[1:]):
            if a <= x <= b:
                return fold[a] + (fold[b] - fold[a]) * (x - a) / (b - a)
    nx, ny = len(xs), len(ys)
    top = {}
    for i, x in enumerate(xs):
        for j, y in enumerate(ys):
            px, py, pz = x, y, zprof(x)
            if i in (0, nx - 1) or j in (0, ny - 1):    # ragged edge + curled corners
                px += rng.uniform(-0.0035, 0.0035) * (1 if i in (0, nx - 1) else 0.4)
                py += rng.uniform(-0.0035, 0.0035) * (1 if j in (0, ny - 1) else 0.4)
            if i in (0, nx - 1) and j in (0, ny - 1):
                pz += 0.004
            pz += rng.uniform(-0.0012, 0.0012)
            top[i, j] = Vector((px, py, pz))
    for i in range(nx - 1):
        for j in range(ny - 1):
            q = [top[i, j], top[i + 1, j], top[i + 1, j + 1], top[i, j + 1]]
            B.face(q, [(uvu(p.x), uvv(p.y)) for p in q], hint=(0, 0, 1))
    # boundary ring (ccw seen from above) for the sides / underside
    ring = [(i, 0) for i in range(nx)] + [(nx - 1, j) for j in range(1, ny)] + [(i, ny - 1) for i in range(nx - 2, -1, -1)] + [(0, j) for j in range(ny - 2, -1, -1)]
    cen = Vector((0, 0, 0.008))
    perim = 0.0
    segs = []
    for a, b in zip(ring, ring[1:] + ring[:1]):
        d = (top[b] - top[a]).xy.length
        segs.append((perim, perim + d)); perim += d
    bandv0 = 1 - 66 / TEX; bandv1 = 1 - 78 / TEX
    for (a, b), (s0, s1) in zip(zip(ring, ring[1:] + ring[:1]), segs):
        ta, tb = top[a], top[b]
        ba, bb = Vector((ta.x, ta.y, 0)), Vector((tb.x, tb.y, 0))
        u0, u1 = s0 / perim * 96 / TEX, s1 / perim * 96 / TEX
        B.face([ba, bb, tb, ta], [(u0, bandv1), (u1, bandv1), (u1, bandv0), (u0, bandv0)], away=cen)
    under = [Vector((top[k].x, top[k].y, 0)) for k in ring]
    B.face(under, [(104 / TEX, 1 - 8 / TEX)] * len(under), hint=(0, 0, -1))
    # a couple of loose sheets peeking out of the stack (paper layers)
    sheet = (0, 64 / TEX * 0 + (1 - 72 / TEX), 96 / TEX, 1 - 66 / TEX)
    B.box((0.004, 0.0035, 0.0045), (0.296, 0.202, 0.0045), yaw=math.radians(2.2), uvrect=(0.05, 1 - 74 / TEX, 0.40, 1 - 70 / TEX))
    B.box((-0.003, -0.002, 0.0095), (0.292, 0.198, 0.0035), yaw=math.radians(-1.6), uvrect=(0.05, 1 - 74 / TEX, 0.40, 1 - 70 / TEX))
    # string band across the middle panel, wrapped around the stack (runs along Y at x=0)
    d, w = 0.0028, 0.0065
    zt = lambda y: zprof(0.0) + 0.0   # middle panel height at x=0 (ragged noise ignored)
    ringp = [(-0.1045, zt(0) + d)] + [(y, zt(y) + d) for y in (-0.05, 0.0, 0.05)] + [(0.1045, zt(0) + d), (0.1045, -d), (-0.1045, -d)]
    su0, sv0, su1, sv1 = 98 / TEX, 1 - 28 / TEX, 126 / TEX, 1 - 18 / TEX
    for k in range(len(ringp)):
        (y0, z0), (y1, z1) = ringp[k], ringp[(k + 1) % len(ringp)]
        B.face([(-w, y0, z0), (w, y0, z0), (w, y1, z1), (-w, y1, z1)], [(su0, sv0), (su1, sv0), (su1, sv1), (su0, sv1)], away=(0, 0, 0.012))
    # bow on top: knot + two loops + two tails (thin boxes, solid rope uv)
    zb = zt(0) + d + 0.0015
    ru = (98 / TEX, 1 - 28 / TEX, 126 / TEX, 1 - 18 / TEX)
    B.box((0, 0, zb + 0.003), (0.012, 0.012, 0.006), uvrect=ru)
    B.box((-0.0125, 0.003, zb + 0.0025), (0.017, 0.006, 0.0025), yaw=math.radians(25), uvrect=ru)
    B.box((0.0125, 0.003, zb + 0.0025), (0.017, 0.006, 0.0025), yaw=math.radians(-25), uvrect=ru)
    B.box((-0.006, -0.014, zb + 0.001), (0.005, 0.022, 0.0022), yaw=math.radians(-18), uvrect=ru)
    B.box((0.007, -0.016, zb + 0.001), (0.005, 0.024, 0.0022), yaw=math.radians(20), uvrect=ru)
    ob = B.finish("SM_Map")
    ground(ob)
    arr = map_texture()
    img, mat = make_material("Map", arr)
    ob.data.materials.append(mat)
    save_png(img, "Map")
    ao_vertex(ob)
    export(ob, "Map")
    return ob


# ------------------------------------------------------------------ SALT BAG
def salt_texture():
    rng = np.random.default_rng(21)
    cloth = np.array([hx(c) for c in ["6B5A3E", "7E6C4C", "93805C", "A8946A", "BCA878"]])
    ink = hx("2A2438"); dark = hx("1E1812")
    salt = np.array([hx(c) for c in ["9AA2AC", "BCC3CB", "DDE1E6", "F2F4F6", "FFFFFF"]])
    rope = np.array([hx("6A5A3A"), hx("8A7650"), hx("5A4A2E")])
    A = np.zeros((TEX, TEX, 3), np.float32) + cloth[2]
    H, W = 96, 128
    yy, xx = np.mgrid[0:H, 0:W]
    # burlap weave: 2x2 thread blocks with alternating over/under, plus thread-to-thread brightness jitter
    wx, wy = xx // 2, yy // 2
    weave = ((wx + wy) % 2).astype(np.float32)
    thread = rng.random((H // 2 + 1, W // 2 + 1))[wy, wx]
    low = vnoise(H, W, 10, rng)
    v = 0.5 + (weave - 0.5) * 0.22 + (thread - 0.5) * 0.30 + (low - 0.5) * 0.5
    # darker toward the bottom (dirt), a few horizontal fold shadows
    v -= np.clip((yy - 70) / 40.0, 0, 1) * 0.25
    v -= (np.sin(yy / 6.0 + low * 4) > 0.9) * 0.15
    idx = np.clip(np.floor((v + BAYER[yy % 4, xx % 4] * 0.1) * len(cloth)).astype(int), 0, len(cloth) - 1)
    body = cloth[idx].copy()
    # patch seam stitches down the back (u ~ 0..0.04 / 0.96..1)
    for yv in range(0, H, 4):
        body[yv:yv + 2, 1] = dark; body[yv:yv + 2, 126] = dark
    # stencil SAL on the front faces: u 0.40..0.60 -> px 51..77, rows by height. 2x vertical scale for legibility
    ox, oy = 47, 47
    for gi, ch in enumerate("SAL"):
        for r, row in enumerate(GLYPH[ch]):
            for c, bit in enumerate(row):
                if bit == "1":
                    x0 = ox + gi * 8 + c * 2; y0 = oy + r * 3
                    body[y0:y0 + 3, x0:x0 + 2] = ink
    # stencil frame line above / below
    body[oy - 4, 45:71:2] = ink; body[oy + 17, 45:71:2] = ink
    A[0:H, 0:W] = body
    # rope (rows 96..112, x 64..128)
    r = np.zeros((16, 64, 3), np.float32)
    for yv in range(16):
        for xv in range(64):
            r[yv, xv] = rope[((xv + yv) // 2) % 2 if (xv * 7 + yv * 3) % 11 else 2]
    A[96:112, 64:128] = r
    return A


def build_salt():
    clean_scene()
    rng = random.Random(5)
    B = Builder()
    N = 10
    # profile rings: (z, radius, centre x offset)
    prof = [(0.000, 0.052), (0.010, 0.068), (0.045, 0.075), (0.085, 0.072), (0.125, 0.056), (0.150, 0.038), (0.162, 0.030)]
    frill = [(0.176, 0.040), (0.200, 0.055), (0.222, 0.044)]
    allp = prof + frill
    H = allp[-1][0]
    lean = lambda z: 0.006 * (z / H) ** 1.5
    rings = []
    for k, (z, r) in enumerate(allp):
        pts = []
        for i in range(N):
            a = math.radians(36 * i)
            jr = 1.0 + (rng.uniform(-0.05, 0.05) if k < len(prof) else rng.uniform(-0.12, 0.10))
            jz = rng.uniform(-0.002, 0.002) if k >= len(prof) - 1 else 0.0
            if k == 0:
                jz = 0.0
            pts.append(Vector((math.cos(a) * r * jr + lean(z), math.sin(a) * r * jr, z + jz)))
        rings.append(pts)
    BU = 128.0 / TEX
    def uv_of(i, k):
        u = ((i - 3) % N) / N
        return u
    caxis = lambda z: Vector((lean(z), 0, z))
    for k in range(len(rings) - 1):
        for i in range(N):
            i2 = (i + 1) % N
            a, b, c, d = rings[k][i], rings[k][i2], rings[k + 1][i2], rings[k + 1][i]
            u0 = ((i - 3) % N) / N
            u1 = u0 + 1.0 / N      # i2 may be the seam vertex: continuous u (1.0 at the seam end)
            z0, z1 = allp[k][0], allp[k + 1][0]
            v0 = 1 - (96 - z0 / H * 96) / TEX
            v1 = 1 - (96 - z1 / H * 96) / TEX
            # keep v inside the body region (rows 0..96)
            f = B.face([a, b, c, d], [(u0, v0 - 0.0), (u1, v0), (u1, v1), (u0, v1)], away=(lean((z0 + z1) / 2), 0, (z0 + z1) / 2))
    # bottom cap + top cap (dipped centre)
    bot = rings[0]
    B.face(bot, [(0.2 + 0.1 * math.cos(math.radians(36 * i)), 0.85 + 0.1 * math.sin(math.radians(36 * i))) for i in range(N)], hint=(0, 0, -1))
    topr = rings[-1]
    cz = H - 0.012
    ctr = Vector((lean(H), 0, cz))
    for i in range(N):
        i2 = (i + 1) % N
        B.face([topr[i], topr[i2], ctr], [(0.3, 0.97), (0.34, 0.97), (0.32, 0.93)], hint=(0, 0, 1))
    # neck tie: short banded tube just under the gathered neck, plus knot and two tails
    zt0, zt1, rt = 0.153, 0.168, 0.0
    ru = (68 / TEX, 1 - 110 / TEX, 124 / TEX, 1 - 98 / TEX)
    def ringat(z, k):
        # interpolate profile radius at z and add a little thickness
        for (za, ra), (zb, rb) in zip(allp[:-1], allp[1:]):
            if za <= z <= zb:
                return ra + (rb - ra) * (z - za) / (zb - za)
    for i in range(N):
        i2 = (i + 1) % N
        a0 = math.radians(36 * i); a1 = math.radians(36 * (i2 if i2 else N))
        r0 = ringat(zt0, 0) * 1.0 + 0.004; r1 = ringat(zt1, 0) + 0.004
        p = lambda a, r, z: Vector((math.cos(a) * r + lean(z), math.sin(a) * r, z))
        B.face([p(a0, r0, zt0), p(a1, r0, zt0), p(a1, r1, zt1), p(a0, r1, zt1)], [(ru[0], ru[1]), (ru[2], ru[1]), (ru[2], ru[3]), (ru[0], ru[3])], away=(lean(zt0), 0, zt0))
    # knot at the front (-Y) + tails dangling down the front of the neck
    fr = ringat(0.160, 0) + 0.004
    B.box((lean(0.16), -fr - 0.003, 0.160), (0.012, 0.008, 0.012), yaw=math.radians(8), uvrect=ru)
    B.box((lean(0.15) - 0.007, -fr - 0.005, 0.147), (0.005, 0.0035, 0.026), yaw=math.radians(-6), uvrect=ru)
    B.box((lean(0.15) + 0.008, -fr - 0.005, 0.148), (0.005, 0.0035, 0.022), yaw=math.radians(10), uvrect=ru)
    # (no spilled salt: the bag is a clean, usable item; the salt is poured at runtime by SaltBagUse)
    ob = B.finish("SM_SaltBag")
    ground(ob)
    arr = salt_texture()
    img, mat = make_material("SaltBag", arr)
    ob.data.materials.append(mat)
    save_png(img, "SaltBag")
    ao_vertex(ob)
    export(ob, "SaltBag")
    return ob


def report(ob):
    d = ob.dimensions
    return {"name": ob.name, "tris": tris(ob), "verts": len(ob.data.vertices), "dims_m": tuple(round(x, 4) for x in d)}


def build_all():
    out = []
    for fn, nm in ((build_map, "Props_Map.blend"), (build_salt, "Props_SaltBag.blend")):
        ob = fn()
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SRC, nm), copy=True)
        out.append(report(ob))
    return out


def preview(ob, path, az=-30, el=45, dist=0.5, res=(900, 700), vcol=True):
    """quick Cycles preview with a camera aimed at the object's bounding-box centre. vcol=True multiplies the AO colour in (as PS2Lit does)."""
    sc = bpy.context.scene
    try:
        sc.render.engine = 'CYCLES'
    except TypeError:
        pass
    sc.cycles.samples = 24; sc.cycles.device = 'CPU'
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.image_settings.file_format = 'PNG'
    sc.render.film_transparent = False
    if sc.world is None:
        sc.world = bpy.data.worlds.new("W")
    sc.world.use_nodes = True
    bg = next(n for n in sc.world.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs[0].default_value = (0.32, 0.32, 0.34, 1); bg.inputs[1].default_value = 1.0
    mat = ob.data.materials[0]; nt = mat.node_tree
    for n in [n for n in nt.nodes if n.type == 'VERTEX_COLOR' or n.name == "AOmul"]:
        nt.nodes.remove(n)
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tn = next(n for n in nt.nodes if n.type == 'TEX_IMAGE')
    if vcol:
        vc = nt.nodes.new("ShaderNodeVertexColor"); vc.layer_name = "Col"
        mul = nt.nodes.new("ShaderNodeMix"); mul.name = "AOmul"; mul.data_type = 'RGBA'; mul.blend_type = 'MULTIPLY'
        mul.inputs[0].default_value = 1.0
        nt.links.new(tn.outputs["Color"], mul.inputs[6]); nt.links.new(vc.outputs["Color"], mul.inputs[7])
        nt.links.new(mul.outputs[2], bsdf.inputs["Base Color"])
    else:
        nt.links.new(tn.outputs["Color"], bsdf.inputs["Base Color"])
    cam = bpy.data.objects.get("PrevCam")
    if cam is None:
        cam = bpy.data.objects.new("PrevCam", bpy.data.cameras.new("PrevCam")); sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.lens = 50
    d = ob.dimensions
    c = Vector((0, 0, d.z / 2))
    a, e = math.radians(az), math.radians(el)
    cam.location = c + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * dist
    cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
    for L in [o for o in bpy.data.objects if o.type == 'LIGHT']:
        bpy.data.objects.remove(L, do_unlink=True)
    sun = bpy.data.objects.new("PrevSun", bpy.data.lights.new("PrevSun", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 3.0; sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path
