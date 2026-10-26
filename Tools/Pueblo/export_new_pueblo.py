"""Offline FBX/Unity-texture export for the 14 additions in new_assets.json.

Run: D:/applications/blender/blender.exe -b --python Tools/Pueblo/export_new_pueblo.py
Source GLBs are read-only. Import into Unity from Assets/Zandea/Pueblo/Models.
"""
import bmesh
import bpy
import json
import os
from mathutils import Vector

project = os.getcwd()
manifest_path = os.path.join(project, "Tools", "Pueblo", "new_assets.json")
source_dir = r"C:\Users\green\Downloads\puebloBonito\output"
output_dir = os.path.join(project, "Assets", "Zandea", "Pueblo", "Models")
report_path = os.path.join(project, "Tools", "Pueblo", "work", "new_export_report.json")
os.makedirs(output_dir, exist_ok=True)
os.makedirs(os.path.dirname(report_path), exist_ok=True)
with open(manifest_path, encoding="utf-8") as file:
    manifest = json.load(file)

results = []
for entry in manifest:
    source = os.path.join(source_dir, entry["source"])
    if not os.path.isfile(source):
        raise FileNotFoundError(source)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.gltf(filepath=source)
    meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError("No mesh in " + source)
    faces_before = sum(len(obj.data.polygons) for obj in meshes)
    loose_removed = 0
    for obj in meshes:
        mesh = bmesh.new()
        mesh.from_mesh(obj.data)
        loose = [vertex for vertex in mesh.verts if not vertex.link_faces]
        loose_removed += len(loose)
        if loose:
            bmesh.ops.delete(mesh, geom=loose, context="VERTS")
            mesh.to_mesh(obj.data)
            obj.data.update()
        mesh.free()
        target = max(500, round(entry["triangleBudget"] * len(obj.data.polygons) / faces_before))
        if len(obj.data.polygons) > target:
            bpy.ops.object.select_all(action="DESELECT")
            obj.select_set(True)
            bpy.context.view_layer.objects.active = obj
            modifier = obj.modifiers.new("Moderate offline decimation", "DECIMATE")
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
        obj.location = (obj.location - Vector((middle_x, middle_y, bottom))) * entry["scale"]
        obj.scale *= entry["scale"]
        obj.name = entry["key"] + "_mesh"

    images = []
    for obj in meshes:
        for material in obj.data.materials:
            if material and material.use_nodes:
                for node in material.node_tree.nodes:
                    if node.type == "TEX_IMAGE" and node.image and node.image not in images:
                        images.append(node.image)
    if len(images) != 1:
        raise RuntimeError(entry["key"] + " expected one source albedo; found " + str(len(images)))
    image = images[0]
    image.scale(1024, 1024)  # Unity runtime texture; the original 2048 albedo is exported separately.
    image.filepath_raw = os.path.join(output_dir, entry["key"] + "_base.png")
    image.file_format = "PNG"
    image.save()

    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    destination = os.path.join(output_dir, entry["key"] + ".fbx")
    bpy.ops.export_scene.fbx(filepath=destination, use_selection=True, add_leaf_bones=False,
                             axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
                             path_mode="STRIP")
    result = {"key": entry["key"], "source": entry["source"], "kind": entry["kind"],
              "facesBefore": faces_before,
              "facesAfter": sum(len(obj.data.polygons) for obj in meshes),
              "looseVerticesRemoved": loose_removed}
    results.append(result)
    print("PUEBLO_NEW_EXPORT " + json.dumps(result, ensure_ascii=False), flush=True)

with open(report_path, "w", encoding="utf-8") as file:
    json.dump(results, file, ensure_ascii=False, indent=2)
