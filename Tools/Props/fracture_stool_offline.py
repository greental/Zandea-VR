"""Blender authoring step: make a seat and four lower frame pieces from the stool."""
import bmesh
import bpy
import sys
from mathutils import Vector

source, destination = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.open_mainfile(filepath=source)
original = next(o for o in bpy.data.objects if o.type == "MESH" and not o.hide_render)
bpy.context.view_layer.objects.active = original
bpy.ops.object.select_all(action="DESELECT")
original.select_set(True)
decimate = original.modifiers.new("Fragment budget", "DECIMATE")
decimate.ratio = 2400 / sum(len(p.vertices)-2 for p in original.data.polygons)
bpy.ops.object.modifier_apply(modifier=decimate.name)
coords = [v.co for v in original.data.vertices]
mid_x = (min(v.x for v in coords) + max(v.x for v in coords)) / 2
mid_y = (min(v.y for v in coords) + max(v.y for v in coords)) / 2
floor = min(v.z for v in coords)
cut_z = floor + (max(v.z for v in coords) - floor) * .66

def clip(bm, coordinate, normal, keep_positive):
    result = bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
                                    dist=.00001, plane_co=coordinate, plane_no=normal,
                                    clear_inner=keep_positive, clear_outer=not keep_positive)
    bm.normal_update()
    edges = [e for e in bm.edges if e.is_boundary and all(abs((v.co-coordinate).dot(normal)) < .0002 for v in e.verts)]
    if edges:
        bmesh.ops.holes_fill(bm, edges=edges, sides=0)

regions = [[(Vector((0,0,cut_z)),Vector((0,0,1)),True)]]
for x in [False, True]:
    for y in [False, True]:
        regions.append([(Vector((0,0,cut_z)),Vector((0,0,1)),False),
                        (Vector((mid_x,0,0)),Vector((1,0,0)),x),
                        (Vector((0,mid_y,0)),Vector((0,1,0)),y)])
parts=[]
for index, region in enumerate(regions):
    part=original.copy()
    part.data=original.data.copy()
    bpy.context.collection.objects.link(part)
    bm=bmesh.new()
    bm.from_mesh(part.data)
    for coordinate, normal, positive in region:
        clip(bm,coordinate,normal,positive)
    bm.to_mesh(part.data)
    part.data.update()
    print("ZANDEA_WOOD %d tris=%d boundary_edges=%d" %
          (index,sum(len(p.vertices)-2 for p in part.data.polygons),sum(e.is_boundary for e in bm.edges)))
    bm.free()
    part.name="StoolSeat" if index==0 else "StoolFrame_%d" % index
    part.scale *= 2.15
    part.location=(part.location-Vector((mid_x,mid_y,floor)))*2.15
    parts.append(part)
bpy.data.objects.remove(original,do_unlink=True)
bpy.ops.object.select_all(action="DESELECT")
for part in parts: part.select_set(True)
bpy.context.view_layer.objects.active=parts[0]
bpy.ops.export_scene.fbx(filepath=destination,use_selection=True,add_leaf_bones=False,
                         axis_forward="-Z",axis_up="Y",apply_unit_scale=True,path_mode="STRIP")
