"""Blender worker for one extracted prop; called by import_remaining.py."""
import bmesh
import bpy
import json
from pathlib import Path
import re
import sys
from mathutils import Matrix, Vector

record_path = Path(sys.argv[sys.argv.index("--") + 1])
record = json.loads(record_path.read_text(encoding="utf-8"))
key = record["key"]
model_path = Path(record["model"])
project = Path.cwd()
output = project / "Assets/Zandea/Props/Remaining"
model_output = output / "Models"
texture_output = output / "Textures"
report_output = project / "Tools/Props/work/remaining-import/reports"
for folder in (model_output, texture_output, report_output):
    folder.mkdir(parents=True, exist_ok=True)

if model_path.suffix.lower() == ".blend":
    bpy.ops.wm.open_mainfile(filepath=str(model_path))
else:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    extension = model_path.suffix.lower()
    if extension in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=str(model_path))
    elif extension == ".fbx":
        bpy.ops.import_scene.fbx(filepath=str(model_path))
    elif extension == ".obj":
        if bpy.app.version >= (3, 4, 0):
            bpy.ops.wm.obj_import(filepath=str(model_path))
        else:
            bpy.ops.import_scene.obj(filepath=str(model_path))
    elif extension == ".dae":
        bpy.ops.wm.collada_import(filepath=str(model_path))
    else:
        raise RuntimeError(f"Unsupported model: {model_path}")

meshes = [obj for obj in bpy.data.objects if obj.type == "MESH" and not obj.hide_render]
if not meshes:
    raise RuntimeError(f"No visible meshes in {model_path}")
if record.get("rotateYDegrees"):
    from math import radians
    adjustment = Matrix.Rotation(radians(record["rotateYDegrees"]), 4, "Y")
    for obj in meshes:
        obj.matrix_world = adjustment @ obj.matrix_world
before = sum(sum(len(face.vertices) - 2 for face in obj.data.polygons) for obj in meshes)
if before == 0:
    raise RuntimeError(f"No triangles in {model_path}")
removed = 0
for obj in meshes:
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    loose = [vertex for vertex in bm.verts if not vertex.link_faces]
    removed += len(loose)
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bm.to_mesh(obj.data)
        obj.data.update()
    bm.free()
    count = sum(len(face.vertices) - 2 for face in obj.data.polygons)
    target = max(120, round(record["triangleBudget"] * count / before))
    if count > target * 1.12:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        modifier = obj.modifiers.new("Offline prop decimation", "DECIMATE")
        modifier.ratio = min(1.0, target / count)
        bpy.ops.object.modifier_apply(modifier=modifier.name)

for obj in meshes:
    world = obj.matrix_world.copy()
    obj.parent = None
    obj.matrix_world = world
bpy.context.view_layer.update()
points = [obj.matrix_world @ Vector(corner) for obj in meshes for corner in obj.bound_box]
low = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
high = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
dimensions = high - low
longest = max(dimensions)
if longest <= 0:
    raise RuntimeError(f"Zero extent: {key}")
scale = record["sizeMeters"] / longest
origin = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))
for obj in meshes:
    obj.location = (obj.location - origin) * scale
    obj.scale *= scale
    obj.name = key + "_" + re.sub(r"[^A-Za-z0-9_]", "_", obj.name)[:45]

textures = [Path(p) for p in record["textures"]]
non_color = r"normal|_nor|rough|metalness|metallic|_metal|_ao|occl|height|gloss|spec|emiss"
color_textures = [p for p in textures if not re.search(non_color, p.name, re.I)]
base_candidates = [p for p in color_textures if re.search(r"albedo|base.?color|_base|diff|_color|color_|color\.", p.name, re.I)]
fallback = max(base_candidates or color_textures, key=lambda p: p.stat().st_size) if color_textures else None
materials = []
for obj in meshes:
    if not obj.data.materials:
        obj.data.materials.append(bpy.data.materials.new(key + "_plain"))
    for material in obj.data.materials:
        if material and material not in materials:
            materials.append(material)
if len(materials) > 32:
    raise RuntimeError(f"Too many materials ({len(materials)}) for {key}; inspect source grouping")
material_records = []
for index, material in enumerate(materials):
    image = None
    overrides = record.get("materialTextureOverrides", [])
    if index < len(overrides) and overrides[index]:
        match = next((p for p in textures if p.name.lower() == overrides[index].lower()), None)
        if not match:
            raise RuntimeError(f"Missing texture override {overrides[index]} for {key}")
        image = bpy.data.images.load(str(match), check_existing=False)
    if material.use_nodes:
        nodes = material.node_tree.nodes
        principled = next((n for n in nodes if n.type == "BSDF_PRINCIPLED"), None)
        if principled and not image:
            color = principled.inputs.get("Base Color")
            if color and color.is_linked:
                source = color.links[0].from_node
                if source.type == "TEX_IMAGE":
                    image = source.image
        if not image:
            image = next((n.image for n in nodes if n.type == "TEX_IMAGE" and n.image), None)
    if image and re.search(non_color, image.name, re.I):
        image = None
    if not image and fallback:
        image = bpy.data.images.load(str(fallback), check_existing=False)
    if image and (not image.has_data or min(image.size) <= 0):
        try:
            image.reload()
        except Exception:
            pass
        if not image.has_data or min(image.size) <= 0:
            image = bpy.data.images.load(str(fallback), check_existing=False) if fallback else None
    destination = texture_output / f"{key}_{index:02d}_base.png"
    if image:
        try:
            if not image.has_data:
                image.reload()
            width, height = image.size
            if width <= 0 or height <= 0:
                raise ValueError("source image has no pixels")
            ratio = min(1, 1024 / max(width, height))
            if ratio < 1:
                image.scale(max(1, round(width * ratio)), max(1, round(height * ratio)))
            image.filepath_raw = str(destination)
            image.file_format = "PNG"
            image.save()
        except Exception as error:
            raise RuntimeError(f"Cannot export material {material.name} image for {key}: {error}") from error
    else:
        color = material.diffuse_color
        image = bpy.data.images.new(key + "_flat", width=4, height=4)
        image.pixels = list(color) * 16
        image.filepath_raw = str(destination)
        image.file_format = "PNG"
        image.save()
    material.name = f"{key}_mat_{index:02d}"
    material_records.append({"index": index, "name": material.name,
                             "texture": destination.name, "sourceImage": image.name})

bpy.ops.object.select_all(action="DESELECT")
for obj in meshes:
    obj.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
fbx = model_output / (key + ".fbx")
bpy.ops.export_scene.fbx(filepath=str(fbx), use_selection=True, add_leaf_bones=False,
                         axis_forward="-Z", axis_up="Y", apply_unit_scale=True, path_mode="STRIP")
after = sum(sum(len(face.vertices) - 2 for face in obj.data.polygons) for obj in meshes)
result = {"key": key, "archive": record["archive"], "kind": record["kind"],
          "sizeMeters": record["sizeMeters"], "meshCount": len(meshes),
          "trianglesBefore": before, "trianglesAfter": after,
          "looseVerticesRemoved": removed, "sourceDimensions": list(dimensions),
          "materials": material_records}
(report_output / (key + ".json")).write_text(json.dumps(result, indent=2), encoding="utf-8")
print("ZANDEA_REMAINING_EXPORT=" + json.dumps(result), flush=True)
