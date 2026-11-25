# Fluffterror — authored map chunks builder (Blender 4.5).
#
# Builds every CH_* chunk into Chunks.blend (each under its own root empty, laid out side by side),
# writes the palette atlas T_Chunks_D.png, exports one FBX per chunk into the Unity project and
# writes chunks_manifest.json (footprint, doors, walkable mask, sockets in Unity coords, tri counts).
#
# Run inside Blender:  exec(open(r"<repo>/ArtSource/Environment/Chunks/build_chunks.py").read())
# Headless:            blender --background --python build_chunks.py
#
# Coordinates: everything is authored in "natural" chunk coords (x = East, y = North, z = Up,
# origin = footprint centre at floor level). FBX export (-Z forward, Y up, bake space transform)
# maps Blender (x, y, z) -> Unity (-x, z, -y), so each chunk is rotated 180 deg about Z when it is
# written into Blender (R below). In Chunks.blend North is therefore Blender -Y. In Unity: natural
# (x, y, z) -> Unity (x, z, y) exactly.
import bpy, bmesh, math, random, json, os, zlib, shutil
from mathutils import Vector, Matrix

REPO = r"C:\Users\redux\Desktop\proyectos\juego-unity\juego"
SRC = os.path.join(REPO, r"ArtSource\Environment\Chunks")
UNITY_DIR = os.path.join(REPO, r"Assets\Art\Environment\Chunks")
UNITY_MODELS = os.path.join(UNITY_DIR, "Models")

R = Matrix.Rotation(math.pi, 4, 'Z')          # natural -> Blender export frame
JUT_CHANCE = 0.16
CONSISTENT_STONE = True      # one floor / wall palette key per material (was a per-tile / per-box random pick)
WOOD_FRAMES = os.environ.get("CHUNKS_WOOD_FRAMES", "1") != "0"   # wood door frames + wainscot band
# Meshy hero props (Assets/Art/Environment/Props/Meshy/PROP_Meshy<Name>.prefab): when on, the procedural
# feature they supersede is not built and a HERO_<Name> empty marks where ChunkPrefabBuilder nests the prop.
HERO_PROPS = globals().get("HERO_PROPS", True)                               # jutting wall stones per slot (was 0.45 before the PS2 texture pass)
DOOR_W, DOOR_H = 3.0, 3.0                       # clear opening
WT = 0.5                                        # wall thickness

# ───────────────────────────── palette atlas ─────────────────────────────
PAL = [
    # name, sRGB hex           (row-major into an 8x8 grid of 8 px cells)
    # walls: Modular Dungeon kit Wall_Dark 7A6B57 / Wall_Medium 77634E / Wall_Highlights 90816E (measured in Unity)
    ("wall0", "7A6B57"), ("wall1", "77634E"), ("wall2", "90816E"), ("wall3", "5E5142"),
    ("wall4", "A39280"), ("wall5", "6C5C49"), ("mortar", "2B241E"), ("void", "0D0F14"),
    # floors: kit Grey_Floor 4D4C5A / DarkGrey_Floor 3C3A46 (cool, darker than the walls)
    ("floor0", "4D4C5A"), ("floor1", "3C3A46"), ("floor2", "45444F"), ("floor3", "35343F"),
    ("floor4", "57566A"), ("dirt0", "4A3D30"), ("dirt1", "54463A"), ("dirt2", "3B3027"),
    ("wood0", "5A3F2A"), ("wood1", "6E4E33"), ("wood2", "47311F"), ("wood3", "7E5B3C"),
    ("iron0", "2E2F33"), ("iron1", "3C3E44"), ("rust", "5C3A2A"), ("brass", "8A6A2E"),
    ("bone0", "E8E2D0"), ("bone1", "CFC6AE"), ("bone2", "B5AA8E"), ("wax", "D9D0B4"),
    ("straw0", "9C8650"), ("straw1", "B39B5C"), ("straw2", "7E6C3E"), ("rope", "8A7650"),
    ("red0", "7A1E24"), ("red1", "B3202A"), ("moss", "4A5A3E"), ("green", "3F7F5C"),
    ("green1", "7CFFB2"), ("amber", "FFB347"), ("ember", "C2502A"), ("cloth", "5E5648"),
    ("hazard", "A8842E"), ("brass1", "A88440"), ("stone_dk", "463B30"), ("slime", "3D4A3A"),
    ("water", "1E3A40"), ("rock0", "5B5248"), ("rock1", "4A433B"), ("rock2", "6A6156"),
    ("rock3", "3A342E"), ("crystal", "5FB3C0"), ("glass_r", "9A2E2E"), ("glass_b", "35589A"),
    ("glass_y", "C49A3E"), ("glass_g", "4A8A55"), ("meat0", "8C4A3E"), ("meat1", "B0705A"),
    # PS2 pass: texture-only keys (a patterned panel replaces per-item geometry)
    ("skulls", "B5AA8E"), ("books", "6E4E33"),
    # ritual-room blood pool (appended: palette index seeds rng 1000+i)
    ("blood0", "3A0A0E"), ("blood1", "5C1217"),
    # vertical planks for door jambs (appended: index seeds the pattern)
    ("wood_v", "5A3F2A"),
]
PAL_IDX = {n: i for i, (n, _) in enumerate(PAL)}
WALL = ["wall0", "wall1", "wall5", "wall3"]
ROCK = ["rock0", "rock1", "rock2", "rock3"]
WALL_HI = ["wall2", "wall4", "wall1"]
FLOOR = ["floor0", "floor1", "floor2", "floor3", "floor4"]
DIRT = ["dirt0", "dirt1", "dirt2", "floor3"]
WOOD = ["wood0", "wood1", "wood2", "wood3"]
BONE = ["bone0", "bone1", "bone2"]
STRAW = ["straw0", "straw1", "straw2"]
CELLPX, GRID = 8, 8
TEX = CELLPX * GRID


def srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def make_palette_png(path):
    import struct
    rng = random.Random(1234)
    px = [[(0, 0, 0)] * TEX for _ in range(TEX)]
    for i, (_, hx) in enumerate(PAL):
        r, g, b = (int(hx[k:k + 2], 16) for k in (0, 2, 4))
        cx, cy = i % GRID, i // GRID
        for y in range(CELLPX):
            for x in range(CELLPX):
                # faint ordered 2-tone dither so faces aren't dead flat (PS1 grain)
                d = ((x * 3 + y * 5) % 4 == 0) * 4 - ((x + y * 3) % 7 == 0) * 4 + rng.randint(-1, 1)
                px[cy * CELLPX + y][cx * CELLPX + x] = tuple(max(0, min(255, v + d)) for v in (r, g, b))
    # write PNG (top row first; cell row 0 at the top of the image -> v = 1 - ...)
    raw = b"".join(b"\x00" + bytes([c for p in row for c in p]) for row in px)

    def chunk(t, data):
        c = struct.pack(">I", len(data)) + t + data
        return c + struct.pack(">I", zlib.crc32(t + data) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", TEX, TEX, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def cell_uv(key, s, t):
    """s,t in [0,1] inside the face -> atlas UV inside the cell (1 px margin)."""
    i = PAL_IDX[key]
    cx, cy = i % GRID, i // GRID
    u = (cx * CELLPX + 1 + s * (CELLPX - 2)) / TEX
    v = 1.0 - (cy * CELLPX + 1 + (1 - t) * (CELLPX - 2)) / TEX
    return u, v


exec(open(os.path.join(SRC, "ps2_style.py"), encoding="utf-8").read(), globals())


# ───────────────────────────── mesh builder ─────────────────────────────
class MB:
    def __init__(self, rng):
        self.v, self.f, self.k, self.c = [], [], [], []
        self.rng = rng
        self.cat = "S"

    def pick(self, key):
        """random choice from a list (the rng draw is KEPT so colliders/rocks/jut placement stay put), but
        stone families resolve to one canonical key so floors/walls read as one material."""
        if not isinstance(key, (list, tuple)):
            return key
        k = self.rng.choice(key)
        if not CONSISTENT_STONE:
            return k
        ks = set(key)
        if ks <= set(FLOOR):
            return "floor0"
        if ks <= set(WALL):
            return "wall0"
        if ks <= set(WALL) | set(WALL_HI):
            return "wall2"
        return k

    def poly(self, pts, key):
        b = len(self.v)
        self.v += [Vector(p) for p in pts]
        self.f.append(list(range(b, b + len(pts))))
        self.k.append(self.pick(key))
        self.c.append(self.cat)

    def box(self, x0, y0, z0, x1, y1, z1, key, skip=()):
        """axis-aligned box; skip any of 'b','t','n','s','e','w'."""
        if x0 > x1: x0, x1 = x1, x0
        if y0 > y1: y0, y1 = y1, y0
        if z0 > z1: z0, z1 = z1, z0
        key = self.pick(key)
        c = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
             (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        faces = {'b': (0, 3, 2, 1), 't': (4, 5, 6, 7), 's': (0, 1, 5, 4), 'n': (2, 3, 7, 6),
                 'e': (1, 2, 6, 5), 'w': (3, 0, 4, 7)}
        for n, idx in faces.items():
            if n in skip:
                continue
            self.poly([c[i] for i in idx], key)

    def box_in(self, x0, y0, z0, x1, y1, z1, key, skip=()):
        """box seen from the inside (faces flipped)."""
        n0 = len(self.f)
        self.box(x0, y0, z0, x1, y1, z1, key, skip)
        for i in range(n0, len(self.f)):
            self.f[i] = list(reversed(self.f[i]))

    def obox(self, center, size, rot=(0, 0, 0), key="wall0", skip_bottom=False):
        """oriented box: rot = euler XYZ radians."""
        key = self.pick(key)
        m = Matrix.Translation(Vector(center)) @ (Matrix.Rotation(rot[2], 4, 'Z') @ Matrix.Rotation(rot[1], 4, 'Y') @ Matrix.Rotation(rot[0], 4, 'X'))
        sx, sy, sz = (s * 0.5 for s in size)
        c = [m @ Vector((x, y, z)) for (x, y, z) in
             [(-sx, -sy, -sz), (sx, -sy, -sz), (sx, sy, -sz), (-sx, sy, -sz),
              (-sx, -sy, sz), (sx, -sy, sz), (sx, sy, sz), (-sx, sy, sz)]]
        faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (1, 2, 6, 5), (3, 0, 4, 7)]
        for j, idx in enumerate(faces):
            if skip_bottom and j == 0:
                continue
            self.poly([c[i] for i in idx], key)

    def cyl(self, cx, cy, z0, z1, r, seg=8, key="wall0", r_top=None, caps=(False, True), rot=0.0):
        key = self.pick(key)
        rt = r if r_top is None else r_top
        a = [rot + 2 * math.pi * i / seg for i in range(seg)]
        lo = [(cx + r * math.cos(t), cy + r * math.sin(t), z0) for t in a]
        hi = [(cx + rt * math.cos(t), cy + rt * math.sin(t), z1) for t in a]
        for i in range(seg):
            j = (i + 1) % seg
            if rt < 1e-4:
                self.poly([lo[i], lo[j], hi[i]], key)
            else:
                self.poly([lo[i], lo[j], hi[j], hi[i]], key)
        if caps[0]:
            self.poly(list(reversed(lo)), key)
        if caps[1] and rt > 1e-4:
            self.poly(hi, key)

    def hcyl(self, p0, p1, r, seg=6, key="wood0"):
        """cylinder between two points (beams, bones, pipes, rope)."""
        key = self.pick(key)
        p0, p1 = Vector(p0), Vector(p1)
        d = (p1 - p0)
        q = d.to_track_quat('Z', 'Y').to_matrix()
        ring0, ring1 = [], []
        for i in range(seg):
            t = 2 * math.pi * i / seg
            o = q @ Vector((r * math.cos(t), r * math.sin(t), 0))
            ring0.append(p0 + o); ring1.append(p1 + o)
        for i in range(seg):
            j = (i + 1) % seg
            self.poly([ring0[i], ring0[j], ring1[j], ring1[i]], key)
        self.poly(list(reversed(ring0)), key)
        self.poly(ring1, key)

    def ring(self, cx, cy, z, r0, r1, seg, key):
        for i in range(seg):
            a0, a1 = 2 * math.pi * i / seg, 2 * math.pi * (i + 1) / seg
            self.poly([(cx + r0 * math.cos(a0), cy + r0 * math.sin(a0), z),
                       (cx + r1 * math.cos(a0), cy + r1 * math.sin(a0), z),
                       (cx + r1 * math.cos(a1), cy + r1 * math.sin(a1), z),
                       (cx + r0 * math.cos(a1), cy + r0 * math.sin(a1), z)], key)

    def tris(self, cats=None):
        return sum(len(f) - 2 for f, c in zip(self.f, self.c) if cats is None or c in cats)

    def to_object(self, name, mat, coll, matrix=R, cats=None, origin=(0, 0, 0), bake=None, split=True):
        """origin = where this MB's local (0,0,0) sits in chunk-natural coords (pivoted parts), used for
        world-grid UVs and the vertex-colour bake. split=False keeps faces whole (collision boxes)."""
        origin = Vector(origin)
        polys = []          # (pts, key)
        for f, k, c in zip(self.f, self.k, self.c):
            if cats is not None and c not in cats:
                continue
            pts = [self.v[vi].copy() for vi in f]
            if not split:
                polys.append((pts, k)); continue
            n = poly_normal(pts)
            ax = max(range(3), key=lambda a: abs(n[a]))
            ua, va = PROJ[ax]
            axes = (ua,) if k in FIT_V else (ua, va)
            # small faces (inside one texture period) stay whole: face_uvs gives them a local origin
            P = family(k)[1]
            ext = [max(q[a] for q in pts) - min(q[a] for q in pts) for a in (ua, va)]
            if max(ext) <= P + 1e-6 or min(ext) < 0.2:      # small, or thin (bars, trims): keep whole
                polys.append((pts, k)); continue
            # split in world-natural space (shift by origin, cut, shift back)
            wpts = [p + origin for p in pts]
            for piece in split_grid(wpts, axes, family(k)[1]):
                polys.append(([q - origin for q in piece], k))
        if not polys:
            return None
        verts, faces, keys, remap = [], [], [], {}
        for pts, k in polys:
            nf = []
            for p in pts:
                kk = (round(p.x, 5), round(p.y, 5), round(p.z, 5))
                if kk not in remap:
                    remap[kk] = len(verts); verts.append(p)
                nf.append(remap[kk])
            # degenerate guard (welding can collapse slivers)
            if len(set(nf)) < 3:
                continue
            faces.append(nf); keys.append(k)
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(matrix @ v) for v in verts], [], faces)
        me.update()
        for p in me.polygons:      # flat shading
            p.use_smooth = False
        # create the colour attribute BEFORE the UV layer: adding an attribute reallocates the layers
        col = me.color_attributes.new("Col", 'FLOAT_COLOR', 'CORNER') if bake else None
        uvl = me.uv_layers.new(name="UVMap")
        if col:
            col = me.color_attributes["Col"]
        for pi, p in enumerate(me.polygons):
            key = keys[pi]
            pts = [verts[vi] for vi in faces[pi]]
            if split:
                uvs = face_uvs(pts, key, origin)
            else:
                uvs = [cell_uv(key, 0.5, 0.5)] * len(pts)
            nrm = poly_normal(pts)
            for li, uv, q in zip(p.loop_indices, uvs, pts):
                uvl.data[li].uv = uv
                if col:
                    col.data[li].color = bake.color(q + origin, nrm)
        me.materials.append(mat)
        ob = bpy.data.objects.new(name, me)
        coll.objects.link(ob)
        return ob


# ───────────────────────────── chunk ─────────────────────────────
SIDES = {'N': (0, 1), 'S': (0, -1), 'E': (1, 0), 'W': (-1, 0)}


class Chunk:
    def __init__(self, name, cw, ch, H=4.0, role="", floor_keys=None, wall_z0=-0.25):
        if VARIANT.get("H"):
            H = VARIANT["H"]
            name = name + VARIANT["suffix"]
        self.name, self.cw, self.ch, self.H, self.role = name, cw, ch, H, role
        self.wall_visual = None       # optional hook(d, x0, y0, x1, y1, z0, line, span) replacing masonry walls
        self.slab_jitter = 0.012
        self.W, self.D = 8 * cw, 8 * ch
        self.walk, self.nofloor, self.noceil, self.blocked = set(), set(), set(), set()
        self.doors = []           # (label, side, center_offset)
        self.rng = random.Random(zlib.crc32(name.encode()))
        self.mb, self.ceil, self.extra = MB(self.rng), MB(self.rng), {}
        self.cols, self.socks = [], []
        self.floor_keys = floor_keys or FLOOR
        self.wall_z0 = wall_z0
        self.counters = {}
        self.door_ring = set()
        self.heroes = []          # (prop, pos, face, scale, part) -> HERO_<prop>[_NN] empties

    # tiles ------------------------------------------------------------------
    def t2m(self, i, j):
        return -self.W / 2 + i, -self.D / 2 + j

    def tiles_in(self, x0, y0, x1, y1):
        out = []
        for i in range(self.W):
            for j in range(self.D):
                x, y = self.t2m(i, j)
                if x >= x0 - 1e-6 and x + 1 <= x1 + 1e-6 and y >= y0 - 1e-6 and y + 1 <= y1 + 1e-6:
                    out.append((i, j))
        return out

    def rect(self, x0, y0, x1, y1):
        self.walk.update(self.tiles_in(x0, y0, x1, y1))

    def door(self, side, k=0):
        nseg = (self.cw if side in "NS" else self.ch)
        label = side + (str(k) if nseg > 1 else "")
        if side in "NS":
            c = -self.W / 2 + 8 * k + 4
            ye = self.D / 2 if side == 'N' else -self.D / 2
            y0, y1 = (ye - 1, ye) if side == 'N' else (ye, ye + 1)
            t = self.tiles_in(c - 2, y0, c + 2, y1)
            pos, face = (c, ye, 0), SIDES[side]
        else:
            c = -self.D / 2 + 8 * k + 4
            xe = self.W / 2 if side == 'E' else -self.W / 2
            x0, x1 = (xe - 1, xe) if side == 'E' else (xe, xe + 1)
            t = self.tiles_in(x0, c - 2, x1, c + 2)
            pos, face = (xe, c, 0), SIDES[side]
        self.walk.update(t)
        self.door_ring.update(t)
        self.doors.append((label, side, c))
        self.sock("Door_" + label, pos, face, numbered=False)

    # sockets / colliders ----------------------------------------------------
    def sock(self, kind, pos, face=(0, 1), numbered=True):
        if numbered:
            n = self.counters.get(kind, 0) + 1
            self.counters[kind] = n
            name = "SOCKET_%s_%02d" % (kind, n)
        else:
            name = "SOCKET_" + kind
        self.socks.append((name, Vector(pos), face))

    def hero(self, prop, pos, face=(0, -1), scale=1.0, part=None):
        """mark a Meshy hero prop: pos/face in chunk-natural coords (prop front = face), uniform scale,
        part = name suffix of a moving object to parent under ("Bell", "Lid_03")."""
        self.heroes.append((prop, Vector(pos), face, scale, part))

    def scratch_mb(self):
        """MB that shares this chunk's RNG but is never exported: build superseded geometry into it so the
        random sequence (and every later draw) stays identical with HERO_PROPS on or off."""
        return MB(self.rng)

    def torch(self, x, y, face, z=2.0):
        self.sock("Torch", (x, y, z), face)

    def col(self, kind, x0, y0, z0, x1, y1, z1, rot=None):
        c = ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)
        s = (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0))
        self.cols.append((kind, c, s, rot or (0, 0, 0)))

    def col_o(self, kind, center, size, rot=(0, 0, 0)):
        self.cols.append((kind, tuple(center), tuple(size), rot))

    # shell --------------------------------------------------------------------
    def is_walk(self, i, j):
        return (i, j) in self.walk

    def inside(self, i, j):
        return 0 <= i < self.W and 0 <= j < self.D

    def build_shell(self, decor=True, ceiling=True):
        H, mb = self.H, self.mb
        z0 = self.wall_z0
        # --- wall runs from tile edges ---
        runs = []   # (dir, line, a0, a1)
        for d, (dx, dy) in SIDES.items():
            edges = {}
            for (i, j) in self.walk:
                ni, nj = i + dx, j + dy
                if self.inside(ni, nj) and not self.is_walk(ni, nj):
                    if d in "NS":
                        edges.setdefault(j, []).append(i)
                    else:
                        edges.setdefault(i, []).append(j)
            for line, lst in edges.items():
                lst.sort()
                s = p = lst[0]
                for v in lst[1:] + [None]:
                    if v is not None and v == p + 1:
                        p = v
                        continue
                    runs.append((d, line, s, p))
                    if v is not None:
                        s = p = v
        wi = 0
        for (d, line, a0, a1) in runs:
            dx, dy = SIDES[d]
            if d in "NS":
                # extend into inner corners
                lo, hi = a0, a1 + 1
                if not self.is_walk(a0 - 1, line) and not self.is_walk(a0 - 1, line + dy):
                    lo -= 0.5
                if not self.is_walk(a1 + 1, line) and not self.is_walk(a1 + 1, line + dy):
                    hi += 0.5
                x0, x1 = -self.W / 2 + lo, -self.W / 2 + hi
                x0, x1 = max(x0, -self.W / 2), min(x1, self.W / 2)
                yl = -self.D / 2 + line + (1 if d == 'N' else 0)
                y0, y1 = (yl, yl + WT) if d == 'N' else (yl - WT, yl)
                self.col("Wall", x0, y0, z0, x1, y1, H)
                if self.wall_visual:
                    self.wall_visual(d, x0, y0, x1, y1, z0, yl, (x0, x1))
                else:
                    mb.box(x0, y0, z0, x1, y1, H, WALL, skip=self.hidden(d, ceiling))
                    if decor:
                        self.wall_decor((x0, x1), yl, d, z0)
            else:
                lo, hi = a0, a1 + 1
                if not self.is_walk(line, a0 - 1) and not self.is_walk(line + dx, a0 - 1):
                    lo -= 0.5
                if not self.is_walk(line, a1 + 1) and not self.is_walk(line + dx, a1 + 1):
                    hi += 0.5
                y0, y1 = -self.D / 2 + lo, -self.D / 2 + hi
                y0, y1 = max(y0, -self.D / 2), min(y1, self.D / 2)
                xl = -self.W / 2 + line + (1 if d == 'E' else 0)
                x0, x1 = (xl, xl + WT) if d == 'E' else (xl - WT, xl)
                self.col("Wall", x0, y0, z0, x1, y1, H)
                if self.wall_visual:
                    self.wall_visual(d, x0, y0, x1, y1, z0, xl, (y0, y1))
                else:
                    mb.box(x0, y0, z0, x1, y1, H, WALL, skip=self.hidden(d, ceiling))
                    if decor:
                        self.wall_decor((y0, y1), xl, d, z0)
        # --- door frames ---
        for (label, side, c) in self.doors:
            self.door_frame(side, c)
        # --- floor slabs ---
        fk = self.floor_keys
        for (i, j) in self.walk:
            if (i, j) in self.nofloor:
                continue
            x, y = self.t2m(i, j)
            self.slab(x, y, 0.0, fk, lod1=False)
        # floor colliders + merged flat LOD1 floor (greedy rects)
        for (i0, j0, i1, j1) in greedy_rects(self.walk - self.nofloor):
            x0, y0 = self.t2m(i0, j0); x1, y1 = self.t2m(i1 + 1, j1 + 1)
            self.col("Floor", x0, y0, -0.5, x1, y1, 0.0)
            old = mb.cat; mb.cat = "L1"
            mb.poly([(x0, y0, 0.0), (x1, y0, 0.0), (x1, y1, 0.0), (x0, y1, 0.0)], "floor0" if (CONSISTENT_STONE and fk[0] in FLOOR) else fk[0])
            mb.cat = old
        # --- ceiling ---
        if ceiling:
            for (i0, j0, i1, j1) in greedy_rects(self.walk - self.noceil):
                x0, y0 = self.t2m(i0, j0); x1, y1 = self.t2m(i1 + 1, j1 + 1)
                self.ceil.poly([(x0, y0, H), (x0, y1, H), (x1, y1, H), (x1, y0, H)], "stone_dk")

    @staticmethod
    def hidden(d, ceiling):
        """faces of a shell wall box nobody can see: the bottom (under the floor), the back (in the rock
        ring) and the top when a ceiling covers it (open-sky themes get the roof cap instead)."""
        back = {'N': 'n', 'S': 's', 'E': 'e', 'W': 'w'}[d]
        return ('b', back, 't') if ceiling else ('b', back)

    def slab(self, x, y, z, keys, size=1.0, c=0.06, lod1=True):
        """flagstone tile (LOD0, cat F), top at z. PS2 pass: a flat quad; the flagstone seams, chips and
        wear live in the floor texture, not in chamfer geometry. lod1=True also adds the LOD1 quad."""
        mb = self.mb
        k = mb.pick(keys)
        if CONSISTENT_STONE and isinstance(keys, (list, tuple)) and not set(keys) <= set(FLOOR):
            # intentionally mixed list (dirt patches, moss on rock...): the draw above is kept for rng parity;
            # the choice itself is a coherent 2 m blob mask on the tile coordinates
            uniq = []
            for kk in keys:
                kk = "floor0" if kk in FLOOR else ("rock0" if kk in ROCK else kk)
                if kk not in uniq:
                    uniq.append(kk)
            ix, iy = int(math.floor(x / 2.0)), int(math.floor(y / 2.0))
            h = (ix * 73856093 ^ iy * 19349663 ^ 0x5bd1e995) & 0xffffff
            k = uniq[(h >> 5) % len(uniq)]
        old = mb.cat
        q = [(x, y, z), (x + size, y, z), (x + size, y + size, z), (x, y + size, z)]
        if lod1:
            mb.cat = "L1"
            mb.poly(q, k)
        mb.cat = "F"
        mb.poly(q, k)
        mb.cat = old

    def wall_decor(self, span, line, d, z0):
        """plinth, cornice and jutting stones on the inner face of a wall run."""
        a0, a1 = span
        L = a1 - a0
        if L < 1.6:
            return
        H, mb, rng = self.H, self.mb, self.rng
        n = -1 if d in "NE" else 1   # inward sign along the normal axis

        def bx(u0, u1, zz0, zz1, p, key, skip=()):
            if d in "NS":
                mb.box(u0, line, zz0, u1, line + n * p, zz1, key, skip)
            else:
                mb.box(line, u0, zz0, line + n * p, u1, zz1, key, skip)
        bx(a0, a1, 0.0, 0.28, 0.07, "wall3", skip=('b',))
        bx(a0, a1, H - 0.22, H, 0.1, "wall2")
        if WOOD_FRAMES and H >= 2.0:
            bx(a0, a1, 0.28, 1.1, 0.03, "wood0")      # wainscot band above the plinth
        # jutting stones in loose courses
        rows = int((H - 0.6) / 0.42)
        old = mb.cat
        mb.cat = "D"
        for r in range(rows):
            zz = 0.4 + r * 0.42
            u = a0 + rng.uniform(0.0, 0.8)
            while u < a1 - 0.5:
                w = rng.uniform(0.45, 1.1)
                if rng.random() < JUT_CHANCE:
                    u1 = min(u + w, a1 - 0.05)
                    pr = rng.uniform(0.09, 0.15)
                    mb.cat = "S" if pr > 0.085 else "D"   # the boldest stones survive into LOD1
                    bx(u, u1, zz, zz + rng.uniform(0.26, 0.38), pr, WALL_HI + WALL)
                u += w + rng.uniform(0.1, 0.9)
        mb.cat = old
        # pilaster at the run midpoint for long runs
        if L >= 5.5:
            m = (a0 + a1) / 2
            bx(m - 0.3, m + 0.3, 0.0, H, 0.16, "wall1", skip=('b',))

    def door_frame(self, side, c):
        """jambs + lintel + chamfered top corners inside the 1-tile ring of a door socket."""
        mb, H = self.mb, self.H
        JAMB, LINTEL = ("wood_v", "wood0") if WOOD_FRAMES else ("wall1", "wall0")
        dx, dy = SIDES[side]
        if side in "NS":
            ye = self.D / 2 if side == 'N' else -self.D / 2
            r0, r1 = (ye - 1, ye) if side == 'N' else (ye, ye + 1)
            for s0, s1 in ((c - 2, c - DOOR_W / 2), (c + DOOR_W / 2, c + 2)):
                mb.box(s0, r0, self.wall_z0, s1, r1, DOOR_H, JAMB, skip=('b', 't'))
                self.col("Wall", s0, r0, self.wall_z0, s1, r1, DOOR_H)
            mb.box(c - 2, r0, DOOR_H, c + 2, r1, H, LINTEL)
            self.col("Wall", c - 2, r0, DOOR_H, c + 2, r1, H)
            # chamfer wedges
            for sx in (-1, 1):
                xa = c + sx * DOOR_W / 2
                xb = c + sx * (DOOR_W / 2 - 0.45)
                for yy, flip in ((r0, False), (r1, True)):
                    pts = [(xa, yy, DOOR_H - 0.45), (xa, yy, DOOR_H), (xb, yy, DOOR_H)]
                    mb.poly(pts if (sx > 0) != flip else list(reversed(pts)), "wall1")
                p0 = [(xa, r0, DOOR_H - 0.45), (xb, r0, DOOR_H), (xb, r1, DOOR_H), (xa, r1, DOOR_H - 0.45)]
                mb.poly(p0 if sx > 0 else list(reversed(p0)), "wall2")
            # keystone
            mb.box(c - 0.3, r0 - 0.08, DOOR_H - 0.1, c + 0.3, r1 + 0.08, DOOR_H + 0.55, "wall4")
        else:
            xe = self.W / 2 if side == 'E' else -self.W / 2
            r0, r1 = (xe - 1, xe) if side == 'E' else (xe, xe + 1)
            for s0, s1 in ((c - 2, c - DOOR_W / 2), (c + DOOR_W / 2, c + 2)):
                mb.box(r0, s0, self.wall_z0, r1, s1, DOOR_H, JAMB, skip=('b', 't'))
                self.col("Wall", r0, s0, self.wall_z0, r1, s1, DOOR_H)
            mb.box(r0, c - 2, DOOR_H, r1, c + 2, H, LINTEL)
            self.col("Wall", r0, c - 2, DOOR_H, r1, c + 2, H)
            for sy in (-1, 1):
                ya = c + sy * DOOR_W / 2
                yb = c + sy * (DOOR_W / 2 - 0.45)
                for xx, flip in ((r0, False), (r1, True)):
                    pts = [(xx, ya, DOOR_H - 0.45), (xx, ya, DOOR_H), (xx, yb, DOOR_H)]
                    mb.poly(pts if (sy < 0) != flip else list(reversed(pts)), "wall1")
                p0 = [(r0, ya, DOOR_H - 0.45), (r0, yb, DOOR_H), (r1, yb, DOOR_H), (r1, ya, DOOR_H - 0.45)]
                mb.poly(p0 if sy < 0 else list(reversed(p0)), "wall2")
            mb.box(r0 - 0.08, c - 0.3, DOOR_H - 0.1, r1 + 0.08, c + 0.3, DOOR_H + 0.55, "wall4")

    # dressing helpers ---------------------------------------------------------
    def rubble(self, cx, cy, rx, ry, h, n, keys=None, z=0.0, max_r=0.45):
        rng = self.rng
        keys = keys or (WALL + WALL_HI)
        for _ in range(n):
            a = rng.uniform(0, 2 * math.pi); t = math.sqrt(rng.random())
            x = cx + math.cos(a) * rx * t; y = cy + math.sin(a) * ry * t
            top = z + h * (1 - t * t) * rng.uniform(0.6, 1.0)
            s = rng.uniform(0.18, max_r)
            self.mb.obox((x, y, max(z + s * 0.4, top - s * 0.5)), (s * rng.uniform(1, 1.8), s * rng.uniform(0.8, 1.4), s),
                         (rng.uniform(-0.4, 0.4), rng.uniform(-0.4, 0.4), rng.uniform(0, math.pi)), keys)

    def bone(self, x, y, z, length, yaw, r=0.035, tilt=0.0):
        d = Vector((math.cos(yaw), math.sin(yaw), math.sin(tilt))) * (length / 2)
        p0 = Vector((x, y, z + r)) - d; p1 = Vector((x, y, z + r)) + d
        self.mb.hcyl(p0, p1, r, 4, BONE)
        for p in (p0, p1):
            self.mb.obox(tuple(p), (r * 3.2, r * 3.2, r * 2.6), (0, 0, yaw), BONE)

    def skull(self, x, y, z, yaw=0.0, s=0.22):
        mb = self.mb
        mb.obox((x, y, z + s * 0.55), (s, s * 1.15, s * 0.9), (0, 0, yaw), "bone0")
        fx, fy = math.cos(yaw - math.pi / 2), math.sin(yaw - math.pi / 2)
        mb.obox((x + fx * s * 0.45, y + fy * s * 0.45, z + s * 0.62), (s * 0.7, s * 0.06, s * 0.22), (0, 0, yaw), "void")
        mb.obox((x + fx * s * 0.3, y + fy * s * 0.3, z + s * 0.12), (s * 0.7, s * 0.5, s * 0.22), (0, 0, yaw), "bone1")

    def bones_scatter(self, cx, cy, r, n, z=0.0):
        rng = self.rng
        for _ in range(n):
            a = rng.uniform(0, 2 * math.pi); t = math.sqrt(rng.random()) * r
            self.bone(cx + math.cos(a) * t, cy + math.sin(a) * t, z, rng.uniform(0.25, 0.5), rng.uniform(0, math.pi))

    def candle(self, x, y, z=0.0, h=0.25):
        self.mb.cyl(x, y, z, z + h, 0.05, 5, "wax")
        self.mb.cyl(x, y, z + h, z + h + 0.07, 0.02, 4, "amber", r_top=0.0)

    def torch_bracket(self, x, y, face, z=2.0):
        """small iron bracket under a torch socket (the torch prefab itself is placed by code)."""
        fx, fy = face
        self.mb.obox((x + fx * 0.07, y + fy * 0.07, z - 0.38), (0.14 if fx else 0.12, 0.14 if fy else 0.12, 0.14),
                     (0, 0, 0), "iron0")

    def wall_torch(self, x, y, face, z=2.0):
        self.torch(x, y, face, z)
        self.torch_bracket(x, y, face, z)

    def ribs(self, along, positions, span, z=None, w=0.35, depth=0.28, pil=True):
        """ceiling ribs across the corridor/room + matching wall pilasters."""
        H = self.H if z is None else z
        a0, a1 = span
        for p in positions:
            if along == 'y':   # rib runs along x at y=p
                self.ceil.box(a0, p - w / 2, H - depth, a1, p + w / 2, H, "wall1", skip=('t',))
                if pil:
                    self.mb.box(a0, p - w / 2 - 0.05, 0, a0 + 0.14, p + w / 2 + 0.05, H - depth, "wall2", skip=('b', 't'))
                    self.mb.box(a1 - 0.14, p - w / 2 - 0.05, 0, a1, p + w / 2 + 0.05, H - depth, "wall2", skip=('b', 't'))
            else:
                self.ceil.box(p - w / 2, a0, H - depth, p + w / 2, a1, H, "wall1", skip=('t',))
                if pil:
                    self.mb.box(p - w / 2 - 0.05, a0, 0, p + w / 2 + 0.05, a0 + 0.14, H - depth, "wall2", skip=('b', 't'))
                    self.mb.box(p - w / 2 - 0.05, a1 - 0.14, 0, p + w / 2 + 0.05, a1, H - depth, "wall2", skip=('b', 't'))

    def pillar(self, x, y, H, r=0.45, broken_at=None, col=True, zb=0.0):
        mb = self.mb
        top = H if broken_at is None else broken_at
        n0 = len(mb.v)
        mb.box(x - 0.65, y - 0.65, 0, x + 0.65, y + 0.65, 0.3, "wall3", skip=('b',))
        mb.box(x - 0.55, y - 0.55, 0.3, x + 0.55, y + 0.55, 0.5, "wall1", skip=('b',))
        mb.cyl(x, y, 0.5, top - (0.5 if broken_at is None else 0), r, 8, "wall0", rot=math.pi / 8)
        if broken_at is None:
            mb.box(x - 0.55, y - 0.55, H - 0.5, x + 0.55, y + 0.55, H - 0.25, "wall1")
            mb.box(x - 0.65, y - 0.65, H - 0.25, x + 0.65, y + 0.65, H, "wall2", skip=('t',))
        else:
            mb.cyl(x, y, top, top + 0.25, r * 0.95, 8, "wall2", r_top=r * 0.5, rot=0.3)
        if zb:
            for k in range(n0, len(mb.v)):
                if mb.v[k].z < 0.6:
                    mb.v[k].z += zb
        if col:
            self.col("Block", x - 0.65, y - 0.65, zb, x + 0.65, y + 0.65, top)
        self.mark_blocked(x - 0.65, y - 0.65, x + 0.65, y + 0.65)

    def mark_blocked(self, x0, y0, x1, y1):
        for i in range(self.W):
            for j in range(self.D):
                x, y = self.t2m(i, j)
                if x < x1 and x + 1 > x0 and y < y1 and y + 1 > y0 and (i, j) in self.walk:
                    self.blocked.add((i, j))

    def scratches(self, x, y, z, face, n=3, key="void"):
        """claw marks: thin dark slanted strips just off a wall."""
        fx, fy = face
        for k in range(n):
            off = (k - (n - 1) / 2) * 0.12
            if fx:   # wall normal along x
                mb_pts = [(x + fx * 0.01, y + off, z), (x + fx * 0.01, y + off + 0.03, z),
                          (x + fx * 0.01, y + off + 0.25, z + 0.55), (x + fx * 0.01, y + off + 0.22, z + 0.55)]
            else:
                mb_pts = [(x + off, y + fy * 0.01, z), (x + off + 0.03, y + fy * 0.01, z),
                          (x + off + 0.25, y + fy * 0.01, z + 0.55), (x + off + 0.22, y + fy * 0.01, z + 0.55)]
            # orient towards the room
            nrm = (Vector(mb_pts[1]) - Vector(mb_pts[0])).cross(Vector(mb_pts[2]) - Vector(mb_pts[0]))
            if nrm.dot(Vector((fx, fy, 0))) < 0:
                mb_pts.reverse()
            self.mb.poly(mb_pts, key)


def greedy_rects(tiles):
    """merge a tile set into rectangles (row runs then vertical merge)."""
    tiles = set(tiles)
    out = []
    while tiles:
        i0, j0 = min(tiles, key=lambda t: (t[1], t[0]))
        i1 = i0
        while (i1 + 1, j0) in tiles:
            i1 += 1
        j1 = j0
        while all((i, j1 + 1) in tiles for i in range(i0, i1 + 1)):
            j1 += 1
        for i in range(i0, i1 + 1):
            for j in range(j0, j1 + 1):
                tiles.discard((i, j))
        out.append((i0, j0, i1, j1))
    return out


# ───────────────────────────── chunk definitions ─────────────────────────────
def ch_corridor_straight():
    c = Chunk("CH_Corridor_Straight", 1, 1, role="corridor")
    c.rect(-2, -4, 2, 4); c.door('N'); c.door('S')
    c.build_shell()
    c.ribs('y', [-2.0, 2.0], (-2, 2))
    c.wall_torch(2.0, 0.0, (-1, 0))
    c.sock("Prop", (-1.4, 1.0, 0), (1, 0)); c.sock("Prop", (1.4, -2.8, 0), (-1, 0))
    c.sock("Drip", (-0.6, -1.0, c.H - 0.1), (0, -1))
    c.rubble(-1.6, -2.6, 0.4, 0.5, 0.25, 5, max_r=0.22)
    c.skull(1.5, 2.6, 0.0, 2.4)
    return c


def ch_corridor_corner():
    c = Chunk("CH_Corridor_Corner", 1, 1, role="corridor")
    c.rect(-2, -2, 2, 4); c.rect(-2, -2, 4, 2); c.door('N'); c.door('E')
    c.build_shell()
    c.ribs('y', [2.6], (-2, 2)); c.ribs('x', [2.6], (-2, 2))
    c.wall_torch(-2.0, -0.5, (1, 0))
    c.sock("Prop", (-1.3, -1.3, 0), (1, 1)); c.sock("Drip", (0.3, 0.3, c.H - 0.1), (0, -1))
    c.bones_scatter(-1.0, -1.2, 0.5, 3)
    return c


def ch_corridor_t():
    c = Chunk("CH_Corridor_T", 1, 1, role="corridor")
    c.rect(-4, -2, 4, 2); c.rect(-2, -2, 2, 4); c.door('N'); c.door('E'); c.door('W')
    c.build_shell()
    c.ribs('x', [-2.6, 2.6], (-2, 2)); c.ribs('y', [2.6], (-2, 2))
    c.wall_torch(0.0, -2.0, (0, 1))
    c.sock("Prop", (-1.2, -1.4, 0), (0, 1)); c.sock("Prop", (1.3, -1.4, 0), (0, 1))
    c.sock("Drip", (0.0, 0.0, c.H - 0.1), (0, -1))
    c.rubble(1.4, -1.5, 0.4, 0.3, 0.2, 4, max_r=0.2)
    return c


def ch_corridor_cross():
    c = Chunk("CH_Corridor_Cross", 1, 1, role="corridor")
    c.rect(-4, -2, 4, 2); c.rect(-2, -4, 2, 4)
    for s in "NESW":
        c.door(s)
    c.build_shell()
    c.ribs('x', [-2.6, 2.6], (-2, 2)); c.ribs('y', [-2.6, 2.6], (-2, 2))
    # central floor drain grate (iron bars over a dark pit, flush with the floor)
    c.mb.box(-0.6, -0.6, -0.02, 0.6, 0.6, 0.015, "iron1", skip=('b',))
    for k in range(5):
        x = -0.48 + k * 0.24
        c.mb.box(x - 0.04, -0.5, 0.0, x + 0.04, 0.5, 0.02, "iron0", skip=('b',))
    c.mb.poly([(-0.5, -0.5, -0.01), (0.5, -0.5, -0.01), (0.5, 0.5, -0.01), (-0.5, 0.5, -0.01)], "void")
    c.sock("Drip", (0.0, 0.0, c.H - 0.1), (0, -1))
    c.sock("Prop", (-1.4, 1.4, 0), (1, -1)); c.sock("Prop", (1.4, -1.4, 0), (-1, 1))
    c.skull(1.5, 1.5, 0.0, 3.6)
    return c


def ch_corridor_deadend():
    c = Chunk("CH_Corridor_DeadEnd", 1, 1, role="corridor")
    c.rect(-2, -4, 2, 2); c.door('S')
    c.build_shell()
    c.ribs('y', [-1.8], (-2, 2))
    # shrine niche against the north wall: stepped stone shelf + skulls + candles
    c.mb.box(-1.3, 1.25, 0, 1.3, 2.0, 0.9, "wall3", skip=('b',))
    c.mb.box(-1.4, 1.15, 0.9, 1.4, 2.0, 1.0, "wall2")
    c.mb.box(-0.9, 1.6, 1.0, 0.9, 2.0, 1.45, "wall1")
    c.mb.box(-1.0, 1.5, 1.45, 1.0, 2.0, 1.53, "wall2")
    c.col("Block", -1.4, 1.15, 0, 1.4, 2.0, 1.53)
    c.mark_blocked(-1.4, 1.15, 1.4, 2.0)
    for x in (-0.55, 0.0, 0.55):
        c.skull(x, 1.8, 1.53, -math.pi / 2 + 0.2 * x, 0.2)
    for x in (-1.15, 1.15):
        c.candle(x, 1.45, 1.0, 0.3)
    c.sock("Evidence", (0.0, 1.45, 1.0), (0, -1))     # on the shrine shelf
    c.sock("Candle", (-1.15, 1.45, 1.0), (0, -1)); c.sock("Candle", (1.15, 1.45, 1.0), (0, -1))
    c.wall_torch(-2.0, -1.0, (1, 0))
    c.sock("Prop", (1.3, -2.5, 0), (-1, 0))
    c.sock("Drip", (0.8, -0.3, c.H - 0.1), (0, -1))
    c.bones_scatter(0.6, 0.4, 0.6, 4)
    return c


def ch_room_storage():
    c = Chunk("CH_Room_Storage", 1, 1, role="room")
    c.rect(-3, -3, 3, 3); c.door('S')
    c.build_shell()
    c.ribs('y', [0.0], (-3, 3))
    # built-in wooden shelving along the east wall
    x0, x1 = 2.25, 3.0
    for y in (-2.4, -0.6, 1.2, 2.9):
        c.mb.box(x0, y - 0.08, 0, x1, y + 0.08, 2.3, "wood2", skip=('b',))
    for z in (0.35, 1.15, 1.95):
        c.mb.box(x0, -2.5, z, x1, 3.0, z + 0.07, "wood1")
    c.col("Block", x0, -2.5, 0, x1, 3.0, 2.3); c.mark_blocked(x0, -2.5, x1, 3.0)
    # sacks / jars on shelves
    rng = c.rng
    for z in (0.42, 1.22, 2.02):
        y = -2.2
        while y < 2.6:
            if rng.random() < 0.6:
                h = rng.uniform(0.2, 0.42)
                c.mb.cyl(2.62, y + 0.2, z, z + h, rng.uniform(0.12, 0.17), 6, ["cloth", "rust", "wood3", "bone2"],
                         r_top=rng.uniform(0.06, 0.15))
            y += rng.uniform(0.35, 0.6)
    # broken crate pile in the NW corner
    c.mb.obox((-2.4, 2.4, 0.3), (0.6, 0.6, 0.6), (0, 0, 0.2), WOOD)
    c.mb.obox((-1.75, 2.45, 0.25), (0.5, 0.5, 0.5), (0, 0, -0.3), WOOD)
    c.mb.obox((-2.3, 2.3, 0.82), (0.45, 0.45, 0.45), (0.1, 0, 0.6), WOOD)
    c.col("Block", -2.8, 1.9, 0, -1.4, 2.8, 1.0); c.mark_blocked(-2.8, 1.9, -1.4, 2.8)
    c.wall_torch(-3.0, -0.8, (1, 0))
    c.sock("Prop_Barrel", (-2.3, -2.2, 0), (1, 1)); c.sock("Prop_Crate", (0.0, 2.3, 0), (0, -1))
    c.sock("Prop", (-0.8, 0.6, 0), (0, -1))
    c.sock("Evidence", (2.6, 0.3, 1.15 + 0.07), (-1, 0)); c.sock("Evidence", (-1.0, -1.5, 0), (0, 1))
    c.sock("Spawn", (0.5, -1.0, 0), (0, 1))
    c.sock("Drip", (-1.5, 0.8, c.H - 0.1), (0, -1))
    return c


def ch_room_pillarhall():
    c = Chunk("CH_Room_PillarHall", 2, 2, H=5.0, role="room")
    c.rect(-7, -7, 7, 7)
    c.door('N', 1); c.door('S', 0); c.door('E', 0); c.door('W', 1)
    c.build_shell()
    for x in (-3.5, 3.5):
        for y in (-4.5, 0.0, 4.5):
            c.pillar(x, y, c.H)
    # beams pillar-to-pillar and pillar-to-wall
    for y in (-4.5, 0.0, 4.5):
        c.ceil.box(-7, y - 0.25, c.H - 0.45, 7, y + 0.25, c.H, "wall1", skip=('t',))
    for x in (-3.5, 3.5):
        c.ceil.box(x - 0.2, -7, c.H - 0.3, x + 0.2, 7, c.H, "wall3", skip=('t',))
    # worn central runner of darker slabs + a cracked floor patch with rubble
    c.rubble(4.5, -6.2, 0.7, 0.4, 0.45, 10, max_r=0.35)
    c.col("Block", 3.8, -6.7, 0, 5.2, -5.7, 0.6)
    c.mark_blocked(3.8, -6.7, 5.2, -5.7)
    c.pillar(6.0, 6.0, c.H, broken_at=1.4)     # a broken column stump in the NW corner
    for x, y, f in ((-7, -4.5, (1, 0)), (7, 4.5, (-1, 0)), (-1.5, 7, (0, -1)), (1.5, -7, (0, 1))):
        c.wall_torch(x, y, f)
    c.sock("Landmark", (0.0, 0.0, 0.0), (0, -1))
    for p, f in (((-5.8, -2.3, 0), (1, 0)), ((5.8, 2.3, 0), (-1, 0)), ((-2.0, 5.8, 0), (0, -1)),
                 ((2.0, -5.8, 0), (0, 1)), ((5.8, 5.8, 0), (-1, -1)), ((-5.8, -5.8, 0), (1, 1))):
        c.sock("Prop", p, f)
    c.sock("Evidence", (-5.5, 2.3, 0), (1, 0)); c.sock("Evidence", (5.5, -2.3, 0), (-1, 0))
    c.sock("Spawn", (0.0, -5.0, 0), (0, 1))
    c.sock("Drip", (-1.8, 2.2, c.H - 0.1), (0, -1)); c.sock("Drip", (2.0, -3.0, c.H - 0.1), (0, -1))
    c.bones_scatter(-5.0, -5.5, 0.6, 3)
    return c


def ch_room_crypt():
    c = Chunk("CH_Room_Crypt", 1, 2, role="room")
    c.rect(-3, -7, 3, 7); c.door('S'); c.door('N')
    c.build_shell()
    c.ribs('y', [-4.5, 0.0, 4.5], (-3, 3), pil=False)
    # burial niche banks (loculi) along E and W walls: 2 rows x 4 bays each side
    for sx in (-1, 1):
        xf, xb = sx * 2.2, sx * 3.0
        x0, x1 = min(xf, xb), max(xf, xb)
        c.col("Block", x0, -5.6, 0, x1, 5.6, 3.0); c.mark_blocked(x0, -5.6, x1, 5.6)
        for z0, z1 in ((0.0, 0.3), (1.25, 1.45), (2.6, 3.0)):
            c.mb.box(x0, -5.6, z0, x1, 5.6, z1, "wall2" if z1 < 3 else "wall1")
        bays = [(-5.6, -2.9), (-2.7, 0.0), (0.2, 2.9), (3.1, 5.6)]
        edges = sorted({b for bay in bays for b in bay})
        for k in range(len(bays) - 1):
            ya, yb = bays[k][1], bays[k + 1][0]
            c.mb.box(x0, ya, 0.3, x1, yb, 2.6, "wall1")
        c.mb.box(x0, -5.8, 0, x1, -5.6, 3.0, "wall0"); c.mb.box(x0, 5.6, 0, x1, 5.8, 3.0, "wall0")
        # back of the niches (dark), wrapped bodies and skulls
        xbk = sx * 2.95
        for (ya, yb) in bays:
            for zr in ((0.3, 1.25), (1.45, 2.6)):
                c.mb.poly([(xbk, ya, zr[0]), (xbk, yb, zr[0]), (xbk, yb, zr[1]), (xbk, ya, zr[1])][::(-sx)], "void")
                if c.rng.random() < 0.6:
                    c.mb.obox((sx * 2.6, (ya + yb) / 2, zr[0] + 0.16), (0.42, (yb - ya) * 0.8, 0.3), (0, 0, 0), ["cloth", "bone2"])
                else:
                    c.skull(sx * 2.6, (ya + yb) / 2 + c.rng.uniform(-0.6, 0.6), zr[0], (math.pi if sx > 0 else 0.0), 0.2)
    # central sarcophagus with the lid slid aside
    c.mb.box(-0.55, -1.2, 0, 0.55, 1.2, 0.85, "wall2", skip=('b', 't'))
    c.mb.box(-0.45, -1.1, 0.25, 0.45, 1.1, 0.3, "void")
    for sx in (-1, 1):
        c.mb.box(sx * 0.45, -1.2, 0.3, sx * 0.55, 1.2, 0.85, "wall2", skip=('b', 's', 'n'))
    c.mb.box(-0.55, -1.2, 0.3, 0.55, -1.1, 0.85, "wall2", skip=('b',))
    c.mb.box(-0.55, 1.1, 0.3, 0.55, 1.2, 0.85, "wall2", skip=('b',))
    c.mb.obox((0.35, 0.2, 0.95), (1.25, 2.55, 0.18), (0.0, 0.08, 0.25), "wall4")
    c.mb.box(-0.65, -1.3, 0, 0.65, 1.3, 0.12, "wall3", skip=('b',))
    c.col("Block", -0.65, -1.3, 0, 0.65, 1.3, 0.9); c.mark_blocked(-0.65, -1.3, 0.65, 1.3)
    c.sock("Evidence", (0.0, 0.0, 0.3), (0, -1))           # inside the open sarcophagus
    c.sock("Evidence", (-1.6, -5.0, 0), (1, 0))
    c.torch(2.2, -2.8, (-1, 0)); c.torch(-2.2, 3.0, (1, 0))
    for (ya, zr) in ((-4.25, 1.45), (1.55, 1.45), (-1.35, 0.3), (4.35, 0.3)):
        c.sock("Candle", (2.3 if ya > 0 else -2.3, ya, zr), (-1 if ya > 0 else 1, 0))
    c.sock("Drip", (0.8, 3.5, c.H - 0.1), (0, -1)); c.sock("Drip", (-0.6, -3.3, c.H - 0.1), (0, -1))
    c.sock("Spawn", (0.0, -4.5, 0), (0, 1))
    c.bones_scatter(1.4, 3.0, 0.5, 3)
    return c


def ritual_blood(c, cy):
    """Dark blood pool + smears toward the south door, baked flat on the floor (detail category, LOD0 only).
    Own RNG so the chunk's other random draws are unchanged."""
    rg = random.Random(7741)
    prev = c.mb.cat
    c.mb.cat = "D"
    Z = 0.012
    cx0, cy0, R0 = 0.0, -4.6, 2.2
    def ok(x, y):                      # keep off the dais and the candle ring
        if abs(x) < 3.2 and abs(y - cy) < 3.2:
            return False
        return math.hypot(x, y - cy) > 4.45
    def cell(x, y, s, key):
        if ok(x - s / 2, y - s / 2) and ok(x + s / 2, y + s / 2) and ok(x - s / 2, y + s / 2) and ok(x + s / 2, y - s / 2):
            c.mb.poly([(x - s / 2, y - s / 2, Z), (x + s / 2, y - s / 2, Z), (x + s / 2, y + s / 2, Z), (x - s / 2, y + s / 2, Z)], key)
    g = 0.25
    n = int(R0 / g) + 2
    for i in range(-n, n + 1):
        for j in range(-n, n + 1):
            x, y = cx0 + i * g, cy0 + j * g
            d = math.hypot(x - cx0, (y - cy0) * 1.1)
            edge = R0 * (0.78 + 0.3 * rg.random())
            if d < edge:
                cell(x, y, g, "blood0" if d < R0 * 0.55 or rg.random() < 0.35 else "blood1")
    # smears toward the south door (y = -7): thin chunky streaks
    for k in range(4):
        x = cx0 + rg.uniform(-1.4, 1.4); y = cy0 - R0 * 0.5
        w = rg.choice((0.25, 0.25, 0.5)); L = rg.uniform(1.0, 2.2)
        steps = int(L / g)
        for t in range(steps):
            cell(x + rg.uniform(-0.03, 0.03), y - t * g, w if t < steps - 1 else g, "blood1" if t % 3 else "blood0")
            if y - t * g < -6.7:
                break
    # a couple of drips
    for k in range(2):
        x = cx0 + rg.uniform(-2.2, 2.2); y = cy0 + rg.uniform(-2.4, 1.6)
        cell(x, y, 0.25, "blood0"); cell(x, y - g, 0.25, "blood1")
    c.mb.cat = prev


def ch_room_ritual():
    c = Chunk("CH_Room_Ritual", 2, 2, H=5.0, role="room")
    c.rect(-7, -7, 7, 7); c.door('S', 0); c.door('N', 1)
    c.build_shell()
    cy = 0.5
    # stepped dais (0.25 + 0.25), altar on top
    c.mb.box(-3, cy - 3, 0, 3, cy + 3, 0.25, "wall3", skip=('b',))
    c.mb.box(-2, cy - 2, 0.25, 2, cy + 2, 0.5, "wall1", skip=('b',))
    c.col("Floor", -3, cy - 3, -0.5, 3, cy + 3, 0.25); c.col("Floor", -2, cy - 2, 0.25, 2, cy + 2, 0.5)
    alt = c.scratch_mb() if HERO_PROPS else c.mb
    alt.box(-1.1, cy - 0.5, 0.5, 1.1, cy + 0.5, 1.35, "stone_dk", skip=('b',))
    alt.box(-1.25, cy - 0.62, 1.35, 1.25, cy + 0.62, 1.5, "wall4")
    if HERO_PROPS:
        c.hero("Altar", (0.0, cy, 0.5), (0, -1))
    c.col("Block", -1.25, cy - 0.62, 0.5, 1.25, cy + 0.62, 1.5)
    c.mark_blocked(-1.25, cy - 0.62, 1.25, cy + 0.62)
    # blood-red drips on the altar front, painted inner ring; a blood pool replaces the old star
    for x in (-0.7, -0.1, 0.5):
        alt.poly([(x, cy - 0.51, 1.35), (x + 0.14, cy - 0.51, 1.35), (x + 0.09, cy - 0.51, 0.9), (x + 0.04, cy - 0.51, 0.75)][::-1], "red0")
    c.mb.ring(0, cy, 0.505, 1.6, 1.8, 16, "red1")
    ritual_blood(c, cy)
    # 8 candle spots around the circle, some candles already modelled
    for k in range(8):
        a = 2 * math.pi * k / 8 + math.pi / 8
        x, y = math.cos(a) * 4.0, cy + math.sin(a) * 4.0
        if k % 2 == 0:
            c.candle(x, y, 0.0, 0.3); c.candle(x + 0.15, y + 0.08, 0.0, 0.18)
        c.sock("Candle", (x, y, 0.0), (-math.cos(a), -math.sin(a)))
    # corner pillars (one broken) + chains/banners as hanging strips
    c.pillar(-5.5, -5.5, c.H); c.pillar(5.5, 5.5, c.H); c.pillar(5.5, -5.5, c.H, broken_at=2.0); c.pillar(-5.5, 5.5, c.H)
    for x in (-4.5, -1.0):
        c.mb.box(x - 0.6, 6.96, 1.6, x + 0.6, 7.0, 4.4, "red0")
        c.mb.box(x - 0.7, 6.9, 4.4, x + 0.7, 7.0, 4.5, "wood2")
    c.wall_torch(-7, 1.5, (1, 0)); c.wall_torch(7, -1.5, (-1, 0))
    c.sock("Landmark", (0.0, cy, 1.5), (0, -1))
    c.sock("Evidence", (0.0, cy, 1.5), (0, -1)); c.sock("Evidence", (5.6, 1.5, 0), (-1, 0))
    c.sock("Prop", (-6.0, -2.0, 0), (1, 0)); c.sock("Prop", (6.0, 3.0, 0), (-1, 0)); c.sock("Prop", (-2.5, -6.0, 0), (0, 1))
    c.sock("Spawn", (-4.0, -5.0, 0), (0, 1))
    c.bones_scatter(2.5, -3.8, 0.7, 4)
    c.skull(-1.8, -2.2, 0.0, 1.0); c.skull(2.1, 3.2, 0.5, 2.3, 0.2)
    return c


def ch_sp_collapsed():
    c = Chunk("CH_SP_CollapsedCorridor", 1, 1, role="setpiece")
    c.rect(-2, -4, 2, 4); c.door('N'); c.door('S')
    # hole in the ceiling above the rubble
    c.noceil.update(c.tiles_in(-2, -1, 1, 1))
    c.build_shell()
    c.ribs('y', [-2.6, 2.6], (-2, 2))
    gx0, gx1 = 0.6, 2.0          # crawl gap (1.4 m wide x 1.3 m high)
    # rubble mound (blocking) + fallen slab and beam over the crawl gap
    # solid core so the mound never shows holes (matches the blocking collider), rubble on top of it
    def frustum(b0, b1, t0, t1, z0, z1, key):
        lo = [(b0[0], b0[1], z0), (b1[0], b0[1], z0), (b1[0], b1[1], z0), (b0[0], b1[1], z0)]
        hi = [(t0[0], t0[1], z1), (t1[0], t0[1], z1), (t1[0], t1[1], z1), (t0[0], t1[1], z1)]
        for i in range(4):
            j = (i + 1) % 4
            c.mb.poly([lo[i], lo[j], hi[j], hi[i]], key)
        c.mb.poly(hi, key)
    frustum((-2.0, -1.5), (gx0, 1.5), (-1.7, -0.5), (gx0, 0.5), 0.0, 3.7, "wall3")
    c.rubble(-0.8, 0.0, 1.5, 2.0, 3.2, 70, max_r=0.55)
    c.rubble(-0.6, 0.0, 1.2, 1.4, 3.9, 25, max_r=0.6)
    c.mb.obox((1.3, 0.0, 1.45), (1.9, 3.2, 0.3), (0.05, -0.12, 0.0), ["wall2", "wall4"])
    c.mb.hcyl((-0.5, -1.8, 1.9), (2.0, 1.6, 1.4), 0.17, 6, "wood2")
    c.rubble(1.3, 0.0, 0.6, 1.4, 2.3, 18, z=1.6, max_r=0.45)
    c.mb.box(gx0 - 0.28, -0.9, 0.0, gx0 - 0.02, -0.6, 1.3, "wood1")   # a prop post holding it up (outside the gap)
    c.rubble(0.0, -2.3, 1.2, 0.6, 0.35, 10, max_r=0.22)   # spill on the floor (low, no collider)
    c.rubble(-0.5, 2.4, 1.2, 0.6, 0.35, 10, max_r=0.22)
    # shaft above the collapsed ceiling (seen from inside), dark at the top
    c.ceil.box_in(-2, -1, c.H, 1, 1, c.H + 2.5, ["wall3", "stone_dk"], skip=('b', 't'))
    c.ceil.poly([(-2, -1, c.H + 2.5), (-2, 1, c.H + 2.5), (1, 1, c.H + 2.5), (1, -1, c.H + 2.5)], "void")
    # broken ceiling edge stones + hanging roots
    for k in range(7):
        x = -1.8 + k * 0.45
        c.ceil.obox((x, 1.05, c.H - 0.15), (0.4, 0.35, 0.3), (0.2, 0, 0.1 * k), "wall3")
        c.ceil.obox((x, -1.05, c.H - 0.15), (0.4, 0.35, 0.3), (-0.2, 0, -0.1 * k), "wall3")
    c.col("Block", -2.0, -1.5, 0, gx0, 1.5, c.H)
    c.col("Block", gx0, -1.5, 1.3, 2.0, 1.5, c.H)
    c.mark_blocked(-2.0, -1.5, gx0, 1.5)
    c.wall_torch(-2.0, -3.0, (1, 0))
    c.sock("Prop_Warning", (1.3, -2.2, 0), (0, -1))
    c.sock("Drip", (-0.5, 0.0, c.H - 0.3), (0, -1))
    c.extra["crawl_gap"] = {"x": [gx0, gx1], "height": 1.3, "length": 3.0}
    return c


def ch_sp_flooded():
    c = Chunk("CH_SP_FloodedCrypt", 1, 2, role="setpiece", wall_z0=-0.75)
    c.rect(-3, -7, 3, 7); c.door('S'); c.door('N')
    sunk = set(c.tiles_in(-3, -6, 3, 6))
    c.nofloor.update(sunk)
    c.build_shell()
    c.ribs('y', [-3.0, 3.0], (-3, 3))
    depth, wl = -0.5, -0.15
    # ramps (1.5 m long, 18 deg) + sunken flagstones
    for j in range(c.D):
        for i in range(c.W):
            if (i, j) not in sunk:
                continue
            x, y = c.t2m(i, j)
            if -4.5 <= y < 4.5:
                c.slab(x, y, depth, ["moss", "floor3", "floor0", "slime"])
    for sy in (-1, 1):
        ya, yb = sy * 6.0, sy * 4.5
        for x in range(-3, 3):
            c.mb.poly([(x, ya, 0.0), (x + 1, ya, 0.0), (x + 1, yb, depth), (x, yb, depth)][::(1 if sy < 0 else -1)],
                      ["floor2", "floor1", "moss"])
        ang = math.atan2(-depth, 1.5)
        c.col_o("Ramp", (0, sy * (5.25 + 0.25 * math.sin(ang)), depth / 2 - 0.25 * math.cos(ang)),
                (6.0, math.hypot(1.5, depth) + 0.2, 0.5), (sy * ang, 0, 0))
    c.col("Floor", -3, -4.5, depth - 0.5, 3, 4.5, depth)
    # water surface (separate object/material) + trigger volume
    wy = 6.0 - (wl / depth) * 1.5
    water = MB(c.rng)
    water.poly([(-3, -wy, wl), (3, -wy, wl), (3, wy, wl), (-3, wy, wl)], "water")
    c.extra["water"] = water
    c.extra["water_vol"] = ((0, 0, (depth + wl) / 2), (6.0, 2 * wy, wl - depth))   # top = water surface
    # broken columns standing in the water, a toppled one as a bridge-ish log, floating planks
    c.pillar(-1.6, -2.5, c.H, broken_at=1.3 + depth, zb=depth)
    c.pillar(1.9, -0.3, c.H, zb=depth)
    c.mb.hcyl((-2.6, 1.4, depth + 0.35), (-0.6, 2.8, depth + 0.35), 0.4, 8, "wall0")
    c.col_o("Block", (-1.6, 2.1, depth + 0.35), (2.45, 0.8, 0.8), (0, 0, math.atan2(1.4, 2.0)))
    for (x, y, a) in ((1.2, -1.0, 0.4), (0.6, -3.6, -0.9), (-0.8, 0.4, 1.4)):
        c.mb.obox((x, y, wl + 0.02), (1.1, 0.25, 0.06), (0, 0, a), WOOD)
    c.skull(-2.3, -4.0, depth, 0.8)
    c.wall_torch(-3.0, -5.5, (1, 0)); c.wall_torch(3.0, 5.5, (-1, 0))
    for p in ((-1.0, -1.0), (1.2, 1.0), (0.2, 3.5)):
        c.sock("Drip", (p[0], p[1], c.H - 0.1), (0, -1))
    c.sock("Evidence", (2.0, -2.2, depth), (0, 1))      # submerged
    c.sock("Evidence", (-2.2, 6.4, 0.0), (1, 0))
    c.sock("Prop", (2.2, -6.4, 0), (-1, 0))
    c.extra["flooded"] = {"floor": depth, "water_level": wl, "ramp_slope_deg": round(math.degrees(math.atan2(-depth, 1.5)), 1)}
    return c


def ch_sp_pitbridge():
    c = Chunk("CH_SP_PitBridge", 1, 2, role="setpiece", wall_z0=-3.25)
    c.rect(-3, -7, 3, 7); c.door('S'); c.door('N')
    pit = set(c.tiles_in(-3, -4, 3, 4))
    c.nofloor.update(pit)
    c.build_shell()
    c.ribs('y', [-5.5, 5.5], (-3, 3))
    PZ = -3.0
    # pit bottom flagstones + liner walls under the floor edges
    for (i, j) in pit:
        x, y = c.t2m(i, j)
        c.slab(x, y, PZ, DIRT)
    c.col("Floor", -3, -4, PZ - 0.5, 3, 4, PZ)
    for sy in (-1, 1):
        y0, y1 = (4.0, 4.5) if sy > 0 else (-4.5, -4.0)
        c.mb.box(-3, y0, PZ, 3, y1, 0.0, WALL, skip=('t',))
        c.col("Wall", -3, y0, PZ, 3, y1, -0.02)
        c.mb.box(-3, sy * 4.0, -0.18, 3, sy * 4.0 + sy * 0.2, 0.0, "wall2", skip=('b',))   # curb
    # plank bridge x in [-1, 1], top at 0
    bx0, bx1 = -1.0, 1.0
    y = -4.4
    k = 0
    while y < 4.4:
        if k != 9:    # one missing plank (visual only, collider is continuous)
            c.mb.box(bx0 - c.rng.uniform(0, 0.08), y, -0.1, bx1 + c.rng.uniform(0, 0.08), y + 0.26, 0.0, WOOD)
        y += 0.3; k += 1
    for sx in (-1, 1):
        c.mb.box(sx * 0.9 - 0.09, -4.5, -0.32, sx * 0.9 + 0.09, 4.5, -0.1, "wood2")
        for yy in (-4.3, 0.0, 4.3):
            c.mb.box(sx * 1.05 - 0.08, yy - 0.08, -0.3, sx * 1.05 + 0.08, yy + 0.08, 1.05, "wood2")
        # rails: east intact, west snapped in the middle
        if sx > 0:
            c.mb.box(1.0, -4.3, 0.95, 1.1, 4.3, 1.03, "wood1")
            c.col("Rail", 0.98, -4.4, 0.0, 1.12, 4.4, 1.05)
        else:
            c.mb.box(-1.1, -4.3, 0.95, -1.0, -0.6, 1.03, "wood1")
            c.mb.obox((-1.2, 1.4, 0.4), (0.1, 2.9, 0.08), (0.35, 0, 0.12), "wood1")
            c.col("Rail", -1.12, -4.4, 0.0, -0.98, -0.5, 1.05)
    # support beam under the middle + a rope
    c.mb.hcyl((-1.7, 0.0, -0.75), (3.0, 0.0, -0.35), 0.12, 6, "wood2")
    c.col("Floor", bx0, -4.5, -0.3, bx1, 4.5, 0.0)
    # escape stairs inside the pit along the west wall: from (y=-1, z=-3) up to (y=4, z=0)
    sx0, sx1 = -3.0, -1.8
    steps = 12
    run = 5.0 / steps
    for s in range(steps):
        y0 = -1.0 + s * run
        c.mb.box(sx0, y0, PZ, sx1, 4.0, PZ + (s + 1) * 0.25, ["wall1", "wall0"], skip=('b',) if s else ())
    ang = math.atan2(3.0, 5.0)
    L = math.hypot(3.0, 5.0)
    c.col_o("Ramp", ((sx0 + sx1) / 2, 1.5 + 0.25 * math.sin(ang), -1.5 - 0.25 * math.cos(ang) + 0.12), (sx1 - sx0, L + 0.3, 0.5), (ang, 0, 0))
    c.col("Block", sx0, 1.5, PZ, sx1, 4.0, -1.4)
    c.mark_blocked(-3, -4, 3, 4)
    # bones and a lost explorer at the bottom
    c.bones_scatter(1.2, -1.8, 1.2, 9, z=PZ)
    c.skull(1.6, -2.4, PZ, 0.5); c.skull(0.3, 2.2, PZ, 2.0)
    c.mb.obox((2.2, 1.0, PZ + 0.15), (0.9, 0.3, 0.3), (0, 0, 0.3), "rust")
    c.rubble(2.3, 3.2, 0.6, 0.5, 0.5, 8, z=PZ, max_r=0.3)
    c.wall_torch(-3.0, -5.6, (1, 0)); c.wall_torch(3.0, 5.6, (-1, 0))
    c.sock("Evidence", (1.6, -1.2, PZ), (0, 1))          # risk/reward at the bottom
    c.sock("Prop", (2.2, -5.8, 0), (-1, 0)); c.sock("Prop", (-2.2, 6.2, 0), (1, 0))
    c.sock("Drip", (1.8, 0.5, c.H - 0.1), (0, -1))
    c.extra["pit"] = {"bottom": PZ, "bridge_x": [bx0, bx1], "stairs": "x -3..-1.8, y -1 -> 4, 31 deg"}
    return c


def ch_sp_belltower():
    c = Chunk("CH_SP_BellTower", 1, 1, H=12.0, role="setpiece")
    c.rect(-3, -3, 3, 3); c.door('S'); c.door('W')
    c.build_shell()
    BZ = 2.65
    # balcony along the north wall (y 1.4..3) at 3 m + railing
    c.mb.box(-3, 1.5, BZ - 0.25, 3, 3, BZ, "wood1")
    for x in (-2.6, -1.2, 0.2):
        c.mb.box(x - 0.12, 1.5, BZ - 0.55, x + 0.12, 3.0, BZ - 0.25, "wood2")     # joists
    c.col("Floor", -3, 1.5, BZ - 0.25, 3, 3, BZ)
    for x in (-2.9, -1.9, -0.9, 0.1, 1.1):
        c.mb.box(x, 1.5, BZ, x + 0.12, 1.62, BZ + 1.0, "wood0")
    c.mb.box(-3, 1.48, BZ + 0.95, 1.6, 1.64, BZ + 1.05, "wood3")
    c.col("Rail", -3, 1.48, BZ, 1.6, 1.64, BZ + 1.05)
    # stairs along the east wall: x 1.6..3, y -2.4 -> 1.5 (foot 0.6 m off the south wall), rising 2.65 m (34 deg)
    steps = 11
    y_foot, run_len = -2.4, 3.9
    run = run_len / steps
    rise = BZ / steps
    for s in range(steps):
        y0 = y_foot + s * run
        c.mb.box(1.6, y0, 0.0, 3.0, 1.5, (s + 1) * rise, ["wall1", "wall0"], skip=('b',))
    ang = math.atan2(BZ, run_len); L = math.hypot(BZ, run_len)
    c.col_o("Ramp", (2.3, (y_foot + 1.5) / 2 + 0.25 * math.sin(ang), BZ / 2 - 0.25 * math.cos(ang) + 0.06), (1.4, L + 0.2, 0.5), (ang, 0, 0))
    c.col("Block", 1.6, 0.0, 0, 3.0, 1.5, 1.2)
    c.mark_blocked(1.6, y_foot, 3.0, 1.5)
    # tall-tower dressing: string courses, slit windows, wooden floors high up, bell beam
    for z in (4.5, 8.0):
        for (x0, y0, x1, y1) in ((-3, -3, 3, -2.88), (-3, 2.88, 3, 3), (-3, -3, -2.88, 3), (2.88, -3, 3, 3)):
            c.mb.box(x0, y0, z, x1, y1, z + 0.25, "wall2")
    for (x, y, f) in ((0, 3.0, (0, -1)), (3.0, 0.0, (-1, 0)), (-3.0, 0.0, (1, 0))):
        if f[1]:
            c.mb.poly([(x - 0.3, y - 0.01, 6.0), (x + 0.3, y - 0.01, 6.0), (x + 0.3, y - 0.01, 7.6), (x - 0.3, y - 0.01, 7.6)], "void")
        else:
            s = 1 if f[0] > 0 else -1
            p = [(x + s * 0.01, y - 0.3, 6.0), (x + s * 0.01, y + 0.3, 6.0), (x + s * 0.01, y + 0.3, 7.6), (x + s * 0.01, y - 0.3, 7.6)]
            c.mb.poly(p if s > 0 else p[::-1], "void")
    c.ceil.box(-3, -0.2, 10.6, 3, 0.2, 11.0, "wood2")       # bell beam
    for x in (-2.0, 2.0):
        c.ceil.box(x - 0.15, -3, 11.4, x + 0.15, 3, 11.7, "wood2", skip=('t',))
    # the bell (separate object, pivot at the top) + rope down to the floor
    bell = MB(c.rng)
    body = MB(c.rng) if HERO_PROPS else bell      # HERO: the Meshy bell hangs under SM_*_Bell (same pivot / swing)
    body.cyl(0, 0, -1.9, -1.7, 0.95, 10, "brass", r_top=0.9, caps=(True, False))
    body.cyl(0, 0, -1.7, -0.6, 0.9, 10, "brass1", r_top=0.55)
    body.cyl(0, 0, -0.6, -0.25, 0.55, 10, "brass", r_top=0.3)
    body.box(-0.12, -0.12, -0.25, 0.12, 0.12, 0.0, "iron0")
    body.cyl(0, 0, -1.9, -1.75, 0.16, 6, "iron1", caps=(True, True))   # clapper
    if HERO_PROPS:
        c.hero("Bell", (0.0, 0.0, 10.6), (0, -1), part="Bell")
    bell.hcyl((0.3, 0.0, -1.0), (0.3, 0.0, -9.2), 0.035, 4, "rope")
    c.extra["bell"] = (bell, (0.0, 0.0, 10.6))
    c.wall_torch(-3.0, -1.6, (1, 0)); c.wall_torch(-1.0, 3.0, (0, -1), z=BZ + 1.9)
    c.sock("Landmark", (0.0, 0.0, 8.7), (0, -1))
    c.sock("BellRope", (0.3, 0.0, 1.4), (0, -1))
    c.sock("Evidence", (-2.2, 2.3, BZ), (0, -1))         # up on the balcony
    c.sock("Prop", (-2.3, -2.3, 0), (1, 1)); c.sock("Prop", (0.8, 2.3, BZ), (0, -1))
    c.sock("Spawn", (-1.0, -1.5, 0), (1, 0))
    c.bones_scatter(-1.5, 0.3, 0.6, 3)
    c.extra["balcony"] = {"height": BZ, "stairs": "x 1.6..3, y -2.4 -> 1.5, 34.2 deg, balcony 2.65 m"}
    return c


def ch_sp_den():
    c = Chunk("CH_SP_ChuchoDen", 1, 2, role="setpiece", floor_keys=DIRT)
    c.rect(-3, -7, 3, 7); c.door('S')
    c.build_shell()
    c.ribs('y', [-3.5, 1.5], (-3, 3), pil=False)
    # straw nest (low mound, walkable) at the north end
    nx, ny = 0.6, 4.9
    nest = c.scratch_mb() if HERO_PROPS else c.mb
    if HERO_PROPS:
        c.hero("Nest", (nx, ny, 0.0), (0, -1))
    nest.cyl(nx, ny, 0.0, 0.14, 1.9, 9, "straw2", r_top=1.6)
    nest.cyl(nx, ny, 0.14, 0.12, 1.6, 9, "straw0", r_top=1.0)
    nest.cyl(nx, ny, 0.12, 0.2, 1.0, 9, "straw1", r_top=0.0, caps=(False, False))
    for _ in range(26):
        a = c.rng.uniform(0, 2 * math.pi); r = c.rng.uniform(0.9, 2.1)
        nest.obox((nx + math.cos(a) * r, ny + math.sin(a) * r, 0.1), (0.6, 0.06, 0.04),
                  (c.rng.uniform(-0.3, 0.3), 0.2, a + c.rng.uniform(-0.6, 0.6)), STRAW)
    # bone pile mound (landmark) in the NW corner, leaving free floor in front of it
    px, py = -1.9, 5.4
    c.mb.cyl(px, py, 0.0, 0.55, 1.05, 8, "dirt2", r_top=0.35)
    for _ in range(34):
        a = c.rng.uniform(0, 2 * math.pi); r = math.sqrt(c.rng.random()) * 1.0
        z = 0.55 * (1 - (r / 1.05) ** 2) + 0.02
        c.bone(px + math.cos(a) * r, py + math.sin(a) * r, z - 0.03, c.rng.uniform(0.3, 0.55), c.rng.uniform(0, math.pi),
               tilt=c.rng.uniform(-0.4, 0.4))
    c.skull(px + 0.2, py - 0.3, 0.5, -1.2, 0.26); c.skull(px - 0.6, py + 0.4, 0.3, 0.5, 0.22)
    c.col("Rail", px - 0.9, py - 0.9, 0, px + 0.9, py + 0.9, 0.5)
    c.mark_blocked(px - 0.9, py - 0.9, px + 0.9, py + 0.9)
    # dog bowl (with a bone in it) + chewed planks + claw marks everywhere
    c.mb.cyl(2.2, 0.6, 0.0, 0.14, 0.32, 8, "iron1", r_top=0.38)
    c.mb.cyl(2.2, 0.6, 0.14, 0.1, 0.33, 8, "rust", r_top=0.0, caps=(False, False))
    c.bone(2.2, 0.6, 0.12, 0.45, 0.8)
    for (x, y, a) in ((-1.5, 1.0, 0.3), (-2.1, 0.3, 1.9), (1.0, -2.6, 2.6)):
        c.mb.obox((x, y, 0.03), (1.2, 0.22, 0.05), (0, 0, a), WOOD)
    for (x, y, z, f) in ((3.0, -1.0, 0.5, (-1, 0)), (3.0, 3.0, 0.9, (-1, 0)), (-3.0, -3.5, 0.4, (1, 0)),
                         (-3.0, 2.6, 0.7, (1, 0)), (-1.0, 7.0, 0.6, (0, -1))):
        c.scratches(x, y, z, f, 3)
    c.scratches(1.8, -7.0, 1.1, (0, 1), 4)
    # a dug hole/burrow mound (where Chucho buries stolen items)
    c.mb.cyl(-1.4, -3.0, 0.0, 0.22, 0.85, 7, "dirt1", r_top=0.3)
    c.mb.cyl(-1.4, -3.0, 0.22, 0.24, 0.3, 7, "void", r_top=0.0, caps=(False, False))
    c.bones_scatter(0.5, -1.0, 2.0, 6)
    # wooden sign on the north wall with "CHUCHO" in chunky pixel letters
    c.mb.box(-1.75, 6.9, 2.15, 1.75, 7.0, 2.95, "wood1")
    c.mb.box(-1.85, 6.86, 2.1, -1.65, 7.0, 3.0, "wood2"); c.mb.box(1.65, 6.86, 2.1, 1.85, 7.0, 3.0, "wood2")
    FONT = {"C": ["111", "100", "100", "100", "111"], "H": ["101", "101", "111", "101", "101"],
            "U": ["101", "101", "101", "101", "111"], "O": ["111", "101", "101", "101", "111"]}
    px = 0.09
    x = -1.5
    for ch in "CHUCHO":
        g = FONT[ch]
        for r, row in enumerate(g):
            z = 2.8 - r * px
            col = 0
            while col < 3:          # merge runs of lit pixels in the row
                if row[col] == "1":
                    e = col
                    while e + 1 < 3 and row[e + 1] == "1":
                        e += 1
                    c.mb.box(x + col * px, 6.86, z - px, x + (e + 1) * px, 6.9, z, "bone0", skip=('n',))
                    col = e + 1
                else:
                    col += 1
        x += 4 * px + 0.13
    # paw prints (pads + toes) leading from the door to the nest
    for k in range(9):
        t = k / 8.0
        y = -6.5 + t * 10.0
        xo = 0.25 * (1 if k % 2 else -1) + 0.4 * math.sin(t * 3.0)
        c.mb.poly([(xo - 0.07, y - 0.06, 0.008), (xo + 0.07, y - 0.06, 0.008), (xo + 0.07, y + 0.06, 0.008), (xo - 0.07, y + 0.06, 0.008)], "mortar")
        for tx in (-0.07, 0.0, 0.07):
            c.mb.poly([(xo + tx - 0.025, y + 0.09, 0.008), (xo + tx + 0.025, y + 0.09, 0.008),
                       (xo + tx + 0.025, y + 0.14, 0.008), (xo + tx - 0.025, y + 0.14, 0.008)], "mortar")
    c.wall_torch(-3.0, -5.5, (1, 0))
    c.sock("Den", (0.6, 4.4, 0.0), (0, -1))
    c.sock("Landmark", (px, py, 0.6), (1, -1))
    for p in ((-0.4, 3.4), (1.9, 3.0), (-1.9, 3.3), (2.0, 6.3)):
        c.sock("Prop_Limb", (p[0], p[1], 0.0), (0, -1))
    c.sock("Evidence", (-1.4, -3.0, 0.24), (0, 1)); c.sock("Evidence", (1.6, 5.8, 0.2), (0, -1))
    c.sock("Spawn", (0.6, 4.9, 0.2), (0, -1))            # creature spawn (on the nest)
    c.sock("Drip", (-0.5, -0.5, c.H - 0.1), (0, -1))
    return c


def ch_generator_alcove():
    c = Chunk("CH_Generator_Alcove", 1, 1, role="objective")
    c.rect(-3, -3, 3, 3); c.door('S')
    c.build_shell()
    c.ribs('y', [-1.0], (-3, 3))
    # iron floor plate with hazard edging
    c.mb.box(-1.4, 0.0, 0.0, 1.4, 2.8, 0.04, "iron1", skip=('b',))
    for k in range(7):
        x = -1.4 + k * 0.4
        c.mb.box(x, -0.12, 0.0, x + 0.2, 0.0, 0.03, "hazard", skip=('b',))
        c.mb.box(x + 0.2, -0.12, 0.0, x + 0.4, 0.0, 0.03, "mortar", skip=('b',))
    # pipes up the north wall and along the ceiling to the door, fuse box, cables
    for x in (-2.2, -1.9, 2.0):
        c.mb.cyl(x, 2.85, 0.0, c.H, 0.09, 6, "iron0")
    c.mb.hcyl((-2.2, 2.85, 3.6), (-2.2, -3.0, 3.6), 0.09, 6, "rust")
    c.mb.hcyl((-1.9, 2.85, 3.75), (2.0, 2.85, 3.75), 0.09, 6, "iron0")
    c.mb.box(2.7, 0.6, 1.2, 3.0, 1.6, 2.4, "iron1")
    c.mb.box(2.68, 0.75, 1.9, 2.7, 1.45, 2.25, "amber")
    c.mb.box(2.68, 0.75, 1.35, 2.7, 1.45, 1.75, "mortar")
    for k, y in enumerate((0.8, 1.1, 1.4)):
        c.mb.hcyl((2.7, y, 1.2), (1.2, y - 0.3, 0.02), 0.03, 4, "iron0")
    c.sock("Generator", (0.0, 1.4, 0.04), (0, -1))
    c.wall_torch(-3.0, -1.2, (1, 0))
    c.sock("Prop_Crate", (-2.3, -2.2, 0), (1, 0)); c.sock("Prop_Barrel", (2.3, -2.3, 0), (-1, 0))
    c.sock("Evidence", (-2.3, 0.8, 0), (1, 0))
    c.extra["generator_clearance"] = "3 x 3 m plate area kept free (x -1.4..1.4, y 0..2.8)"
    return c


def ch_extraction_gate():
    c = Chunk("CH_Extraction_Gate", 2, 2, H=5.0, role="objective")
    c.rect(-7, -7, 7, 7); c.door('S', 0)
    # exit tunnel through the north ring (not a socket — it ends in darkness behind the gate)
    tun = c.tiles_in(-2, 7, 2, 8)
    c.walk.update(tun)
    c.build_shell()
    # tunnel back = darkness, lintel over the tunnel, gate frame on the room side
    c.mb.poly([(-2, 7.98, 0), (2, 7.98, 0), (2, 7.98, c.H), (-2, 7.98, c.H)], "void")
    c.mb.box(-2, 7.0, 4.2, 2, 8.0, c.H, "wall0")
    c.col("Wall", -2, 7.0, 4.2, 2, 8.0, c.H)
    c.mb.box(-2.6, 6.6, 0, -1.9, 7.0, 4.2, "wall4"); c.mb.box(1.9, 6.6, 0, 2.6, 7.0, 4.2, "wall4")
    c.mb.box(-2.6, 6.6, 4.0, 2.6, 7.0, 4.6, "wall2")
    c.mb.box(-0.4, 6.55, 4.05, 0.4, 7.05, 4.75, "green")         # rune keystone
    c.col("Wall", -2.6, 6.6, 0, -1.9, 7.0, 4.6); c.col("Wall", 1.9, 6.6, 0, 2.6, 7.0, 4.6)
    gate = MB(c.rng)
    for k in range(9):
        x = -1.8 + k * 0.45
        gate.box(x - 0.05, -0.05, 0.0, x + 0.05, 0.05, 4.0, "iron0")
    for z in (0.6, 1.8, 3.0, 3.9):
        gate.box(-1.9, -0.07, z, 1.9, 0.07, z + 0.12, "iron1")
    c.extra["gate"] = (gate, (0.0, 7.5, 0.0))
    c.extra["gate_collider"] = ((0.0, 7.5, 2.1), (4.0, 0.25, 4.2))
    # rune circle where the extraction zone sits + sigils
    cy = 1.0
    c.mb.ring(0, cy, 0.012, 3.6, 3.85, 24, "green")
    c.mb.ring(0, cy, 0.012, 2.4, 2.5, 16, "green")
    for k in range(8):
        a = 2 * math.pi * k / 8
        x, y = math.cos(a) * 3.0, cy + math.sin(a) * 3.0
        c.mb.poly([(x - 0.15, y - 0.15, 0.014), (x + 0.15, y - 0.15, 0.014), (x + 0.15, y + 0.15, 0.014), (x - 0.15, y + 0.15, 0.014)], "green1")
    # side pillars + braziers bowls (light sockets) at the gate
    for x in (-5.0, 5.0):
        c.pillar(x, 4.5, c.H)
        c.pillar(x, -3.0, c.H)
    for x in (-3.3, 3.3):
        c.mb.cyl(x, 5.0, 0.0, 0.9, 0.18, 6, "iron0")
        c.mb.cyl(x, 5.0, 0.9, 1.15, 0.2, 6, "iron1", r_top=0.45)
        c.col("Rail", x - 0.45, 4.55, 0, x + 0.45, 5.45, 1.15)
        c.mark_blocked(x - 0.45, 4.55, x + 0.45, 5.45)
        c.sock("Torch", (x, 5.0, 1.2), (0, -1))          # brazier: free-standing light
    c.wall_torch(-7, -3.0, (1, 0)); c.wall_torch(7, -3.0, (-1, 0))
    c.sock("Extraction", (0.0, cy, 0.0), (0, 1))
    c.sock("Beacon", (0.0, cy, 3.6), (0, 1))
    c.sock("ExitGate", (0.0, 7.5, 0.0), (0, 1))
    c.sock("Prop", (-6.0, 6.0, 0), (1, -1)); c.sock("Prop", (6.0, 6.0, 0), (-1, -1)); c.sock("Prop", (6.0, -6.0, 0), (-1, 1))
    c.sock("Spawn", (-3.5, -5.0, 0), (0, 1))
    c.extra["extraction_zone_size"] = [9.5, 3.3, 7.0]
    c.rubble(-5.8, -6.0, 0.6, 0.5, 0.35, 6, max_r=0.25)
    return c


def as_cat(fn, default="D"):
    def w(self, *a, **k):
        cat = k.pop("cat", default)
        old = self.mb.cat
        self.mb.cat = cat
        try:
            return fn(self, *a, **k)
        finally:
            self.mb.cat = old
    w.__name__ = fn.__name__
    return w


for _n in ("bone", "skull", "candle", "torch_bracket", "scratches", "rubble", "bones_scatter"):
    setattr(Chunk, _n, as_cat(getattr(Chunk, _n)))

VARIANT = {}
exec(open(os.path.join(SRC, "chunks_extra.py"), encoding="utf-8").read(), globals())

BASE_CHUNKS = [ch_corridor_straight, ch_corridor_corner, ch_corridor_t, ch_corridor_cross, ch_corridor_deadend,
          ch_room_storage, ch_room_pillarhall, ch_room_crypt, ch_room_ritual,
          ch_sp_collapsed, ch_sp_flooded, ch_sp_pitbridge, ch_sp_belltower, ch_sp_den,
          ch_generator_alcove, ch_extraction_gate]
CHUNKS = BASE_CHUNKS + EXTRA_CHUNKS


# ───────────────────────────── scene assembly / export ─────────────────────────────
def get_mat(name, color, alpha=1.0, tex=None):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    m.use_backface_culling = alpha >= 1          # like Unity (Cull Back): renders match the game
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = 0.85 if alpha >= 1 else 0.1
    if alpha < 1:
        bsdf.inputs["Alpha"].default_value = alpha
        try:
            m.surface_render_method = 'BLENDED'
        except Exception:
            pass
    if tex:
        tn = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
        img = bpy.data.images.get(os.path.basename(tex))
        if img:
            img.reload()
        else:
            img = bpy.data.images.load(tex)
        tn.image = img
        tn.interpolation = 'Closest'
        # texture x baked vertex colour (same as Fluffterror/PS2Lit in Unity), for EEVEE renders
        vc = next((n for n in nt.nodes if n.type == "VERTEX_COLOR"), None) or nt.nodes.new("ShaderNodeVertexColor")
        vc.layer_name = "Col"
        mix = next((n for n in nt.nodes if n.type == "MIX" and n.data_type == 'RGBA'), None)
        if mix is None:
            mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'
        mix.blend_type = 'MULTIPLY'
        mix.inputs[0].default_value = 1.0
        nt.links.new(tn.outputs["Color"], mix.inputs[6])
        nt.links.new(vc.outputs["Color"], mix.inputs[7])
        nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    return m


def clear_chunks(only=None):
    for coll in list(bpy.data.collections):
        if coll.name.startswith("CH_") and (not only or coll.name in only):
            for o in list(coll.objects):
                bpy.data.objects.remove(o, do_unlink=True)
            bpy.data.collections.remove(coll)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)


def mask_rows(c):
    rows = []
    for j in reversed(range(c.D)):
        s = ""
        for i in range(c.W):
            t = (i, j)
            if t not in c.walk:
                s += "#"
            elif t in c.blocked:
                s += "o"
            elif t in c.door_ring:
                s += "D"
            else:
                s += "."
        rows.append(s)
    return rows


def unity_vec(p):
    return [round(p[0], 4), round(p[2], 4), round(p[1], 4)]


def collar_mb(c):
    """Door collars: a 1 m tall slab on the outer edge of every door, local z 0..1 (object pivot at z = H).
    Scale it (Unity local Y) to (corridor height - H) when a taller generator corridor meets the door, so
    the corridor's cross-section is closed above the lintel. Off by default."""
    m = MB(c.rng)
    for (label, side, cc) in c.doors:
        if side in "NS":
            ye = c.D / 2 if side == 'N' else -c.D / 2
            y0, y1 = (ye - 0.3, ye) if side == 'N' else (ye, ye + 0.3)
            m.box(cc - 2.5, y0, 0.0, cc + 2.5, y1, 1.0, "wall0")
        else:
            xe = c.W / 2 if side == 'E' else -c.W / 2
            x0, x1 = (xe - 0.3, xe) if side == 'E' else (xe, xe + 0.3)
            m.box(x0, cc - 2.5, 0.0, x1, cc + 2.5, 1.0, "wall0")
    return m


def roof_mb(c):
    """Upward-facing cap over the footprint for open-sky themes (Graveyard). Off by default."""
    m = MB(c.rng)
    w, d = c.W / 2, c.D / 2
    m.poly([(-w, -d, 0.0), (w, -d, 0.0), (w, d, 0.0), (-w, d, 0.0)], "wall3")
    return m


def place_obj(o, piv):
    o.matrix_world = R @ Matrix.Translation(Vector(piv)) @ R.inverted()


def build_one(c, coll, mat, wmat, smat):
    base = "SM_" + c.name[3:]
    objs = []
    tris = {}

    bake = Baker([(c.mb, Vector()), (c.ceil, Vector())])

    def add(mb, name, material, cats=None, piv=None):
        o = mb.to_object(name, material, coll, cats=cats, origin=piv or (0, 0, 0), bake=bake)
        if o is None:
            return None
        if piv is not None:
            place_obj(o, piv)
        objs.append(o)
        return o

    # LOD split: structure (both LODs) / detail + slabs (LOD0) / flat floor (LOD1)
    add(c.mb, base, mat, cats={"S"})
    add(c.mb, base + "_Detail", mat, cats={"D", "F"})
    add(c.mb, base + "_FloorLOD1", mat, cats={"L1"})
    tris["structure"] = c.mb.tris({"S"})
    tris["detail"] = c.mb.tris({"D", "F"})
    tris["floor_lod1"] = c.mb.tris({"L1"})
    shared = 0
    if c.ceil.f:
        add(c.ceil, base + "_Ceiling", mat)
        tris["ceiling"] = c.ceil.tris(); shared += tris["ceiling"]
    if "water" in c.extra:
        add(c.extra["water"], base + "_Water", wmat)
        tris["water"] = c.extra["water"].tris(); shared += tris["water"]
    if "shaft" in c.extra:
        add(c.extra["shaft"], base + "_Shaft", smat)
        tris["shaft"] = c.extra["shaft"].tris(); shared += tris["shaft"]
    for part in ("bell", "gate"):
        if part in c.extra:
            mbp, piv = c.extra[part]
            add(mbp, base + "_" + part.capitalize(), mat, piv=piv)
            tris[part] = mbp.tris(); shared += tris[part]
    for k, (mbp, piv) in enumerate(c.extra.get("lids", [])):
        add(mbp, "%s_Lid_%02d" % (base, k + 1), mat, piv=piv)
        tris["lids"] = tris.get("lids", 0) + mbp.tris(); shared += mbp.tris()
    add(collar_mb(c), base + "_Collar", mat, piv=(0, 0, c.H))
    add(roof_mb(c), base + "_Roof", mat, piv=(0, 0, c.H + 0.02))
    # exported (split) triangle counts per object
    def otris(suffix):
        o = next((q for q in objs if q.name.split(".")[0] == base + suffix), None)
        return sum(len(p.vertices) - 2 for p in o.data.polygons) if o else 0
    tris = {"structure": otris(""), "detail": otris("_Detail"), "floor_lod1": otris("_FloorLOD1")}
    shared = 0
    for suf, k in (("_Ceiling", "ceiling"), ("_Water", "water"), ("_Shaft", "shaft"), ("_Bell", "bell"), ("_Gate", "gate")):
        if otris(suf):
            tris[k] = otris(suf); shared += tris[k]
    lids = sum(sum(len(p.vertices) - 2 for p in q.data.polygons) for q in objs if "_Lid_" in q.name)
    if lids:
        tris["lids"] = lids; shared += lids
    lod0 = tris["structure"] + tris["detail"] + shared
    lod1 = tris["structure"] + tris["floor_lod1"] + shared
    # colliders
    counters = {}
    cols = list(c.cols)
    if "gate_collider" in c.extra:
        gc = c.extra["gate_collider"]; cols.append(("Gate", gc[0], gc[1], (0, 0, 0)))
    for k, lc in enumerate(c.extra.get("lid_colliders", [])):
        cols.append(("Lid", lc[0], lc[1], (0, 0, 0)))
    if "water_vol" in c.extra:
        wv = c.extra["water_vol"]; cols.append(("VOLWater", wv[0], wv[1], (0, 0, 0)))
    if "trap_vol" in c.extra:
        tv = c.extra["trap_vol"]; cols.append(("VOLTrap", tv[0], tv[1], (0, 0, 0)))
    for (kind, cen, size, rot) in cols:
        n = counters.get(kind, 0) + 1; counters[kind] = n
        if kind.startswith("VOL"):
            name = "VOL_" + kind[3:]
        else:
            name = "COL_%s_%02d" % (kind, n)
        bm = MB(c.rng)
        sx, sy, sz = (q / 2 for q in size)
        bm.box(-sx, -sy, -sz, sx, sy, sz, "mortar")
        o = bm.to_object(name, mat, coll, matrix=Matrix(), split=False)
        rm = Matrix.Rotation(rot[2], 4, 'Z') @ Matrix.Rotation(rot[1], 4, 'Y') @ Matrix.Rotation(rot[0], 4, 'X')
        o.matrix_world = Matrix.Translation(R @ Vector(cen)) @ R @ rm @ R.inverted()
        o.display_type = 'WIRE'; o.hide_render = True
        objs.append(o)
    # sockets
    socks_out = []
    for (name, pos, face) in c.socks:
        e = bpy.data.objects.new(name, None)
        e.empty_display_type = 'SINGLE_ARROW' if not name.startswith("SOCKET_Door") else 'CUBE'
        e.empty_display_size = 0.5 if name.startswith("SOCKET_Door") else 0.35
        fx, fy = face
        ln = math.hypot(fx, fy) or 1
        fx, fy = fx / ln, fy / ln
        phi = math.atan2(-fx, fy)
        # Unity forward = the empty's local -Y (FBX -Z fwd / Y up / bake space transform).
        # Rotation(phi + pi) maps local -Y to (fx, fy) in natural coords, R takes it to the Blender frame.
        e.matrix_world = R @ Matrix.Translation(pos) @ Matrix.Rotation(phi + math.pi, 4, 'Z')
        e.hide_render = True
        coll.objects.link(e)
        objs.append(e)
        socks_out.append({"name": name, "pos": unity_vec(pos), "forward": [round(fx, 3), 0, round(fy, 3)]})
    # hero prop anchors (Unity forward = prop front, like the sockets)
    counts = {}
    for (prop, pos, face, scale, part) in c.heroes:
        counts[prop] = counts.get(prop, 0) + 1
    seen = {}
    for (prop, pos, face, scale, part) in c.heroes:
        seen[prop] = seen.get(prop, 0) + 1
        # a moving part is encoded in the name (HERO_<Prop>__<Part>): ChunkPrefabBuilder re-parents it in Unity.
        # (Parenting an empty under a mesh here breaks the FBX bake_space_transform: it lands Y/Z-swapped.)
        name = "HERO_" + prop + ("_%02d" % seen[prop] if counts[prop] > 1 else "") + ("__" + part if part else "")
        e = bpy.data.objects.new(name, None)
        e.empty_display_type = 'ARROWS'; e.empty_display_size = 0.5
        fx, fy = face
        ln = math.hypot(fx, fy) or 1
        fx, fy = fx / ln, fy / ln
        phi = math.atan2(-fx, fy)
        e.matrix_world = R @ Matrix.Translation(pos) @ Matrix.Rotation(phi + math.pi, 4, 'Z') @ Matrix.Scale(scale, 4)
        e.hide_render = True
        coll.objects.link(e)
        objs.append(e)
        c.extra.setdefault("heroes", []).append({"name": name, "prefab": "PROP_Meshy" + prop, "pos": unity_vec(pos),
                                                 "forward": [round(fx, 3), 0, round(fy, 3)], "scale": scale, "part": part or ""})
    return objs, tris, lod0, lod1, socks_out


def default_tags(c):
    torches = sum(1 for s in c.socks if s[0].startswith("SOCKET_Torch"))
    role = c.role or "room"
    t = {"themes": ["Any"], "role": role, "weight": 1.0 if role == "corridor" else 0.5,
         "min": 0, "max": 4 if role == "corridor" else 2,
         "torches_suggested": min(torches, 1 if role == "corridor" else 3)}
    base_name = c.name.split("_H")[0] if VARIANT.get("suffix") else c.name
    t.update(TAGS.get(base_name, {}))
    if VARIANT.get("suffix") == "_H36":
        t["themes"] = ["Catacombs"]
    elif VARIANT.get("suffix") == "_H50":
        t["themes"] = ["GothicRuins", "Transition", "HellPillars"]
    return t


def build_all(export=True, only=None):
    os.makedirs(UNITY_MODELS, exist_ok=True)
    tex_path = os.path.join(SRC, "T_Chunks_D.png")
    make_ps2_atlas(tex_path)
    shutil.copyfile(tex_path, os.path.join(UNITY_DIR, "T_Chunks_D.png"))
    ntex_path = os.path.join(SRC, "T_Chunks_N.png")
    make_ps2_normal_atlas(ntex_path)
    shutil.copyfile(ntex_path, os.path.join(UNITY_DIR, "T_Chunks_N.png"))
    mat = get_mat("M_Chunks", (1, 1, 1), tex=tex_path)
    wmat = get_mat("M_ChunksWater", (0.03, 0.09, 0.1), alpha=0.75)
    wmat.diffuse_color = (0.05, 0.16, 0.18, 0.8)
    smat = get_mat("M_ChunksShaft", (0.55, 0.62, 0.75), alpha=0.2)
    smat.diffuse_color = (0.6, 0.68, 0.8, 0.25)
    clear_chunks(only)
    scene = bpy.context.scene
    manifest = {"cell_m": 8, "tile_m": 1, "door": {"width": DOOR_W, "height": DOOR_H}, "wall_thickness": WT,
                "pivot": "footprint centre, floor level", "forward": "+Z = North",
                "theme_wall_heights": THEME_HEIGHTS,
                "collar": "SM_<Chunk>_Collar: 1 m slab per door above the chunk height, scale Y = corridorHeight - wall_height (AuthoredChunk.FitToTheme)",
                "chunks": []}
    labels = bpy.data.collections.get("Labels")
    if labels:
        for o in list(labels.objects):
            if not only or o.name[4:] in only:
                bpy.data.objects.remove(o, do_unlink=True)
    else:
        labels = bpy.data.collections.new("Labels")
        scene.collection.children.link(labels)
    jobs = [(fn, None) for fn in CHUNKS]
    for fn in HEIGHT_VARIANT_FNS:
        for (h, suf) in ((3.6, "_H36"), (5.0, "_H50")):
            jobs.append((fn, (h, suf)))
    idx = 0
    for fn, var in jobs:
        VARIANT.clear()
        if var:
            VARIANT.update(H=var[0], suffix=var[1])
        c = fn()
        if only and c.name not in only:
            VARIANT.clear()
            idx += 1
            continue
        coll = bpy.data.collections.new(c.name)
        scene.collection.children.link(coll)
        root = bpy.data.objects.new(c.name, None)
        root.empty_display_type = 'PLAIN_AXES'
        coll.objects.link(root)
        objs, tris, lod0, lod1, socks_out = build_one(c, coll, mat, wmat, smat)
        for o in objs:
            if o.parent is None:
                o.parent = root
            if o.name.endswith(("_Collar", "_Roof")):
                o.hide_render = True
        home = Vector(((idx % 6) * 24.0, -(idx // 6) * 26.0, 0))
        root.location = home
        fbx = os.path.join(UNITY_MODELS, c.name + ".fbx")
        if export:
            root.location = (0, 0, 0)
            bpy.ops.object.select_all(action='DESELECT')
            root.select_set(True)
            for o in objs:
                o.select_set(True)
            bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'MESH', 'EMPTY'},
                                     apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                                     bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False,
                                     use_mesh_modifiers=False, path_mode='STRIP', embed_textures=False,
                                     colors_type='LINEAR', prioritize_active_color=True)
            root.location = home
        tags = default_tags(c)
        manifest["chunks"].append({
            "name": c.name, "role": tags["role"], "cells": [c.cw, c.ch], "size_m": [c.W, c.D], "wall_height": c.H,
            "tags": tags,
            "doors": [{"label": d[0], "side": d[1], "offset": d[2]} for d in c.doors],
            "tris": tris, "tris_lod0": lod0, "tris_lod1": lod1,
            "mask_north_up": mask_rows(c),
            "sockets": socks_out,
            "notes": {k: v for k, v in c.extra.items() if isinstance(v, (dict, str, list)) and k not in ("lids", "lid_colliders")},
        })
        print("%-30s %3dx%-3d H%.1f LOD0 %5d LOD1 %5d (%2d%%) sockets %d" % (c.name, c.W, c.D, c.H, lod0, lod1, 100 * lod1 // max(lod0, 1), len(socks_out)))
        cu = bpy.data.curves.new(c.name + "_label", 'FONT')
        cu.body = "%s  %dx%d  %d/%d tris" % (c.name[3:], c.W, c.D, lod0, lod1)
        cu.size = 0.9
        cu.align_x = 'CENTER'
        lab = bpy.data.objects.new("LBL_" + c.name, cu)
        lab.location = home + Vector((0, c.D / 2 + 1.6, 0.05))
        lab.rotation_euler = (0, 0, math.pi)
        labels.objects.link(lab)
        idx += 1
        VARIANT.clear()
    mpath = os.path.join(SRC, "chunks_manifest.json")
    if only and os.path.exists(mpath):      # partial rebuild: keep every other chunk's entry, in order
        with open(mpath) as f:
            prev = json.load(f)
        new = {e["name"]: e for e in manifest["chunks"]}
        merged = [new.pop(e["name"], e) for e in prev.get("chunks", [])]
        manifest["chunks"] = merged + list(new.values())
    with open(mpath, "w") as f:
        json.dump(manifest, f, indent=1)
    shutil.copyfile(mpath, os.path.join(UNITY_DIR, "chunks_manifest.json"))
    return manifest


if not globals().get("CHUNKS_NO_AUTORUN"):
    build_all(export=True, only=globals().get("CHUNKS_ONLY"))
