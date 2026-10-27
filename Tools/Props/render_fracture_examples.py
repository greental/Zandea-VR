"""Offline review image for representative intact and broken vessels."""
import bpy
from mathutils import Vector
from pathlib import Path

project = Path.cwd()
examples = [
    ("Zandea_brass_pot", "Assets/Zandea/Props/Remaining/Models/brass_pot.fbx"),
    ("Zandea_market_pots_pot01_a", "Assets/Zandea/Props/Remaining/Models/market_pots.fbx"),
    ("Pueblo_earth_pot", "Assets/Zandea/Pueblo/Models/earth_pot.fbx"),
]
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)

for row, (key, source) in enumerate(examples):
    for column, path in enumerate((source, f"Assets/Zandea/Props/Remaining/Fractures/Models/{key}_fractured.fbx")):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=str(project / path))
        objs = [o for o in set(bpy.data.objects) - before if o.type == "MESH"]
        if key.endswith("pot01_a") and column == 0:
            objs = [o for o in objs if "Pot_01_010" in o.name]
        bpy.context.view_layer.update()
        points = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
        low = Vector(tuple(min(v[i] for v in points) for i in range(3)))
        high = Vector(tuple(max(v[i] for v in points) for i in range(3)))
        center = (low + high) / 2
        scale = 2 / max(high - low)
        target = Vector(((column - .5) * 3.4, (1 - row) * 3.3, 0))
        for obj in objs:
            obj.matrix_world.translation = (obj.matrix_world.translation - center) * scale + target
            obj.scale *= scale
            obj.color = (.68, .45, .30, 1) if not key.startswith("Pueblo") else (.69, .52, .39, 1)
            if column == 1:
                offset = obj.matrix_world.translation - target
                offset.z = 0
                if offset.length > .001:
                    obj.matrix_world.translation += offset.normalized() * .16
        bpy.ops.object.text_add(location=target + Vector((-1.2, -1.25, -1.1)))
        bpy.context.object.data.body = key + (" intact" if column == 0 else " fractured")
        bpy.context.object.data.size = .18

bpy.ops.object.camera_add(location=(0, -15, 13))
camera = bpy.context.object
camera.rotation_euler = (Vector((0, 0, 0)) - camera.location).to_track_quat("-Z", "Y").to_euler()
camera.data.type = "ORTHO"
camera.data.ortho_scale = 11
scene = bpy.context.scene
scene.camera = camera
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "OBJECT"
scene.render.resolution_x = 1200
scene.render.resolution_y = 1600
scene.render.image_settings.file_format = "PNG"
scene.render.filepath = str(project / "Tools/Props/work/remaining-import/fracture-examples.png")
bpy.ops.render.render(write_still=True)
print("FRACTURE_REVIEW=" + scene.render.filepath)
