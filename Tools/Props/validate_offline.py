"""File-only checks for the Slice 8 terrain and prop asset wiring.

Run with ``python Tools/Props/validate_offline.py``. Unity Play Mode remains a
separate check: this script cannot validate physics, NavMesh, or XR interactions.
"""

from pathlib import Path
import re


PROJECT = Path(__file__).resolve().parents[2]
ASSETS = PROJECT / "Assets"


def guid(path: Path) -> str:
    match = re.search(r"^guid: ([0-9a-f]{32})$", path.read_text(), re.MULTILINE)
    assert match, f"Missing Unity GUID: {path}"
    return match.group(1)


def main() -> None:
    tile_set = ASSETS / "MyAssets/Terrain/PlanetTerrain/TileSets/terrain_28798d35_TileSet.asset"
    treadmill = ASSETS / "MyAssets/prefabs/Terrain/Generated Planet Treadmill 42.prefab"
    config = treadmill.read_text()
    reference = re.search(r"^  authoredTileSet: \{fileID: 11400000, guid: ([0-9a-f]{32})", config, re.MULTILINE)
    assert reference and reference.group(1) == guid(tile_set.with_suffix(".asset.meta")), "Treadmill TileSet reference is broken"
    assert "  generateMissingRuntimeTiles: 1" in config, "Missing baked tiles would be hidden"
    radius = int(re.search(r"^  visibleTileRadius: (\d+)$", config, re.MULTILINE).group(1))
    authored = len(re.findall(r"^  - coord:", tile_set.read_text(), re.MULTILINE))
    assert authored == 9 and radius >= 1

    prefabs = ASSETS / "Zandea/Props/Prefabs"
    expected = {"pot", "stool", "chest", "barrel", "rack", "crate", "sack", "table", "statue"}
    for name in expected:
        assert (prefabs / f"{name}.prefab").is_file(), f"Missing {name} prefab"
        assert (prefabs / f"{name}.prefab.meta").is_file(), f"Missing {name} GUID"
    for name in ("pot_broken", "stool_broken", "barrel_broken"):
        assert (prefabs / f"{name}.prefab").is_file(), f"Missing offline fracture: {name}"

    scene = (ASSETS / "Main.unity").read_text()
    assert "m_Name: Zandea Bandit Outpost" in scene
    authored_props = sum(scene.count(guid(prefabs / f"{name}.prefab.meta")) for name in expected)
    assert authored_props > 0, "Main scene's authored prop references are missing"
    print(f"PASS: valid TileSet GUID, {authored} authored tiles, {(radius * 2 + 1) ** 2}-tile window with fallback")
    print("PASS: nine interaction prefabs, three offline fracture prefabs, Main authored reference")
    print("Unity Play Mode and Quest 2 interaction/performance still require manual checks")


if __name__ == "__main__":
    main()
