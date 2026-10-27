"""Generate all new pot, jar, vase, bowl and plate debris FBXs offline."""
from pathlib import Path
import json
import os
import shutil
import subprocess
import sys

PROJECT = Path(__file__).resolve().parents[2]
WORK = PROJECT / "Tools/Props/work/remaining-import"
BLENDER = Path(os.environ.get("ZANDEA_BLENDER", str(WORK / "blender45/blender-4.5.15-windows-x64/blender.exe")))
WORKER = PROJECT / "Tools/Props/fracture_remaining_vessel_blender.py"
ITEMS = json.loads((PROJECT / "Assets/Zandea/Props/Remaining/fracture_manifest.json").read_text())["items"]
force = "--force" in sys.argv

if shutil.disk_usage("D:/").free < 20 * 1024 ** 3:
    raise RuntimeError("D: has less than 20 GiB free")
if not BLENDER.is_file():
    raise FileNotFoundError("Blender 4.5+ required; set ZANDEA_BLENDER: " + str(BLENDER))
records = WORK / "fracture-records"
logs = WORK / "fracture-logs"
records.mkdir(parents=True, exist_ok=True)
logs.mkdir(parents=True, exist_ok=True)
failures = []
for index, item in enumerate(ITEMS, 1):
    key = item["id"]
    target = PROJECT / "Assets/Zandea/Props/Remaining/Fractures/Models" / (key + "_fractured.fbx")
    if not force and target.is_file() and target.stat().st_size:
        print(f"SKIP {index}/{len(ITEMS)} {key}", flush=True)
        continue
    record = records / (key + ".json")
    record.write_text(json.dumps(item), encoding="utf-8")
    print(f"FRACTURE {index}/{len(ITEMS)} {key}", flush=True)
    result = subprocess.run([str(BLENDER), "-b", "-t", "4", "--python", str(WORKER), "--", str(record)],
                            cwd=PROJECT, text=True, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, encoding="utf-8", errors="replace", timeout=300)
    (logs / (key + ".log")).write_text(result.stdout, encoding="utf-8")
    if result.returncode or not target.is_file():
        failures.append(key)
        print(f"FAIL {key}: {result.stdout[-350:]}", flush=True)
    else:
        line = next((line for line in result.stdout.splitlines() if line.startswith("ZANDEA_VESSEL_FRACTURE=")), "")
        print(line or f"DONE {key}", flush=True)
print("FAILED=" + json.dumps(failures), flush=True)
print(f"D_FREE_GIB={shutil.disk_usage('D:/').free / 1024 ** 3:.1f}", flush=True)
sys.exit(1 if failures else 0)
