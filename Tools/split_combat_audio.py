"""Rebuild the one-shot combat clips from the user's source recordings.

Usage: python Tools/split_combat_audio.py <Grunts folder> <SwordVSword folder>
Requires ffmpeg and ffprobe on PATH.
"""
import json
import re
import subprocess
import sys
from pathlib import Path


def duration(path):
    result = subprocess.check_output([
        "ffprobe", "-v", "error", "-show_entries", "format=duration",
        "-of", "json", str(path)], text=True)
    return float(json.loads(result)["format"]["duration"])


def regions(path, noise, silence_duration, minimum):
    run = subprocess.run([
        "ffmpeg", "-hide_banner", "-i", str(path), "-af",
        f"silencedetect=noise={noise}dB:d={silence_duration}", "-f", "null", "NUL"],
        capture_output=True, text=True, check=False)
    cursor = 0.0
    start = None
    pieces = []
    for line in run.stderr.splitlines():
        match = re.search(r"silence_(start|end): ([0-9.]+)", line)
        if not match:
            continue
        at = float(match.group(2))
        if match.group(1) == "start":
            if at - cursor >= minimum:
                pieces.append((cursor, at))
            start = at
        elif start is not None:
            cursor = at
            start = None
    end = duration(path)
    if start is None and end - cursor >= minimum:
        pieces.append((cursor, end))
    return [(max(0, a - .035), min(end, b + .11)) for a, b in pieces]


def encode(path, output, start, end):
    output.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([
        "ffmpeg", "-v", "error", "-y", "-ss", f"{start:.3f}", "-i", str(path),
        "-t", f"{end-start:.3f}", "-af", "afade=t=in:st=0:d=0.008",
        "-ac", "1", "-ar", "44100", "-c:a", "libvorbis", "-q:a", "4",
        str(output)], check=True)


def main(grunts, swords):
    root = Path(__file__).resolve().parents[1] / "Assets/MeleeCombat/Resources/CombatAudio"
    long_files = [
        "freesound_community-male_grunts-100281.mp3",
        "freesound_community-man-grunt-struggling-2-68927.mp3",
        "freesound_community-punches-32563.mp3",
    ]
    for number, name in enumerate(long_files, 1):
        source = grunts / name
        for index, (a, b) in enumerate(regions(source, -32, .18, .09), 1):
            encode(source, root / f"Male{number}" / f"male{number}_{index:02}.ogg", a, b)
    for source in sorted(grunts.glob("*.mp3")):
        if source.name in long_files:
            continue
        encode(source, root / "SharedGrunts" / f"{source.stem}.ogg", 0, duration(source))
    sword = next(swords.glob("*.mp3"))
    for index, (a, b) in enumerate(regions(sword, -28, .13, .10), 1):
        if b - a > 2.5:
            continue
        encode(sword, root / "Parry" / f"parry_{index:02}.ogg", a, b)
    for folder in sorted(root.iterdir()):
        print(folder.name, len(list(folder.glob("*.ogg"))))


if __name__ == "__main__":
    main(Path(sys.argv[1]), Path(sys.argv[2]))
