"""Blender check that the exported gate has a human-width clear mesh passage."""
import bpy
import os
import sys
from mathutils import Vector

source = sys.argv[sys.argv.index("--") + 1]
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=source)
meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]


def blocked(x, height):
    for obj in meshes:
        matrix = obj.matrix_world.inverted()
        start = matrix @ Vector((x, -10, height))
        direction = matrix.to_3x3() @ Vector((0, 1, 0))
        hit, point, _normal, _face = obj.ray_cast(start, direction)
        if hit and (obj.matrix_world @ point).y < 10:
            return True
    return False


for height in (1.2, 1.7):
    clear = [round(-3 + i * .1, 2) for i in range(61)
             if not blocked(-3 + i * .1, height)]
    print("GATE_CLEAR", height, min(clear), max(clear), "width", round(max(clear) - min(clear), 2))
    if max(clear) - min(clear) < 1.8:
        raise RuntimeError("Pueblo gate passage is too narrow")
