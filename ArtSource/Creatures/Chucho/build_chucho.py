"""Chucho (Creature 01, the bone hound) - procedural low-poly build for Fluffterror.

Run inside Blender 4.5:
    g = {'__file__': r'<repo>/ArtSource/Creatures/Chucho/build_chucho.py'}
    exec(open(g['__file__']).read(), g); g['main']()

Steps (each can be run alone): build_mesh() -> build_uv() -> paint_texture() -> build_rig() -> skin()
-> export().  Conventions: 1 BU = 1 m, Z-up, the dog faces -Y (FBX forward -Z / up Y -> Unity +Z
forward), pivot = world origin = ground between the four paws.
"""
import bpy, bmesh, math, random, os
import numpy as np
from mathutils import Vector, Matrix

V = Vector
HERE = os.path.dirname(os.path.abspath(__file__)) if '__file__' in globals() else os.getcwd()
MESH_NAME = "SK_Chucho"
RIG_NAME = "Chucho_Rig"
TEX_SIZE = 512

PARTS = []          # part names, index stored in the face int layer "part"
PART_BONES = {}     # part -> bones allowed to deform it


def part_id(name, bones):
    if name not in PARTS:
        PARTS.append(name)
    PART_BONES[name] = bones
    return PARTS.index(name)


# ------------------------------------------------------------------ joints (shared by mesh + rig)
# (x, y, z); the dog faces -Y.  Nose ~ y -1.72, tail tip ~ y +1.34  ->  ~3.06 m. Withers 1.5 m.
J = dict(
    root=(0, 0, 0),
    hips=(0, 0.82, 1.28), spine1=(0, 0.55, 1.27), spine2=(0, 0.20, 1.26), spine3=(0, -0.15, 1.27),
    chest=(0, -0.45, 1.29), neck1=(0, -0.80, 1.32), neck2=(0, -0.98, 1.50), head=(0, -1.15, 1.72),
    head_end=(0, -1.62, 1.70),
    jaw=(0, -1.24, 1.655), jaw_end=(0, -1.64, 1.54),
    tongue1=(0, -1.30, 1.615), tongue2=(0, -1.60, 1.565), tongue_end=(0, -1.72, 1.36),
    tail1=(0, 1.00, 1.36), tail2=(0, 1.13, 1.38), tail3=(0, 1.24, 1.29), tail4=(0, 1.31, 1.11),
    tail_end=(0, 1.34, 0.84),
)
FRONT_X, HIND_X = 0.165, 0.185
FL = [(-0.70, 1.22), (-0.60, 0.80), (-0.66, 0.29), (-0.70, 0.09), (-0.93, 0.04)]   # (y, z) chain
HL = [(0.78, 1.22), (0.55, 0.78), (0.95, 0.42), (0.88, 0.09), (0.64, 0.04)]
EAR_L = [(0.085, -1.16, 1.86), (0.105, -1.15, 2.00), (0.115, -1.13, 2.13)]          # pricked up
EAR_R = [(-0.085, -1.16, 1.86), (-0.15, -1.17, 1.95), (-0.235, -1.23, 1.83)]        # flopped
EYE = (0.084, -1.40, 1.80)
COLLAR_C, COLLAR_AXIS = V((0, -0.985, 1.515)), V((0, -0.13, 0.17)).normalized()
COLLAR_W, COLLAR_H = 0.135, 0.150


def collar_frame():
    t = COLLAR_AXIS
    vv = (V((0, 0, 1)) - t * t.z).normalized()     # up-back
    uu = t.cross(vv).normalized()
    return t, uu, vv


def bell_anchor():
    t, uu, vv = collar_frame()
    return COLLAR_C - vv * (COLLAR_H + 0.02)


# ------------------------------------------------------------------ mesh helpers
class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.pl = self.bm.faces.layers.int.new("part")
        self.cur = 0

    def face(self, verts):
        f = self.bm.faces.new(verts)
        f[self.pl] = self.cur
        return f

    def loft(self, rings, cap0=True, cap1=True, cap0_off=None, cap1_off=None):
        bm = self.bm
        vr = [[bm.verts.new(V(p)) for p in r] for r in rings]
        n = len(vr[0])
        for i in range(len(vr) - 1):
            a, b = vr[i], vr[i + 1]
            for k in range(n):
                k2 = (k + 1) % n
                self.face((a[k], a[k2], b[k2], b[k]))

        def cap(ring, off, flip):
            c = sum((v.co for v in ring), V()) / n
            if off is None:
                for k in range(1, n - 1):
                    tri = (ring[0], ring[k], ring[k + 1])
                    self.face(tri if flip else tri[::-1])
                return
            pole = bm.verts.new(c + V(off))
            for k in range(n):
                k2 = (k + 1) % n
                self.face((ring[k2], ring[k], pole) if not flip else (ring[k], ring[k2], pole))
        if cap0:
            cap(vr[0], cap0_off, False)
        if cap1:
            cap(vr[-1], cap1_off, True)
        return vr

    def spike(self, base, apex, side, w):
        """Tetrahedron-ish spike: triangular base around `base`, tip at `apex`."""
        base, apex, side = V(base), V(apex), V(side).normalized()
        d = (apex - base).normalized()
        up = d.cross(side).normalized()
        a = self.bm.verts.new(base + side * w)
        b = self.bm.verts.new(base - side * w)
        c = self.bm.verts.new(base + up * w * 0.9)
        t = self.bm.verts.new(apex)
        for f in ((a, b, t), (b, c, t), (c, a, t), (a, c, b)):
            self.face(f)

    def pyramid(self, base, apex, w, axis=(1, 0, 0)):
        base, apex = V(base), V(apex)
        d = (apex - base).normalized()
        s = V(axis); s = (s - d * s.dot(d)).normalized(); u = d.cross(s)
        b = [self.bm.verts.new(base + (s * math.cos(a) + u * math.sin(a)) * w)
             for a in (0, math.pi / 2, math.pi, 1.5 * math.pi)]
        t = self.bm.verts.new(apex)
        for k in range(4):
            self.face((b[k], b[(k + 1) % 4], t))
        self.face((b[3], b[2], b[1], b[0]))


def ring(c, t, w, h, sides, ref=(0, 0, 1), mod=None, rot=0.0):
    """Ellipse ring around centre c, perpendicular to t. w along the side axis, h along `ref`."""
    c, t = V(c), V(t).normalized()
    vv = V(ref) - t * t.dot(V(ref))
    if vv.length < 1e-6:
        vv = V((0, -1, 0)) - t * t.y
    vv.normalize()
    uu = t.cross(vv).normalized()
    pts = []
    for k in range(sides):
        a = 2 * math.pi * k / sides + rot
        sx, sz, dz = (1, 1, 0) if mod is None else mod(a)
        pts.append(c + uu * (w * math.cos(a) * sx) + vv * (h * math.sin(a) * sz + dz))
    return pts


def chain_rings(pts, sides, ref=(0, 0, 1), rot=0.0):
    """pts: [(centre, w, h)] -> rings perpendicular to the local direction."""
    out = []
    n = len(pts)
    for i, (c, w, h) in enumerate(pts):
        c = V(c)
        if i == 0:
            t = V(pts[1][0]) - c
        elif i == n - 1:
            t = c - V(pts[i - 1][0])
        else:
            t = V(pts[i + 1][0]) - V(pts[i - 1][0])
        out.append(ring(c, t, w, h, sides, ref, rot=rot))
    return out


def lerp_profile(keys, y):
    for i in range(len(keys) - 1):
        a, b = keys[i], keys[i + 1]
        if a[0] >= y >= b[0]:
            f = (y - a[0]) / (b[0] - a[0])
            return [a[j] + (b[j] - a[j]) * f for j in range(len(a))]
    return list(keys[0] if y > keys[0][0] else keys[-1])


# ------------------------------------------------------------------ the dog
TORSO_KEYS = [  # y, top, bottom, half width, keel
    (1.05, 1.36, 1.14, 0.10, 0.0),
    (0.95, 1.44, 1.03, 0.18, 0.1),
    (0.78, 1.46, 1.02, 0.21, 0.2),
    (0.52, 1.42, 1.13, 0.165, 0.1),
    (0.28, 1.41, 1.08, 0.175, 0.2),
    (0.06, 1.43, 0.95, 0.215, 0.3),
    (-0.18, 1.45, 0.84, 0.235, 0.45),
    (-0.40, 1.48, 0.79, 0.24, 0.5),
    (-0.60, 1.52, 0.81, 0.235, 0.45),
    (-0.78, 1.49, 0.90, 0.21, 0.35),
    (-0.92, 1.40, 1.02, 0.16, 0.2),
]
RIB_Y = (0.10, -0.62)   # rib cage region (front of belly to behind the shoulder)


def torso_y_samples():
    ys = [1.05, 0.95, 0.86, 0.78, 0.65, 0.52, 0.40, 0.28, 0.17]
    y = RIB_Y[0]
    rib = True
    while y > RIB_Y[1] - 1e-6:
        ys.append((round(y, 3), rib))
        y -= 0.045
        rib = not rib
    ys += [-0.70, -0.78, -0.86, -0.92]
    return [(v, None) if not isinstance(v, tuple) else v for v in ys]


def build_dog(B):
    rnd = random.Random(7)
    S = 12
    # -------- torso
    B.cur = part_id("torso", ["Hips", "Spine1", "Spine2", "Spine3", "Chest", "Neck1"])
    rings = []
    for y, rib in torso_y_samples():
        _, top, bot, w, keel = lerp_profile(TORSO_KEYS, y)
        zc, h = (top + bot) / 2, (top - bot) / 2

        def mod(a, keel=keel, rib=rib, h=h):
            s = math.sin(a)
            sx = 1 - keel * max(0.0, -s) ** 1.5
            if rib is not None and s < 0.55:
                sx *= 1.025 if rib else 0.935       # rib ridge / groove on the flanks only
            dz = 0.03 / h if abs(math.cos(a)) < 1e-3 and s > 0 else 0   # bony spine ridge
            return sx, 1.0, dz * h
        rings.append(ring((0, y, zc), (0, -1, 0), w, h, S, mod=mod))
    B.loft(rings, cap0_off=(0, 0.05, 0.0), cap1_off=None)
    # spine bumps (vertebrae) along the ridge
    B.cur = part_id("spine_bumps", ["Hips", "Spine1", "Spine2", "Spine3", "Chest"])
    y = 0.98
    while y > -0.86:
        _, top, *_ = lerp_profile(TORSO_KEYS, y)
        top += 0.025
        B.pyramid((0, y, top - 0.02), (0, y + 0.015, top + 0.03 + 0.01 * rnd.random()), 0.036,
                  axis=(1, 0, 0))
        y -= 0.135
    # hip crests + shoulder blades
    for sx in (1, -1):
        B.pyramid((sx * 0.11, 0.80, 1.41), (sx * 0.15, 0.86, 1.52), 0.05)
        B.pyramid((sx * 0.13, -0.60, 1.43), (sx * 0.17, -0.55, 1.53), 0.055)
    # -------- neck
    B.cur = part_id("neck", ["Chest", "Neck1", "Neck2", "Head"])
    neck = [((0, -0.70, 1.26), 0.17, 0.21), ((0, -0.80, 1.32), 0.155, 0.19), ((0, -0.92, 1.44), 0.14, 0.165),
            ((0, -1.04, 1.58), 0.125, 0.145), ((0, -1.15, 1.71), 0.12, 0.13), ((0, -1.21, 1.77), 0.10, 0.11)]
    B.loft(chain_rings(neck, 10), cap0_off=None, cap1_off=None)
    B.cur = part_id("spine_bumps", ["Hips", "Spine1", "Spine2", "Spine3", "Chest"])
    PART_BONES["neck_bumps"] = ["Neck1", "Neck2"]
    B.cur = part_id("neck_bumps", ["Neck1", "Neck2"])
    for p, q in (((0, -0.88, 1.58), (0, -0.85, 1.66)), ((0, -1.00, 1.70), (0, -0.97, 1.77))):
        B.pyramid(p, q, 0.03)

    # -------- head (cranium + snout)
    B.cur = part_id("head", ["Head"])
    hk = [  # y, zc, w, h
        (-1.10, 1.81, 0.09, 0.09), (-1.17, 1.81, 0.145, 0.125), (-1.27, 1.80, 0.155, 0.125),
        (-1.36, 1.77, 0.13, 0.105), (-1.44, 1.725, 0.095, 0.08), (-1.55, 1.70, 0.08, 0.068),
        (-1.66, 1.685, 0.068, 0.058), (-1.72, 1.69, 0.052, 0.048)]

    def hmod(a):
        s = math.sin(a)
        return (1 - 0.25 * max(0, s) ** 2, 1.0, 0)   # narrower top -> wedge skull
    rings = [ring((0, y, z), (0, -1, 0), w, h, 10, mod=hmod) for y, z, w, h in hk]
    B.loft(rings, cap0_off=(0, 0.04, 0), cap1_off=(0, -0.025, -0.005))
    # nose
    B.cur = part_id("nose", ["Head"])
    B.loft([ring((0, -1.715, 1.71), (0, -1, 0), 0.042, 0.03, 6),
            ring((0, -1.745, 1.705), (0, -1, 0), 0.034, 0.024, 6)], cap0=False, cap1_off=(0, -0.012, 0))
    # brows (angry, inner end low)
    B.cur = part_id("brow", ["Head"])
    for sx in (1, -1):
        a, b = V((sx * 0.035, -1.415, 1.835)), V((sx * 0.135, -1.35, 1.87))
        B.loft([ring(a, b - a, 0.028, 0.02, 4, ref=(0, -0.4, 1)),
                ring(b, b - a, 0.02, 0.016, 4, ref=(0, -0.4, 1))], cap0_off=None, cap1_off=None)
    # eyes
    B.cur = part_id("eye", ["Head"])
    for sx in (1, -1):
        c = V((sx * EYE[0], EYE[1], EYE[2]))
        n = V((sx * 0.55, -0.83, 0.1)).normalized()
        B.loft([ring(c - n * 0.012, n, 0.03, 0.022, 6, ref=(0, 0, 1)),
                ring(c + n * 0.008, n, 0.022, 0.016, 6, ref=(0, 0, 1))], cap0=False, cap1_off=n * 0.006)
    # upper teeth
    B.cur = part_id("teeth_up", ["Head"])
    for sx in (1, -1):
        B.spike((sx * 0.046, -1.625, 1.655), (sx * 0.044, -1.63, 1.575), (0, 1, 0), 0.012)
        B.spike((sx * 0.062, -1.53, 1.655), (sx * 0.06, -1.535, 1.61), (0, 1, 0), 0.009)
        B.spike((sx * 0.02, -1.69, 1.655), (sx * 0.02, -1.69, 1.625), (1, 0, 0), 0.008)
    # -------- jaw
    B.cur = part_id("jaw", ["Jaw"])
    jk = [(-1.22, 1.64, 0.08, 0.045), (-1.30, 1.625, 0.085, 0.045), (-1.42, 1.595, 0.07, 0.036),
          (-1.55, 1.565, 0.056, 0.03), (-1.64, 1.545, 0.045, 0.026)]
    rings = [ring((0, y, z), (0, -1, -0.27), w, h, 8) for y, z, w, h in jk]
    B.loft(rings, cap0_off=None, cap1_off=(0, -0.025, -0.006))
    B.cur = part_id("teeth_low", ["Jaw"])
    for sx in (1, -1):
        B.spike((sx * 0.036, -1.60, 1.56), (sx * 0.034, -1.605, 1.615), (0, 1, 0), 0.01)
        B.spike((sx * 0.05, -1.48, 1.585), (sx * 0.05, -1.485, 1.62), (0, 1, 0), 0.008)
    # -------- tongue (lies on the jaw, hangs out the front)
    B.cur = part_id("tongue", ["Tongue1", "Tongue2"])
    tp = [((0, -1.28, 1.618), 0.042, 0.012), ((0, -1.42, 1.605), 0.046, 0.012), ((0, -1.56, 1.58), 0.05, 0.012),
          ((0, -1.665, 1.555), 0.052, 0.012), ((0, -1.715, 1.49), 0.05, 0.012), ((0, -1.725, 1.42), 0.046, 0.011),
          ((0, -1.72, 1.36), 0.035, 0.01)]
    B.loft(chain_rings(tp, 6, ref=(0, -1, 0.3)), cap0_off=None, cap1_off=(0, -0.003, -0.02))

    # -------- ears
    for name, pts, bones in (("ear_L", EAR_L, ["Head", "EarL1", "EarL2"]),
                             ("ear_R", EAR_R, ["Head", "EarR1", "EarR2"])):
        B.cur = part_id(name, bones)
        p0, p1, p2 = (V(p) for p in pts)
        flat = (0, -1, 0)
        rs = chain_rings([(p0 + (p0 - p1) * 0.25, 0.06, 0.02), (p1, 0.055, 0.016), (p2, 0.012, 0.006)], 4,
                         ref=flat, rot=math.pi / 4)
        B.loft(rs, cap0_off=None, cap1_off=(p2 - p1).normalized() * 0.02)

    # -------- legs
    for side, sx in (("L", 1), ("R", -1)):
        for kind, chain, X in (("F", FL, FRONT_X), ("H", HL, HIND_X)):
            pre = "Leg%s%s_" % (kind, side)
            bones = [pre + b for b in ("Upper", "Lower", "Foot", "Toe")]
            parent = "Chest" if kind == "F" else "Hips"
            B.cur = part_id("leg_%s%s" % (kind, side), [parent] + bones[:3])
            x = sx * X
            if kind == "F":
                pts = [((x, -0.73, 1.36), 0.12, 0.13), ((x, -0.70, 1.20), 0.115, 0.12), ((x, -0.65, 1.00), 0.085, 0.09),
                       ((x * 1.02, -0.585, 0.80), 0.07, 0.075), ((x, -0.62, 0.62), 0.062, 0.066),
                       ((x, -0.655, 0.30), 0.052, 0.056), ((x, -0.68, 0.18), 0.048, 0.052), ((x, -0.70, 0.10), 0.05, 0.05)]
            else:
                pts = [((x, 0.82, 1.38), 0.12, 0.15), ((x, 0.78, 1.22), 0.13, 0.15), ((x * 1.06, 0.66, 1.00), 0.13, 0.14),
                       ((x * 1.06, 0.555, 0.80), 0.08, 0.085), ((x, 0.72, 0.62), 0.066, 0.068), ((x, 0.90, 0.46), 0.055, 0.056),
                       ((x, 0.93, 0.36), 0.05, 0.05), ((x, 0.90, 0.20), 0.048, 0.05), ((x, 0.88, 0.10), 0.05, 0.05)]
            B.loft(chain_rings(pts, 6, ref=(0, -1, 0)), cap0_off=None, cap1_off=None)
            # huge paw
            B.cur = part_id("paw_%s%s" % (kind, side), [bones[2], bones[3]])
            if kind == "F":
                pk = [(-0.60, 0.075, 0.075, 0.065), (-0.68, 0.085, 0.115, 0.08), (-0.80, 0.07, 0.125, 0.07),
                      (-0.90, 0.05, 0.105, 0.05)]
                tip = (0, -0.05, -0.01)
            else:
                pk = [(0.98, 0.075, 0.075, 0.065), (0.90, 0.085, 0.115, 0.08), (0.78, 0.07, 0.125, 0.07),
                      (0.68, 0.05, 0.105, 0.05)]
                tip = (0, -0.05, -0.01)

            def pmod(a):
                return (1, 1, 0)
            prs = []
            for y, z, w, h in pk:
                r = ring((x, y, z), (0, -1, 0), w, h, 8)
                for p in r:
                    p.z = max(p.z, 0.0)
                prs.append(r)
            B.loft(prs, cap0_off=(0, 0.03, 0.01), cap1_off=tip)
            # claws
            B.cur = part_id("claw_%s%s" % (kind, side), [bones[3]])
            ytip = pk[-1][0] - 0.03
            for cx in (-0.06, -0.02, 0.02, 0.06):
                B.spike((x + cx, ytip + 0.02, 0.04), (x + cx * 1.1, ytip - 0.06, 0.008), (1, 0, 0), 0.016)
    # -------- tail
    B.cur = part_id("tail", ["Hips", "Tail1", "Tail2", "Tail3", "Tail4"])
    tp = [((0, 0.96, 1.35), 0.065, 0.065), ((0, 1.06, 1.38), 0.058, 0.058), ((0, 1.16, 1.36), 0.048, 0.05),
          ((0, 1.25, 1.27), 0.04, 0.04), ((0, 1.31, 1.12), 0.033, 0.033), ((0, 1.335, 0.96), 0.026, 0.026),
          ((0, 1.34, 0.86), 0.018, 0.018)]
    B.loft(chain_rings(tp, 5, ref=(0, 1, 0)), cap0_off=None, cap1_off=(0, 0.0, -0.05))

    # -------- fur tufts (big jagged spikes)
    def tuft(part, bones, base, d, side, n, length, w, spread):
        B.cur = part_id(part, bones)
        base, d, side = V(base), V(d).normalized(), V(side).normalized()
        for i in range(n):
            f = (i - (n - 1) / 2) / max(1, n - 1)
            b = base + side * spread * f
            L = length * (0.7 + 0.5 * rnd.random())
            dd = (d + side * f * 0.5 + V((rnd.uniform(-.15, .15),) * 3)).normalized()
            B.spike(b, b + dd * L, side, w)
    tuft("tuft_neck", ["Neck1", "Neck2"], (0, -0.86, 1.55), (0, 0.6, 0.8), (1, 0, 0), 5, 0.16, 0.045, 0.18)
    tuft("tuft_chest", ["Chest", "Spine3"], (0, -0.52, 0.83), (0, 0.45, -0.9), (1, 0, 0), 4, 0.15, 0.04, 0.2)
    tuft("tuft_belly", ["Spine2", "Spine3"], (0, -0.05, 0.98), (0, 0.5, -0.85), (1, 0, 0), 3, 0.1, 0.035, 0.14)
    for side, sx in (("L", 1), ("R", -1)):
        tuft("tuft_shoulder_" + side, ["Chest", "LegF%s_Upper" % side], (sx * 0.24, -0.66, 1.30),
             (sx * 0.35, 0.6, -0.7), (0, 1, 0.2), 3, 0.15, 0.04, 0.16)
        tuft("tuft_thigh_" + side, ["Hips", "LegH%s_Upper" % side], (sx * 0.27, 0.86, 1.10),
             (sx * 0.3, 0.85, -0.45), (0, 0.2, 1), 3, 0.14, 0.04, 0.16)
        tuft("tuft_cheek_" + side, ["Head"], (sx * 0.15, -1.24, 1.74), (sx * 0.8, 0.5, -0.3),
             (0, 0.3, 1), 2, 0.09, 0.03, 0.06)
    tuft("tail_tufts", ["Tail2", "Tail3", "Tail4"], (0, 1.27, 1.25), (0, 0.9, 0.0), (0, 0.4, -1), 3, 0.11,
         0.03, 0.3)
    tuft("tail_tip", ["Tail4"], (0, 1.34, 0.88), (0, 0.15, -1), (1, 0, 0), 3, 0.12, 0.025, 0.06)

    # -------- collar + oversized bell + tag
    B.cur = part_id("collar", ["Collar"])
    t, uu, vv = collar_frame()
    SEG = 12
    rings = []
    for i in range(SEG):
        a = 2 * math.pi * i / SEG
        c = COLLAR_C + uu * (COLLAR_W + 0.012) * math.cos(a) + vv * (COLLAR_H + 0.012) * math.sin(a)
        tang = (-uu * COLLAR_W * math.sin(a) + vv * COLLAR_H * math.cos(a)).normalized()
        rings.append(ring(c, tang, 0.026, 0.012, 4, ref=t, rot=math.pi / 4))
    rings.append(rings[0])
    # close the torus: loft without duplicating verts by building faces manually
    bm = B.bm
    vr = [[bm.verts.new(p) for p in r] for r in rings[:-1]]
    for i in range(SEG):
        a, b = vr[i], vr[(i + 1) % SEG]
        for k in range(4):
            k2 = (k + 1) % 4
            B.face((a[k], a[k2], b[k2], b[k]))
    B.cur = part_id("bell", ["Bell"])
    anc = bell_anchor()
    # loop
    B.loft([ring(anc + V((0, 0, -0.005)), (1, 0, 0), 0.03, 0.035, 6, ref=(0, 0, 1)),
            ring(anc + V((0, 0, -0.005)), (1, 0, 0), 0.018, 0.022, 6, ref=(0, 0, 1))], cap0=False, cap1=False)
    bc = anc + V((0, 0, -0.14))
    R = 0.105
    bk = [(0.085, 0.25), (0.06, 0.62), (0.02, 0.88), (-0.025, 1.0), (-0.065, 0.86), (-0.09, 0.55)]
    rings = [ring(bc + V((0, 0, dz)), (0, 0, -1), R * s, R * s, 10) for dz, s in bk]
    B.loft(rings, cap0_off=(0, 0, 0.012), cap1_off=(0, 0, 0.012))
    # clapper ball
    B.loft([ring(bc + V((0, 0, -0.085)), (0, 0, -1), 0.03, 0.03, 6), ring(bc + V((0, 0, -0.115)), (0, 0, -1), 0.03, 0.03, 6)],
           cap0_off=(0, 0, 0.015), cap1_off=(0, 0, -0.015))
    # bone-shaped tag "CHUCHO" hanging beside the bell
    B.cur = part_id("tag", ["Bell"])
    tc = anc + V((0.165, -0.035, -0.085))
    outline = []
    for px, pz in [(-0.075, 0.018), (-0.06, 0.03), (-0.045, 0.026), (-0.04, 0.018), (0.04, 0.018), (0.045, 0.026),
                   (0.06, 0.03), (0.075, 0.018), (0.075, -0.018), (0.06, -0.03), (0.045, -0.026), (0.04, -0.018),
                   (-0.04, -0.018), (-0.045, -0.026), (-0.06, -0.03), (-0.075, -0.018)]:
        outline.append((px * 1.15, pz * 1.15))
    rot = Matrix.Rotation(math.radians(-18), 3, 'Z')
    front = [bm.verts.new(tc + rot @ V((px, -0.007, pz))) for px, pz in outline]
    back = [bm.verts.new(tc + rot @ V((px, 0.007, pz))) for px, pz in outline]
    B.face(front[::-1]); B.face(back)
    n = len(outline)
    for k in range(n):
        k2 = (k + 1) % n
        B.face((front[k], front[k2], back[k2], back[k]))
    # split-ring strap from the bell loop to the tag
    a0, a1 = anc + V((0.012, 0, -0.012)), tc + rot @ V((-0.01, 0, 0.03))
    B.loft([ring(a0, a1 - a0, 0.007, 0.007, 4), ring(a1, a1 - a0, 0.007, 0.007, 4)], cap0=False, cap1=False)
    return rnd


TAG_FRAME = None   # (centre, x axis, z axis) used by the texture painter for the text


def tag_frame():
    anc = bell_anchor()
    tc = anc + V((0.165, -0.035, -0.085))
    rot = Matrix.Rotation(math.radians(-18), 3, 'Z')
    return tc, rot @ V((1, 0, 0)), V((0, 0, 1)), rot @ V((0, -1, 0))


HEAD_SCALE, HEAD_PIVOT = 1.2, V((0, -1.17, 1.74))
HEAD_PARTS = {"head", "nose", "brow", "eye", "teeth_up", "jaw", "teeth_low", "tongue", "ear_L", "ear_R",
              "tuft_cheek_L", "tuft_cheek_R"}


SY = 0.92   # global length squash so nose-to-tail is ~3.0 m with the 1.2x head


def bs(p):
    """Body-space point -> final point."""
    p = V(p)
    return V((p.x, p.y * SY, p.z))


def hs(p):
    """Head-space point -> final point (the head is modelled small, scaled up about the skull base and
    carried along with the squashed neck)."""
    q = HEAD_PIVOT + (V(p) - HEAD_PIVOT) * HEAD_SCALE
    return q + V((0, HEAD_PIVOT.y * (SY - 1), 0))


NO_JITTER = {"collar", "bell", "tag", "eye", "teeth_up", "teeth_low", "claw_FL", "claw_FR", "claw_HL", "claw_HR",
             "nose", "tongue", "spine_bumps", "neck_bumps"}


def build_mesh():
    PARTS.clear(); PART_BONES.clear()
    for o in list(bpy.data.objects):
        if o.name.startswith(("SK_Chucho", "Chucho_Rig")):
            bpy.data.objects.remove(o, do_unlink=True)
    B = Builder()
    rnd = build_dog(B)
    bm = B.bm
    bm.verts.ensure_lookup_table()
    for v in bm.verts:
        if {PARTS[f[B.pl]] for f in v.link_faces} & HEAD_PARTS:
            v.co = hs(v.co)
        else:
            v.co = bs(v.co)
    # deterministic jitter for irregular facets (skip hard-surface / tiny parts)
    jit = random.Random(11)
    for v in bm.verts:
        parts = {PARTS[f[B.pl]] for f in v.link_faces}
        if parts & NO_JITTER:
            continue
        amp = 0.006 if parts & {"head", "jaw", "ear_L", "ear_R", "brow"} else 0.012
        if any(p.startswith("paw") for p in parts):
            amp = 0.008
        z0 = v.co.z
        v.co += V((jit.uniform(-amp, amp), jit.uniform(-amp, amp), jit.uniform(-amp, amp)))
        if z0 <= 1e-4:
            v.co.z = 0.0
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(MESH_NAME)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(MESH_NAME, me)
    bpy.context.scene.collection.objects.link(ob)
    ob["parts"] = ",".join(PARTS)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    print("mesh built: %d verts, %d faces, %d tris, %d parts" % (len(me.vertices), len(me.polygons), tris, len(PARTS)))
    return ob


# ------------------------------------------------------------------ UVs
UV_SCALE = {"tag": 10.0, "eye": 3.0, "head": 1.7, "jaw": 1.7, "nose": 1.7, "brow": 1.7, "teeth_up": 1.5,
            "teeth_low": 1.5, "tongue": 1.6, "bell": 2.0, "collar": 1.6, "ear_L": 1.4, "ear_R": 1.4}


def get_ob():
    return bpy.data.objects[MESH_NAME]


def parts_of(ob):
    return ob["parts"].split(",")


def build_uv():
    ob = get_ob()
    me = ob.data
    names = parts_of(ob)
    for o in bpy.context.selected_objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.0, area_weight=0.0,
                             correct_aspect=True, scale_to_bounds=False)
    bm = bmesh.from_edit_mesh(me)
    uvl = bm.loops.layers.uv.active
    pl = bm.faces.layers.int["part"]
    for f in bm.faces:
        k = UV_SCALE.get(names[f[pl]], 1.0)
        for l in f.loops:
            l[uvl].uv *= k
    bmesh.update_edit_mesh(me)
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(rotate=True, margin=0.008)
    bpy.ops.object.mode_set(mode='OBJECT')
    # texel density report
    dens = {}
    pa = me.attributes["part"].data
    uv = me.uv_layers.active.data
    me.calc_loop_triangles()
    for t in me.loop_triangles:
        u = [uv[l].uv for l in t.loops]
        a2 = abs((u[1] - u[0]).cross(u[2] - u[0])) / 2 * TEX_SIZE * TEX_SIZE
        nm = names[pa[t.polygon_index].value]
        d = dens.setdefault(nm, [0, 0])
        d[0] += a2; d[1] += t.area
    for nm in ("torso", "head", "tag", "eye", "bell", "leg_FL"):
        if nm in dens and dens[nm][1] > 0:
            print("texel density %-8s %.0f px/m" % (nm, math.sqrt(dens[nm][0] / dens[nm][1])))


# ------------------------------------------------------------------ texture
def hexc(h):
    h = h.lstrip('#')
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


C = {k: hexc(v) for k, v in dict(
    FUR_DARK="#4A4340", FUR_MID="#6B5F55", FUR_SHADOW="#2F2A28", FUR_LIGHT="#85776A", LEG="#3A3432",
    MANGE="#7D6862", MANGE_RIM="#262120", BONE="#C9BCA2", BONE_SH="#9F8F77", RIDGE="#A09280", RIB="#7E7062",
    NOSE="#1C1817", GUM="#A8404A", GUM_DK="#5E1F28", TONGUE="#C25664", TONGUE_DK="#8E3546", TEETH="#E6DCC4",
    EYE="#B3202A", EYE_HOT="#FF6A48", EAR_IN="#6A4644", COLLAR="#5B1F1F", COLLAR_HI="#7A2E28",
    BRASS="#B08A3A", BRASS_HI="#E0BC66", BRASS_SH="#6E5420", SLOT="#22180C", TAG="#D4B060", TAG_TXT="#3A2A12",
    CLAW="#B9AD95").items()}

FONT = {  # 5x7
    "C": ["01111", "10000", "10000", "10000", "10000", "10000", "01111"],
    "H": ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
    "U": ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
}
TEXT = "CHUCHO"
TEXT_BITS = np.zeros((7, len(TEXT) * 6 - 1))
for _i, _ch in enumerate(TEXT):
    for _r, _row in enumerate(FONT[_ch]):
        for _c, _b in enumerate(_row):
            TEXT_BITS[_r, _i * 6 + _c] = float(_b == "1")

MANGE_SPOTS = [((0.20, 0.42, 1.24), 0.085), ((-0.19, 0.55, 1.30), 0.07), ((0.23, -0.16, 1.02), 0.075),
               ((-0.21, 0.10, 1.14), 0.09), ((0.12, 0.80, 1.40), 0.06), ((-0.10, -0.70, 1.40), 0.06)]


def hash01(*a):
    x = math.sin(sum(v * k for v, k in zip(a, (12.9898, 78.233, 37.719, 4.1414)))) * 43758.5453
    return x - math.floor(x)


def shade(part, P, n, fi):
    """P: (N,3) world points of the texels, n: face normal, fi: face index -> (rgb (N,3), emission (N,))."""
    N = len(P)
    jit = 1.0 + (hash01(fi, 3.1) - 0.5) * 0.16
    rgb = np.zeros((N, 3))
    em = np.zeros(N)
    x, y, z = P[:, 0], P[:, 1], P[:, 2]

    def fill(c):
        rgb[:] = c
    if part in ("torso", "neck") or part.startswith(("leg_", "tuft", "tail")):
        if n.z > 0.45:
            base = C["FUR_DARK"]
        elif n.z < -0.45:
            base = C["FUR_SHADOW"]
        else:
            base = C["FUR_MID"] if hash01(fi, 9.7) > 0.3 else C["FUR_DARK"]
        if part.startswith(("tuft", "tail_t")) and hash01(fi, 5.5) > 0.5:
            base = C["FUR_LIGHT"]
        fill(base)
        if part.startswith("leg_"):
            rgb[z < 0.62] = C["LEG"]
        if part == "torso":
            yy = y / SY
            inrib = (yy < RIB_Y[0] + 0.02) & (yy > RIB_Y[1] - 0.02) & (z < 1.36) & (z > 0.86) & (abs(n.x) > 0.35)
            ph = np.cos(2 * math.pi * (yy - RIB_Y[0]) / 0.09)
            rgb[inrib & (ph > 0.35)] = C["RIB"]
            rgb[inrib & (ph < -0.55)] = C["FUR_SHADOW"]
        for c, r in MANGE_SPOTS:
            d = np.sqrt(((P - np.array(c) * np.array((1, SY, 1))) ** 2).sum(1))
            wob = r * (0.8 + 0.4 * np.sin(x * 61 + z * 47 + y * 13))
            rgb[(d < wob) & (d > wob - 0.018)] = C["MANGE_RIM"]
            rgb[d < wob - 0.018] = C["MANGE"]
        rgb *= jit
    elif part in ("spine_bumps", "neck_bumps"):
        fill(C["RIDGE"] if n.z > 0.2 else C["BONE_SH"]); rgb *= jit
    elif part == "head":
        stop_y = hs((0, -1.44, 1.7)).y
        fill(C["FUR_DARK"] if n.z > 0.3 else C["FUR_MID"])
        sn = y < stop_y + 0.03
        rgb[sn] = C["BONE"] if n.z > -0.3 else C["BONE_SH"]
        if n.z < -0.55:
            rgb[sn] = C["GUM"]
        for sx in (1, -1):      # dark mask around the eyes
            e = hs((sx * EYE[0], EYE[1], EYE[2]))
            d = np.sqrt(((P - np.array(e)) ** 2).sum(1))
            rgb[d < 0.085] = C["FUR_SHADOW"]
        rgb *= jit
    elif part == "jaw":
        fill(C["BONE"] if n.z > -0.4 else C["BONE_SH"])
        if n.z > 0.45:
            fill(C["GUM"])
        rgb *= jit
    elif part == "nose":
        fill(C["NOSE"])
    elif part == "brow":
        fill(C["FUR_SHADOW"] * 0.8)
    elif part == "eye":
        fill(C["EYE_HOT"] if n.y < -0.6 else C["EYE"])
        em[:] = 1.0
    elif part.startswith("teeth"):
        fill(C["TEETH"] * jit)
    elif part == "tongue":
        fill(C["TONGUE"])
        rgb[np.abs(x) < 0.008 * HEAD_SCALE] = C["TONGUE_DK"]
    elif part.startswith("ear_"):
        fill(C["EAR_IN"] if n.y < -0.35 else C["FUR_DARK"])
        rgb *= jit
    elif part.startswith("paw_"):
        fill(C["FUR_SHADOW"] * jit)
    elif part.startswith("claw_"):
        fill(C["CLAW"])
    elif part == "collar":
        fill(C["COLLAR_HI"] if n.z > 0.3 else C["COLLAR"])
    elif part == "bell":
        bc = bs(bell_anchor()) + V((0, 0, -0.14))
        fill(C["BRASS_HI"] if n.z > 0.45 else (C["BRASS"] if n.z > -0.3 else C["BRASS_SH"]))
        rel = z - bc.z
        rgb[(rel < -0.035) & (rel > -0.1) & (np.abs(x) < 0.012)] = C["SLOT"]
        rgb[np.abs(rel + 0.03) < 0.006] = C["BRASS_SH"]
        rgb[rel < -0.1] = C["BRASS_SH"] * 0.6
    elif part == "tag":
        fill(C["TAG"] if n.z > -0.5 else C["BRASS_SH"])
        tc, xa, za, fa = tag_frame()
        tcf = bs(tc); xa = (bs(tc + xa) - tcf).normalized(); za = V((0, 0, 1))
        fa_f = (bs(tc + fa) - tcf).normalized()
        if n.dot(fa_f) > 0.6:
            u = (P - np.array(tcf)) @ np.array(xa)
            w = (P - np.array(tcf)) @ np.array(za)
            W, H = 0.135, 0.027
            cols = np.floor((u + W / 2) / W * TEXT_BITS.shape[1]).astype(int)
            rows = np.floor((H / 2 - w) / H * 7).astype(int)
            ok = (cols >= 0) & (cols < TEXT_BITS.shape[1]) & (rows >= 0) & (rows < 7)
            on = np.zeros(N, bool)
            on[ok] = TEXT_BITS[rows[ok], cols[ok]] > 0.5
            rgb[on] = C["TAG_TXT"]
    else:
        fill(C["FUR_MID"])
    return np.clip(rgb, 0, 1), em


def paint_texture(save=True):
    ob = get_ob()
    me = ob.data
    names = parts_of(ob)
    S = TEX_SIZE
    img = np.zeros((S, S, 3)); emi = np.zeros((S, S)); cov = np.zeros((S, S), bool)
    pa = me.attributes["part"].data
    uvd = me.uv_layers.active.data
    verts = me.vertices
    me.calc_loop_triangles()
    for t in me.loop_triangles:
        part = names[pa[t.polygon_index].value]
        poly = me.polygons[t.polygon_index]
        uv = np.array([uvd[l].uv[:] for l in t.loops]) * S - 0.5
        P3 = np.array([verts[v].co[:] for v in t.vertices])
        lo = np.clip(np.floor(uv.min(0)).astype(int) - 1, 0, S - 1)
        hi = np.clip(np.ceil(uv.max(0)).astype(int) + 1, 0, S - 1)
        xs, ys = np.meshgrid(np.arange(lo[0], hi[0] + 1), np.arange(lo[1], hi[1] + 1))
        px = np.stack([xs.ravel(), ys.ravel()], 1).astype(float)
        a, b, c = uv
        den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(den) < 1e-12:
            continue
        l1 = ((b[1] - c[1]) * (px[:, 0] - c[0]) + (c[0] - b[0]) * (px[:, 1] - c[1])) / den
        l2 = ((c[1] - a[1]) * (px[:, 0] - c[0]) + (a[0] - c[0]) * (px[:, 1] - c[1])) / den
        l3 = 1 - l1 - l2
        e = -0.02
        inside = (l1 >= e) & (l2 >= e) & (l3 >= e)
        if not inside.any():
            continue
        L = np.stack([l1, l2, l3], 1)[inside]
        rgb, em = shade(part, L @ P3, poly.normal.copy(), t.polygon_index)
        ix = px[inside].astype(int)
        img[ix[:, 1], ix[:, 0]] = rgb
        emi[ix[:, 1], ix[:, 0]] = em
        cov[ix[:, 1], ix[:, 0]] = True
    # dilate into the gutters so Point filtering never samples background at island edges
    for _ in range(6):
        nc = cov.copy()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sh_c = np.roll(np.roll(cov, dy, 0), dx, 1)
            sh_i = np.roll(np.roll(img, dy, 0), dx, 1)
            sh_e = np.roll(np.roll(emi, dy, 0), dx, 1)
            m = sh_c & ~nc
            img[m] = sh_i[m]; emi[m] = sh_e[m]; nc |= m
        cov = nc
    img[~cov] = C["FUR_SHADOW"]
    img = np.round(img * 31) / 31      # 32 levels per channel: crisp palette under Point filtering
    out = {}
    for nm, arr in (("T_Chucho_D", img), ("T_Chucho_E", np.repeat(emi[:, :, None], 3, 2))):
        im = bpy.data.images.get(nm) or bpy.data.images.new(nm, S, S, alpha=False)
        if nm == "T_Chucho_E":
            im.colorspace_settings.name = 'Non-Color'
        lin = arr      # byte images take display (sRGB) values directly
        rgba = np.concatenate([lin, np.ones((S, S, 1))], 2).astype(np.float32)
        im.pixels.foreach_set(rgba.ravel())
        im.filepath_raw = os.path.join(HERE, nm + ".png")
        im.file_format = 'PNG'
        if save:
            im.save()
        out[nm] = im
    setup_material(get_ob(), out["T_Chucho_D"], out["T_Chucho_E"])
    print("painted, coverage %d%%" % (100 * cov.mean()))


def setup_material(ob, dif, emi):
    mat = bpy.data.materials.get("M_Chucho") or bpy.data.materials.new("M_Chucho")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    td = nt.nodes.new("ShaderNodeTexImage"); td.image = dif; td.interpolation = 'Closest'
    te = nt.nodes.new("ShaderNodeTexImage"); te.image = emi; te.interpolation = 'Closest'
    mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
    mix.inputs[0].default_value = 1.0
    nt.links.new(td.outputs[0], bsdf.inputs["Base Color"])
    nt.links.new(te.outputs[0], mix.inputs[6]); nt.links.new(td.outputs[0], mix.inputs[7])
    nt.links.new(mix.outputs[2], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 4.0
    bsdf.inputs["Roughness"].default_value = 0.9
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    ob.data.materials.clear()
    ob.data.materials.append(mat)


# ------------------------------------------------------------------ rig
def bone_table():
    """name -> (head, tail, parent, deform). Final (post-squash) coordinates."""
    b, h = bs, hs
    T = {}

    def add(n, hd, tl, par, deform=True):
        T[n] = (V(hd), V(tl), par, deform)
    add("root", (0, 0, 0), (0, 0, 0.35), None, False)
    add("Hips", b(J["hips"]), b(J["spine1"]), "root")
    add("Spine1", b(J["spine1"]), b(J["spine2"]), "Hips")
    add("Spine2", b(J["spine2"]), b(J["spine3"]), "Spine1")
    add("Spine3", b(J["spine3"]), b(J["chest"]), "Spine2")
    add("Chest", b(J["chest"]), b(J["neck1"]), "Spine3")
    add("Neck1", b(J["neck1"]), b(J["neck2"]), "Chest")
    add("Neck2", b(J["neck2"]), h(J["head"]), "Neck1")
    add("Head", h(J["head"]), h(J["head_end"]), "Neck2")
    add("Jaw", h(J["jaw"]), h(J["jaw_end"]), "Head")
    add("Tongue1", h(J["tongue1"]), h(J["tongue2"]), "Jaw")
    add("Tongue2", h(J["tongue2"]), h(J["tongue_end"]), "Tongue1")
    add("MouthSocket", h((0, -1.52, 1.585)), h((0, -1.60, 1.585)), "Jaw", False)
    for s, sx in (("L", 1), ("R", -1)):
        add("Eye" + s, h((sx * EYE[0], EYE[1], EYE[2])), h((sx * EYE[0], EYE[1] - 0.06, EYE[2])), "Head", False)
    for s, pts in (("L", EAR_L), ("R", EAR_R)):
        add("Ear%s1" % s, h(pts[0]), h(pts[1]), "Head")
        add("Ear%s2" % s, h(pts[1]), h(pts[2]), "Ear%s1" % s)
    tails = ["tail1", "tail2", "tail3", "tail4", "tail_end"]
    for i in range(4):
        add("Tail%d" % (i + 1), b(J[tails[i]]), b(J[tails[i + 1]]), "Hips" if i == 0 else "Tail%d" % i)
    for s, sx in (("L", 1), ("R", -1)):
        for kind, chain, X, par in (("F", FL, FRONT_X, "Chest"), ("H", HL, HIND_X, "Hips")):
            names = ["Leg%s%s_%s" % (kind, s, n) for n in ("Upper", "Lower", "Foot", "Toe")]
            for i, nm in enumerate(names):
                p0 = b((sx * X, chain[i][0], chain[i][1]))
                p1 = b((sx * X, chain[i + 1][0], chain[i + 1][1]))
                add(nm, p0, p1, par if i == 0 else names[i - 1])
    anc = bs(bell_anchor())
    add("Collar", bs(COLLAR_C), anc, "Neck2")
    add("Bell", anc, anc + V((0, 0, -0.25)), "Collar")
    return T


def build_rig():
    for o in list(bpy.data.objects):
        if o.name.startswith(RIG_NAME):
            bpy.data.objects.remove(o, do_unlink=True)
    arm = bpy.data.armatures.new(RIG_NAME)
    arm.display_type = 'STICK'
    rig = bpy.data.objects.new(RIG_NAME, arm)
    bpy.context.scene.collection.objects.link(rig)
    rig.show_in_front = True
    for o in bpy.context.selected_objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    T = bone_table()
    for n, (hd, tl, par, deform) in T.items():
        eb = arm.edit_bones.new(n)
        eb.head, eb.tail = hd, tl
        eb.use_deform = deform
        # roll convention: legs / ears / collar / bell: local Z = forward (-Y); tail: local Z = dorsal;
        # spine, neck, head, jaw, tongue, sockets: local Z = up.  => +X rotation lifts a bone's tip
        # (spine/neck/head/jaw/tail) or swings it forward (legs, bell); local Z rotation = yaw / sideways swing.
        if n.startswith(("Leg", "Ear", "Collar", "Bell")) or n == "root":
            eb.align_roll(V((0, -1, 0)))
        elif n.startswith("Tail"):
            eb.align_roll(V((0, 0.6, 0.8)))
        else:
            eb.align_roll(V((0, 0, 1)))
    for n, (hd, tl, par, deform) in T.items():
        if par:
            eb = arm.edit_bones[n]
            eb.parent = arm.edit_bones[par]
            eb.use_connect = (eb.head - eb.parent.tail).length < 1e-4
    bpy.ops.object.mode_set(mode='OBJECT')
    print("rig: %d bones (%d deform)" % (len(T), sum(1 for v in T.values() if v[3])))
    return rig


def skin():
    ob, rig = get_ob(), bpy.data.objects[RIG_NAME]
    me = ob.data
    names = parts_of(ob)
    T = bone_table()
    PB = part_bones()
    ob.vertex_groups.clear()
    vgs = {n: ob.vertex_groups.new(name=n) for n, v in T.items() if v[3]}
    pa = me.attributes["part"].data
    vpart = {}
    for p in me.polygons:
        for vi in p.vertices:
            vpart[vi] = names[pa[p.index].value]
    for v in me.vertices:
        bones = PB[vpart[v.index]]
        P = v.co
        ws = []
        for bn in bones:
            hd, tl = T[bn][0], T[bn][1]
            ax = tl - hd
            L = ax.length
            t = (P - hd).dot(ax) / (L * L)
            out = max(0.0, -t, t - 1.0) * L
            perp = (P - (hd + ax * min(1, max(0, t)))).length
            d = math.sqrt(out * out + (0.3 * perp) ** 2)
            ws.append((1.0 / (d + 0.03) ** 3, bn))
        ws.sort(reverse=True)
        ws = ws[:3]
        tot = sum(w for w, _ in ws)
        ws = [(w / tot, bn) for w, bn in ws if w / tot > 0.04]
        tot = sum(w for w, _ in ws)
        for w, bn in ws:
            vgs[bn].add([v.index], w / tot, 'REPLACE')
    for m in list(ob.modifiers):
        ob.modifiers.remove(m)
    mod = ob.modifiers.new("Armature", 'ARMATURE')
    mod.object = rig
    ob.parent = rig
    ob.matrix_parent_inverse.identity()
    print("skinned %d verts" % len(me.vertices))


def part_bones():
    """Allowed deform bones per part (rebuilt from the part names so it also works without build_mesh)."""
    pb = {}
    for nm in parts_of(get_ob()):
        if nm in ("torso",):
            pb[nm] = ["Hips", "Spine1", "Spine2", "Spine3", "Chest", "Neck1"]
        elif nm == "spine_bumps":
            pb[nm] = ["Hips", "Spine1", "Spine2", "Spine3", "Chest"]
        elif nm in ("neck_bumps", "tuft_neck"):
            pb[nm] = ["Neck1", "Neck2"]
        elif nm == "neck":
            pb[nm] = ["Chest", "Neck1", "Neck2", "Head"]
        elif nm in ("head", "nose", "brow", "eye", "teeth_up") or nm.startswith("tuft_cheek"):
            pb[nm] = ["Head"]
        elif nm in ("jaw", "teeth_low"):
            pb[nm] = ["Jaw"]
        elif nm == "tongue":
            pb[nm] = ["Tongue1", "Tongue2"]
        elif nm.startswith("ear_"):
            s = nm[-1]
            pb[nm] = ["Head", "Ear%s1" % s, "Ear%s2" % s]
        elif nm.startswith("leg_"):
            k, s = nm[4], nm[5]
            pb[nm] = ["Chest" if k == "F" else "Hips"] + ["Leg%s%s_%s" % (k, s, n) for n in ("Upper", "Lower", "Foot")]
        elif nm.startswith("paw_"):
            k, s = nm[4], nm[5]
            pb[nm] = ["Leg%s%s_Foot" % (k, s), "Leg%s%s_Toe" % (k, s)]
        elif nm.startswith("claw_"):
            pb[nm] = ["Leg%s%s_Toe" % (nm[5], nm[6])]
        elif nm == "tail":
            pb[nm] = ["Hips", "Tail1", "Tail2", "Tail3", "Tail4"]
        elif nm == "tail_tufts":
            pb[nm] = ["Tail2", "Tail3", "Tail4"]
        elif nm == "tail_tip":
            pb[nm] = ["Tail4"]
        elif nm == "tuft_chest":
            pb[nm] = ["Chest", "Spine3"]
        elif nm == "tuft_belly":
            pb[nm] = ["Spine2", "Spine3"]
        elif nm.startswith("tuft_shoulder"):
            pb[nm] = ["Chest", "LegF%s_Upper" % nm[-1]]
        elif nm.startswith("tuft_thigh"):
            pb[nm] = ["Hips", "LegH%s_Upper" % nm[-1]]
        elif nm == "collar":
            pb[nm] = ["Collar"]
        elif nm in ("bell", "tag"):
            pb[nm] = ["Bell"]
        else:
            raise KeyError(nm)
    return pb


# ------------------------------------------------------------------ test poses
POSES = {
    "walk": {"LegFL_Upper": (-28, 0, 0), "LegFL_Lower": (10, 0, 0), "LegFR_Upper": (25, 0, 0), "LegFR_Lower": (-35, 0, 0),
             "LegFR_Foot": (-30, 0, 0), "LegHL_Upper": (25, 0, 0), "LegHL_Lower": (-20, 0, 0), "LegHR_Upper": (-25, 0, 0),
             "LegHR_Lower": (30, 0, 0), "LegHR_Foot": (25, 0, 0), "Tail1": (0, 0, 20), "Tail2": (0, 0, 20),
             "Bell": (0, 0, 25), "Spine2": (0, 0, 4), "Chest": (0, 0, -4)},
    "sniff": {"Neck1": (-24, 0, 0), "Neck2": (-18, 0, 0), "Head": (-12, 0, 0), "Chest": (-5, 0, 0),
              "LegFL_Upper": (6, 0, 0), "LegFR_Upper": (6, 0, 0), "EarR2": (-25, 0, 0), "Bell": (40, 0, 0),
              "Tail1": (25, 0, 0), "Tail2": (20, 0, 0)},
    "jaw_open": {"Jaw": (-30, 0, 0), "Tongue1": (8, 0, 0), "Tongue2": (20, 0, 0), "EarL2": (0, 0, 20),
                 "EarR2": (25, 0, 0)},
    "bell_swing": {"Bell": (25, 0, 35), "Head": (0, 0, -12), "Neck2": (0, 0, -8)},
    "tail_wag": {"Tail1": (0, 0, 35), "Tail2": (0, 0, 30), "Tail3": (0, 0, 25), "Tail4": (0, 0, 25),
                 "Hips": (0, 0, 6), "Bell": (0, 0, -35), "Head": (0, 0, 15)},
}


def set_pose(name=None):
    rig = bpy.data.objects[RIG_NAME]
    for pb in rig.pose.bones:
        pb.rotation_mode = 'XYZ'
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
    if name:
        for bn, e in POSES[name].items():
            rig.pose.bones[bn].rotation_euler = [math.radians(a) for a in e]
    bpy.context.view_layer.update()


# ------------------------------------------------------------------ preview renders
def setup_stage():
    sc = bpy.context.scene
    for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
        try:
            sc.render.engine = eng
            break
        except TypeError:
            pass
    sc.render.resolution_x, sc.render.resolution_y = 1000, 750
    sc.render.film_transparent = False
    w = sc.world or bpy.data.worlds.new("World")
    sc.world = w
    w.use_nodes = True
    bg = next(n for n in w.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs[0].default_value = (0.16, 0.16, 0.17, 1)
    bg.inputs[1].default_value = 0.6
    cam = bpy.data.objects.get("ChuchoCam")
    if not cam:
        cam = bpy.data.objects.new("ChuchoCam", bpy.data.cameras.new("ChuchoCam"))
        sc.collection.objects.link(cam)
    cam.data.lens = 50
    sc.camera = cam
    sun = bpy.data.objects.get("ChuchoSun")
    if not sun:
        sun = bpy.data.objects.new("ChuchoSun", bpy.data.lights.new("ChuchoSun", 'SUN'))
        sc.collection.objects.link(sun)
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    ground = bpy.data.objects.get("ChuchoGround")
    if not ground:
        gm = bpy.data.meshes.new("ChuchoGround")
        gm.from_pydata([(-6, -6, 0), (6, -6, 0), (6, 6, 0), (-6, 6, 0)], [], [(0, 1, 2, 3)])
        ground = bpy.data.objects.new("ChuchoGround", gm)
        sc.collection.objects.link(ground)
        mat = bpy.data.materials.new("ChuchoGroundMat"); mat.use_nodes = True
        next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs[0].default_value = (0.32, 0.3, 0.28, 1)
        gm.materials.append(mat)
    return cam


def render(path, direction, target=(0, -0.2, 1.05), dist=6.0, lens=50):
    cam = setup_stage()
    cam.data.lens = lens
    t = V(target)
    d = V(direction).normalized()
    cam.location = t + d * dist
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


# ------------------------------------------------------------------ export
def export(path=None):
    ob, rig = get_ob(), bpy.data.objects[RIG_NAME]
    set_pose(None)
    path = path or os.path.join(HERE, "Chucho.fbx")
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True); rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE',
                             use_armature_deform_only=False, primary_bone_axis='Y', secondary_bone_axis='X',
                             armature_nodetype='NULL', path_mode='STRIP', embed_textures=False,
                             use_mesh_modifiers=False)
    print("exported", path)


def tri_count():
    return sum(len(p.vertices) - 2 for p in get_ob().data.polygons)


def main():
    build_mesh()
    build_uv()
    paint_texture()
    build_rig()
    skin()
    print("tris:", tri_count())
