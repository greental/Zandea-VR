"""Background Blender garment baker. Run with: blender -b -P clothing_baker.py -- --config job.json"""
import argparse
import json
import math
import sys
import traceback
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

EXTENSIONS = {'.glb', '.gltf', '.fbx', '.obj'}
DEFAULT_MORPHS = ['WeightThin', 'WeightHeavy', 'MuscularitySlim', 'MuscularityMuscular']


def import_file(path):
    before = set(bpy.data.objects)
    suffix = path.suffix.lower()
    if suffix in ('.glb', '.gltf'):
        bpy.ops.import_scene.gltf(filepath=str(path))
    elif suffix == '.fbx':
        bpy.ops.import_scene.fbx(filepath=str(path))
    elif suffix == '.obj':
        bpy.ops.wm.obj_import(filepath=str(path))
    else:
        raise ValueError('Unsupported file: ' + str(path))
    return list(set(bpy.data.objects) - before)


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)


def world_point(obj, vertex):
    return obj.matrix_world @ vertex.co


def body_reference(objects, names):
    meshes = [o for o in objects if o.type == 'MESH' and o.data.shape_keys]
    meshes = [o for o in meshes if all(n in o.data.shape_keys.key_blocks for n in names)]
    if not meshes:
        raise ValueError('Reference has no mesh with all requested body morphs: ' + ', '.join(names))
    # Largest matching mesh is usually skin; explicit body_mesh overrides this.
    return max(meshes, key=lambda o: len(o.data.vertices))


def triangles(body):
    body.data.calc_loop_triangles()
    verts = [world_point(body, v) for v in body.data.vertices]
    faces = [tuple(t.vertices) for t in body.data.loop_triangles]
    return verts, faces, BVHTree.FromPolygons(verts, faces, all_triangles=True)


def barycentric(p, a, b, c):
    u, v, w = b-a, c-a, p-a
    d00, d01, d11 = u.dot(u), u.dot(v), v.dot(v)
    d20, d21 = w.dot(u), w.dot(v)
    denom = d00*d11-d01*d01
    if abs(denom) < 1e-14:
        return (1.0, 0.0, 0.0)
    y = (d11*d20-d01*d21)/denom
    z = (d00*d21-d01*d20)/denom
    return (1-y-z, y, z)


def body_weights(body, index):
    result = {}
    for group in body.data.vertices[index].groups:
        result[body.vertex_groups[group.group].name] = group.weight
    return result


def bake_item(path, config, output, reference):
    clear_scene()
    imported = import_file(reference)
    names = config['morphs'] if config['generate_morphs'] else []
    body = body_reference(imported, names or [DEFAULT_MORPHS[0]])
    if config.get('body_mesh'):
        candidates = [o for o in imported if o.type == 'MESH' and o.name == config['body_mesh']]
        if len(candidates) != 1:
            raise ValueError('body_mesh does not uniquely identify a reference mesh')
        body = candidates[0]
    armature = body.find_armature()
    if not armature:
        raise ValueError('Reference body has no armature')
    for name in names:
        if not body.data.shape_keys or name not in body.data.shape_keys.key_blocks:
            raise ValueError('Missing reference morph ' + name)
    vertices, faces, bvh = triangles(body)
    garments = [o for o in import_file(path) if o.type == 'MESH']
    if not garments:
        raise ValueError('No garment mesh in input')
    max_distance = float(config.get('max_correspondence_distance', 0.35))
    clearance = float(config.get('clearance', 0.008))
    fit_strength = float(config.get('fit_strength', 0.35))
    loose = float(config.get('loose_fabric', 0.35))
    if not 0 <= fit_strength <= 1 or not 0 <= loose <= 1 or clearance < 0:
        raise ValueError('Invalid fit_strength, loose_fabric, or clearance')
    warnings = []
    summary = []
    for garment in garments:
        if len(garment.data.vertices) == 0:
            raise ValueError('Empty garment mesh: ' + garment.name)
        # Keep mesh topology/materials. Bake object transforms into mesh coordinates
        # in the reference armature's space before adding keys or weights.
        garment.data = garment.data.copy()
        transform = armature.matrix_world.inverted() @ garment.matrix_world
        garment.data.transform(transform)
        garment.matrix_world = armature.matrix_world.copy()
        for mod in list(garment.modifiers):
            if mod.type == 'ARMATURE':
                garment.modifiers.remove(mod)
        for group in list(garment.vertex_groups):
            garment.vertex_groups.remove(group)
        base = [v.co.copy() for v in garment.data.vertices]
        matches = []
        far = 0
        for i, local in enumerate(base):
            point = garment.matrix_world @ local
            hit, normal, face_index, distance = bvh.find_nearest(point)
            if hit is None:
                raise ValueError('No body surface correspondence')
            if distance > max_distance:
                far += 1
            ids = faces[face_index]
            weights = barycentric(hit, *(vertices[j] for j in ids))
            # Only correct penetration or a small gap; preserve loose silhouette.
            signed = (point-hit).dot(normal)
            correction = normal * max(0.0, clearance-signed) * fit_strength
            fitted = local + garment.matrix_world.inverted().to_3x3() @ correction
            garment.data.vertices[i].co = fitted
            matches.append((ids, weights, fitted.copy()))
            influences = {}
            for j, factor in zip(ids, weights):
                for bone, weight in body_weights(body, j).items():
                    influences[bone] = influences.get(bone, 0) + factor * weight
            influences = {key: val for key, val in influences.items() if val > 0.001}
            total = sum(influences.values())
            if total <= 0:
                raise ValueError('No transferable skin weights at vertex ' + str(i))
            for bone, weight in influences.items():
                garment.vertex_groups.get(bone) or garment.vertex_groups.new(name=bone)
                garment.vertex_groups[bone].add([i], weight/total, 'REPLACE')
        if far:
            raise ValueError(f'{garment.name}: {far} vertices exceed correspondence distance {max_distance}')
        garment.shape_key_add(name='Basis')
        inverse = garment.matrix_world.inverted().to_3x3()
        for name in names:
            key = garment.shape_key_add(name=name)
            source = body.data.shape_keys.key_blocks[name]
            for i, (ids, weights, fitted) in enumerate(matches):
                delta = Vector((0, 0, 0))
                for j, factor in zip(ids, weights):
                    delta += body.matrix_world.to_3x3() @ (source.data[j].co-body.data.vertices[j].co) * factor
                key.data[i].co = fitted + inverse @ delta * (1-loose)
        mod = garment.modifiers.new('Character skeleton', 'ARMATURE')
        mod.object = armature
        garment.parent = armature
        garment.matrix_parent_inverse = armature.matrix_world.inverted()
        summary.append({'mesh': garment.name, 'vertices': len(base), 'morphs': names})
    checks = {}
    if config['validate']:
        samples = [{'name': 'neutral', 'weights': {} }]
        samples += [{'name': name, 'weights': {name: 1.0}} for name in names]
        if all(n in names for n in ('WeightHeavy', 'MuscularityMuscular')):
            samples.append({'name': 'heavy_muscular_mix', 'weights': {'WeightHeavy': .5, 'MuscularityMuscular': .5}})
        if all(n in names for n in ('WeightThin', 'MuscularitySlim')):
            samples.append({'name': 'thin_slim_mix', 'weights': {'WeightThin': .5, 'MuscularitySlim': .5}})
        for sample in samples:
            for name in names:
                body.data.shape_keys.key_blocks[name].value = sample['weights'].get(name, 0)
                for garment in garments:
                    garment.data.shape_keys.key_blocks[name].value = sample['weights'].get(name, 0)
            bpy.context.view_layer.update()
            deps = bpy.context.evaluated_depsgraph_get()
            evaluated = body.evaluated_get(deps).to_mesh()
            try:
                positions = [body.matrix_world @ v.co for v in evaluated.vertices]
                tree = BVHTree.FromPolygons(positions, faces, all_triangles=True)
                for garment in garments:
                    gmesh = garment.evaluated_get(deps).to_mesh()
                    try:
                        collisions = 0
                        for v in gmesh.vertices:
                            p = garment.matrix_world @ v.co
                            hit, normal, _, distance = tree.find_nearest(p)
                            if hit is not None and (p-hit).dot(normal) < -clearance*.25:
                                collisions += 1
                        checks[f'{sample["name"]}/{garment.name}'] = collisions
                        if collisions:
                            warnings.append(f'{sample["name"]}/{garment.name}: {collisions} likely penetrating vertices')
                    finally:
                        garment.evaluated_get(deps).to_mesh_clear()
            finally:
                body.evaluated_get(deps).to_mesh_clear()
        for name in names:
            body.data.shape_keys.key_blocks[name].value = 0
            for garment in garments:
                garment.data.shape_keys.key_blocks[name].value = 0
    for obj in bpy.data.objects:
        obj.select_set(False)
    armature.select_set(True)
    for garment in garments:
        garment.select_set(True)
    bpy.context.view_layer.objects.active = armature
    destination = output / (path.stem + '.glb')
    if destination.exists() and not config['overwrite']:
        raise FileExistsError(str(destination))
    bpy.ops.export_scene.gltf(filepath=str(destination), export_format='GLB', use_selection=True,
                              export_apply=False, export_morph=True, export_skins=True)
    return {'item': path.name, 'category': config['category'], 'output': str(destination),
            'meshes': summary, 'generated_morphs': names, 'settings': {k: config[k] for k in
            ('clearance', 'fit_strength', 'loose_fabric', 'max_correspondence_distance')},
            'validation': checks, 'warnings': warnings, 'requires_manual_adjustment': bool(warnings),
            'status': 'warning' if warnings else 'success'}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--config', required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    config_path = Path(args.config).resolve()
    config = json.loads(config_path.read_text(encoding='utf-8'))
    config = {**{'recursive': False, 'generate_morphs': True, 'morphs': DEFAULT_MORPHS,
                 'validate': True, 'overwrite': False, 'clearance': .008,
                 'fit_strength': .35, 'loose_fabric': .35,
                 'max_correspondence_distance': .35}, **config}
    resolve = lambda value: (config_path.parent / value).resolve()
    reference, source, output = resolve(config['reference_body']), resolve(config['input']), resolve(config['output'])
    if not reference.is_file() or not source.exists():
        raise FileNotFoundError('Reference or input does not exist')
    output.mkdir(parents=True, exist_ok=True)
    inputs = [source] if source.is_file() else sorted(p for p in
        (source.rglob('*') if config['recursive'] else source.iterdir()) if p.suffix.lower() in EXTENSIONS)
    report = {'reference_body': str(reference), 'items': []}
    for path in inputs:
        try:
            item = bake_item(path, config, output, reference)
            report['items'].append(item)
            (output/(path.stem + '.metadata.json')).write_text(json.dumps(item, indent=2), encoding='utf-8')
        except Exception as exc:
            report['items'].append({'item': path.name, 'status': 'failed', 'error': str(exc),
                                    'traceback': traceback.format_exc(), 'requires_manual_adjustment': True})
    report['successful'] = sum(i['status'] == 'success' for i in report['items'])
    report['warnings'] = sum(i['status'] == 'warning' for i in report['items'])
    report['failed'] = sum(i['status'] == 'failed' for i in report['items'])
    (output/'baker_report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({k: report[k] for k in ('successful', 'warnings', 'failed')}))
    if report['failed']:
        sys.exit(2)


if __name__ == '__main__':
    main()
