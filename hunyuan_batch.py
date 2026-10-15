"""Sequential batch client for the running Hunyuan3D Gradio UI."""
import argparse
import json
import os
import shutil
import struct
import sys
import tempfile
import time
from datetime import datetime, timezone
from pathlib import Path
from urllib.request import urlopen

from gradio_client import Client, handle_file

EXTENSIONS = {'.png', '.jpg', '.jpeg', '.jfif', '.webp'}


def valid_glb(path):
    """Check the GLB header and chunk boundaries without reading mesh data."""
    try:
        size = path.stat().st_size
        if size < 20:
            return False
        with path.open('rb') as stream:
            magic, version, length = struct.unpack('<4sII', stream.read(12))
            if magic != b'glTF' or version != 2 or length != size:
                return False
            offset, json_seen, bin_seen = 12, False, False
            while offset < size:
                if size - offset < 8:
                    return False
                chunk_length, chunk_type = struct.unpack('<I4s', stream.read(8))
                offset += 8 + chunk_length
                if offset > size or chunk_length % 4:
                    return False
                if chunk_type == b'JSON':
                    json_seen = True
                elif chunk_type == b'BIN\0':
                    bin_seen = True
                stream.seek(chunk_length, 1)
            return offset == size and json_seen and bin_seen
    except (OSError, ValueError, struct.error):
        return False


def save_manifest(path, manifest):
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile('w', encoding='utf-8', dir=path.parent,
                                     prefix='.manifest-', suffix='.tmp', delete=False) as stream:
        json.dump(manifest, stream, indent=2)
        stream.write('\n')
        temp = Path(stream.name)
    os.replace(temp, path)


def file_path(value):
    if isinstance(value, (str, Path)):
        return Path(value)
    if isinstance(value, dict):
        if 'value' in value:
            return file_path(value['value'])
        if 'path' in value:
            return Path(value['path'])
    raise ValueError(f'Unexpected Gradio file result: {str(value)[:300]}')


def file_data(value):
    """Supply the FileData envelope required by the export endpoint."""
    return {'path': str(file_path(value)), 'meta': {'_type': 'gradio.FileData'}}


def call(client, endpoint, args, timeout):
    job = client.submit(*args, api_name='/' + endpoint)
    try:
        return job.result(timeout=timeout)
    except (TimeoutError, KeyboardInterrupt):
        job.cancel()
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--url', default='http://localhost:8080')
    parser.add_argument('--overwrite', action='store_true')
    texture = parser.add_mutually_exclusive_group()
    texture.add_argument('--shape-only', action='store_true')
    texture.add_argument('--texture', action='store_true')
    parser.add_argument('--max-retries', type=int, default=2)
    parser.add_argument('--timeout', type=float, default=3600, help='Seconds per queued API call')
    parser.add_argument('--dry-run', action='store_true')
    parser.add_argument('--recursive', action='store_true')
    args = parser.parse_args()
    if args.max_retries < 0 or args.timeout <= 0:
        parser.error('--max-retries must be nonnegative and --timeout positive')
    if not args.input.is_dir():
        parser.error(f'Input folder does not exist: {args.input}')

    root = args.url.rstrip('/')
    with urlopen(root + '/config', timeout=30) as response:
        config = json.load(response)
    if not config.get('enable_queue'):
        raise RuntimeError('The running Gradio application has no queue; refusing GPU submissions')
    components = {c['id']: c['props'] for c in config['components']}
    endpoints = {d['api_name']: d for d in config['dependencies'] if d.get('api_name')}
    textured_available = ('generation_all' in endpoints and
                          components[19].get('visible') and
                          'Unavailable' not in components[73].get('value', ''))
    if args.texture and not textured_available:
        raise RuntimeError('Texture generation is unavailable in the running UI')
    textured = textured_available and not args.shape_only
    endpoint = 'generation_all' if textured else 'shape_generation'
    for name in (endpoint, 'on_export_click'):
        if name not in endpoints:
            raise RuntimeError(f'Required Gradio endpoint missing: {name}')
    expected = [8, 6, 12, 13, 15, 16, 35, 39, 33, 36, 30, 40, 31]
    if endpoints[endpoint]['inputs'] != expected or endpoints['on_export_click']['inputs'] != [21, 22, 45, 46, 47, 49]:
        raise RuntimeError('The live Gradio API signature changed; inspect /config before using this script')

    images = sorted((args.input.rglob('*') if args.recursive else args.input.iterdir()),
                    key=lambda p: str(p).lower())
    images = [p for p in images if p.is_file() and p.suffix.lower() in EXTENSIONS]
    targets = [str(p.relative_to(args.input).with_suffix('.glb')).lower() for p in images]
    if len(targets) != len(set(targets)):
        raise RuntimeError('Two input images map to the same GLB filename; rename one of them')
    args.output.mkdir(parents=True, exist_ok=True)
    manifest_path = args.output / 'hunyuan_manifest.json'
    try:
        manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    except (OSError, ValueError):
        manifest = {'url': root, 'images': {}}
    manifest['url'] = root
    manifest['texture'] = textured
    print(f'Gradio {config.get("version")}: {endpoint} -> on_export_click; {len(images)} image(s)')
    client = None if args.dry_run else Client(root, verbose=False)
    durations = []
    run_failed = False
    try:
        for index, image in enumerate(images, 1):
            relative = image.relative_to(args.input)
            destination = args.output / relative.with_suffix('.glb')
            key = str(relative).replace('\\', '/')
            if valid_glb(destination) and not args.overwrite:
                print(f'[{index}/{len(images)}] skip {key} (valid GLB exists)')
                previous = manifest['images'].get(key, {})
                manifest['images'][key] = {**previous, 'status': 'skipped',
                                            'output_path': str(destination),
                                            'duration_seconds': previous.get('duration_seconds', 0),
                                            'errors': previous.get('errors', [])}
                if not args.dry_run:
                    save_manifest(manifest_path, manifest)
                continue
            if args.dry_run:
                print(f'[{index}/{len(images)}] would generate {key} -> {destination}')
                continue
            errors = []
            started = time.monotonic()
            for attempt in range(1, args.max_retries + 2):
                print(f'[{index}/{len(images)}] {key}, attempt {attempt}', flush=True)
                try:
                    defaults = lambda cid: components[cid]['value']
                    generation = call(client, endpoint,
                        [None, handle_file(str(image.resolve())), None, None, None, None,
                         defaults(35), defaults(39), defaults(33), defaults(36),
                         defaults(30), defaults(40), defaults(31)], args.timeout)
                    result = call(client, 'on_export_click',
                        [file_data(generation[0]), file_data(generation[1]) if textured else None,
                         defaults(45), defaults(46), textured, defaults(49)], args.timeout)
                    source = file_path(result[1])
                    if not valid_glb(source):
                        raise ValueError(f'Exported file is not a valid GLB: {source}')
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    with tempfile.NamedTemporaryFile(dir=destination.parent, prefix='.hunyuan-',
                                                     suffix='.glb', delete=False) as stream:
                        temp = Path(stream.name)
                    try:
                        shutil.copyfile(source, temp)
                        if not valid_glb(temp):
                            raise ValueError('Copied GLB failed validation')
                        os.replace(temp, destination)
                    finally:
                        temp.unlink(missing_ok=True)
                    duration = round(time.monotonic() - started, 1)
                    durations.append(duration)
                    manifest['images'][key] = {'status': 'success', 'output_path': str(destination),
                                                'duration_seconds': duration, 'errors': errors,
                                                'completed_at': datetime.now(timezone.utc).isoformat()}
                    remaining = len(images) - index
                    print(f'  done in {duration:.1f}s; estimated remaining {remaining * sum(durations)/len(durations)/60:.1f} min')
                    break
                except KeyboardInterrupt:
                    manifest['images'][key] = {'status': 'interrupted', 'output_path': str(destination),
                                                'duration_seconds': round(time.monotonic()-started, 1),
                                                'errors': errors}
                    raise
                except TimeoutError as exc:
                    errors.append(f'Attempt {attempt}: queued job exceeded {args.timeout:g}s; cancellation requested')
                    manifest['images'][key] = {'status': 'failed', 'output_path': str(destination),
                                                'duration_seconds': round(time.monotonic()-started, 1),
                                                'errors': errors}
                    print('  ' + errors[-1], file=sys.stderr, flush=True)
                    return 1
                except Exception as exc:
                    errors.append(f'Attempt {attempt}: {type(exc).__name__}: {exc}')
                    print('  ' + errors[-1], file=sys.stderr, flush=True)
                    if attempt == args.max_retries + 1:
                        run_failed = True
                        manifest['images'][key] = {'status': 'failed', 'output_path': str(destination),
                                                    'duration_seconds': round(time.monotonic()-started, 1),
                                                    'errors': errors}
                finally:
                    save_manifest(manifest_path, manifest)
    except KeyboardInterrupt:
        print('\nInterrupted; completed outputs and manifest preserved.', file=sys.stderr)
        save_manifest(manifest_path, manifest)
        return 130
    return 1 if run_failed else 0


if __name__ == '__main__':
    sys.exit(main())
