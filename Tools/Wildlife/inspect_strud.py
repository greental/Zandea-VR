import bpy, os
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.abspath('Assets/MyAssets/mesh/Wildlife/Strud/Strud.glb'))
m=next(o for o in bpy.context.scene.objects if o.type=='MESH')
print('BOUNDS', [(min(v.co[i] for v in m.data.vertices),max(v.co[i] for v in m.data.vertices)) for i in range(3)])
bpy.context.scene.render.engine='BLENDER_WORKBENCH'
bpy.context.scene.display.shading.light='STUDIO'
bpy.context.scene.display.shading.color_type='MATERIAL'
bpy.context.scene.render.resolution_x=1200;bpy.context.scene.render.resolution_y=900;bpy.context.scene.render.resolution_percentage=100
for name,pos in [('side',(3,0,0)),('front',(0,-3,0)),('angle',(2,-3,1.5))]:
 bpy.ops.object.camera_add(location=pos); c=bpy.context.object;c.rotation_euler=(-c.location).to_track_quat('-Z','Y').to_euler();c.data.type='ORTHO';c.data.ortho_scale=2.5;bpy.context.scene.camera=c
 bpy.context.scene.render.filepath=os.path.abspath('Temp/StrudReview/'+name+'.png');bpy.ops.render.render(write_still=True)
