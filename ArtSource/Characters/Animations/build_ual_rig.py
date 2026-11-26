# T-pose rig only (armature + decimated mannequin, no clips): the master avatar for every UAL clip.
import bpy,sys
src,out=sys.argv[sys.argv.index('--')+1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
arm.animation_data_clear()
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
for pb in arm.pose.bones: pb.matrix_basis.identity()
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out,use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=False,apply_scale_options='FBX_SCALE_ALL',global_scale=1.0)
print("RIG done", [ (o.name,o.type) for o in bpy.data.objects])
