"""Offline geometry contact sheet for the 32 remaining prop FBXs.

Run with Blender 4.5+ in background mode; writes only into the named work folder.
"""
import bpy
import json
from pathlib import Path
from mathutils import Vector

project = Path.cwd()
items = json.loads((project / "Assets/Zandea/Props/Remaining/manifest.json").read_text())["items"]
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)

for number, item in enumerate(items):
    before = set(bpy.data.objects)
    path = project / "Assets/Zandea/Props/Remaining/Models" / (item["key"] + ".fbx")
    bpy.ops.import_scene.fbx(filepath=str(path))
    meshes = [obj for obj in set(bpy.data.objects) - before if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError("Missing mesh " + item["key"])
    bpy.context.view_layer.update()
    points = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
    low = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    high = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    size = high - low
    scale = 2.0 / max(size)
    center = (high + low) / 2
    column, row = number % 4, number // 4
    target = Vector(((column - 1.5) * 4, (3.5 - row) * 4, 0))
    for obj in meshes:
        obj.matrix_world.translation = (obj.matrix_world.translation - center) * scale + target
        obj.scale *= scale
        obj.color = (.65, .45, .27, 1) if item["category"] != "statue" else (.62, .62, .55, 1)
    bpy.ops.object.text_add(location=target + Vector((-1.7, -1.55, -1.1)))
    label = bpy.context.object
    label.data.body = item["key"]
    label.data.size = .22
    label.rotation_euler = (0, 0, 0)

bpy.ops.object.camera_add(location=(0, -38, 28))
camera = bpy.context.object
direction = Vector((0, 0, 0)) - camera.location
camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
camera.data.type = "ORTHO"
camera.data.ortho_scale = 34
bpy.context.scene.camera = camera
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "OBJECT"
scene.render.resolution_x = 1600
scene.render.resolution_y = 3200
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = str(project / "Tools/Props/work/remaining-import/contact.png")
bpy.ops.render.render(write_still=True)
print("CONTACT=" + scene.render.filepath)
