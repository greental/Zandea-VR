import bpy, glob, math, os
from mathutils import Vector
downloads = os.path.expanduser("~/Downloads")
output = os.path.abspath("Temp/CharacterCreatorDownloadsPreview")
os.makedirs(output, exist_ok=True)
files = glob.glob(os.path.join(downloads, "textured_mesh*.glb"))
files += [os.path.join(downloads, "white_mesh.glb"), os.path.join(downloads, "male_body.glb")]
files.sort(key=lambda p: (0 if os.path.basename(p) == "textured_mesh.glb" else int(os.path.basename(p).split("(")[1].split(")")[0]) if "(" in os.path.basename(p) else 100))
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 256
scene.render.resolution_y = 256
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.world.color = (0.5, 0.5, 0.5)
for path in files:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    try:
        bpy.ops.import_scene.gltf(filepath=path)
        meshes = [o for o in scene.objects if o.type == "MESH"]
        if not meshes:
            print("SKIP", path, "no mesh")
            continue
        points = [o.matrix_world @ Vector(corner) for o in meshes for corner in o.bound_box]
        lo = Vector(tuple(min(v[i] for v in points) for i in range(3)))
        hi = Vector(tuple(max(v[i] for v in points) for i in range(3)))
        center = (lo + hi) / 2
        extent = max((hi - lo).x, (hi - lo).y, (hi - lo).z)
        camera_data = bpy.data.cameras.new("Preview camera")
        camera = bpy.data.objects.new("Preview camera", camera_data)
        scene.collection.objects.link(camera)
        camera.location = center + Vector((0.7, -1.5, 0.4)).normalized() * extent * 2
        camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
        camera_data.type = "ORTHO"
        camera_data.ortho_scale = extent * 1.5
        scene.camera = camera
        light_data = bpy.data.lights.new("Preview light", "AREA")
        light = bpy.data.objects.new("Preview light", light_data)
        scene.collection.objects.link(light)
        light.location = center + Vector((0, -1, 2)) * extent
        light_data.energy = 600
        light_data.shape = "DISK"
        light_data.size = extent * 2
        scene.render.filepath = os.path.join(output, os.path.basename(path) + ".png")
        bpy.ops.render.render(write_still=True)
        print("RENDERED", os.path.basename(path), "bounds", tuple(round(x, 3) for x in hi - lo))
    except Exception as exc:
        print("FAILED", os.path.basename(path), str(exc))



