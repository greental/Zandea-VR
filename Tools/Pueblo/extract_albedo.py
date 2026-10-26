"""Extract untouched, full-resolution base-color images from Pueblo source GLBs.

Run: python Tools/Pueblo/extract_albedo.py
Default output: Tools/Pueblo/work/albedo_exports (one reusable export copy).
The manifest records the source, material and glTF color factor for each image.
"""
import argparse
import base64
import json
from pathlib import Path
import struct

PROJECT = Path(__file__).resolve().parents[2]
SOURCE = Path(r"C:\Users\green\Downloads\puebloBonito\output")
DEFAULT_OUTPUT = PROJECT / "Tools/Pueblo/work/albedo_exports"
OLD_KEYS = (
    "house_base", "house_base_stairs", "house_large_two_stories", "house_single",
    "Ramada_attached", "Ramada_attached_3", "Ramada_full", "round", "stairCase",
    "wall_1", "wall_2", "wall_3", "wall_gate",
)


def chunks(path):
    with path.open("rb") as stream:
        magic, version, length = struct.unpack("<4sII", stream.read(12))
        if magic != b"glTF" or version != 2 or length != path.stat().st_size:
            raise ValueError(f"Invalid glTF 2 binary: {path}")
        json_data = None
        binary = None
        while stream.tell() < length:
            size, kind = struct.unpack("<I4s", stream.read(8))
            payload = stream.read(size)
            if kind == b"JSON":
                json_data = json.loads(payload)
            elif kind == b"BIN\x00":
                binary = payload
        if json_data is None:
            raise ValueError(f"No glTF JSON chunk: {path}")
        return json_data, binary


def image_bytes(gltf, binary, image, source_dir):
    if "bufferView" in image:
        view = gltf["bufferViews"][image["bufferView"]]
        if view.get("buffer", 0) != 0 or binary is None:
            raise ValueError("Only the primary embedded GLB buffer is supported")
        start = view.get("byteOffset", 0)
        return binary[start:start + view["byteLength"]]
    uri = image["uri"]
    if uri.startswith("data:"):
        return base64.b64decode(uri.split(",", 1)[1])
    path = (source_dir / uri).resolve()
    if not path.is_relative_to(source_dir.resolve()):
        raise ValueError("Image URI leaves source directory")
    return path.read_bytes()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--new-only", action="store_true", help="Export only the 14 additions")
    args = parser.parse_args()
    additions = json.loads((PROJECT / "Tools/Pueblo/new_assets.json").read_text(encoding="utf-8"))
    entries = [] if args.new_only else [(key, f"{key}.glb") for key in OLD_KEYS]
    entries += [(item["key"], item["source"]) for item in additions]
    args.output.mkdir(parents=True, exist_ok=True)
    manifest = []
    for key, filename in entries:
        source = SOURCE / filename
        gltf, binary = chunks(source)
        materials = gltf.get("materials", [])
        textures = gltf.get("textures", [])
        images = gltf.get("images", [])
        for index, material in enumerate(materials):
            pbr = material.get("pbrMetallicRoughness", {})
            slot = pbr.get("baseColorTexture")
            if slot is None:
                continue
            image_index = textures[slot["index"]]["source"]
            image = images[image_index]
            mime = image.get("mimeType", "image/png")
            extension = ".jpg" if mime == "image/jpeg" else ".png"
            suffix = "" if len(materials) == 1 else f"_material_{index:02d}"
            destination = args.output / f"{key}{suffix}_albedo{extension}"
            destination.write_bytes(image_bytes(gltf, binary, image, SOURCE))
            manifest.append({"key": key, "source": filename, "materialIndex": index,
                             "materialName": material.get("name", f"Material {index}"),
                             "baseColorFactor": pbr.get("baseColorFactor", [1, 1, 1, 1]),
                             "image": destination.name, "bytes": destination.stat().st_size})
    (args.output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2),
                                                  encoding="utf-8")
    print(f"Exported {len(manifest)} source albedos to {args.output}")


if __name__ == "__main__":
    main()
