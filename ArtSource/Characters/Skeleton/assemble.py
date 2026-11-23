# Full v3 pipeline: derive_core.run() + build_skel.py kept parts -> join -> UV -> bakes -> split jaw.
# Run inside Blender with REF_Full present (Meshy model.obj, +0.998 m Z, transforms applied).
import bpy, bmesh, math, os
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

DIR = os.path.dirname(os.path.abspath(__file__)) if '__file__' in globals() else r'C:\Users\redux\Desktop\proyectos\juego-unity\juego\ArtSource\Characters\Skeleton'
HINGE = Vector((0.0, 0.03, 1.645))
MOUTH_Z = 1.600
PROF = [(-0.30, 1.47), (0.050, 1.47), (0.050, 1.652), (0.012, 1.652), (-0.070, MOUTH_Z), (-0.30, MOUTH_Z)]

def load(name, extra=None):
    g = {'__name__': 'lib'}
    if extra: g.update(extra)
    exec(open(os.path.join(DIR, name)).read(), g)
    return g

def on_cutter(c, n):
    segs = [(Vector(PROF[i]), Vector(PROF[(i + 1) % len(PROF)])) for i in range(len(PROF))]
    if abs(c.x) > 0.172: return False
    if abs(abs(c.x) - 0.05) < 0.004 and c.y > -0.062 and 1.47 < c.z < 1.655 and abs(n.x) > 0.7: return True
    if abs(c.y + 0.06) < 0.004 and abs(c.x) < 0.052 and 1.47 < c.z < 1.655 and abs(n.y) > 0.7: return True
    if abs(c.x) < 0.05 and c.y > -0.06: return False
    p = Vector((c.y, c.z)); nyz = Vector((n.y, n.z))
    for a, b in segs:
        ab = b - a; t = max(0, min(1, (p - a).dot(ab) / ab.length_squared)); d = (a + ab * t - p).length
        en = Vector((ab.y, -ab.x)).normalized()
        if d < 0.004 and nyz.length > 0.5 and abs(nyz.normalized().dot(en)) > 0.8: return True
    return False

def face_attr(o, name, fn, typ='INT'):
    me = o.data
    if name in me.attributes: me.attributes.remove(me.attributes[name])
    a = me.attributes.new(name, typ, 'FACE'); mw = o.matrix_world; nm = mw.to_3x3()
    for p in me.polygons:
        a.data[p.index].value = fn(mw @ p.center, (nm @ p.normal).normalized(), p)

def main():
    D = load('derive_core.py')
    for n in ('Skeleton_LowPoly', 'Skeleton_Jaw', 'Body_kept'):
        o = bpy.data.objects.get(n)
        if o: m = o.data; bpy.data.objects.remove(o); bpy.data.meshes.remove(m)
    core, jaw = D['run']()
    load('build_skel.py', {'BUILD_NAME': 'Body_kept'})
    body = bpy.data.objects['Body_kept']
    vox = bpy.data.objects['CORE_vox']                      # uncut solid: cut faces point INTO it
    bmv = bmesh.new(); bmv.from_mesh(vox.data); tree = BVHTree.FromBMesh(bmv); bmv.free()
    def is_cut(c, n):
        q = c + n * 0.004
        loc, nrm, i, d = tree.find_nearest(q)
        return loc is not None and (q - loc).dot(nrm) < 0
    for o, fr, j in ((body, 0, 0), (core, 1, 0), (jaw, 1, 1)):
        o.hide_set(False)
        face_attr(o, 'FromRef', lambda c, n, p, fr=fr: fr)
        face_attr(o, 'IsJaw', lambda c, n, p, j=j: j)
        face_attr(o, 'Mouth', lambda c, n, p, fr=fr: 1 if fr and 1.50 < c.z < 1.66 and abs(c.x) < 0.18 and is_cut(c, n) else 0)
    face_attr(core, 'Mouth2', lambda c, n, p: 1.0 if (1.575 < c.z < 1.665 and abs(c.x) < 0.095 and -0.19 < c.y < 0.02 and n.z < -0.5) else 0.0, 'FLOAT')
    face_attr(jaw, 'Mouth2', lambda c, n, p: 1.0 if ((n.z > 0.3 and c.z > 1.55) or (abs(c.x) < 0.1 and c.y > -0.17 and n.x * (1 if c.x > 0 else -1) < -0.4)) else 0.0, 'FLOAT')
    face_attr(body, 'Mouth2', lambda c, n, p: 0.0, 'FLOAT')
    bpy.ops.object.select_all(action='DESELECT')
    for o in (body, core, jaw): o.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join(); sk = body; sk.name = 'Skeleton_LowPoly'; sk.data.name = 'Skeleton_LowPoly'
    # clean + triangulate ngons
    bm = bmesh.new(); bm.from_mesh(sk.data); D['fix_manifold'](bm)
    ng = [f for f in bm.faces if len(f.verts) > 4]
    if ng: bmesh.ops.triangulate(bm, faces=ng)
    bm.to_mesh(sk.data); bm.free()
    for p in sk.data.polygons: p.use_smooth = True
    bake(sk)
    # split jaw, origin at hinge
    bpy.ops.object.select_all(action='DESELECT'); sk.select_set(True); bpy.context.view_layer.objects.active = sk
    bpy.ops.object.mode_set(mode='EDIT')
    bme = bmesh.from_edit_mesh(sk.data); lay = bme.faces.layers.int.get('IsJaw')
    for f in bme.faces: f.select_set(f[lay] == 1)
    bmesh.update_edit_mesh(sk.data)
    bpy.ops.mesh.separate(type='SELECTED'); bpy.ops.object.mode_set(mode='OBJECT')
    jw = [o for o in bpy.context.selected_objects if o != sk][0]
    jw.name = 'Skeleton_Jaw'; jw.data.name = 'Skeleton_Jaw'
    bpy.context.scene.cursor.location = HINGE
    bpy.ops.object.select_all(action='DESELECT'); jw.select_set(True); bpy.context.view_layer.objects.active = jw
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR'); bpy.context.scene.cursor.location = (0, 0, 0)
    for o in (sk, jw):
        for n in ('FromRef', 'IsJaw', 'Mouth', 'Mouth2', 'Sock'):
            if n in o.data.attributes: o.data.attributes.remove(o.data.attributes[n])
    return sk, jw

def bake(sk):
    me = sk.data
    bpy.ops.object.select_all(action='DESELECT'); sk.select_set(True); bpy.context.view_layer.objects.active = sk
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.003, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    sock = me.attributes.new('Sock', 'FLOAT', 'POINT')
    for v in me.vertices:
        c = v.co; w = 0
        if c.y < -0.10 and 1.62 < c.z < 1.88:
            for cx, cz, rx, rz in [(0.074, 1.782, 0.056, 0.040), (-0.074, 1.782, 0.056, 0.040), (0, 1.705, 0.024, 0.032)]:
                d = ((c.x - cx) / rx) ** 2 + ((c.z - cz) / rz) ** 2
                if d < 1: w = max(w, min(1, (1 - d) * 2.5))
        sock.data[v.index].value = w
    mat = bpy.data.materials.get('M_Skeleton') or bpy.data.materials.new('M_Skeleton'); mat.use_nodes = True
    me.materials.clear(); me.materials.append(mat); nt = mat.node_tree
    def reset():
        for n in list(nt.nodes): nt.nodes.remove(n)
        return nt.nodes.new('ShaderNodeOutputMaterial')
    def newimg(n):
        i = bpy.data.images.get(n)
        if i: bpy.data.images.remove(i)
        return bpy.data.images.new(n, 1024, 1024)
    def target(i):
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = i; nt.nodes.active = t
    sc = bpy.context.scene
    try: sc.render.engine = 'CYCLES'
    except TypeError as e: print(e)
    sc.cycles.samples = 16; bk = sc.render.bake; bk.use_clear = True; bk.use_selected_to_active = False
    def emit_attr(name, img, margin):
        out = reset(); at = nt.nodes.new('ShaderNodeAttribute'); at.attribute_name = name
        em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(at.outputs['Fac'], em.inputs['Color']); nt.links.new(em.outputs[0], out.inputs['Surface'])
        target(img); bk.margin = margin; bpy.ops.object.bake(type='EMIT')
    imgs = {k: newimg('BAKE_' + k) for k in 'ABMSDE'}
    out = reset()
    ao = nt.nodes.new('ShaderNodeAmbientOcclusion'); ao.samples = 16; ao.only_local = True; ao.inputs['Distance'].default_value = 0.035
    ramp = nt.nodes.new('ShaderNodeValToRGB'); e = ramp.color_ramp.elements
    e[0].position = 0.0; e[0].color = (0.36, 0.30, 0.22, 1); e[1].position = 0.9; e[1].color = (0.80, 0.75, 0.60, 1)
    em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(ao.outputs['AO'], ramp.inputs['Fac']); nt.links.new(ramp.outputs['Color'], em.inputs['Color']); nt.links.new(em.outputs[0], out.inputs['Surface'])
    target(imgs['A']); bk.margin = 6; bpy.ops.object.bake(type='EMIT')
    emit_attr('FromRef', imgs['M'], 2); emit_attr('Sock', imgs['S'], 4); emit_attr('Mouth', imgs['D'], 3); emit_attr('Mouth2', imgs['E'], 3)
    out = reset(); bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled'); nt.links.new(bsdf.outputs[0], out.inputs['Surface']); target(imgs['B'])
    ref = bpy.data.objects['REF_Full']; ref.hide_set(False); ref.hide_render = False
    ref.select_set(True); sk.select_set(True); bpy.context.view_layer.objects.active = sk
    bk.use_selected_to_active = True; bk.cage_extrusion = 0.02; bk.max_ray_distance = 0.06; bk.margin = 6
    bk.use_pass_direct = False; bk.use_pass_indirect = False; bk.use_pass_color = True
    bpy.ops.object.bake(type='DIFFUSE')
    bk.use_selected_to_active = False; ref.select_set(False); ref.hide_set(True)
    def arr(i):
        a = np.empty(1024 * 1024 * 4, dtype=np.float32); i.pixels.foreach_get(a); return a.reshape(1024, 1024, 4)
    a, b = arr(imgs['A']), arr(imgs['B'])
    m, s, d, e2 = (arr(imgs[k])[..., 0:1] for k in 'MSDE')
    isref = m[..., 0] > 0.5
    amed = np.median(a[(~isref) & (a[..., :3].sum(-1) > 0.05)][:, :3], 0); bmed = np.median(b[isref][:, :3], 0)
    at = a.copy(); at[..., :3] = np.clip(a[..., :3] * (bmed * 1.08 / amed), 0, 1)
    w = np.clip(m, 0, 1).copy(); w[b[..., :3].sum(-1) < 0.03] = 0
    F = at * (1 - w) + b * w
    F = F * (1 - s) + np.array([0.05, 0.045, 0.05, 1], np.float32) * s
    F = F * (1 - d) + np.array([0.10, 0.06, 0.06, 1], np.float32) * d
    F = F * (1 - e2) + np.array([0.11, 0.075, 0.07, 1], np.float32) * e2
    F[..., 3] = 1
    T = bpy.data.images.get('T_Skeleton_D') or bpy.data.images.new('T_Skeleton_D', 1024, 1024)
    T.pixels.foreach_set(F.ravel())
    T.filepath_raw = os.path.join(DIR, 'T_Skeleton_D.png'); T.file_format = 'PNG'; T.save()
    out = reset(); bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled'); bsdf.inputs['Roughness'].default_value = 0.8
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = T
    nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color']); nt.links.new(bsdf.outputs[0], out.inputs['Surface'])
    for i in imgs.values(): bpy.data.images.remove(i)
