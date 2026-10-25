"""Rig and animate the downloaded Thornmaw in Blender 2.93, then export to Unity."""
import bpy
import math
import os
from mathutils import Vector

SOURCE = r'C:\Users\green\Downloads\Meshy_AI_Thornmaw_Arachnid_1024115833_texture.glb'
OUT = r'D:\applications\Unity\Projects\Kenshi_VR\Assets\Wildlife\Thornmaw\Art'
SOURCE_OUT = OUT
os.makedirs(OUT, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=SOURCE)
mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
mesh.name = 'Thornmaw_Mesh'
for material in mesh.data.materials:
    if material and material.use_nodes:
        for node in material.node_tree.nodes:
            if node.type == 'TEX_IMAGE' and node.image:
                image = node.image
                image.filepath_raw = os.path.join(OUT, 'Thornmaw_Albedo.png')
                image.file_format = 'PNG'
                image.save()

arm_data = bpy.data.armatures.new('Thornmaw_Armature')
arm = bpy.data.objects.new('Thornmaw_Rig', arm_data)
bpy.context.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
body = arm_data.edit_bones.new('Body')
body.head = (0, 0.15, -0.05)
body.tail = (0, -0.2, -0.05)
legs = []
for side, tag in ((-1, 'L'), (1, 'R')):
    for i, y in enumerate((-0.43, -0.19, 0.09, 0.36), 1):
        # The four pairs fan from the mouth towards the abdomen.
        spread_y = (-0.57, -0.29, 0.14, 0.53)[i-1]
        hip = Vector((side*0.20, y, -0.12))
        knee = Vector((side*(0.42 if i < 3 else 0.49), spread_y, 0.14 if i < 3 else 0.08))
        foot = Vector((side*(0.59 if i < 3 else 0.67), spread_y+(-0.06 if i < 3 else 0.08), -0.46))
        upper = arm_data.edit_bones.new(f'{tag}{i}_Upper')
        upper.head, upper.tail = hip, knee
        upper.parent = body
        lower = arm_data.edit_bones.new(f'{tag}{i}_Lower')
        lower.head, lower.tail = knee, foot
        lower.parent = upper
        legs.append((tag, i, upper.name, lower.name, hip, knee, foot))
bpy.ops.object.mode_set(mode='OBJECT')

body_group = mesh.vertex_groups.new(name='Body')
groups = {}
for _, _, upper, lower, *_ in legs:
    groups[upper] = mesh.vertex_groups.new(name=upper)
    groups[lower] = mesh.vertex_groups.new(name=lower)

def segment_distance(point, start, end):
    line = end-start
    t = max(0.0, min(1.0, (point-start).dot(line)/line.length_squared))
    return (point-(start+t*line)).length, t

for vertex in mesh.data.vertices:
    p = vertex.co
    candidates = []
    for tag, i, upper, lower, hip, knee, foot in legs:
        if p.x * hip.x < 0:
            continue
        d1, t1 = segment_distance(p, hip, knee)
        d2, t2 = segment_distance(p, knee, foot)
        if d1 <= d2:
            candidates.append((d1, upper, t1))
        else:
            candidates.append((d2, lower, t2))
    distance, bone, fraction = min(candidates, key=lambda item: item[0])
    # Keep the central carapace stable; fade each leg into its socket.
    radial = abs(p.x)
    leg_amount = max(0.0, min(1.0, (radial-0.22)/0.12))
    proximity = max(0.0, min(1.0, (0.21-distance)/0.13))
    leg_amount *= proximity
    if leg_amount > 0.001:
        groups[bone].add([vertex.index], leg_amount, 'REPLACE')
    if leg_amount < 0.999:
        body_group.add([vertex.index], max(0.001, 1-leg_amount), 'REPLACE')

modifier = mesh.modifiers.new('Thornmaw_Armature', 'ARMATURE')
modifier.object = arm
mesh.parent = arm

scene = bpy.context.scene
scene.render.fps = 30
for name, frame_count, swing, lift, bob in (
    ('Thornmaw_Walk', 30, 0.25, 0.22, 0.025),
    ('Thornmaw_Run', 18, 0.38, 0.34, 0.055),
):
    action = bpy.data.actions.new(name)
    arm.animation_data_create()
    arm.animation_data.action = action
    for frame in range(frame_count+1):
        phase = 2*math.pi*frame/frame_count
        body_pose = arm.pose.bones['Body']
        body_pose.location.z = bob*(1-math.cos(2*phase))*0.5
        body_pose.keyframe_insert('location', frame=frame, group='Body')
        for tag, i, upper_name, lower_name, *_ in legs:
            # Alternating tetrapod: neighbouring legs and opposite counterparts alternate.
            offset = math.pi*((i + (0 if tag == 'L' else 1)) % 2)
            cycle = phase+offset
            upper = arm.pose.bones[upper_name]
            lower = arm.pose.bones[lower_name]
            upper.rotation_mode = lower.rotation_mode = 'XYZ'
            upper.rotation_euler.z = swing*math.sin(cycle)*(1 if tag == 'L' else -1)
            upper.rotation_euler.x = lift*max(0, math.cos(cycle))
            lower.rotation_euler.x = -0.24*max(0, math.cos(cycle))
            upper.keyframe_insert('rotation_euler', frame=frame, group=upper_name)
            lower.keyframe_insert('rotation_euler', frame=frame, group=lower_name)
    for fcurve in action.fcurves:
        for key in fcurve.keyframe_points:
            key.interpolation = 'BEZIER'
    action.use_fake_user = True
    print('THORN ACTION', name, frame_count)

arm.animation_data.action = bpy.data.actions['Thornmaw_Walk']
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SOURCE_OUT, 'Thornmaw_Rig.blend'))
bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True)
mesh.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'Thornmaw_Rigged.fbx'), use_selection=True,
    apply_unit_scale=True, add_leaf_bones=False, bake_anim=True,
    bake_anim_use_all_bones=True, bake_anim_use_all_actions=True,
    bake_anim_force_startend_keying=True, path_mode='COPY', embed_textures=False)
print('THORN EXPORT COMPLETE', OUT)
