"""Export all distinct remaining downloaded props through the offline Blender pipeline.

Run after extract_remaining.py: python Tools/Props/import_remaining.py
Individual failures are reported; no source archive is modified.
"""
from pathlib import Path
import json
import os
import shutil
import subprocess
import sys

PROJECT = Path(__file__).resolve().parents[2]
WORK = PROJECT / "Tools/Props/work/remaining-import"
BLENDER = Path(os.environ.get("ZANDEA_BLENDER", str(WORK / "blender45/blender-4.5.15-windows-x64/blender.exe")))
EXPORTER = PROJECT / "Tools/Props/export_remaining_blender.py"


def main() -> None:
    if shutil.disk_usage("D:/").free < 20 * 1024 ** 3:
        raise RuntimeError("D: has less than 20 GiB free")
    if not BLENDER.is_file():
        raise FileNotFoundError("Blender 4.5+ required; set ZANDEA_BLENDER to its executable: " + str(BLENDER))
    manifest = json.loads((WORK / "extracted_manifest.json").read_text(encoding="utf-8"))
    if len(sys.argv) == 3 and sys.argv[1] == "--only":
        manifest = [entry for entry in manifest if entry["key"] == sys.argv[2]]
        if not manifest:
            raise ValueError("Unknown prop key: " + sys.argv[2])
    records = WORK / "records"
    logs = WORK / "logs"
    records.mkdir(exist_ok=True)
    logs.mkdir(exist_ok=True)
    failures = []
    for index, record in enumerate(manifest, 1):
        key = record["key"]
        report = WORK / "reports" / (key + ".json")
        if report.is_file():
            print(f"SKIP {index}/{len(manifest)} {key}: report exists", flush=True)
            continue
        record_path = records / (key + ".json")
        record_path.write_text(json.dumps(record), encoding="utf-8")
        print(f"EXPORT {index}/{len(manifest)} {key}", flush=True)
        result = subprocess.run([str(BLENDER), "-b", "-t", "4", "--python", str(EXPORTER),
                                 "--", str(record_path)], cwd=PROJECT, text=True,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                encoding="utf-8", errors="replace")
        (logs / (key + ".log")).write_text(result.stdout, encoding="utf-8")
        if result.returncode or not report.is_file():
            failures.append(key)
            print(f"FAIL {key}: {result.stdout[-450:]}", flush=True)
        else:
            data = json.loads(report.read_text(encoding="utf-8"))
            print(f"DONE {key}: {data['trianglesBefore']} -> {data['trianglesAfter']} triangles; "
                  f"{len(data['materials'])} materials", flush=True)
    print("FAILED=" + json.dumps(failures), flush=True)
    print(f"D_FREE_GIB={shutil.disk_usage('D:/').free / 1024 ** 3:.1f}", flush=True)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
