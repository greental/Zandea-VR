"""Offline radial fracture for one imported vessel; Blender 4.5+ background worker."""
import bmesh
import bpy
import json
import math
import sys
from pathlib import Path
from mathutils import Vector

record = json.loads(Path(sys.argv[sys.argv.index("--") + 1]).read_text(encoding="utf-8"))
key = record["id"]
project = Path.cwd()
source = project / record["source"]
destination = project / "Assets/Zandea/Props/Remaining/Fractures/Models" / (key + "_fractured.fbx")
destination.parent.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(source))
meshes = [obj for obj in bpy.data.objects if obj.type == "MESH"]
needle = record.get("meshContains", "")
if needle:
    meshes = [obj for obj in meshes if needle.lower() in obj.name.lower()]
if not meshes:
    raise RuntimeError(f"No source mesh for {key}: {needle}")
if needle and len(meshes) != 1:
    raise RuntimeError(f"Ambiguous source mesh for {key}: {[o.name for o in meshes]}")

# Bake source transforms into one mesh; keep its actual surface materials and UVs.
created = []
depsgraph = bpy.context.evaluated_depsgraph_get()
for obj in meshes:
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
    mesh.transform(obj.matrix_world)
    baked = bpy.data.objects.new(key + "_source", mesh)
    bpy.context.collection.objects.link(baked)
    created.append(baked)
bpy.ops.object.select_all(action="DESELECT")
for obj in created:
    obj.select_set(True)
bpy.context.view_layer.objects.active = created[0]
bpy.ops.object.join()
original = created[0]
vertices = [v.co for v in original.data.vertices]
low = Vector(tuple(min(v[axis] for v in vertices) for axis in range(3)))
high = Vector(tuple(max(v[axis] for v in vertices) for axis in range(3)))
height = high.z - low.z
if height < .001:
    raise RuntimeError(f"Flat or wrongly oriented vessel: {key}, extent={high - low}")
center = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))
for vertex in original.data.vertices:
    vertex.co -= center
before = sum(len(face.vertices) - 2 for face in original.data.polygons)
if before > 8000:
    modifier = original.modifiers.new("Debris surface budget", "DECIMATE")
    modifier.ratio = 8000 / before
    bpy.ops.object.modifier_apply(modifier=modifier.name)

# Thin open vessels need an inner face before cutting. Existing closed surfaces
# retain their source thickness.
bm = bmesh.new()
bm.from_mesh(original.data)
boundary = sum(edge.is_boundary for edge in bm.edges)
bm.free()
if boundary:
    modifier = original.modifiers.new("Debris wall thickness", "SOLIDIFY")
    modifier.thickness = min(.015, max(.003, height * .025))
    modifier.offset = -1
    bpy.ops.object.modifier_apply(modifier=modifier.name)

angles = [0, 38, 86, 130, 181, 222, 271, 316, 360]
shards = []
for index in range(len(angles) - 1):
    shard = original.copy()
    shard.data = original.data.copy()
    bpy.context.collection.objects.link(shard)
    bm = bmesh.new()
    bm.from_mesh(shard.data)
    for angle, keep_left in ((angles[index], True), (angles[index + 1], False)):
        theta = math.radians(angle)
        direction = Vector((math.cos(theta), math.sin(theta), 0))
        normal = Vector((-direction.y, direction.x, 0))
        bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                               dist=.0001, plane_co=Vector((0, 0, 0)), plane_no=normal,
                               clear_inner=not keep_left, clear_outer=keep_left)
        bm.normal_update()
        cut_edges = [edge for edge in bm.edges if edge.is_boundary and
                     all(abs(vertex.co.dot(normal)) < .001 for vertex in edge.verts)]
        if cut_edges:
            try:
                bmesh.ops.holes_fill(bm, edges=cut_edges, sides=0)
            except RuntimeError:
                pass
    if len(bm.faces) < 3:
        bm.free()
        bpy.data.objects.remove(shard, do_unlink=True)
        continue
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(shard.data)
    bm.free()
    shard.data.update()
    shard.name = f"{key}_shard_{index:02d}"
    shard_triangles = sum(len(face.vertices) - 2 for face in shard.data.polygons)
    if shard_triangles > 1000:
        bpy.ops.object.select_all(action="DESELECT")
        shard.select_set(True)
        bpy.context.view_layer.objects.active = shard
        modifier = shard.modifiers.new("Shard VR budget", "DECIMATE")
        modifier.ratio = 1000 / shard_triangles
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    shards.append(shard)

if len(shards) < 5:
    raise RuntimeError(f"Only {len(shards)} valid shards for {key}")
bpy.data.objects.remove(original, do_unlink=True)
bpy.ops.object.select_all(action="DESELECT")
for shard in shards:
    shard.select_set(True)
bpy.context.view_layer.objects.active = shards[0]
bpy.ops.export_scene.fbx(filepath=str(destination), use_selection=True,
                         add_leaf_bones=False, axis_forward="-Z", axis_up="Y",
                         apply_unit_scale=True, path_mode="STRIP")
after = sum(sum(len(face.vertices) - 2 for face in shard.data.polygons) for shard in shards)
if after > 9000:
    raise RuntimeError(f"Debris budget exceeded for {key}: {after}")
print("ZANDEA_VESSEL_FRACTURE=" + json.dumps({"id": key, "shards": len(shards),
      "sourceTriangles": before, "debrisTriangles": after, "sourceBoundaryEdges": boundary}), flush=True)
