"""Verify the imported Pueblo sources before the Unity editor build step."""
from pathlib import Path
import json
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path.home() / "Downloads/puebloBonito/output"
MODELS = ROOT / "Assets/Zandea/Pueblo/Models"


def main() -> None:
    names = sorted(path.stem for path in SOURCE.glob("*.glb"))
    assert len(names) == 13, f"Expected 13 user-provided GLBs, found {len(names)}"
    manifest = json.loads((SOURCE / "hunyuan_manifest.json").read_text())
    assert all(item["status"] == "success" for item in manifest["images"].values())
    for name in names:
        model = MODELS / f"{name}.fbx"
        texture = MODELS / f"{name}_base.png"
        assert model.is_file() and model.stat().st_size > 100_000, model
        assert texture.is_file(), texture
        with Image.open(texture) as image:
            assert image.size == (1024, 1024), (texture, image.size)
    assert (MODELS / "basket.fbx").stat().st_size > 20_000
    with Image.open(MODELS / "basket_base.jpg") as image:
        assert image.size == (1024, 1024)
    print("PASS: 13 Pueblo FBXs, 13 compressed-size texture sources, basket mesh and texture")
    print("Unity prefab import, layout placement, NavMesh, and headset interaction remain to verify")


if __name__ == "__main__":
    main()
