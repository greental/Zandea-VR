import bpy, os
print('START', flush=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
print('CLEARED', flush=True)
bpy.ops.import_scene.gltf(filepath=r'D:\applications\Unity\Projects\Kenshi_VR\Assets\MyAssets\mesh\Wildlife\Strud\Strud.glb')
print('IMPORTED', flush=True)
from mathutils import Vector
sc=bpy.context.scene
sc.render.engine='BLENDER_WORKBENCH'
sc.render.resolution_x=1200
sc.render.resolution_y=900
sc.render.resolution_percentage=100
for name,pos in [('side',(3,0,0)),('front',(0,-3,0)),('angle',(2,-3,1.5))]:
 bpy.ops.object.camera_add(location=pos)
 c=bpy.context.object
 c.rotation_euler=(-c.location).to_track_quat('-Z','Y').to_euler()
 c.data.type='ORTHO';c.data.ortho_scale=2.5;sc.camera=c
 sc.render.filepath=os.path.abspath('Temp/StrudReview/'+name+'.png')
 bpy.ops.render.render(write_still=True)
