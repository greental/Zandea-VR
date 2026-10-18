"""Run with Blender --background <file> --python inspect_model.py to inspect source art."""
import bpy
import json

objects = []
for obj in bpy.data.objects:
    if obj.type != "MESH":
        continue
    mesh = obj.data
    objects.append({
        "name": obj.name,
        "triangles": sum(len(face.vertices) - 2 for face in mesh.polygons),
        "dimensions": [round(v, 4) for v in obj.dimensions],
        "materials": [mat.name if mat else None for mat in mesh.materials],
        "hidden": obj.hide_render,
    })
print("ZANDEA_AUDIT=" + json.dumps(objects))
