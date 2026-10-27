"""Use explicit source albedos when imported FBX material links are missing or wrong.

The downloaded archives are read-only. Run after import_remaining.py.
"""
from pathlib import Path
import json
from PIL import Image

PROJECT = Path(__file__).resolve().parents[2]
WORK = PROJECT / "Tools/Props/work/remaining-import"
OUTPUT = PROJECT / "Assets/Zandea/Props/Remaining/Textures"
SOURCES = json.loads((WORK / "extracted_manifest.json").read_text(encoding="utf-8"))

OVERRIDES = {
    "campfire_simple": ["campfire_base.png"],
    "jail_cage": ["Metal023_2K-JPG_Color.jpg"],
    "statue_guanyin": ["170511_mia337_005788_603_200Kfaces_OBJ4.jpg"],
    # FBX material slot order was matched to the seven separate texture sets.
    "old_well": ["stones_albedo.jpg", "roof_albedo.jpg", "wood_albedo.jpg",
                 "handle_albedo.jpg", "rope_albedo.jpg", "basket_albedo.jpg",
                 "water_albedo.jpg"],
}

for record in SOURCES:
    key = record["key"]
    if key not in OVERRIDES:
        continue
    report_path = WORK / "reports" / (key + ".json")
    report = json.loads(report_path.read_text(encoding="utf-8"))
    names = OVERRIDES[key]
    if len(names) != len(report["materials"]):
        raise RuntimeError(f"{key}: source texture count does not match material slots")
    for material, name in zip(report["materials"], names):
        candidates = [Path(path) for path in record["textures"] if Path(path).name.lower() == name.lower()]
        if not candidates:
            raise FileNotFoundError(f"{key}: {name}")
        source = candidates[0]
        destination = OUTPUT / material["texture"]
        with Image.open(source) as image:
            image = image.convert("RGB")
            image.thumbnail((1024, 1024), Image.Resampling.LANCZOS)
            image.save(destination, format="PNG", optimize=True)
        material["sourceImage"] = name
        print(f"ALBEDO {key}:{material['index']} <- {name}")
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
