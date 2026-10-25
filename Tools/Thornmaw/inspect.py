import bpy
from mathutils import Vector

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=r'C:\Users\green\Downloads\Meshy_AI_Thornmaw_Arachnid_1024115833_texture.glb')
for o in bpy.data.objects:
    print('THORN OBJECT', o.name, o.type, tuple(round(v,3) for v in o.dimensions), tuple(round(v,3) for v in o.location))
    if o.type == 'MESH':
        print('THORN MESH',len(o.data.vertices),len(o.data.polygons),[(m.name) for m in o.data.materials])
        for axis in range(3):
            for sign in [-1,1]:
                verts=sorted(o.data.vertices,key=lambda v:sign*v.co[axis])[:15]
                print('THORN EXTREME',axis,sign,[(tuple(round(c,3) for c in v.co)) for v in verts[:5]])

world=bpy.context.scene.world
world.color=(0.5,0.5,0.5)
cam_data=bpy.data.cameras.new('Camera')
cam=bpy.data.objects.new('Camera',cam_data)
bpy.context.collection.objects.link(cam)
cam.location=(2.7,-3.5,2.5)
direction=Vector((0,0,0))-cam.location
cam.rotation_euler=direction.to_track_quat('-Z','Y').to_euler()
cam_data.type='ORTHO'; cam_data.ortho_scale=3.2
bpy.context.scene.camera=cam
light_data=bpy.data.lights.new('Key','AREA'); light=bpy.data.objects.new('Key',light_data)
bpy.context.collection.objects.link(light); light.location=(1,-2,4); light_data.energy=900; light_data.size=5
bpy.context.scene.render.engine='BLENDER_EEVEE'
bpy.context.scene.render.resolution_x=800; bpy.context.scene.render.resolution_y=800
bpy.context.scene.render.resolution_percentage=100
bpy.context.scene.render.filepath=r'D:\applications\Unity\Projects\Kenshi_VR\Tools\Thornmaw\inspect.png'
bpy.ops.render.render(write_still=True)
