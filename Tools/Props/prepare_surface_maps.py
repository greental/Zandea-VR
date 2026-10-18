"""Build 1K normal and URP metallic/smoothness maps from selected source textures."""
import os
from pathlib import Path
os.environ["OPENCV_IO_ENABLE_OPENEXR"] = "1"
import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "Temp" / "ZandeaProps"
OUT = ROOT / "Assets" / "Zandea" / "Props" / "Textures"
SOURCES = {
    "stool": ("wooden_stool_02_4k.blend/textures/wooden_stool_02_nor_gl_4k.exr", "wooden_stool_02_4k.blend/textures/wooden_stool_02_rough_4k.exr", None),
    "chest": ("treasure_chest_4k.blend/textures/treasure_chest_nor_gl_4k.exr", "treasure_chest_4k.blend/textures/treasure_chest_rough_4k.exr", "treasure_chest_4k.blend/textures/treasure_chest_metal_4k.exr"),
    "barrel": ("wooden_barrels_01_4k.blend/textures/wooden_barrels_01_barrel01_nor_gl_4k.png", "wooden_barrels_01_4k.blend/textures/wooden_barrels_01_barrel01_rough_4k.png", "wooden_barrels_01_4k.blend/textures/wooden_barrels_01_barrel01_metal_4k.png"),
    "pot": ("large_ceramic_container/textures/Material_Texture.dds_normal.png", None, None),
    "rack": ("weapon_rack/textures/Rack_normal.png", None, None),
    "crate": ("wooden_crate_01_4k.blend/textures/wooden_crate_01_nor_gl_4k.exr", "wooden_crate_01_4k.blend/textures/wooden_crate_01_rough_4k.exr", "wooden_crate_01_4k.blend/textures/wooden_crate_01_metal_4k.exr"),
    "table": ("wooden_table_02_4k.blend/textures/wooden_table_02_nor_gl_4k.exr", "wooden_table_02_4k.blend/textures/wooden_table_02_rough_4k.jpg", "wooden_table_02_4k.blend/textures/wooden_table_02_metal_4k.exr"),
    "sack": ("burlap-sack/textures/gltf_embedded_2.png", None, None),
}

def load(name, gray=False):
    if not name:
        return None
    arr = cv2.imread(str(SRC / name), cv2.IMREAD_UNCHANGED)
    if arr is None:
        raise FileNotFoundError(name)
    if gray and arr.ndim == 3:
        arr = cv2.cvtColor(arr[:, :, :3], cv2.COLOR_BGR2GRAY)
    if arr.dtype.kind == "f":
        arr = np.clip(arr, 0, 1) * 255
    return cv2.resize(arr.astype(np.uint8), (1024, 1024), interpolation=cv2.INTER_AREA)

for key, (normal, rough, metal) in SOURCES.items():
    n = load(normal)
    if n.ndim == 3:
        n = n[:, :, :3]
    cv2.imwrite(str(OUT / f"{key}_normal.png"), n)
    if rough:
        r = load(rough, True)
        m = load(metal, True) if metal else np.zeros_like(r)
        packed = np.zeros((1024, 1024, 4), np.uint8)
        packed[:, :, 2] = m  # OpenCV BGRA -> Unity RGBA: red metallic
        packed[:, :, 3] = 255 - r
        cv2.imwrite(str(OUT / f"{key}_metallic_smooth.png"), packed)
    print(key, "normal", bool(normal), "surface", bool(rough))
