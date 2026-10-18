import bpy, os
from mathutils import Vector
stage=os.environ.get('STRUD_STAGE')
bpy.ops.wm.open_mainfile(filepath=os.path.join(stage,'Strud_Authored.blend') if stage else os.path.abspath('Tools/Wildlife/Strud_Authored.blend'))
scene=bpy.context.scene
scene.render.engine='BLENDER_WORKBENCH';scene.render.resolution_x=1000;scene.render.resolution_y=750;scene.render.resolution_percentage=100
bpy.ops.object.camera_add(location=(8,-10,5));cam=bpy.context.object;cam.rotation_euler=(Vector((0,0,1.9))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=7.5;scene.camera=cam
rig=bpy.data.objects['StrudRig']
for name,frame in [('Graze',90),('Alert',30),('Idle',30),('Death',60),('Trot',10)]:
 rig.animation_data.action=bpy.data.actions[name];scene.frame_set(frame)
 scene.render.filepath=os.path.join(stage,'StrudReview',name.lower()+'.png') if stage else os.path.abspath('Temp/StrudReview/'+name.lower()+'.png');bpy.ops.render.render(write_still=True)
