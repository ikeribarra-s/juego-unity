# Derives the low-poly head / torso / pelvis + separate jaw of Skeleton_LowPoly from the Meshy source.
# Run inside Blender with an object "REF_Full" = Meshy model.obj imported and moved +0.998 m in Z (feet at 0),
# transforms applied. Produces objects CORE_low (skull+torso+pelvis, no lower jaw) and JAW_low.
import bpy, bmesh, math
from mathutils import Vector

HEAD_Z, TORSO_Z = 1.47, 1.04
TARGET_CORE = 3100          # head + torso + pelvis tris
TARGET_JAW = 320
HEAD_W, PELVIS_W = 0.65, 0.80   # decimate weights (lower = keeps more detail)
MOUTH_Z, TMJ = 1.600, Vector((0.0, 0.02, 1.645))

def link(name, me):
    o = bpy.data.objects.get(name)
    if o:
        m = o.data; bpy.data.objects.remove(o); bpy.data.meshes.remove(m)
    o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o); return o

def apply_mod(o, m):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=m.name)

def keep(c):
    x, y, z = abs(c.x), c.y, c.z
    if 1.33 < z < 1.50 and x > 0.19: return False      # arms
    if x > 0.26: return False
    if z < 0.90 and x > 0.165: return False            # femur tops / trochanters (kept leg tubes)
    if z > 0.84: return True                            # head, torso, pelvis
    if z > 0.72 and x < 0.095: return True              # pubis / ischium centre (femoral heads excluded)
    return False

def islands_min(bm, n):
    seen = set(); kill = []
    for v0 in bm.verts:
        if v0 in seen: continue
        isl = [v0]; seen.add(v0); st = [v0]
        while st:
            v = st.pop()
            for e in v.link_edges:
                w = e.other_vert(v)
                if w not in seen: seen.add(w); isl.append(w); st.append(w)
        if len(isl) < n: kill += isl
    bmesh.ops.delete(bm, geom=kill, context='VERTS')

def prism(name, profile_yz, x0, x1):
    bm = bmesh.new()
    a = [bm.verts.new((x0, y, z)) for y, z in profile_yz]
    b = [bm.verts.new((x1, y, z)) for y, z in profile_yz]
    n = len(a)
    bm.faces.new(a[::-1]); bm.faces.new(b)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    return link(name, me)

def boolean(o, cutter, op):
    m = o.modifiers.new('b', 'BOOLEAN'); m.operation = op; m.object = cutter; m.solver = 'EXACT'
    apply_mod(o, m)

def decimate(o, target, weights=None):
    tot = sum(len(p.vertices) - 2 for p in o.data.polygons)
    d = o.modifiers.new('dec', 'DECIMATE'); d.decimate_type = 'COLLAPSE'; d.use_symmetry = True; d.symmetry_axis = 'X'
    d.ratio = target / tot
    if weights:
        vg = o.vertex_groups.new(name='W')
        for v in o.data.vertices:
            vg.add([v.index], weights(v.co), 'REPLACE')
        d.vertex_group = 'W'
    apply_mod(o, d); o.vertex_groups.clear()
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0003)
    fix_manifold(bm)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(o.data); bm.free()
    for p in o.data.polygons: p.use_smooth = True

def fix_manifold(bm):
    # drop faces on non-manifold edges/verts, refill the holes, repeat
    for it in range(10):
        bad = [e for e in bm.edges if len(e.link_faces) != 2]
        badv = [v for v in bm.verts if not v.is_manifold]
        if not bad and not badv: return
        faces = set()
        for e in bad: faces.update(e.link_faces)
        for v in badv: faces.update(v.link_faces)
        if faces: bmesh.ops.delete(bm, geom=list(faces), context='FACES')
        loose = [v for v in bm.verts if not v.link_faces]
        if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
        wires = [e for e in bm.edges if not e.link_faces]
        if wires: bmesh.ops.delete(bm, geom=wires, context='EDGES')
        bnd = [e for e in bm.edges if e.is_boundary]
        if bnd:
            r = bmesh.ops.holes_fill(bm, edges=bnd, sides=0)
            bmesh.ops.triangulate(bm, faces=r['faces'])

def run():
    src = bpy.data.objects['REF_Full']
    # 1. region cut
    me = src.data.copy(); bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not keep(v.co)], context='VERTS')
    bm.to_mesh(me); bm.free()
    o = link('CORE_src', me)
    # 2. close the AI mesh (octree smooth), clean, mirror +X
    m = o.modifiers.new('rm', 'REMESH'); m.mode = 'SMOOTH'; m.octree_depth = 8; m.scale = 0.9; m.use_remove_disconnected = False
    apply_mod(o, m)
    bm = bmesh.new(); bm.from_mesh(o.data); islands_min(bm, 200)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, 0), plane_no=(1, 0, 0), clear_inner=True)
    for v in bm.verts:
        if abs(v.co.x) < 1e-5: v.co.x = 0
    bm.to_mesh(o.data); bm.free()
    mm = o.modifiers.new('mir', 'MIRROR'); mm.use_clip = True; mm.use_mirror_merge = True; mm.merge_threshold = 0.0005
    apply_mod(o, mm)
    # 3. carve eye + nose sockets into the dense mesh
    for v in o.data.vertices:
        c = v.co
        if c.y > -0.12 or not (1.62 < c.z < 1.88): continue
        w = 0
        for cx, cz, rx, rz, dp in [(0.074, 1.782, 0.056, 0.040, 0.045), (-0.074, 1.782, 0.056, 0.040, 0.045), (0, 1.705, 0.024, 0.032, 0.03)]:
            d = ((c.x - cx) / rx) ** 2 + ((c.z - cz) / rz) ** 2
            if d < 1: w = max(w, dp * math.sqrt(1 - d))
        c.y += w
    # 4. voxel remesh -> clean manifold, symmetrize
    m = o.modifiers.new('rm', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = 0.0035; m.adaptivity = 0
    apply_mod(o, m)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.symmetrize(direction='POSITIVE_X', threshold=0.0005); bpy.ops.object.mode_set(mode='OBJECT')
    bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0003); bm.to_mesh(o.data); bm.free()
    o.name = 'CORE_vox'
    # 5. jaw cutter: side profile (Y,Z) = below the mouth line, rising behind to the TMJ; minus the neck column
    prof = [(-0.30, 1.47), (0.050, 1.47), (0.050, 1.652), (0.012, 1.652), (-0.070, MOUTH_Z), (-0.30, MOUTH_Z)]
    cut = prism('JAW_cutter', prof, -0.17, 0.17)
    neck = prism('NECK_box', [(-0.06, 1.40), (0.30, 1.40), (0.30, 1.70), (-0.06, 1.70)], -0.05, 0.05)
    boolean(cut, neck, 'DIFFERENCE')
    jaw = o.copy(); jaw.data = o.data.copy(); jaw.name = 'JAW_hi'; bpy.context.scene.collection.objects.link(jaw)
    boolean(jaw, cut, 'INTERSECT')
    body = o.copy(); body.data = o.data.copy(); body.name = 'CORE_hi'; bpy.context.scene.collection.objects.link(body)
    boolean(body, cut, 'DIFFERENCE')
    for x in (cut, neck):
        mx = x.data; bpy.data.objects.remove(x); bpy.data.meshes.remove(mx)
    return finish(o, jaw, body)

def finish(o=None, jaw=None, body=None):
    jaw = jaw or bpy.data.objects['JAW_hi']; body = body or bpy.data.objects['CORE_hi']
    # 6. decimate
    jl = jaw.copy(); jl.data = jaw.data.copy(); jl.name = 'JAW_low'; bpy.context.scene.collection.objects.link(jl)
    decimate(jl, TARGET_JAW)
    cl = body.copy(); cl.data = body.data.copy(); cl.name = 'CORE_low'; bpy.context.scene.collection.objects.link(cl)
    decimate(cl, TARGET_CORE, lambda c: HEAD_W if c.z > HEAD_Z else (1.0 if c.z > TORSO_Z else PELVIS_W))
    for x in (o, jaw, body):
        if x: x.hide_set(True)
    return cl, jl

if __name__ == '__main__' or True:
    pass
