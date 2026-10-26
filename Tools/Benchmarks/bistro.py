"""Fetches NVIDIA ORCA's Amazon Lumberyard Bistro (CC-BY 4.0) and turns it into levels Mana opens, for benchmarks.

Everything lands in Library/Benchmarks/Bistro (gitignored, ~3 GB): the zip, the FBX source, FBX2glTF, the converted
glTF, and the .manalevel files. Each step is skipped when its output is already there.

    python Tools/Benchmarks/bistro.py            # exterior and interior
    python Tools/Benchmarks/bistro.py --force    # redo textures, materials and levels (not the download)

The FBX is converted with Godot's FBX2glTF; its materials are then rebuilt from the pack's texture naming, since FBX
carries none of the PBR layout: <name>_BaseColor (alpha = opacity), _Specular (R occlusion, G roughness, B metal -- glTF's
ORM), _Normal (DirectX, so green is flipped for glTF) and _Emissive. The glTF names the pack's DDS files where they
lie (Mana uploads BC blocks and mips as stored); only the BC5 normal maps are rewritten, flipped inside their blocks.
"""
import argparse
import json
import multiprocessing
import os
import shutil
import subprocess
import sys
import urllib.request
import uuid
import zipfile

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ROOT = os.path.join(REPO, "Library", "Benchmarks", "Bistro")
ZIP_URL = "https://developer.nvidia.com/bistro"
FBX2GLTF_URL = "https://github.com/godotengine/FBX2glTF/releases/download/v0.13.1/FBX2glTF-windows-x86_64.zip"
SOURCE = os.path.join(ROOT, "src", "Bistro_v5_2")
TEXTURES = os.path.join(SOURCE, "Textures")

# Where each scene sits in the level: clear of the editor room's zero-axis walls, and above its floor.
OFFSET = [100.0, 8.0, 100.0]
# The local axis of the FBX's light node the sun shines along. FBX2glTF leaves its -Z pointing at the sun: +Z puts it
# 28.8 degrees up, where the pack's HDR has its sun (28.6).
SUN_AXIS = [0.0, 0.0, 1.0]
SCENES = {
    "Exterior": "BistroExterior.fbx",
    "Interior": "BistroInterior.fbx",
}


def log(message):
    print(f"[bistro] {message}", flush=True)


def download(url, path):
    if os.path.exists(path):
        return
    log(f"downloading {url}")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with urllib.request.urlopen(url) as response, open(path + ".part", "wb") as out:
        shutil.copyfileobj(response, out, 1 << 20)
    os.replace(path + ".part", path)


REFERENCE_URL = "https://d29g4g2dyqv443.cloudfront.net/sites/default/files/"
REFERENCES = ["akamai/Bistro_Exterior_1.png", "akamai/Bistro_Exterior_2.png", "Bistro_Night.png",
              "akamai/Bistro_Interior_1.png", "akamai/Bistro_Interior_2.png", "akamai/Bistro_Interior_3.png"]


def fetch_source():
    archive = os.path.join(ROOT, "Bistro_v5_2.zip")
    download(ZIP_URL, archive)
    # The ORCA page's own renders, to set ours beside (2017 Lumberyard ones: not the FBX's light or the HDR sky).
    for name in REFERENCES:
        download(REFERENCE_URL + name, os.path.join(ROOT, "reference", os.path.basename(name)))
    if not os.path.exists(os.path.join(SOURCE, "BistroExterior.fbx")):
        log("unpacking")
        with zipfile.ZipFile(archive) as zf:
            zf.extractall(os.path.join(ROOT, "src"))


def fbx2gltf():
    folder = os.path.join(ROOT, "..", "ToolsDl", "FBX2glTF")
    exe = os.path.join(folder, "FBX2glTF-windows-x86_64", "FBX2glTF-windows-x86_64.exe")
    if not os.path.exists(exe):
        archive = os.path.join(folder, "FBX2glTF.zip")
        download(FBX2GLTF_URL, archive)
        with zipfile.ZipFile(archive) as zf:
            zf.extractall(folder)
    return exe


def convert(scene, fbx, exe):
    out_dir = os.path.join(ROOT, "gltf", f"Bistro{scene}")
    raw = os.path.join(out_dir, f"Bistro{scene}_out", f"Bistro{scene}.gltf")
    if not os.path.exists(raw):
        log(f"converting {fbx} (a few minutes)")
        os.makedirs(out_dir, exist_ok=True)
        subprocess.run([exe, "--input", os.path.join(SOURCE, fbx), "--output", os.path.join(out_dir, f"Bistro{scene}"),
                        "--pbr-metallic-roughness", "--long-indices", "auto",
                        "-k", "position", "-k", "normal", "-k", "uv0"], check=True, stdout=subprocess.DEVNULL)
    return raw


def flip_bc5_green(data, offset):
    """DirectX to glTF normals inside BC5 blocks: G becomes 255 - G without re-encoding. Each green (BC4) block gets
    its endpoints swapped and inverted, and its 3-bit indices remapped to the mirrored palette entries."""
    import numpy as np
    blocks = np.frombuffer(data, dtype=np.uint8, offset=offset).reshape(-1, 16).copy()
    green = blocks[:, 8:16]
    e0 = green[:, 0].copy()
    e1 = green[:, 1].copy()
    bits = np.zeros(len(green), dtype=np.uint64)
    for i in range(6):
        bits |= green[:, 2 + i].astype(np.uint64) << np.uint64(8 * i)
    indices = np.stack([(bits >> np.uint64(3 * i)) & np.uint64(7) for i in range(16)], axis=1).astype(np.uint8)
    eight = np.array([1, 0, 7, 6, 5, 4, 3, 2], dtype=np.uint8)  # e0 > e1: eight interpolated values
    six = np.array([1, 0, 5, 4, 3, 2, 7, 6], dtype=np.uint8)    # otherwise six, then 0 and 255
    indices = np.where((e0 > e1)[:, None], eight[indices], six[indices])
    bits = np.zeros(len(green), dtype=np.uint64)
    for i in range(16):
        bits |= indices[:, i].astype(np.uint64) << np.uint64(3 * i)
    green[:, 0] = 255 - e1
    green[:, 1] = 255 - e0
    for i in range(6):
        green[:, 2 + i] = ((bits >> np.uint64(8 * i)) & np.uint64(255)).astype(np.uint8)
    blocks[:, 8:16] = green
    return bytes(data[:offset]) + blocks.tobytes()


def texture_job(job):
    """One texture's glTF URI (relative to the glTF folder) and whether its alpha is real. DDS is referenced where it
    lies; only normal maps are rewritten (green flipped), and anything else (TGA) becomes a PNG."""
    from PIL import Image
    source, kind, out_dir, gltf_dir = job
    stem, extension = os.path.splitext(os.path.basename(source))
    has_alpha = False
    if kind == "BaseColor":
        image = Image.open(source)
        image.load()
        has_alpha = image.mode == "RGBA" and image.getchannel("A").getextrema()[0] < 250

    if extension.lower() == ".dds":
        target = source
        data = open(source, "rb").read()
        if kind == "Normal" and data[84:88] in (b"ATI2", b"BC5U"):
            target = os.path.join(out_dir, stem + ".dds")
            if not os.path.exists(target):
                with open(target + ".part", "wb") as out:
                    out.write(flip_bc5_green(data, 128))
                os.replace(target + ".part", target)
        elif kind == "Normal":
            raise ValueError(f"{source}: normal map is not BC5; the green flip only handles BC5")
        return source, os.path.relpath(target, gltf_dir).replace("\\", "/"), has_alpha

    image = Image.open(source)
    image.load()
    if kind == "Normal":
        r, g, b = image.convert("RGB").split()
        image = Image.merge("RGB", (r, g.point(lambda v: 255 - v), b))
    target = os.path.join(out_dir, stem + ".png")
    if not os.path.exists(target):
        image.save(target + ".part", "PNG", compress_level=3)
        os.replace(target + ".part", target)
    return source, os.path.relpath(target, gltf_dir).replace("\\", "/"), has_alpha


def find_texture(prefixes, kind):
    for prefix in prefixes:
        for extension in (".dds", ".tga", ".TGA"):
            path = os.path.join(TEXTURES, f"{prefix}_{kind}{extension}")
            if os.path.exists(path):
                return path
    return None


def rebuild_materials(scene, raw):
    """Writes Bistro<scene>.gltf beside the raw one: materials from the texture naming, no animation or camera."""
    folder = os.path.dirname(os.path.dirname(raw))
    target = os.path.join(folder, f"Bistro{scene}.gltf")
    gltf = json.load(open(raw, encoding="utf-8"))
    out_textures = os.path.join(ROOT, "gltf", "Textures")
    os.makedirs(out_textures, exist_ok=True)

    old_images = [image.get("uri", "") for image in gltf.get("images", [])]
    plans = []
    for material in gltf["materials"]:
        name = material.get("name", "")
        base = name.removesuffix(".DoubleSided")
        # FBX2glTF keeps the file names the FBX pointed at; their prefix is the truest name for the texture set.
        prefixes = []
        for slot in ("baseColorTexture",):
            index = material.get("pbrMetallicRoughness", {}).get(slot, {}).get("index")
            if index is not None:
                prefixes.append(os.path.splitext(old_images[gltf["textures"][index]["source"]])[0].rsplit("_", 1)[0])
        index = material.get("normalTexture", {}).get("index")
        if index is not None:
            prefixes.append(os.path.splitext(old_images[gltf["textures"][index]["source"]])[0].rsplit("_", 1)[0])
        prefixes.append(base)
        found = {kind: find_texture(prefixes, kind) for kind in ("BaseColor", "Normal", "Specular", "Emissive")}
        plans.append((material, name, base, found))

    jobs = sorted({(path, kind, out_textures, folder) for _, _, _, found in plans for kind, path in found.items() if path})
    log(f"{scene}: preparing {len(jobs)} textures")
    with multiprocessing.Pool() as pool:
        results = {source: (written, alpha) for source, written, alpha in pool.map(texture_job, jobs, chunksize=2)}

    images, textures, lookup = [], [], {}

    def texture(path):
        if path not in lookup:
            images.append({"uri": results[path][0]})
            textures.append({"sampler": 0, "source": len(images) - 1})
            lookup[path] = len(textures) - 1
        return {"index": lookup[path]}

    missing = []
    for material, name, base, found in plans:
        rebuilt = {"name": base, "doubleSided": name.endswith(".DoubleSided")}
        pbr = {"baseColorFactor": [1, 1, 1, 1], "metallicFactor": 0.0, "roughnessFactor": 0.6}
        old_factor = material.get("pbrMetallicRoughness", {}).get("baseColorFactor")
        if found["BaseColor"]:
            pbr["baseColorTexture"] = texture(found["BaseColor"])
            if results[found["BaseColor"]][1]:
                # Glass reads through; everything else with holes (leaves, railings) is cut out.
                if "glass" in base.lower() or "window" in base.lower():
                    rebuilt["alphaMode"] = "BLEND"
                else:
                    rebuilt["alphaMode"] = "MASK"
                    rebuilt["alphaCutoff"] = 0.5
        else:
            missing.append(base)
            if old_factor:
                pbr["baseColorFactor"] = old_factor
        if found["Specular"]:
            pbr["metallicRoughnessTexture"] = texture(found["Specular"])
            pbr["metallicFactor"] = 1.0
            pbr["roughnessFactor"] = 1.0
            rebuilt["occlusionTexture"] = texture(found["Specular"])
        if found["Normal"]:
            rebuilt["normalTexture"] = texture(found["Normal"])
        if found["Emissive"]:
            rebuilt["emissiveTexture"] = texture(found["Emissive"])
            rebuilt["emissiveFactor"] = [1, 1, 1]
        rebuilt["pbrMetallicRoughness"] = pbr
        material.clear()
        material.update(rebuilt)

    gltf["images"] = images
    gltf["textures"] = textures
    gltf["samplers"] = [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}]
    for key in ("animations", "cameras"):
        gltf.pop(key, None)
    for node in gltf["nodes"]:
        node.pop("camera", None)
    gltf["buffers"][0]["uri"] = f"Bistro{scene}_out/" + gltf["buffers"][0]["uri"]
    with open(target, "w", encoding="utf-8") as out:
        json.dump(gltf, out)
    if missing:
        log(f"{scene}: {len(missing)} materials without a BaseColor texture: {', '.join(sorted(set(missing))[:12])}")
    log(f"{scene}: wrote {os.path.relpath(target, REPO)} ({len(gltf['materials'])} materials, {len(images)} images)")
    return target


def read_hdr(path):
    """A Radiance .hdr (new-style run-length RGBE) as linear float RGB, rows top first."""
    import numpy as np
    data = open(path, "rb").read()
    end = data.index(b"\n\n") + 2
    size_line_end = data.index(b"\n", end)
    parts = data[end:size_line_end].split()
    height, width = int(parts[1]), int(parts[3])
    position = size_line_end + 1
    rgbe = np.empty((height, width, 4), dtype=np.uint8)
    for row in range(height):
        if data[position] != 2 or data[position + 1] != 2:
            raise ValueError("only run-length encoded HDR files are read")
        position += 4
        for channel in range(4):
            line = bytearray()
            while len(line) < width:
                count = data[position]
                position += 1
                if count > 128:
                    line += bytes([data[position]]) * (count - 128)
                    position += 1
                else:
                    line += data[position:position + count]
                    position += count
            rgbe[row, :, channel] = np.frombuffer(bytes(line), dtype=np.uint8)
    exponent = np.where(rgbe[..., 3] > 0, np.ldexp(1.0, rgbe[..., 3].astype(np.int32) - 136), 0.0)
    return rgbe[..., :3].astype(np.float32) * exponent[..., None].astype(np.float32)


def write_sky_photo(sun_direction, force=False):
    """The pack's HDR sky as the 8-bit picture Mana's Photo sky reads: turned so its sun stands where the light comes
    from, exposed to match Mana's own sky photo, clipped, sRGB."""
    import math
    import numpy as np
    from PIL import Image
    target = os.path.join(ROOT, "gltf", "Textures", "san_giuseppe_bridge_4k.png")
    if os.path.exists(target) and not force:
        return target
    log("converting the HDR sky")
    linear = read_hdr(os.path.join(SOURCE, "san_giuseppe_bridge_4k.hdr"))
    luminance = linear @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    row, column = np.unravel_index(np.argmax(luminance), luminance.shape)
    log(f"HDR sun at elevation {90.0 - 180.0 * (row + 0.5) / luminance.shape[0]:.1f} deg")
    # Mana reads a panorama at u = atan2(z, x) / 2pi + 0.5 (ManaWebGpuEnvironmentReflectionMap.EquirectImage).
    toward_sun = [-v for v in sun_direction]
    wanted = math.atan2(toward_sun[2], toward_sun[0]) / (2 * math.pi) + 0.5
    shift = int(round(wanted * luminance.shape[1])) - int(column)
    linear = np.roll(linear, shift, axis=1)
    upper = luminance[: luminance.shape[0] // 2]
    # As bright as Mana's own sky photo (kloofendal: upper-half median 0.35 linear), which its lighting is tuned for.
    exposed = np.clip(linear * (0.35 / float(np.median(upper))), 0.0, 1.0)
    encoded = np.where(exposed <= 0.0031308, exposed * 12.92, 1.055 * np.power(exposed, 1 / 2.4) - 0.055)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    Image.fromarray((encoded * 255 + 0.5).astype(np.uint8), "RGB").save(target)
    return target


def authored_sun(raw):
    """The FBX's directional light: colour and the direction it shines, in glTF space."""
    import numpy as np
    gltf = json.load(open(raw, encoding="utf-8"))
    lights = gltf.get("extensions", {}).get("KHR_lights_punctual", {}).get("lights", [])
    for node in gltf["nodes"]:
        index = node.get("extensions", {}).get("KHR_lights_punctual", {}).get("light")
        if index is None or lights[index].get("type") != "directional":
            continue
        x, y, z, w = node.get("rotation", [0, 0, 0, 1])
        rotation = np.array([
            [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
        direction = rotation @ np.array(SUN_AXIS)
        return lights[index]["color"], [round(float(v), 4) for v in direction]
    return None


def authored_camera(raw):
    """The FBX's own camera (Falcor's start view) in glTF space, through every parent: eye and a point ahead."""
    import numpy as np
    gltf = json.load(open(raw, encoding="utf-8"))
    parents = {child: index for index, node in enumerate(gltf["nodes"]) for child in node.get("children", [])}
    cameras = [index for index, node in enumerate(gltf["nodes"]) if "camera" in node]
    if not cameras:
        return None
    matrix = np.eye(4)
    index = cameras[0]
    while index is not None:
        node = gltf["nodes"][index]
        x, y, z, w = node.get("rotation", [0, 0, 0, 1])
        local = np.eye(4)
        local[:3, :3] = np.array([
            [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]) * np.array(node.get("scale", [1, 1, 1]))
        local[:3, 3] = node.get("translation", [0, 0, 0])
        matrix = local @ matrix
        index = parents.get(index)
    forward = matrix[:3, :3] @ np.array([0.0, 0.0, -1.0])
    forward /= np.linalg.norm(forward)
    eye = matrix[:3, 3]
    return [float(v) for v in eye], [float(v) for v in eye + 10 * forward]


def write_level(scene, gltf_path, raw):
    path = os.path.join(ROOT, f"Bistro {scene}.manalevel")
    asset = os.path.relpath(gltf_path, REPO).replace("\\", "/")
    eye, look = authored_camera(raw) or ([0.0, 2.0, 10.0], [0.0, 1.5, 0.0])
    position = [eye[i] + OFFSET[i] for i in range(3)]
    log(f"{scene}: start view from {[round(eye[i] + OFFSET[i], 2) for i in range(3)]} "
        f"toward {[round(look[i] + OFFSET[i], 2) for i in range(3)]}")
    import math
    dx, dy, dz = (look[i] - eye[i] for i in range(3))
    pitch = math.atan2(dy, math.hypot(dx, dz))
    yaw = math.atan2(-dx, -dz)
    level = {
        "format": "mana-level",
        "version": 1,
        "name": f"Bistro {scene}",
        "camera": {"position": [round(v, 3) for v in position], "rotation": [round(pitch, 4), round(yaw, 4), 0]},
        "nodes": [{
            "id": uuid.uuid5(uuid.NAMESPACE_URL, f"mana-bistro-{scene}").hex,
            "name": f"Bistro {scene}",
            "kind": "Mesh",
            "position": OFFSET,
            "rotation": [0, 0, 0],
            "scale": [1, 1, 1],
            "asset": asset,
        }],
    }
    sun = authored_sun(raw)
    if sun:
        color, direction = sun
        # Falcor's own lighting: the pack's HDR as the sky, and its sun as the only light, as bright as Mana's own.
        level["sky"] = {"mode": "Photo", "photo": os.path.relpath(write_sky_photo(direction), REPO).replace("\\", "/")}
        level["nodes"].append({
            "id": uuid.uuid5(uuid.NAMESPACE_URL, f"mana-bistro-{scene}-sun").hex,
            "name": "Sun",
            "kind": "Group",
            "position": [OFFSET[0], OFFSET[1] + 40, OFFSET[2]],
            "rotation": [0, 0, 0],
            "scale": [1, 1, 1],
            "scripts": [{
                "id": uuid.uuid5(uuid.NAMESPACE_URL, f"mana-bistro-{scene}-sun-light").hex,
                "type": "Light",
                "properties": {
                    "color": [*color[:3], 1],
                    "intensity": round(1.6 / max(color[:3]), 3),
                    "lightType": "Directional",
                    "castsShadows": True,
                    "direction": direction,
                },
            }],
        })
        log(f"{scene}: sun shining along {direction}")
    with open(path, "w", encoding="utf-8") as out:
        json.dump(level, out, indent=2)
    log(f"{scene}: wrote {os.path.relpath(path, REPO)}")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--force", action="store_true", help="redo textures, materials and levels")
    parser.add_argument("--scene", choices=sorted(SCENES), action="append", help="only these scenes")
    args = parser.parse_args()
    fetch_source()
    exe = fbx2gltf()
    for scene in args.scene or SCENES:
        raw = convert(scene, SCENES[scene], exe)
        target = os.path.join(ROOT, "gltf", f"Bistro{scene}", f"Bistro{scene}.gltf")
        if args.force or not os.path.exists(target):
            target = rebuild_materials(scene, raw)
        write_level(scene, target, raw)


if __name__ == "__main__":
    multiprocessing.freeze_support()
    sys.exit(main())
