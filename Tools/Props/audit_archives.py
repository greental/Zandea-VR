"""Read-only inventory of the downloaded prop archives, including nested archives."""
from __future__ import annotations

import collections
import hashlib
import io
from pathlib import Path
import zipfile

SOURCE = Path.home() / "Downloads" / "Props"
OUT = Path(__file__).resolve().parents[2] / "Assets" / "Zandea" / "Props" / "ASSET_AUDIT.md"
MODEL = {".blend", ".fbx", ".obj", ".glb", ".gltf", ".dae"}
TEXTURE = {".png", ".jpg", ".jpeg", ".exr", ".tga", ".tif", ".tiff"}


def members(blob: bytes, depth: int = 0):
    with zipfile.ZipFile(io.BytesIO(blob)) as archive:
        for item in archive.infolist():
            if item.is_dir():
                continue
            suffix = Path(item.filename).suffix.lower()
            if suffix == ".zip" and depth < 2:
                try:
                    yield from members(archive.read(item), depth + 1)
                except (OSError, zipfile.BadZipFile):
                    yield item.filename, item.file_size
            else:
                yield item.filename, item.file_size


def main():
    lines = [
        "# Slice 8 downloaded asset audit",
        "",
        "Source: `C:/Users/green/Downloads/Props`. Originals remain in Downloads.",
        "This archive inventory does not establish mesh triangles, hierarchy, material quality, scale, or reuse rights.",
        "Those require model import and source-page/license review before each asset is shipped.",
        "",
        "| Archive | Models found (including nested ZIPs) | Textures | Other notes | SHA-256 |",
        "|---|---|---:|---|---|",
    ]
    hashes = collections.defaultdict(list)
    for path in sorted(SOURCE.rglob("*.zip"), key=lambda p: str(p).lower()):
        data = path.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        hashes[digest].append(str(path.relative_to(SOURCE)).replace("\\", "/"))
        content = list(members(data))
        models = [n for n, _ in content if Path(n).suffix.lower() in MODEL]
        textures = [n for n, _ in content if Path(n).suffix.lower() in TEXTURE]
        ext = collections.Counter(Path(n).suffix.lower() for n, _ in content)
        notes = []
        if not models:
            notes.append("No supported model found; inspect nested formats")
        if any("licen" in n.lower() or "credit" in n.lower() for n, _ in content):
            notes.append("License/credits file present; review")
        if ".rar" in ext or ".7z" in ext:
            notes.append("Contains RAR/7z")
        if any(size > 100_000_000 for n, size in content if Path(n).suffix.lower() in MODEL):
            notes.append("Very large source mesh; optimize before Unity")
        model_text = ", ".join(Path(n).name for n in models[:5]) or "—"
        if len(models) > 5:
            model_text += f" (+{len(models) - 5})"
        lines.append(f"| {path.relative_to(SOURCE).as_posix()} | {model_text} | {len(textures)} | {'; '.join(notes) or '—'} | `{digest[:16]}` |")
    lines.extend(["", "## Exact duplicate archives", ""])
    duplicates = [names for names in hashes.values() if len(names) > 1]
    lines.extend(("- " + ", ".join(names)) for names in duplicates)
    if not duplicates:
        lines.append("No byte-identical archives found.")
    lines.extend([
        "", "## Import gates", "",
        "- Review each model's source page/license and record required attribution before redistribution.",
        "- Measure mesh triangles, dimensions, materials, texture dimensions, and moving-part separation after import.",
        "- Statues are static scenery only. Heavy scans need decimation and texture reduction before POI use.",
        "- Brass vessels should dent or ring unless a specific brittle break behavior is authored; do not assign ceramic shards by folder alone.",
        "- Compare similar crate, table, campfire, and statue archives visually; hashes only establish exact duplicates.",
    ])
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"Audited {sum(len(v) for v in hashes.values())} archives; report: {OUT}")


if __name__ == "__main__":
    main()
