"""Add POC mesh morph targets to a copy of male.glb without altering its rig or animation."""
import json
import math
import pathlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = ROOT / "MyAssets/mesh/Charecters/male/male.glb"
OUTPUT = ROOT / "CharacterCreator/Models/male_creator.glb"


def read_glb(path):
    raw = path.read_bytes()
    assert raw[:4] == b"glTF"
    pos = 12
    chunks = []
    while pos < len(raw):
        size, kind = struct.unpack_from("<I4s", raw, pos)
        pos += 8
        chunks.append((kind, raw[pos:pos + size]))
        pos += size
    return json.loads(chunks[0][1]), bytearray(chunks[1][1])


def positions(document, binary, mesh):
    accessor = document["accessors"][mesh["primitives"][0]["attributes"]["POSITION"]]
    view = document["bufferViews"][accessor["bufferView"]]
    assert accessor["componentType"] == 5126 and accessor["type"] == "VEC3"
    offset = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
    stride = view.get("byteStride", 12)
    return [struct.unpack_from("<fff", binary, offset + i * stride) for i in range(accessor["count"])]


def gaussian(value, center, width):
    return math.exp(-((value - center) / width) ** 2)


def smoothstep(lo, hi, value):
    t = max(0.0, min(1.0, (value - lo) / (hi - lo)))
    return t * t * (3 - 2 * t)


def displacement(mesh_index, name, point):
    x, y, z = point
    if mesh_index in (11, 12) and name == "TeethNarrow":
        # Tooth rows are separate skinned meshes. Narrowing in model X keeps
        # them inside the lips as the jaw and lower face become smaller.
        center_y = 164.4 if mesh_index == 11 else 162.3
        return (-.28 * x, -.05 * (y - center_y), -.1 - .1 * (z - 2.5))
    sign = -1 if name.endswith(("Thin", "Slim", "Narrow", "Small", "Close", "Low", "Down")) else 1
    if mesh_index in (1, 2, 3, 4, 5, 13) and name.startswith(("Weight", "Muscularity")):
        # A single continuous field is evaluated on every skin material part,
        # nails and underwear. Coincident seam vertices therefore move together.
        body_fade = smoothstep(4, 22, y) * (1 - smoothstep(162, 173, y))
        torso = gaussian(x, 0, 25) * gaussian(y, 113, 43)
        arm = smoothstep(17, 36, abs(x)) * (1 - smoothstep(68, 84, abs(x))) * gaussian(y, 145, 18)
        thigh = gaussian(abs(x), 11, 11) * gaussian(y, 61, 24)
        calf = gaussian(abs(x), 9, 9) * gaussian(y, 28, 18)
        neck = gaussian(x, 0, 11) * gaussian(y, 160, 8)
        front = smoothstep(-7, 5, z)
        radial_x = math.tanh(x / 6)
        radial_z = math.tanh((z + 3) / 7)
        if name == "WeightHeavy":
            belly = gaussian(x, 0, 17) * gaussian(y, 108, 19) * smoothstep(-3, 5, z)
            breast = gaussian(abs(x), 10, 8) * gaussian(y, 139, 8) * smoothstep(0, 7, z)
            dx = (5.5 * torso + 3.3 * thigh + 1.8 * calf + 2.1 * neck) * radial_x
            dy = (y - 145) * .14 * arm
            chest_projection_fade = 1 - .8 * gaussian(y, 139, 10) * front
            dz = (3.2 * torso * radial_z * chest_projection_fade + 17.0 * belly + 2.5 * breast
                  + 3.2 * arm * radial_z + 2.7 * thigh * radial_z + 1.7 * calf * radial_z)
            return (dx * body_fade, dy * body_fade, dz * body_fade)
        if name == "WeightThin":
            dx = -(4.1 * torso + 2.7 * thigh + 1.8 * calf + 1.0 * neck) * radial_x
            dy = -(y - 145) * .12 * arm
            dz = -(2.8 * torso + 2.5 * arm + 1.8 * thigh + 1.2 * calf) * radial_z
            return (dx * body_fade, dy * body_fade, dz * body_fade)
        shoulder = gaussian(y, 145, 14) * gaussian(abs(x), 25, 18)
        chest = gaussian(x, 0, 26) * gaussian(y, 139, 16)
        if name == "MuscularityMuscular":
            pectoral = gaussian(abs(x), 11, 8) * gaussian(y, 140, 8) * front
            bicep = gaussian(abs(x), 40, 12) * gaussian(y, 145, 11)
            dx = (7.2 * shoulder + 3.3 * chest + 3.9 * thigh + 2.8 * calf + 2.5 * bicep) * radial_x
            dy = (y - 145) * .2 * arm
            dz = (1.5 * chest * front + 1.07 * pectoral + 4.5 * arm * radial_z +
                  3.0 * thigh * radial_z + 2.3 * calf * radial_z)
            return (dx * body_fade, dy * body_fade, dz * body_fade)
        if name == "MuscularitySlim":
            dx = -(1.4 * shoulder + 1.0 * chest + .9 * thigh) * radial_x
            dy = -(y - 145) * .07 * arm
            dz = -(1.0 * chest * front + .8 * arm * radial_z + .7 * thigh * radial_z)
            return (dx * body_fade, dy * body_fade, dz * body_fade)
    if mesh_index == 1:
        # Facial deltas stay on local tissue; the jaw, skull and neck are not
        # moved as a unit. All coordinates are in the source CC_Base mesh space.
        # The brow paint on this head sits just above the upper eyelid.
        brow_y = 172.65
        brow = gaussian(abs(x), 3.3, 2.3) * gaussian(y, brow_y, 1.2) * gaussian(z, 5.6, 3.2)
        lid = gaussian(abs(x), 3.15, 2.4) * gaussian(y, 170.7, 1.7) * gaussian(z, 5.7, 3.0)
        lip = gaussian(x, 0, 4.8) * gaussian(y, 164.1, 1.55) * gaussian(z, 6.2, 3.2)
        corner = gaussian(abs(x), 3.65, 1.5) * gaussian(y, 164.1, 1.8) * gaussian(z, 5.8, 3.5)
        inner_brow = gaussian(abs(x), 1.8, 1.55) * brow
        if name.startswith("Brow"):
            if name == "BrowLow": return (0, -.9 * brow, 0)
            if name == "BrowHigh": return (0, 1.15 * brow, 0)
            if name == "BrowThin": return (0, -.15 * (y - brow_y) * brow, -.38 * brow)
            if name == "BrowThick": return (0, .2 * (y - brow_y) * brow, .65 * brow)
            if name == "BrowAngry": return (0, -1.35 * inner_brow + .25 * (brow - inner_brow), .2 * brow)
            if name == "BrowRaised": return (0, 1.25 * inner_brow + .35 * brow, 0)
            if name == "BrowClose": return (-math.copysign(.6 * brow, x), 0, 0)
            if name == "BrowWide": return (math.copysign(.6 * brow, x), 0, 0)
        if name == "EyeNarrow": return (0, -.18 * (y - 170.7) * lid, 0)
        if name == "EyeRound": return (0, .18 * (y - 170.7) * lid, .08 * lid)
        if name == "EyeTiltDown": return (0, -.12 * math.copysign(1, x) * x * lid, 0)
        if name == "EyeTiltUp": return (0, .12 * math.copysign(1, x) * x * lid, 0)
        if name == "MouthNarrow": return (-.22 * x * lip, 0, 0)
        if name == "MouthWide": return (.25 * x * lip, 0, 0)
        if name == "LipThin": return (0, -.18 * (y - 164.1) * lip, -.3 * lip)
        if name == "LipFull": return (0, .2 * (y - 164.1) * lip, .7 * lip)
        if name == "ExprHappy": return (0, 1.8 * corner + .25 * brow, .3 * corner)
        if name == "ExprAngry": return (0, -1.45 * inner_brow - .55 * corner - .15 * (y - 170.7) * lid, .25 * brow)
        if name == "ExprSad": return (0, 1.25 * inner_brow - 1.25 * corner, 0)
        if name == "ExprSurprised": return (0, 1.5 * brow + .45 * (y - 170.7) * lid - .35 * (y - 164.1) * lip, .25 * lip)
        if name == "ExprAfraid": return (0, 1.15 * inner_brow + .7 * brow + .35 * (y - 170.7) * lid - .25 * (y - 164.1) * lip, 0)
        if name == "ExprPain": return (0, -1.2 * inner_brow - .5 * corner - .3 * (y - 170.7) * lid, .25 * corner)
        if name.startswith("NoseTip"):
            mask = gaussian(y, 166.6, 2.4) * gaussian(z, 8.2, 2.3) * gaussian(x, 0, 2.5)
            return (0, sign * 1.15 * mask, 0)
        if name.startswith("Nose"):
            mask = gaussian(y, 168.5, 5.0) * gaussian(z, 7.2, 3.2) * gaussian(x, 0, 3.8)
            return (sign * x * .38 * mask, 0, 0)
        if name.startswith("Jaw"):
            mask = gaussian(y, 161.6, 5.0) * gaussian(z, 1.7, 8)
            return (sign * math.tanh(x / 2) * 1.5 * mask, 0, 0)
        if name.startswith("Ear"):
            mask = gaussian(abs(x), 7.6, 2.3) * gaussian(y, 170, 5.4) * gaussian(z, -1, 7)
            return (sign * math.copysign(1.3 * mask, x), sign * (y - 170) * .2 * mask, 0)
        if name.startswith("Forehead"):
            mask = gaussian(y, 177.1, 4.6) * gaussian(x, 0, 7) * gaussian(z, 4.5, 5)
            return (0, sign * .35 * mask, sign * 1.15 * mask)
        if name.startswith("EyeSize"):
            mask = gaussian(abs(x), 3.1, 2.2) * gaussian(y, 170.5, 2.1) * gaussian(z, 5.1, 3)
            return (0, sign * (y - 170.5) * .045 * mask, sign * .06 * mask)
        if name.startswith("EyeSpacing"):
            mask = gaussian(abs(x), 3.1, 2.7) * gaussian(y, 170.5, 2.8) * gaussian(z, 5.1, 3)
            return (sign * math.copysign(.12 * mask, x), 0, 0)
    if mesh_index in (7, 8, 9, 10):
        cx = -3.115 if mesh_index in (7, 8) else 3.15
        cy, cz = 170.58, 3.85
        if name.startswith("EyeSize"):
            return (sign * (x - cx) * .035, sign * (y - cy) * .035, sign * (z - cz) * .035)
        if name.startswith("EyeSpacing"):
            return (sign * math.copysign(.12, cx), 0, 0)
    return (0, 0, 0)


SHAPES = {
    1: ["WeightThin", "WeightHeavy", "MuscularitySlim", "MuscularityMuscular",
        "NoseNarrow", "NoseWide", "NoseTipDown", "NoseTipUp",
        "JawNarrow", "JawWide", "EarSmall", "EarLarge",
        "ForeheadLow", "ForeheadHigh", "EyeSizeSmall", "EyeSizeLarge", "EyeSpacingClose", "EyeSpacingWide",
        "BrowLow", "BrowHigh", "BrowThin", "BrowThick", "BrowAngry", "BrowRaised", "BrowClose", "BrowWide",
        "EyeNarrow", "EyeRound", "EyeTiltDown", "EyeTiltUp", "MouthNarrow", "MouthWide", "LipThin", "LipFull",
        "ExprHappy", "ExprAngry", "ExprSad", "ExprSurprised", "ExprAfraid", "ExprPain"],
    2: ["WeightThin", "WeightHeavy", "MuscularitySlim", "MuscularityMuscular"],
    3: ["WeightThin", "WeightHeavy", "MuscularitySlim", "MuscularityMuscular"],
    4: ["WeightThin", "WeightHeavy", "MuscularitySlim", "MuscularityMuscular"],
    5: ["WeightThin", "WeightHeavy", "MuscularitySlim", "MuscularityMuscular"],
    7: ["EyeSizeSmall", "EyeSizeLarge", "EyeSpacingClose", "EyeSpacingWide"],
    8: ["EyeSizeSmall", "EyeSizeLarge", "EyeSpacingClose", "EyeSpacingWide"],
    9: ["EyeSizeSmall", "EyeSizeLarge", "EyeSpacingClose", "EyeSpacingWide"],
    10: ["EyeSizeSmall", "EyeSizeLarge", "EyeSpacingClose", "EyeSpacingWide"],
    11: ["TeethNarrow"],
    12: ["TeethNarrow"],
    13: ["WeightThin", "WeightHeavy", "MuscularitySlim", "MuscularityMuscular"],
}


def main():
    document, binary = read_glb(SOURCE)
    for index, names in SHAPES.items():
        mesh = document["meshes"][index]
        points = positions(document, binary, mesh)
        targets = []
        for name in names:
            while len(binary) % 4:
                binary.append(0)
            offset = len(binary)
            deltas = [displacement(index, name, p) for p in points]
            for delta in deltas:
                binary.extend(struct.pack("<fff", *delta))
            view_index = len(document["bufferViews"])
            document["bufferViews"].append({"buffer": 0, "byteOffset": offset, "byteLength": len(points) * 12})
            accessor_index = len(document["accessors"])
            document["accessors"].append({"bufferView": view_index, "componentType": 5126,
                                          "count": len(points), "type": "VEC3"})
            targets.append({"POSITION": accessor_index})
        mesh["primitives"][0]["targets"] = targets
        mesh["weights"] = [0] * len(names)
        mesh.setdefault("extras", {})["targetNames"] = names
    document["buffers"][0]["byteLength"] = len(binary)
    json_chunk = json.dumps(document, separators=(",", ":")).encode()
    json_chunk += b" " * (-len(json_chunk) % 4)
    binary += b"\0" * (-len(binary) % 4)
    total = 12 + 8 + len(json_chunk) + 8 + len(binary)
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_bytes(struct.pack("<4sII", b"glTF", 2, total) +
                       struct.pack("<I4s", len(json_chunk), b"JSON") + json_chunk +
                       struct.pack("<I4s", len(binary), b"BIN\0") + binary)
    print(f"Wrote {OUTPUT} ({len(binary)} byte buffer)")


if __name__ == "__main__":
    main()
