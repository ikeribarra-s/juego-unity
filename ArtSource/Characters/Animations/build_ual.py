import bpy,sys,math,os
from mathutils import Matrix,Vector
src,outdir,blendout=sys.argv[sys.argv.index('--')+1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
for o in list(bpy.data.objects):
    if o.type=='MESH': bpy.data.objects.remove(o)
arm.animation_data_create()
for b in arm.pose.bones: b.rotation_mode='QUATERNION'
def act(n): return [a for a in bpy.data.actions if a.name.endswith('|'+n)][0]
def setsrc(a):
    arm.animation_data.action=a; arm.animation_data.action_slot=a.slots[0]
def mw(): return arm.matrix_world
pbs=arm.pose.bones
order=[b.name for b in pbs]  # parents come first
TW=math.radians(65)
def twist(theta):
    # rotate pelvis about world Z around its head; counter-rotate spine_01/02 half each
    R=lambda t,p: Matrix.Translation(p)@Matrix.Rotation(t,4,'Z')@Matrix.Translation(-p)
    for name,t in (('pelvis',theta),('spine_01',-theta*0.5),('spine_02',-theta*0.5)):
        pb=pbs[name]; head=mw()@pb.head
        pb.matrix=mw().inverted()@(R(t,head)@(mw()@pb.matrix))
        bpy.context.view_layer.update()
# (name, source, reverse, twist deg, dropLast)
JOBS=[('Idle','Idle_Loop',0,0,1),('IdleTorch','Idle_Torch_Loop',0,0,1),
('Walk_F','Walk_Loop',0,0,1),('Walk_B','Walk_Loop',1,0,1),('Walk_L','Walk_Loop',0,65,1),('Walk_R','Walk_Loop',0,-65,1),
('Jog_F','Jog_Fwd_Loop',0,0,1),('Jog_B','Jog_Fwd_Loop',1,0,1),('Jog_L','Jog_Fwd_Loop',0,65,1),('Jog_R','Jog_Fwd_Loop',0,-65,1),
('Sprint_F','Sprint_Loop',0,0,1),
('CrouchIdle','Crouch_Idle_Loop',0,0,1),
('CrouchWalk_F','Crouch_Fwd_Loop',0,0,1),('CrouchWalk_B','Crouch_Fwd_Loop',1,0,1),('CrouchWalk_L','Crouch_Fwd_Loop',0,65,1),('CrouchWalk_R','Crouch_Fwd_Loop',0,-65,1),
('Jump_Start','Jump_Start',0,0,0),('Jump_InAir','Jump_Loop',0,0,1),('Jump_Land','Jump_Land',0,0,0)]
os.makedirs(outdir,exist_ok=True)
sc=bpy.context.scene
newacts=[]
for name,sn,rev,tw,drop in JOBS:
    a=act(sn); setsrc(a)
    f0,f1=int(a.frame_range[0]),int(a.frame_range[1])
    frames=list(range(f0,f1+1))
    # keep the duplicate last frame: Unity loops last key -> first key, so last must equal first
    if rev: frames=frames[::-1]
    rec=[]
    for f in frames:
        sc.frame_set(f); bpy.context.view_layer.update()
        rec.append({n:pbs[n].matrix_basis.copy() for n in order})
    na=bpy.data.actions.new(name); newacts.append(na)
    arm.animation_data.action=None
    arm.animation_data.action=na
    for i,r in enumerate(rec):
        fr=i+1
        for n in order: pbs[n].matrix_basis=r[n]
        bpy.context.view_layer.update()
        if tw:
            twist(math.radians(tw))
        for n in order:
            pb=pbs[n]
            pb.keyframe_insert('rotation_quaternion',frame=fr,group=n)
            if n in('root','pelvis'): pb.keyframe_insert('location',frame=fr,group=n)
    for n in order:
        pb=pbs[n]; pb.matrix_basis=Matrix.Identity(4)
        pb.keyframe_insert('rotation_quaternion',frame=0,group=n)
        if n in('root','pelvis'): pb.keyframe_insert('location',frame=0,group=n)
    sc.frame_start=1; sc.frame_end=len(rec)
    # make sure slot assigned
    if na.slots: arm.animation_data.action_slot=na.slots[0]
    sc.name=name
    fp=os.path.join(outdir,'Anim_%s.fbx'%name)
    bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); bpy.context.view_layer.objects.active=arm
    sc.frame_set(0); bpy.context.view_layer.update()   # defaults written from the rest pose
    bpy.ops.export_scene.fbx(filepath=fp,use_selection=True,object_types={'ARMATURE'},add_leaf_bones=False,
        bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0.0,
        bake_anim_step=1.0,apply_scale_options='FBX_SCALE_ALL',global_scale=1.0)
    print("EXP",name,len(rec),"frames",os.path.getsize(fp))
for a in list(bpy.data.actions):
    if a not in newacts: a.use_fake_user=True
for a in newacts: a.use_fake_user=True
bpy.ops.wm.save_as_mainfile(filepath=blendout)
