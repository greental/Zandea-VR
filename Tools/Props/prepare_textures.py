"""Resize selected source textures without modifying the downloaded archives."""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Temp" / "ZandeaProps"
TARGET = ROOT / "Assets" / "Zandea" / "Props" / "Textures"
TARGET.mkdir(parents=True, exist_ok=True)
TEXTURES = {
    "stool": ("wooden_stool_02_4k.blend/textures/wooden_stool_02_diff_4k.jpg", 1024),
    "chest": ("treasure_chest_4k.blend/textures/treasure_chest_diff_4k.jpg", 1024),
    "barrel": ("wooden_barrels_01_4k.blend/textures/wooden_barrels_01_barrel01_diff_4k.png", 1024),
    "pot": ("large_ceramic_container/textures/Material_Texture.dds_baseColor.jpeg", 512),
    "rack": ("weapon_rack/textures/Rack_baseColor.jpeg", 512),
    "statue": ("statue/diffuse.jpg", 1024),
    "crate": ("wooden_crate_01_4k.blend/textures/wooden_crate_01_diff_4k.jpg", 1024),
    "table": ("wooden_table_02_4k.blend/textures/wooden_table_02_diff_4k.jpg", 1024),
    "sack": ("burlap-sack/textures/gltf_embedded_0.png", 512),
}
for key, (name, limit) in TEXTURES.items():
    with Image.open(SOURCE / name) as image:
        original = image.size
        image = image.convert("RGB")
        image.thumbnail((limit, limit), Image.Resampling.LANCZOS)
        path = TARGET / f"{key}_base.jpg"
        image.save(path, "JPEG", quality=88, optimize=True)
        print(f"{key}: {original} -> {image.size}, {path.stat().st_size / 1024:.0f} KiB")
