"""Extract the existing wicker basket source without changing its archive.

The .blend entry is a concatenation of Zstandard frames. Blender 2.93 cannot
open that compression directly, so expand it into a disposable work file.
"""
from pathlib import Path
from io import BytesIO
import ctypes
import ctypes.util
import zipfile
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
ARCHIVE = Path.home() / "Downloads/Props/wicker_basket_01_4k.blend.zip"
WORK = ROOT / "Tools/Pueblo/work"
MODELS = ROOT / "Assets/Zandea/Pueblo/Models"


def main() -> None:
    WORK.mkdir(parents=True, exist_ok=True)
    MODELS.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(ARCHIVE) as source:
        data = source.read("wicker_basket_01_4k.blend")
        with Image.open(BytesIO(source.read("textures/wicker_basket_01_diff_4k.jpg"))) as image:
            image.resize((1024, 1024), Image.Resampling.LANCZOS).save(
                MODELS / "basket_base.jpg", quality=88
            )
    library = ctypes.CDLL(ctypes.util.find_library("zstd"))
    library.ZSTD_findFrameCompressedSize.argtypes = [ctypes.c_void_p, ctypes.c_size_t]
    library.ZSTD_findFrameCompressedSize.restype = ctypes.c_size_t
    library.ZSTD_getFrameContentSize.argtypes = [ctypes.c_void_p, ctypes.c_size_t]
    library.ZSTD_getFrameContentSize.restype = ctypes.c_ulonglong
    library.ZSTD_decompress.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_size_t]
    library.ZSTD_decompress.restype = ctypes.c_size_t
    cursor, chunks = 0, []
    while cursor < len(data):
        frame = data[cursor:]
        compressed = library.ZSTD_findFrameCompressedSize(frame, len(frame))
        expanded = library.ZSTD_getFrameContentSize(frame, len(frame))
        if expanded:
            output = ctypes.create_string_buffer(expanded)
            actual = library.ZSTD_decompress(output, expanded, frame, compressed)
            if actual != expanded:
                raise RuntimeError(f"Basket frame at {cursor}: decoded {actual}, expected {expanded}")
            chunks.append(output.raw)
        cursor += compressed
    destination = WORK / "basket_uncompressed.blend"
    destination.write_bytes(b"".join(chunks))
    print(destination)


if __name__ == "__main__":
    main()
