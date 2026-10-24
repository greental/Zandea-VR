"""Export the user's Pueblo Bonito GLBs as grounded, modest Unity FBX meshes.

Run with Blender in background: blender -b --python export_pueblo.py -- <source.glb> <output-dir>
The original GLBs in Downloads are read only. No mesh generation runs in Unity.
"""
import bpy
import json
import math
import os
import sys
from mathutils import Vector

source, output = sys.argv[sys.argv.index("--") + 1:][:2]
name = os.path.splitext(os.path.basename(source))[0]
os.makedirs(output, exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=source)
meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
if not meshes:
    raise RuntimeError("No mesh in " + source)

# The generated gate has a real but only ~0.8 m wide passage at world scale.
# Widen the aperture offline so locomotion and an NPC NavMesh can cross it.
if name == "wall_gate":
    for obj in meshes:
        for vertex in obj.data.vertices:
            x = vertex.co.x
            if x < -0.05:
                vertex.co.x = x - 0.20
            elif x > 0.15:
                vertex.co.x = x + 0.20
            else:
                vertex.co.x = (x + 0.05) * 3 - 0.25

size_m = 4.0 if name.startswith("wall_") else 5.0 if name.startswith("Ramada") else 6.0
budget = 10000 if name.startswith("house_") or name == "round" else 7000
original = sum(len(obj.data.polygons) for obj in meshes)
for obj in meshes:
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    target = max(500, round(budget * len(obj.data.polygons) / original))
    if len(obj.data.polygons) > target:
        modifier = obj.modifiers.new("Offline decimation", "DECIMATE")
        modifier.ratio = target / len(obj.data.polygons)
        bpy.ops.object.modifier_apply(modifier=modifier.name)

for obj in meshes:
    transform = obj.matrix_world.copy()
    obj.parent = None
    obj.matrix_world = transform
bpy.context.view_layer.update()
points = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
middle_x = (min(p.x for p in points) + max(p.x for p in points)) / 2
middle_y = (min(p.y for p in points) + max(p.y for p in points)) / 2
bottom = min(p.z for p in points)
for obj in meshes:
    obj.location = (obj.location - Vector((middle_x, middle_y, bottom))) * size_m
    obj.scale *= size_m
    obj.name = name + "_mesh"

textures = []
for material in bpy.data.materials:
    if not material.use_nodes:
        continue
    for node in material.node_tree.nodes:
        if node.type != "TEX_IMAGE" or not node.image:
            continue
        image = node.image
        if image in textures:
            continue
        textures.append(image)
        if image.size[0] > 1024 or image.size[1] > 1024:
            image.scale(1024, 1024)
        image.filepath_raw = os.path.join(output, name + "_base.png")
        image.file_format = "PNG"
        image.save()
        break
    if textures:
        break

bpy.ops.object.select_all(action="DESELECT")
for obj in meshes:
    obj.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
destination = os.path.join(output, name + ".fbx")
bpy.ops.export_scene.fbx(filepath=destination, use_selection=True, add_leaf_bones=False,
                         axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
                         path_mode="STRIP")
print("PUEBLO_EXPORT=" + json.dumps({
    "name": name, "source": source, "fbx": destination,
    "scale": size_m, "triangles_before": original,
    "triangles_after": sum(len(obj.data.polygons) for obj in meshes),
    "textures": [image.name for image in textures],
}))
