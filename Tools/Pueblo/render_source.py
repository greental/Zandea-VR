"""Render an offline source-mesh review image; outputs are disposable."""
import bpy
import os
import sys
from mathutils import Vector

source, destination = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=source)
mesh = next(obj for obj in bpy.data.objects if obj.type == "MESH")
corners = [mesh.matrix_world @ Vector(c) for c in mesh.bound_box]
center = sum(corners, Vector()) / len(corners)
extent = max((max(p.x for p in corners) - min(p.x for p in corners)),
             (max(p.y for p in corners) - min(p.y for p in corners)))
camera = bpy.data.cameras.new("Review camera")
cam = bpy.data.objects.new("Review camera", camera)
bpy.context.collection.objects.link(cam)
cam.location = center + Vector((extent * 1.8, -extent * 2.1, extent * 1.2))
direction = center - cam.location
cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
cam.data.type = "ORTHO"
cam.data.ortho_scale = extent * 2.1
bpy.context.scene.camera = cam
sun_data = bpy.data.lights.new("Sun", "SUN")
sun = bpy.data.objects.new("Sun", sun_data)
bpy.context.collection.objects.link(sun)
sun.rotation_euler = (0.5, -0.4, 0.7)
sun_data.energy = 3
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1000
scene.render.resolution_y = 800
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = destination
scene.world.color = (0.5, 0.5, 0.5)
bpy.ops.render.render(write_still=True)
