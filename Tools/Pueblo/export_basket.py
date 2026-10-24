"""Run with Blender 2.93 in background on work/basket_uncompressed.blend."""
import bpy
import json
import os
import sys
from mathutils import Vector

output = sys.argv[sys.argv.index("--") + 1]
mesh = next(obj for obj in bpy.data.objects if obj.type == "MESH" and obj.name == "wicker_basket_01")
bpy.context.view_layer.objects.active = mesh
bpy.ops.object.select_all(action="DESELECT")
mesh.select_set(True)
original = len(mesh.data.polygons)
if original > 2500:
    modifier = mesh.modifiers.new("Offline basket decimation", "DECIMATE")
    modifier.ratio = 2500 / original
    bpy.ops.object.modifier_apply(modifier=modifier.name)
world = [mesh.matrix_world @ Vector(corner) for corner in mesh.bound_box]
center = Vector(((min(p.x for p in world) + max(p.x for p in world)) / 2,
                 (min(p.y for p in world) + max(p.y for p in world)) / 2,
                 min(p.z for p in world)))
mesh.parent = None
mesh.location -= center
bpy.ops.export_scene.fbx(filepath=os.path.join(output, "basket.fbx"),
                         use_selection=True, add_leaf_bones=False,
                         axis_forward="-Z", axis_up="Y", path_mode="STRIP")
print("PUEBLO_BASKET=" + json.dumps({"triangles_before": original,
                                   "triangles_after": len(mesh.data.polygons),
                                   "size": list(mesh.dimensions)}))
