"""Conservative adaptation of VR Koombar's external GLB cleanup pass.

The source files are never modified. Each GLB is imported into a fresh Blender
scene, its mesh objects are joined, loose vertices/edges and degenerate faces
are removed, normals are repaired, and a new material-preserving GLB is
written to the Unity project.
"""
import bpy
import bmesh
import json
import os
import sys


def args_after_separator():
    if "--" not in sys.argv:
        raise RuntimeError("Expected -- <manifest> <source-root> <output-root>")
    values = sys.argv[sys.argv.index("--") + 1:]
    if len(values) != 3:
        raise RuntimeError("Expected manifest, source root, and output root")
    return values


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def clean_mesh(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.delete_loose(use_verts=True, use_edges=True, use_faces=False)
    bpy.ops.mesh.remove_doubles(threshold=0.00005)
    bpy.ops.mesh.dissolve_degenerate(threshold=0.000001)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    mesh = obj.data
    mesh.validate(clean_customdata=False)
    mesh.update()


def process(source, destination):
    reset_scene()
    bpy.ops.import_scene.gltf(filepath=source)
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not meshes:
        raise RuntimeError("No mesh objects in " + source)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = os.path.splitext(os.path.basename(destination))[0]
    clean_mesh(joined)
    os.makedirs(os.path.dirname(destination), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=destination,
        export_format="GLB",
        use_selection=False,
        export_apply=True,
        export_materials="EXPORT",
        export_yup=True,
    )
    return len(joined.data.vertices), len(joined.data.polygons)


manifest_path, source_root, output_root = args_after_separator()
with open(manifest_path, "r", encoding="utf-8") as handle:
    manifest = json.load(handle)

report = []
for job in manifest["items"]:
    source = os.path.join(source_root, job["folder"], job["source"] + ".glb")
    destination = os.path.join(output_root, job["folder"], job["name"] + ".glb")
    try:
        vertices, faces = process(source, destination)
        report.append({"name": job["name"], "vertices": vertices, "faces": faces, "ok": True})
        print("CLEANED", job["name"], vertices, faces)
    except Exception as error:
        report.append({"name": job["name"], "ok": False, "error": str(error)})
        print("FAILED", job["name"], error)

report_path = os.path.join(output_root, "cleanup_report.json")
with open(report_path, "w", encoding="utf-8") as handle:
    json.dump(report, handle, indent=2)
if any(not row["ok"] for row in report):
    raise RuntimeError("One or more armor meshes failed; see " + report_path)
