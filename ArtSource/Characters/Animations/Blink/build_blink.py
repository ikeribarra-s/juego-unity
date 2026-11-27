# Skull blink for the Fluffterror player skeleton.
#
# Builds `Skeleton_Lids`: one small mesh with two closed eyelid shells (bone coloured, same UV
# layout / material as the body) and two shape keys:
#   Blink_L  character's LEFT lid  (+X in Blender), 0 = open (tucked inside the brow), 1 = closed
#   Blink_R  character's RIGHT lid (-X in Blender)
# plus two shape-key actions (`Blink`, `BlinkDouble`) and exports the FBXs next to this file.
#
# Run in Blender 4.5 in a scene that holds ArtSource/Characters/Final/skeleton_nojaw.fbx
# (armature + mesh 'Skeleton_LowPoly*', feet at Z=0, faces -Y):
#   g = {'__file__': r'...\build_blink.py'}; exec(open(g['__file__']).read(), g); g['main']()
import bpy, bmesh, math, os
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

HERE = os.path.dirname(__file__)
HINGE = Vector((0.0, 0.030, 1.645))  # jaw hinge = file origin of Skeleton_Jaw.fbx; the lids share it

# Socket opening, measured on the bind-pose mesh (Blender units = m, model faces -Y).
SOCKET_X = 0.080      # |x| of the socket centre (character's left eye at +X)
SOCKET_Z = 1.785      # socket centre height (opening ~0.090 wide x 0.072 tall)
LID_A = 0.054         # lid half width  (overlaps the rim by ~1 cm)
LID_B = 0.047         # lid half height
GRID = 6              # quads across the lid (6x6 front, 6x6 back, 24 border quads per lid)
EDGE_SINK = 0.004     # lid border sits this far inside the bone, so it emerges from the rim
DOME = 0.016          # dome height at the lid centre above the rim membrane (eyeball bulge)
THICK = 0.004         # shell thickness (closed mesh -> the ToonOutline inverted hull works)
TUCK_Z = SOCKET_Z + LID_B + 0.006   # open lid is rolled up to this height ...
TUCK_DEPTH = 0.012    # ... and pushed this far behind the brow surface (hidden)
UV_Z = 1.885          # lid UVs = forehead patch centred at this height above each socket ...
LASH_LINE = True      # bottom vertex row of each lid uses the dark socket texel (soft dark lower edge)
UV_SQUEEZE = 0.35     # ... squeezed to 35 % so the patch stays inside one UV island (no seam on the lid)

FPS = 30
# (frame, weight 0..1). Fast close, short hold, slightly slower open: reads as a cartoon blink.
CLIPS = {
    'Blink':       [(0, 0.0), (1, 0.45), (2, 1.0), (4, 1.0), (5, 0.7), (6, 0.25), (7, 0.0)],
    'BlinkDouble': [(0, 0.0), (1, 0.5), (2, 1.0), (3, 1.0), (4, 0.55), (5, 0.35),
                    (6, 0.7), (7, 1.0), (8, 1.0), (9, 0.7), (10, 0.35), (11, 0.1), (12, 0.0)],
}


def skull():
    return next(o for o in bpy.context.scene.objects
                if o.type == 'MESH' and o.name.startswith('Skeleton_LowPoly'))


def to_bind_pose():
    for o in bpy.context.scene.objects:
        if o.type == 'ARMATURE':
            o.animation_data_clear()          # Mixamo FBX brings a 1-frame take that shifts the hips 4.6 cm
            for pb in o.pose.bones:
                pb.matrix_basis.identity()
    bpy.context.view_layer.update()


class Surface:
    """Ray casts from the front (-Y) against the bind-pose skull mesh."""

    def __init__(self, obj):
        self.mw = obj.matrix_world.copy()
        self.mwi = self.mw.inverted()
        self.bm = bmesh.new()
        self.bm.from_mesh(obj.data)
        bmesh.ops.triangulate(self.bm, faces=self.bm.faces[:])
        self.bm.faces.ensure_lookup_table()
        self.uv = self.bm.loops.layers.uv.active
        self.bvh = BVHTree.FromBMesh(self.bm)
        self.dir = (self.mwi.to_3x3() @ Vector((0, 1, 0))).normalized()

    def hit(self, x, z):
        loc, _, idx, _ = self.bvh.ray_cast(self.mwi @ Vector((x, -1.0, z)), self.dir)
        return None if loc is None else (self.mw @ loc, idx, loc)

    def depth(self, x, z):
        h = self.hit(x, z)
        if h is None:
            raise RuntimeError('no skull surface at x=%.3f z=%.3f' % (x, z))
        return h[0].y

    def uv_at(self, x, z):
        _, idx, local = self.hit(x, z)
        f = self.bm.faces[idx]
        a, b, c = (l.vert.co for l in f.loops)
        u = [Vector((*l[self.uv].uv, 0.0)) for l in f.loops]
        p = barycentric_transform(local, a, b, c, u[0], u[1], u[2])
        return (p.x, p.y)


def lid_grid(S, side):
    """side = +1 (character's left, +X) or -1. Returns closed/open front positions + uvs, (GRID+1)^2."""
    n = GRID
    cx = side * SOCKET_X
    xs, zs, rho, border = {}, {}, {}, set()
    for i in range(n + 1):
        for j in range(n + 1):
            u = -1 + 2 * i / n
            v = -1 + 2 * j / n
            du = u * math.sqrt(1 - v * v / 2)           # square -> disk (keeps the grid even)
            dv = v * math.sqrt(1 - u * u / 2)
            xs[i, j] = cx + side * LID_A * du            # mirror so both lids have the same winding
            zs[i, j] = SOCKET_Z + LID_B * dv
            rho[i, j] = min(1.0, math.hypot(du, dv))
            if i in (0, n) or j in (0, n):
                border.add((i, j))
    # membrane: border = real bone surface (sunk), interior = harmonic fill
    y = {}
    for k in border:
        y[k] = S.depth(xs[k], zs[k]) + EDGE_SINK
    avg = sum(y.values()) / len(y)
    for k in xs:
        if k not in border:
            y[k] = avg
    for _ in range(400):
        for i in range(1, n):
            for j in range(1, n):
                y[i, j] = 0.25 * (y[i - 1, j] + y[i + 1, j] + y[i, j - 1] + y[i, j + 1])
    closed, opened, uvs = {}, {}, {}
    dark = S.uv_at(-SOCKET_X, SOCKET_Z)                    # socket floor = the texture's dark socket mask
    tuck_y = {}
    for k in xs:
        dome = (DOME + EDGE_SINK) * (1 - rho[k] ** 2) ** 0.6
        closed[k] = Vector((xs[k], y[k] - dome, zs[k]))
        i, j = k
        if i not in tuck_y:
            tuck_y[i] = S.depth(xs[k], TUCK_Z) + TUCK_DEPTH
        # open = the whole lid rolled up into a thin strip inside the brow
        opened[k] = Vector((xs[k], tuck_y[i] - 0.002 * (1 - rho[k] ** 2), TUCK_Z + 0.002 * (zs[k] - SOCKET_Z) / LID_B))
        # compressed forehead footprint (one UV island, plain bone colour with a soft gradient)
        # both lids use the -X forehead patch (mirrored): the +X forehead has a UV seam there
        uvs[k] = S.uv_at(-SOCKET_X - side * UV_SQUEEZE * (xs[k] - cx), UV_Z + UV_SQUEEZE * (zs[k] - SOCKET_Z))
        if LASH_LINE and j == 0:
            uvs[k] = dark                                  # dark lower edge = cartoon lash line
    return closed, opened, uvs, border


def build_lids():
    to_bind_pose()
    S = Surface(skull())
    for o in [o for o in bpy.data.objects if o.name.startswith('Skeleton_Lids')]:
        bpy.data.objects.remove(o)
    for m in [m for m in bpy.data.meshes if m.name.startswith('Skeleton_Lids') and m.users == 0]:
        bpy.data.meshes.remove(m)                  # keep the exported mesh name exactly 'Skeleton_Lids'
    me = bpy.data.meshes.new('Skeleton_Lids')
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('UVMap')
    n = GRID
    per_side = []                       # (side, verts closed-ordered list, open positions)
    sk_data = []
    for side in (+1, -1):
        closed, opened, uvs, border = lid_grid(S, side)
        V = {}
        positions_open = {}
        for layer, off in (('f', 0.0), ('b', THICK)):
            for k in closed:
                v = bm.verts.new(closed[k] - HINGE + Vector((0, off, 0)))
                V[layer, k] = v
                positions_open[v] = opened[k] - HINGE + Vector((0, off * 0.5, 0))
        bm.verts.ensure_lookup_table()

        def face(keys, layer, flip):
            vs = [V[layer, k] for k in keys]
            if flip:
                vs = vs[::-1]; keys = keys[::-1]
            f = bm.faces.new(vs)
            for l, k in zip(f.loops, keys):
                l[uvl].uv = uvs[k]
            return f

        for i in range(n):
            for j in range(n):
                q = [(i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)]
                face(q, 'f', False)
                face(q, 'b', True)
        ring = [(i, 0) for i in range(n)] + [(n, j) for j in range(n)] + \
               [(i, n) for i in range(n, 0, -1)] + [(0, j) for j in range(n, 0, -1)]
        for a, b in zip(ring, ring[1:] + ring[:1]):
            vs = [V['f', a], V['f', b], V['b', b], V['b', a]]
            f = bm.faces.new(vs)
            for l, k in zip(f.loops, (a, b, b, a)):
                l[uvl].uv = uvs[k]
        sk_data.append((side, [V[key] for key in V], positions_open))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])   # closed shells -> outward normals
    bm.normal_update()
    # front faces must face -Y (towards the viewer)
    front_ok = sum(1 for f in bm.faces if f.normal.y < -0.5)
    bm.verts.index_update()
    open_by_index = {}
    side_of_index = {}
    for side, verts, popen in sk_data:
        for v in verts:
            open_by_index[v.index] = popen[v].copy()
            side_of_index[v.index] = side
    closed_by_index = {v.index: v.co.copy() for v in bm.verts}
    # sharp border edges (shell rim), smooth lid faces
    for e in bm.edges:
        e.smooth = not (len(e.link_faces) == 2 and e.link_faces[0].normal.dot(e.link_faces[1].normal) < 0.3)
    for f in bm.faces:
        f.smooth = True
    bm.to_mesh(me)
    bm.free()
    if front_ok < GRID * GRID:
        print('WARNING: lid winding looks inverted (front-facing faces: %d)' % front_ok)

    ob = bpy.data.objects.new('Skeleton_Lids', me)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = HINGE
    mat = bpy.data.materials.get('M_Skeleton') or skull().active_material
    me.materials.append(mat)

    # Basis = OPEN (lids rolled up inside the brow, invisible), keys move each lid to CLOSED.
    for v in me.vertices:
        v.co = open_by_index[v.index]
    ob.shape_key_add(name='Basis', from_mix=False)
    for name, side in (('Blink_L', +1), ('Blink_R', -1)):
        k = ob.shape_key_add(name=name, from_mix=False)
        k.slider_min, k.slider_max = 0.0, 1.0
        for i, p in enumerate(k.data):
            if side_of_index[i] == side:
                p.co = closed_by_index[i]
    me.shape_keys.name = 'Skeleton_Lids_Keys'
    return ob


def make_actions(ob):
    key = ob.data.shape_keys
    acts = {}
    for name, keys in CLIPS.items():
        a = bpy.data.actions.get(name)
        if a:
            bpy.data.actions.remove(a)
        a = bpy.data.actions.new(name)
        a.use_fake_user = True
        if not key.animation_data:
            key.animation_data_create()
        key.animation_data.action = a
        for sk in ('Blink_L', 'Blink_R'):
            block = key.key_blocks[sk]
            for f, w in keys:
                block.value = w
                block.keyframe_insert('value', frame=f)
        for fc in a.fcurves if hasattr(a, 'fcurves') else []:
            for kp in fc.keyframe_points:
                kp.interpolation = 'LINEAR'   # stepped at 24 fps in game anyway; linear keeps 0/1 exact
        acts[name] = a
    key.animation_data.action = acts['Blink']
    return acts


def set_action(ob, action):
    key = ob.data.shape_keys
    key.animation_data.action = action
    if hasattr(key.animation_data, 'action_slot') and action.slots:
        key.animation_data.action_slot = action.slots[0]
    sc = bpy.context.scene
    sc.frame_start, sc.frame_end = 0, int(max(f for f, _ in CLIPS[action.name]))
    sc.render.fps = FPS


def export(ob, acts):
    sc = bpy.context.scene
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    loc = ob.location.copy()
    ob.location = (0, 0, 0)                       # pivot (= jaw hinge) at the file origin
    common = dict(use_selection=True, object_types={'MESH'}, apply_scale_options='FBX_SCALE_ALL',
                  axis_forward='-Z', axis_up='Y', add_leaf_bones=False, embed_textures=False,
                  path_mode='STRIP', use_mesh_modifiers=True, mesh_smooth_type='FACE',
                  bake_space_transform=False)
    out = []
    # mesh only, lids open
    for kb in ob.data.shape_keys.key_blocks:
        kb.value = 0.0
    act = ob.data.shape_keys.animation_data.action
    ob.data.shape_keys.animation_data.action = None
    p = os.path.join(HERE, 'Skeleton_Lids.fbx')
    bpy.ops.export_scene.fbx(filepath=p, bake_anim=False, **common)
    out.append(p)
    scene_name = sc.name
    for name, a in acts.items():
        set_action(ob, a)
        sc.name = name                            # the FBX take (= Unity clip) is named after the scene
        p = os.path.join(HERE, 'Skeleton_Lids@%s.fbx' % name)
        bpy.ops.export_scene.fbx(filepath=p, bake_anim=True, bake_anim_use_all_bones=False,
                                 bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
                                 bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                                 bake_anim_simplify_factor=0.0, **common)
        out.append(p)
    sc.name = scene_name
    set_action(ob, acts['Blink'])
    ob.location = loc
    return out


def main(do_export=True):
    ob = build_lids()
    acts = make_actions(ob)
    set_action(ob, acts['Blink'])
    paths = export(ob, acts) if do_export else []
    print('Skeleton_Lids: %d verts, %d tris' % (len(ob.data.vertices), sum(len(p.vertices) - 2 for p in ob.data.polygons)))
    for p in paths:
        print('exported', p)
    return ob
