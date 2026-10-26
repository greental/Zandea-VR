"""Render the 14 original GLBs as texture-enhancement references.

Run: D:/applications/blender/blender.exe -b --python Tools/Pueblo/render_references.py
Writes one 1024px image per new source to Tools/Pueblo/work/reference_renders.
"""
import bpy
import json
import os
from mathutils import Vector

project = os.getcwd()
source = r"C:\Users\green\Downloads\puebloBonito\output"
output = os.path.join(project, "Tools", "Pueblo", "work", "reference_renders")
os.makedirs(output, exist_ok=True)
with open(os.path.join(project, "Tools", "Pueblo", "new_assets.json"), encoding="utf-8") as file:
    entries = json.load(file)

for entry in entries:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.gltf(filepath=os.path.join(source, entry["source"]))
    meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
    bpy.context.view_layer.update()
    points = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    low = Vector([min(p[d] for p in points) for d in range(3)])
    high = Vector([max(p[d] for p in points) for d in range(3)])
    center = (low + high) * .5
    span = max(high[d] - low[d] for d in range(3))
    bpy.ops.object.camera_add(location=center + Vector((2.5, -3.2, 2.1)) * span)
    camera = bpy.context.object
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = span * 2.8
    bpy.context.scene.camera = camera
    bpy.ops.object.light_add(type="AREA", location=center + Vector((-2, -3, 5)) * span)
    bpy.context.object.data.energy = 2500
    bpy.context.object.data.size = span * 3
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = 32
    scene.world.use_nodes = True
    scene.world.node_tree.nodes.get("Background").inputs["Strength"].default_value = .8
    scene.render.resolution_x = 1024
    scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = os.path.join(output, entry["key"] + "_reference.png")
    bpy.ops.render.render(write_still=True)
    print("PUEBLO_REFERENCE " + entry["key"], flush=True)
