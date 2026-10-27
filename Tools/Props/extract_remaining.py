"""Extract the distinct downloaded prop models into one named temporary work area.

Run: python Tools/Props/extract_remaining.py
Original Downloads archives are read-only. Nested ZIP/7z/RAR payloads are handled.
"""
from pathlib import Path
from zipfile import ZipFile
import json
import subprocess

PROJECT = Path(__file__).resolve().parents[2]
SOURCE = Path.home() / "Downloads/Props"
WORK = PROJECT / "Tools/Props/work/remaining-import"
SEVEN_ZIP = Path("D:/applications/7-Zip/7z.exe")
MANIFEST = json.loads((PROJECT / "Tools/Props/remaining_manifest.json").read_text(encoding="utf-8"))
MODELS = {".blend", ".glb", ".gltf", ".fbx", ".obj", ".dae"}
TEXTURES = {".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".dds"}


def safe_path(base: Path, name: str) -> Path:
    target = (base / name.replace("\\", "/")).resolve()
    if not target.is_relative_to(base.resolve()):
        raise ValueError(f"Archive path escapes work area: {name}")
    return target


def extract_zip(archive: Path, destination: Path, depth: int = 0) -> None:
    if depth > 3:
        raise ValueError(f"Nested archive too deep: {archive}")
    with ZipFile(archive) as source:
        for item in source.infolist():
            if item.is_dir() or item.filename.startswith("__MACOSX/"):
                continue
            target = safe_path(destination, item.filename)
            target.parent.mkdir(parents=True, exist_ok=True)
            with source.open(item) as input_file, target.open("wb") as output_file:
                while chunk := input_file.read(1024 * 1024):
                    output_file.write(chunk)
            suffix = target.suffix.lower()
            if suffix == ".zip":
                extract_zip(target, target.with_suffix(""), depth + 1)
            elif suffix in {".7z", ".rar"}:
                nested = target.with_suffix("")
                nested.mkdir(exist_ok=True)
                result = subprocess.run([str(SEVEN_ZIP), "x", "-y", f"-o{nested}", str(target)],
                                        text=True, capture_output=True)
                if result.returncode:
                    raise RuntimeError(f"7-Zip cannot extract {target}: {result.stderr[-500:]}")


def main() -> None:
    free = __import__("shutil").disk_usage("D:/").free
    if free < 20 * 1024 ** 3:
        raise RuntimeError(f"D: has only {free / 1024 ** 3:.1f} GiB free")
    WORK.mkdir(parents=True, exist_ok=True)
    records = []
    for entry in MANIFEST:
        archive = SOURCE / entry["archive"]
        if not archive.is_file():
            raise FileNotFoundError(archive)
        destination = WORK / "sources" / entry["key"]
        if not destination.exists():
            destination.mkdir(parents=True)
            extract_zip(archive, destination)
        candidates = [p for p in destination.rglob("*") if p.is_file() and p.suffix.lower() in MODELS]
        if not candidates:
            raise RuntimeError(f"No supported model extracted from {archive}")
        priority = {".blend": 0, ".glb": 1, ".fbx": 2, ".obj": 3, ".gltf": 4, ".dae": 5}
        candidates.sort(key=lambda p: (priority[p.suffix.lower()], -p.stat().st_size))
        model = candidates[0]
        textures = [p for p in destination.rglob("*") if p.is_file() and p.suffix.lower() in TEXTURES]
        record = dict(entry, model=str(model), textures=[str(p) for p in textures])
        records.append(record)
        print(f"EXTRACTED {entry['key']}: {ascii(str(model.relative_to(destination)))}; {len(textures)} textures", flush=True)
    (WORK / "extracted_manifest.json").write_text(json.dumps(records, indent=2), encoding="utf-8")
    print(f"Ready: {len(records)} models; D: {__import__('shutil').disk_usage('D:/').free / 1024 ** 3:.1f} GiB free")


if __name__ == "__main__":
    main()
