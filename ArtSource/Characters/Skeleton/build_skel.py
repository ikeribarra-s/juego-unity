import bpy, bmesh, math
from mathutils import Vector, Matrix

NAME = "Skeleton_LowPoly"
# v2: head + torso come from the decimated Meshy reference (see README.md); set True to rebuild the v1 procedural ones
LEGACY_UPPER = False
# v3: pelvis + sacrum also come from the reference (derive_core.py)
LEGACY_PELVIS = False
if "BUILD_NAME" in globals():
    NAME = BUILD_NAME
V = Vector

# ---------------------------------------------------------------- helpers
def tube(bm, pts, sides=8, ref=(0, 0, 1), cap0=True, cap1=True, twist=0.0, flat_bottom=None):
    """pts: list of (center, r1, r2). r1 along U = T x Vref, r2 along Vref (perp. to T)."""
    rings = []
    n = len(pts)
    refv = V(ref).normalized()
    for i, (c, r1, r2) in enumerate(pts):
        c = V(c)
        if i == 0:
            t = V(pts[1][0]) - c
        elif i == n - 1:
            t = c - V(pts[i - 1][0])
        else:
            t = (V(pts[i + 1][0]) - V(pts[i - 1][0]))
        t.normalize()
        vv = refv - refv.dot(t) * t
        if vv.length < 1e-6:
            vv = V((1, 0, 0)) - V((1, 0, 0)).dot(t) * t
        vv.normalize()
        uu = t.cross(vv).normalized()
        ring = []
        for k in range(sides):
            a = 2 * math.pi * k / sides + twist + math.pi / sides
            p = c + uu * (r1 * math.cos(a)) + vv * (r2 * math.sin(a))
            if flat_bottom is not None and p.z < flat_bottom:
                p.z = flat_bottom
            ring.append(bm.verts.new(p))
        rings.append(ring)
    for i in range(n - 1):
        a, b = rings[i], rings[i + 1]
        for k in range(sides):
            k2 = (k + 1) % sides
            bm.faces.new((a[k], a[k2], b[k2], b[k]))
    def cap(ring, c, d, flip):
        pole = bm.verts.new(c + d)
        for k in range(sides):
            k2 = (k + 1) % sides
            f = (ring[k2], ring[k], pole) if not flip else (ring[k], ring[k2], pole)
            bm.faces.new(f)
    if cap0:
        c = V(pts[0][0]); d = (c - V(pts[1][0])).normalized() * min(pts[0][1], pts[0][2]) * 0.6
        cap(rings[0], c, d, False)
    if cap1:
        c = V(pts[-1][0]); d = (c - V(pts[-2][0])).normalized() * min(pts[-1][1], pts[-1][2]) * 0.6
        cap(rings[-1], c, d, True)
    return rings


def quadsphere(bm, center, radii, cuts=3, rot=None):
    tmp = bmesh.new()
    bmesh.ops.create_cube(tmp, size=2.0)
    bmesh.ops.subdivide_edges(tmp, edges=tmp.edges[:], cuts=cuts, use_grid_fill=True)
    for v in tmp.verts:
        v.co = v.co.normalized()
    m = Matrix.Diagonal((radii[0], radii[1], radii[2], 1.0))
    if rot:
        m = rot.to_4x4() @ m
    m = Matrix.Translation(center) @ m
    bmesh.ops.transform(tmp, matrix=m, verts=tmp.verts[:])
    mesh = bpy.data.meshes.new("tmp"); tmp.to_mesh(mesh); tmp.free()
    new_faces = []
    vmap = {}
    for v in mesh.vertices:
        vmap[v.index] = bm.verts.new(v.co)
    for p in mesh.polygons:
        new_faces.append(bm.faces.new([vmap[i] for i in p.vertices]))
    bpy.data.meshes.remove(mesh)
    return new_faces


def box(bm, center, size, rot=None):
    tmp = bmesh.new()
    bmesh.ops.create_cube(tmp, size=1.0)
    m = Matrix.Diagonal((size[0], size[1], size[2], 1.0))
    if rot:
        m = rot.to_4x4() @ m
    m = Matrix.Translation(center) @ m
    bmesh.ops.transform(tmp, matrix=m, verts=tmp.verts[:])
    vm = {v: bm.verts.new(v.co) for v in tmp.verts}
    for f in tmp.faces:
        bm.faces.new([vm[v] for v in f.verts])
    tmp.free()


def mirror_x(pts):
    return [((-c[0], c[1], c[2]), a, b) for (c, a, b) in pts]

def lerp(a, b, t):
    return V(a).lerp(V(b), t)

def poly_path(ctrl, n):
    """Catmull-Rom through ctrl points, n samples."""
    P = [V(p) for p in ctrl]
    out = []
    segs = len(P) - 1
    for i in range(n):
        u = i / (n - 1) * segs
        s = min(int(u), segs - 1); t = u - s
        p0 = P[max(s - 1, 0)]; p1 = P[s]; p2 = P[s + 1]; p3 = P[min(s + 2, segs)]
        t2, t3 = t * t, t * t * t
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    return out

# ---------------------------------------------------------------- landmarks (new coords: feet at Z=0)
ARM_Z = 1.415
ARM_Y = 0.04
LEG_X = 0.135
LEG_Y = 0.05

def build_side(bm, s):
    """s = +1 right(+X), -1 left. Everything authored on +X then x-flipped."""
    X = lambda x: x * s
    # ---------- arm: shoulder -> wrist, continuous tube, 8 sides
    arm = [
        ((0.165, ARM_Y, ARM_Z), 0.0500, 0.0525),
        ((0.195, ARM_Y, ARM_Z), 0.0525, 0.0550),  # humeral head
        ((0.230, ARM_Y, ARM_Z), 0.0350, 0.0375),
        ((0.330, ARM_Y, ARM_Z), 0.0275, 0.0287),
        ((0.430, ARM_Y, ARM_Z), 0.0300, 0.0300),
        ((0.490, ARM_Y, ARM_Z), 0.0450, 0.0375),  # elbow loops
        ((0.520, ARM_Y, ARM_Z), 0.0475, 0.0400),
        ((0.550, ARM_Y, ARM_Z), 0.0413, 0.0325),
        ((0.640, ARM_Y, ARM_Z), 0.0375, 0.0238),  # radius+ulna, flattened
        ((0.740, ARM_Y, ARM_Z), 0.0413, 0.0238),
        ((0.785, ARM_Y, ARM_Z), 0.0475, 0.0275),  # wrist
        ((0.805, ARM_Y, ARM_Z), 0.0425, 0.0238),
    ]
    tube(bm, [((X(c[0]), c[1], c[2]), a, b) for c, a, b in arm], sides=8, ref=(0, 0, 1))
    # ---------- palm (metacarpal block), slightly arched, 8 sides
    palm = [
        ((0.800, ARM_Y + 0.000, ARM_Z), 0.040, 0.018),
        ((0.825, ARM_Y + 0.000, ARM_Z), 0.050, 0.019),
        ((0.860, ARM_Y + 0.000, ARM_Z - 0.002), 0.058, 0.019),
        ((0.890, ARM_Y + 0.000, ARM_Z - 0.004), 0.057, 0.017),
    ]
    tube(bm, [((X(c[0]), c[1], c[2]), a, b) for c, a, b in palm], sides=8, ref=(0, 0, 1))
    # ---------- fingers: index(front,-Y) .. pinky(back,+Y); 6-sided, loops at MCP/PIP/DIP
    fingers = [  # (y offset, length, radius, fan angle deg)
        (-0.037, 0.100, 0.0130, -6),
        (-0.012, 0.112, 0.0135, -2),
        (0.013, 0.104, 0.0130, 2),
        (0.037, 0.084, 0.0120, 7),
    ]
    for yo, L, r, fan in fingers:
        base = V((0.882, ARM_Y + yo, ARM_Z - 0.004))
        ang = math.radians(fan)
        d = V((math.cos(ang), math.sin(ang), 0))
        # phalanx proportions 0.45/0.30/0.25 ; slight natural curl down (Z)
        seg = [0.45 * L, 0.30 * L, 0.25 * L]
        curl = [math.radians(4), math.radians(10), math.radians(10)]
        pts = []
        p = base.copy(); pitch = 0.0
        dirs = []
        joints = [p.copy()]
        for i in range(3):
            pitch += curl[i]
            di = V((d.x * math.cos(pitch), d.y * math.cos(pitch), -math.sin(pitch)))
            p = p + di * seg[i]
            joints.append(p.copy()); dirs.append(di)
        # rings: MCP (-,0,+), shaft, PIP(-,0,+), shaft-less, DIP(-,0,+), tip
        def at(j, off):
            if off < 0:
                return joints[j] - dirs[j - 1] * (-off) if j > 0 else joints[0] + dirs[0] * off
            return joints[j] + dirs[min(j, 2)] * off
        e = 0.012
        pts = [
            (at(0, -0.008), r * 1.15, r * 1.05),
            (at(0, 0.0), r * 1.25, r * 1.15),        # MCP knuckle
            (at(0, e), r * 1.05, r * 1.0),
            (lerp(joints[0], joints[1], 0.6), r * 0.92, r * 0.88),
            (at(1, -e * 0.7), r * 1.0, r * 0.95),
            (joints[1], r * 1.12, r * 1.05),          # PIP knuckle
            (at(1, e * 0.7), r * 0.98, r * 0.92),
            (at(2, -e * 0.6), r * 0.92, r * 0.88),
            (joints[2], r * 1.0, r * 0.95),           # DIP knuckle
            (at(2, e * 0.6), r * 0.88, r * 0.84),
            (joints[3], r * 0.80, r * 0.72),          # tip
        ]
        tube(bm, [((X(c[0]), c[1], c[2]), a, b) for c, a, b in pts], sides=6, ref=(0, 0, 1))
    # ---------- thumb: CMC near wrist, points forward(-Y) and out(+X), slightly down
    tb = V((0.815, ARM_Y - 0.040, ARM_Z - 0.006))
    t_dirs = [V((0.55, -0.80, -0.20)).normalized(), V((0.75, -0.62, -0.22)).normalized(), V((0.85, -0.45, -0.28)).normalized()]
    t_len = [0.040, 0.034, 0.030]
    tj = [tb.copy()]
    for di, L in zip(t_dirs, t_len):
        tj.append(tj[-1] + di * L)
    r = 0.0150
    e = 0.010
    tpts = [
        (tj[0] - t_dirs[0] * 0.004, r * 1.15, r * 1.1),
        (lerp(tj[0], tj[1], 0.5), r * 1.0, r * 0.95),
        (tj[1] - t_dirs[0] * e, r * 1.0, r * 0.95),
        (tj[1], r * 1.15, r * 1.05),                  # MCP
        (tj[1] + t_dirs[1] * e, r * 0.98, r * 0.92),
        (tj[2] - t_dirs[1] * e, r * 0.95, r * 0.9),
        (tj[2], r * 1.05, r * 0.98),                  # IP
        (tj[2] + t_dirs[2] * e, r * 0.92, r * 0.86),
        (tj[3], r * 0.80, r * 0.72),
    ]
    tube(bm, [((X(c[0]), c[1], c[2]), a, b) for c, a, b in tpts], sides=6, ref=(0, 0, 1))

    # ---------- leg: femoral head -> ankle, 8 sides (ref = -Y so r1 = X extent, r2 = Y extent)
    hipc = V((0.118, LEG_Y - 0.01, 0.805))
    leg = [
        ((0.125, LEG_Y - 0.005, 0.820), 0.0408, 0.0408),
        ((LEG_X, LEG_Y, 0.790), 0.0504, 0.0480),  # femoral head / greater trochanter
        ((LEG_X, LEG_Y, 0.740), 0.0384, 0.0360),
        ((LEG_X, LEG_Y, 0.620), 0.0312, 0.0312),
        ((LEG_X, LEG_Y, 0.520), 0.0360, 0.0336),
        ((LEG_X, LEG_Y, 0.475), 0.0552, 0.0480),  # knee loops
        ((LEG_X, LEG_Y - 0.005, 0.440), 0.0600, 0.0528),
        ((LEG_X, LEG_Y, 0.400), 0.0516, 0.0432),
        ((LEG_X, LEG_Y, 0.300), 0.0408, 0.0312),  # tibia+fibula
        ((LEG_X, LEG_Y, 0.170), 0.0384, 0.0288),
        ((LEG_X, LEG_Y, 0.110), 0.0480, 0.0360),  # ankle
        ((LEG_X, LEG_Y, 0.075), 0.0432, 0.0336),
    ]
    tube(bm, [((X(c[0]), c[1], c[2]), a, b) for c, a, b in leg], sides=8, ref=(0, -1, 0))
    # ---------- foot: heel -> toes along -Y, flat sole at Z=0 (ref=Z, so r1 = X extent, r2 = Z extent)
    foot = [
        ((LEG_X, 0.125, 0.040), 0.026, 0.028),
        ((LEG_X, 0.105, 0.045), 0.040, 0.045),   # heel
        ((LEG_X, 0.040, 0.050), 0.046, 0.052),   # ankle / tarsals
        ((LEG_X + 0.004, -0.040, 0.038), 0.050, 0.040),
        ((LEG_X + 0.006, -0.110, 0.028), 0.054, 0.030),  # ball
        ((LEG_X + 0.007, -0.135, 0.026), 0.052, 0.027),
        ((LEG_X + 0.007, -0.170, 0.022), 0.044, 0.022),  # toes
    ]
    tube(bm, [((X(c[0]), c[1], c[2]), a, b) for c, a, b in foot], sides=8, ref=(0, 0, 1), flat_bottom=0.0)
    # toe grooves are painted in the texture

    if LEGACY_UPPER:
        # ---------- clavicle: sternum top -> shoulder
        clav = poly_path([(0.035, -0.150, 1.455), (0.100, -0.110, 1.462), (0.150, -0.020, 1.452), (0.180, 0.040, 1.440)], 6)
        tube(bm, [((X(p.x), p.y, p.z), 0.016, 0.014) for p in clav], sides=6, ref=(0, 0, 1))
        # ---------- scapula (flat plate on the back)
        quadsphere(bm, V((X(0.115), 0.125, 1.355)), (0.065, 0.018, 0.075), cuts=1,
                   rot=Matrix.Rotation(math.radians(-20 * s), 3, 'Z'))

        # ---------- ribs: 4 pairs, spine -> around -> towards sternum, sloping down at the front
        rib_defs = [  # (z at spine, half width, front y, drop to front, front x end)
            (1.355, 0.190, -0.170, 0.060, 0.045),
            (1.280, 0.205, -0.175, 0.075, 0.050),
            (1.205, 0.195, -0.160, 0.085, 0.060),
            (1.135, 0.170, -0.120, 0.070, 0.090),
        ]
        for z0, hw, fy, drop, fx in rib_defs:
            ctrl = [
                (0.030, 0.105, z0),
                (hw * 0.75, 0.120, z0 + 0.005),
                (hw, 0.030, z0 - drop * 0.25),
                (hw * 0.85, -0.090, z0 - drop * 0.6),
                (fx + 0.04, fy + 0.005, z0 - drop * 0.9),
                (fx, fy, z0 - drop),
            ]
            path = poly_path(ctrl, 7)
            rr = []
            for i, p in enumerate(path):
                t = i / (len(path) - 1)
                rr.append(((X(p.x), p.y, p.z), 0.013, 0.022 - 0.004 * t))
            tube(bm, rr, sides=6, ref=(0, 0, 1))

    if LEGACY_PELVIS:
        # ---------- pelvis: iliac wing + hip socket + pubic arch half
        quadsphere(bm, V((X(0.130), 0.045, 0.925)), (0.095, 0.026, 0.085), cuts=2,
                   rot=Matrix.Rotation(math.radians(-30 * s), 3, 'Z') @ Matrix.Rotation(math.radians(-15), 3, 'X'))
        pub = poly_path([(0.130, 0.040, 0.870), (0.115, -0.010, 0.810), (0.070, -0.045, 0.775), (0.000, -0.055, 0.770)], 6)
        tube(bm, [((X(p.x), p.y, p.z), 0.026, 0.030) for p in pub], sides=6, ref=(0, 0, 1),
             cap1=True)


def build_center(bm):
    if LEGACY_UPPER:
        # ---------- spine: sacrum -> neck, vertebra bumps (alternating radii), 8 sides
        spine_ctrl = [(0, 0.115, 0.800), (0, 0.105, 0.950), (0, 0.085, 1.080), (0, 0.105, 1.250), (0, 0.110, 1.420), (0, 0.070, 1.560)]
        path = poly_path(spine_ctrl, 19)
        pts = []
        for i, p in enumerate(path):
            t = i / (len(path) - 1)
            base = 0.038 if t < 0.35 else 0.033
            if t > 0.80: base = 0.028
            bump = 1.25 if i % 2 == 0 else 0.92
            pts.append((p, base * bump, base * bump * 0.95))
        tube(bm, pts, sides=6, ref=(0, -1, 0))
    # sacrum block
    if LEGACY_PELVIS:
        quadsphere(bm, V((0, 0.105, 0.860)), (0.060, 0.035, 0.075), cuts=1)
    if not LEGACY_UPPER:
        return
    # ---------- sternum
    st = [((0, -0.150, 1.465), 0.026, 0.010), ((0, -0.165, 1.400), 0.034, 0.013), ((0, -0.178, 1.290), 0.032, 0.013),
          ((0, -0.172, 1.190), 0.024, 0.012), ((0, -0.150, 1.110), 0.010, 0.008)]
    tube(bm, st, sides=6, ref=(0, -1, 0))

    # ---------- cranium: quad-sphere, big cartoony
    tmp = bmesh.new()
    bmesh.ops.create_cube(tmp, size=2.0)
    bmesh.ops.subdivide_edges(tmp, edges=tmp.edges[:], cuts=5, use_grid_fill=True)
    for v in tmp.verts:
        v.co = v.co.normalized()
    C = V((0, 0.010, 1.800))
    for v in tmp.verts:
        x, y, z = v.co
        # flatten the bottom/back of the skull a little, narrow the lower face
        sx, sy, sz = 0.170, 0.212, 0.182
        if z < -0.2:
            sx *= 1.0 - 0.25 * min(1.0, (-z - 0.2) / 0.8)
        if z < 0:
            sz *= 0.92 if y > 0 else 0.92 + 0.32 * min(1.0, -y * 1.4)
        v.co = V((x * sx, y * sy, z * sz)) + C
    # eye sockets + nose: smooth dents pushed into the skull (no topology change, symmetric)
    for cx, cz, rad, depth in [(0.066, 1.775, 0.075, 0.100), (-0.066, 1.775, 0.075, 0.100), (0.0, 1.688, 0.036, 0.045)]:
        for v in tmp.verts:
            if v.co.y > C.y - 0.08:
                continue
            d = math.hypot(v.co.x - cx, v.co.z - cz) / rad
            if d < 1.0:
                w = 1.0 - d * d
                v.co.y += depth * w
    mesh = bpy.data.meshes.new("tmp"); tmp.to_mesh(mesh); tmp.free()
    vm = {v.index: bm.verts.new(v.co) for v in mesh.vertices}
    for p in mesh.polygons:
        bm.faces.new([vm[i] for i in p.vertices])
    bpy.data.meshes.remove(mesh)

    # ---------- mandible: horseshoe tube
    jaw = poly_path([(-0.100, 0.045, 1.625), (-0.112, 0.030, 1.570), (-0.095, -0.095, 1.548), (-0.045, -0.160, 1.540),
                     (0.0, -0.172, 1.537), (0.045, -0.160, 1.540), (0.095, -0.095, 1.548), (0.112, 0.030, 1.570),
                     (0.100, 0.045, 1.625)], 11)
    tube(bm, [(p, 0.024, 0.030) for p in jaw], sides=6, ref=(0, 0, 1))
    # ---------- teeth: upper + lower rows of small boxes along the front arc
    for row, (z, ry) in enumerate([(1.605, 0.178), (1.565, 0.172)]):
        n = 6
        for i in range(n):
            a = math.radians(-50 + 100 * (i + 0.5) / n)
            px = math.sin(a) * 0.100
            py = -math.cos(a) * ry + 0.0
            rot = Matrix.Rotation(-a, 3, 'Z')
            box(bm, V((px, py, z)), (0.026, 0.020, 0.036 if row == 0 else 0.030), rot=rot)
    # ---------- neck vertebrae + atlas under the skull
    for z, r in [(1.500, 0.030), (1.540, 0.032)]:
        quadsphere(bm, V((0, 0.075, z)), (r * 1.3, r, 0.016), cuts=1)


def build():
    old = bpy.data.objects.get(NAME)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    bm = bmesh.new()
    build_side(bm, 1)
    build_side(bm, -1)
    build_center(bm)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    # force outward normals per island via signed volume
    seen = set(); flipped = 0
    for f0 in bm.faces:
        if f0 in seen: continue
        isl = []; stack = [f0]; seen.add(f0)
        while stack:
            f = stack.pop(); isl.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g not in seen:
                        seen.add(g); stack.append(g)
        vol = 0.0
        for f in isl:
            vs = [v.co for v in f.verts]
            for i in range(1, len(vs) - 1):
                vol += vs[0].dot(vs[i].cross(vs[i + 1]))
        if vol < 0:
            bmesh.ops.reverse_faces(bm, faces=isl); flipped += 1
    print("islands flipped", flipped)
    me = bpy.data.meshes.new(NAME)
    bm.to_mesh(me); bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new(NAME, me)
    bpy.context.scene.collection.objects.link(ob)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    print("faces", len(me.polygons), "tris", tris)
    return ob

build()
