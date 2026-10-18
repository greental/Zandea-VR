"""Blender 4.5: export selected downloaded models as modest Unity FBX sources.

Run with: blender -b --python Tools/Props/export_representative.py -- <key> <source> <output>
Original archives and source models are never modified.
"""
import bpy
import json
import math
import sys
from mathutils import Vector

key, source, destination = sys.argv[sys.argv.index("--") + 1:][:3]
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
if source.lower().endswith(".blend"):
    bpy.ops.wm.open_mainfile(filepath=source)
elif source.lower().endswith((".gltf", ".glb")):
    bpy.ops.import_scene.gltf(filepath=source)
elif source.lower().endswith(".obj"):
    bpy.ops.wm.obj_import(filepath=source)
else:
    raise RuntimeError("Unsupported source: " + source)

meshes = [o for o in bpy.data.objects if o.type == "MESH" and not o.hide_render]
if key == "barrel":
    meshes = [o for o in meshes if o.name == "wooden_barrels_01_barrel01"]
elif key == "chest":
    meshes = [o for o in meshes if o.name.startswith("treasure_chest_")]

stats = []
limits = {"pot": 1800, "stool": 2400, "chest": 8000, "barrel": 3000,
          "rack": 3000, "statue": 4500, "crate": 2500, "sack": 1800, "table": 3500}
total = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in meshes)
for obj in meshes:
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    count = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    budget = max(80, limits[key] * count // max(1, total))
    if count > budget * 1.25:
        modifier = obj.modifiers.new("Zandea selective decimation", "DECIMATE")
        modifier.ratio = max(0.01, budget / count)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    stats.append({"name": obj.name, "original_triangles": count,
                  "export_triangles": sum(len(p.vertices) - 2 for p in obj.data.polygons)})

# Preserve separate lid geometry. The pivot is placed at its rear upper edge.
if key == "chest":
    lid = next(o for o in meshes if o.name == "treasure_chest_lid")
    bpy.context.view_layer.objects.active = lid
    bpy.ops.object.select_all(action="DESELECT")
    lid.select_set(True)
    corners = [lid.matrix_world @ Vector(c) for c in lid.bound_box]
    bpy.context.scene.cursor.location = Vector((sum(c.x for c in corners) / len(corners),
                                                 max(c.y for c in corners),
                                                 min(c.z for c in corners)))
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

# Remove imported scene wrappers, apply source transforms, and ground the shared model.
for obj in meshes:
    world = obj.matrix_world.copy()
    obj.parent = None
    obj.matrix_world = world
bpy.context.view_layer.update()
points = [obj.matrix_world @ Vector(c) for obj in meshes for c in obj.bound_box]
mid_x = (min(p.x for p in points) + max(p.x for p in points)) / 2
mid_y = (min(p.y for p in points) + max(p.y for p in points)) / 2
ground = min(p.z for p in points)
scale = 2.15 if key == "stool" else 1.0
for obj in meshes:
    obj.location = (obj.location - Vector((mid_x, mid_y, ground))) * scale
    obj.scale *= scale
    obj.name = key.capitalize() + "_" + obj.name
bpy.ops.object.select_all(action="DESELECT")
for obj in meshes:
    obj.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.export_scene.fbx(filepath=destination, use_selection=True,
                         add_leaf_bones=False, axis_forward="-Z", axis_up="Y",
                         apply_unit_scale=True, path_mode="STRIP")
print("ZANDEA_EXPORT=" + json.dumps({"key": key, "parts": stats, "scale": scale,
                                     "total_before": total,
                                     "total_after": sum(x["export_triangles"] for x in stats)}))
