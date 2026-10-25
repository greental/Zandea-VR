import bpy
from mathutils import Vector

bpy.ops.wm.open_mainfile(filepath=r'D:\applications\Unity\Projects\Kenshi_VR\Assets\Wildlife\Thornmaw\Art\Thornmaw_Rig.blend')
arm=bpy.data.objects['Thornmaw_Rig']
cam_data=bpy.data.cameras.new('PreviewCamera')
cam=bpy.data.objects.new('PreviewCamera',cam_data)
bpy.context.collection.objects.link(cam)
cam.location=(2.7,-3.5,2.5)
cam.rotation_euler=(Vector((0,0,0))-cam.location).to_track_quat('-Z','Y').to_euler()
cam_data.type='ORTHO'; cam_data.ortho_scale=3.2
bpy.context.scene.camera=cam
light_data=bpy.data.lights.new('PreviewKey','AREA')
light=bpy.data.objects.new('PreviewKey',light_data)
bpy.context.collection.objects.link(light)
light.location=(1,-2,4); light_data.energy=900; light_data.size=5
scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE'
scene.render.resolution_x=800; scene.render.resolution_y=800
scene.render.resolution_percentage=100
for action_name, frame in [('Thornmaw_Walk',8),('Thornmaw_Run',5)]:
    arm.animation_data.action=bpy.data.actions[action_name]
    scene.frame_set(frame)
    scene.render.filepath='D:/applications/Unity/Projects/Kenshi_VR/Tools/Thornmaw/'+action_name+'.png'
    bpy.ops.render.render(write_still=True)
