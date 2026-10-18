"""Blender authoring step: save eight ceramic sectors with capped cut faces as FBX.

Run in background with -- <source.gltf> <destination.fbx>. No runtime geometry work.
"""
import bmesh
import bpy
import math
import sys
from mathutils import Vector

source, destination = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
if source.lower().endswith(".blend"):
    bpy.ops.wm.open_mainfile(filepath=source)
    original = bpy.data.objects["wooden_barrels_01_barrel01"]
    decimate = original.modifiers.new("Fragment budget", "DECIMATE")
    decimate.ratio = 3000 / sum(len(p.vertices)-2 for p in original.data.polygons)
    bpy.context.view_layer.objects.active = original
    bpy.ops.object.select_all(action="DESELECT")
    original.select_set(True)
    bpy.ops.object.modifier_apply(modifier=decimate.name)
else:
    bpy.ops.import_scene.gltf(filepath=source)
    original = next(o for o in bpy.data.objects if o.type == "MESH")
bpy.context.view_layer.objects.active = original
bpy.ops.object.select_all(action="DESELECT")
original.select_set(True)
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
solid = original.modifiers.new("Ceramic wall and rim", "SOLIDIFY")
solid.thickness = .018
solid.offset = -1
bpy.ops.object.modifier_apply(modifier=solid.name)

# The original glTF uses Blender's Z-up after import. Eight sectors with uneven angles
# avoid a perfectly regular shattered-pot silhouette.
angles = [0, 38, 86, 130, 181, 222, 271, 316, 360]
center = sum((v.co for v in original.data.vertices), Vector()) / len(original.data.vertices)
created = []
for index in range(8):
    shard = original.copy()
    shard.data = original.data.copy()
    bpy.context.collection.objects.link(shard)
    bm = bmesh.new()
    bm.from_mesh(shard.data)
    for angle, keep_left in [(angles[index], True), (angles[index + 1], False)]:
        theta = math.radians(angle)
        direction = Vector((math.cos(theta), math.sin(theta), 0))
        normal = Vector((-direction.y, direction.x, 0))
        result = bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                                        dist=0.0001, plane_co=center, plane_no=normal,
                                        clear_inner=not keep_left, clear_outer=keep_left)
        bm.normal_update()
        cut_edges = [e for e in bm.edges if e.is_boundary and all(abs((v.co-center).dot(normal)) < .001 for v in e.verts)]
        if cut_edges:
            try:
                bmesh.ops.holes_fill(bm, edges=cut_edges, sides=0)
            except RuntimeError:
                pass
    bm.to_mesh(shard.data)
    bm.free()
    shard.data.update()
    shard.name = ("BarrelStaves_%02d" if source.lower().endswith(".blend") else "CeramicShard_%02d") % index
    created.append(shard)

bpy.data.objects.remove(original, do_unlink=True)
bpy.ops.object.select_all(action="DESELECT")
for shard in created:
    shard.select_set(True)
bpy.context.view_layer.objects.active = created[0]
bpy.ops.export_scene.fbx(filepath=destination, use_selection=True, add_leaf_bones=False,
                         axis_forward="-Z", axis_up="Y", apply_unit_scale=True, path_mode="STRIP")
for shard in created:
    bm = bmesh.new()
    bm.from_mesh(shard.data)
    open_edges = sum(e.is_boundary for e in bm.edges)
    print("ZANDEA_SHARD %s tris=%d boundary_edges=%d" %
          (shard.name, sum(len(p.vertices)-2 for p in shard.data.polygons), open_edges))
    bm.free()
