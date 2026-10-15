"""Writes Assets/Catalog.md: every model the project can place, with its size, so anyone (or any agent) laying out a
level or a prefab knows what is there without opening each pack. Run after adding or changing a pack:

    python Tools/Assets/catalog_assets.py            # the catalog
    python Tools/Assets/catalog_assets.py --sheets   # and a contact sheet of each pack's previews, in Library/AssetPreviews/

Sizes come from the glTF accessors' bounds through each node's transform, in the model's own metres (Y up), before any
scale a level gives it -- as the engine places it: a lone root node becomes the placed node, so its own transform is
dropped (ManaSharpGltfLoader). A model whose origin is not centred on its footprint and on the ground gets its ranges,
since that is where it will sit when placed. ContentAssetChecks fails when a pack in Assets/ is missing from the catalog.
"""
import io
import json
import math
import struct
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets" / "Catalog.md"
SHEETS = ROOT / "Library" / "AssetPreviews"
LOOSE = ROOT / "Mana" / "Assets" / "Models"
CONTENT = ROOT / "Content"


def quat_matrix(x, y, z, w):
    return [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]


def node_matrix(node):
    """A node's local transform as a 3x4 (rotation-scale, translation)."""
    if "matrix" in node:
        m = node["matrix"]
        return [[m[0], m[4], m[8], m[12]], [m[1], m[5], m[9], m[13]], [m[2], m[6], m[10], m[14]]]
    r = quat_matrix(*node.get("rotation", [0, 0, 0, 1]))
    s = node.get("scale", [1, 1, 1])
    t = node.get("translation", [0, 0, 0])
    return [[r[i][0] * s[0], r[i][1] * s[1], r[i][2] * s[2], t[i]] for i in range(3)]


def compose(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) + (a[i][3] if j == 3 else 0) for j in range(4)] for i in range(3)]


def apply(m, p):
    return [m[i][0] * p[0] + m[i][1] * p[1] + m[i][2] * p[2] + m[i][3] for i in range(3)]


def gltf_json(data, name):
    if name.lower().endswith(".glb"):
        length = struct.unpack_from("<I", data, 12)[0]
        return json.loads(data[20:20 + length])
    return json.loads(data)


def describe(doc):
    """(min, max) over every mesh in the default scene, and the animation names."""
    lo = [math.inf] * 3
    hi = [-math.inf] * 3
    identity = [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0]]
    nodes = doc.get("nodes", [])

    def walk(index, parent, depth=0, placed=False):
        if depth > 64:
            return
        node = nodes[index]
        # A lone root node becomes the placed node itself, whose transform the level sets: its own is dropped.
        m = parent if placed else compose(parent, node_matrix(node))
        if "mesh" in node:
            for prim in doc["meshes"][node["mesh"]].get("primitives", []):
                accessor = doc["accessors"][prim["attributes"]["POSITION"]]
                if "min" not in accessor:
                    continue
                a, b = accessor["min"], accessor["max"]
                for corner in ((x, y, z) for x in (a[0], b[0]) for y in (a[1], b[1]) for z in (a[2], b[2])):
                    p = apply(m, corner)
                    for i in range(3):
                        lo[i] = min(lo[i], p[i])
                        hi[i] = max(hi[i], p[i])
        for child in node.get("children", []):
            walk(child, m, depth + 1)

    scenes = doc.get("scenes") or [{"nodes": list(range(len(nodes)))}]
    roots = scenes[doc.get("scene", 0)].get("nodes", [])
    for root in roots:
        walk(root, identity, placed=len(roots) == 1)
    animations = [a.get("name") or f"#{i}" for i, a in enumerate(doc.get("animations", []))]
    if lo[0] == math.inf:
        return None, animations
    return (lo, hi), animations


def fmt(v):
    return f"{v:.2f}".rstrip("0").rstrip(".") if abs(v) >= 0.005 else "0"


MODEL_TABLE = ["| Model | W | H | D | Sits at (when off centre) | Clips |", "|---|---:|---:|---:|---|---|"]


def model_row(name, bounds, animations):
    """One table row: size in metres (X, Y, Z), where it sits about its placed point if that is not centred on the ground."""
    if bounds is None:
        cells = ["", "", "", "no mesh bounds"]
    else:
        lo, hi = bounds
        cells = [fmt(hi[i] - lo[i]) for i in range(3)]
        off = [abs(lo[i] + hi[i]) / 2 > 0.05 * max(1.0, hi[i] - lo[i]) for i in (0, 2)] + [abs(lo[1]) > 0.02]
        cells.append(f"x {fmt(lo[0])}..{fmt(hi[0])}, y {fmt(lo[1])}..{fmt(hi[1])}, z {fmt(lo[2])}..{fmt(hi[2])}" if any(off) else "")
    clips = ", ".join(animations[:14]) + (f", +{len(animations) - 14} more" if len(animations) > 14 else "")
    return f"| `{name}` | " + " | ".join(cells) + f" | {clips} |"


def pack_section(zip_path, sheets):
    """The pack's section, and its counts for the index: (lines, models, pictures)."""
    lines = []
    with zipfile.ZipFile(zip_path) as z:
        names = z.namelist()
        # Kenney packs ship each model in several formats; GLB is what the engine loads, so read those (else glTF).
        models = sorted(n for n in names if n.lower().endswith(".glb"))
        if not models:
            models = sorted(n for n in names if n.lower().endswith(".gltf"))
        pictures = [n for n in names if n.lower().endswith(".png") and "/Textures/" not in n and "preview" not in n.lower()]
        lines.append(f"## {zip_path.stem}")
        lines.append("")
        if models:
            folders = sorted({n.rsplit("/", 1)[0] for n in models})
            lines.append(f"{len(models)} models, placed as `zip:{zip_path.name}/{folders[0]}/<model>{Path(models[0]).suffix}`"
                         + (" (folders: " + ", ".join(f"`{f}`" for f in folders) + ")" if len(folders) > 1 else "") + ".")
            lines.append("")
            lines += MODEL_TABLE
            for name in models:
                try:
                    bounds, animations = describe(gltf_json(z.read(name), name))
                except Exception as error:  # a broken model is listed, not fatal
                    bounds, animations = None, [f"unreadable: {error}"]
                label = name.rsplit("/", 1)[-1].rsplit(".", 1)[0]
                if len(folders) > 1:
                    label = name.split("/", 1)[-1].rsplit(".", 1)[0]
                lines.append(model_row(label, bounds, animations))
        if pictures:
            by_folder = {}
            for n in pictures:
                folder = n.rsplit("/", 1)[0] if "/" in n else "."
                by_folder.setdefault(folder, []).append(n.rsplit("/", 1)[-1][:-4])
            if models:
                lines.append("")
            lines.append(f"{len(pictures)} picture{'' if len(pictures) == 1 else 's'}, as `zip:{zip_path.name}/<folder>/<picture>.png`.")
            lines.append("")
            lines += ["| Folder | Pictures | Examples |", "|---|---:|---|"]
            for folder, files in sorted(by_folder.items()):
                sample = ", ".join(sorted(files)[:12]) + (", ..." if len(files) > 12 else "")
                lines.append(f"| `{folder}/` | {len(files)} | {sample} |")
        if sheets:
            write_sheet(z, names, zip_path.stem)
    lines.append("")
    return lines, len(models), len(pictures)


def write_sheet(z, names, stem):
    """A grid of the pack's own preview pictures, labelled, to see what each model looks like."""
    try:
        from PIL import Image, ImageDraw
    except ImportError:
        print("  (no Pillow: skipping contact sheets)")
        return
    previews = sorted(n for n in names if "Previews" in n.split("/")[:-1] and n.lower().endswith(".png"))
    if not previews:
        return
    cell, cols = 128, 10
    rows = (len(previews) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 14)), "white")
    draw = ImageDraw.Draw(sheet)
    for i, name in enumerate(previews):
        image = Image.open(io.BytesIO(z.read(name))).convert("RGBA")
        image.thumbnail((cell, cell))
        tile = Image.new("RGBA", (cell, cell), (236, 236, 236, 255))
        tile.alpha_composite(image, ((cell - image.width) // 2, (cell - image.height) // 2))
        x, y = (i % cols) * cell, (i // cols) * (cell + 14)
        sheet.paste(tile.convert("RGB"), (x, y))
        draw.text((x + 2, y + cell + 1), name.rsplit("/", 1)[-1][:-4][:22], fill="black")
    SHEETS.mkdir(parents=True, exist_ok=True)
    sheet.save(SHEETS / f"{stem}.png")


def loose_section():
    lines = ["## Loose models", "", "In `Mana/Assets/Models`, copied to the app: placed as `Assets/Models/<path>`.", ""] + MODEL_TABLE
    for path in sorted(LOOSE.rglob("*")):
        if path.suffix.lower() not in (".glb", ".gltf"):
            continue
        # A sample model in several encodings: the binary one is enough.
        if path.suffix.lower() == ".gltf" and any(path.parent.parent.glob("glTF-Binary/*.glb")):
            continue
        if "Draco" in path.parts[-2] or "SpecularGlossiness" in path.parts[-2]:
            continue
        try:
            bounds, animations = describe(gltf_json(path.read_bytes(), path.name))
        except Exception as error:
            bounds, animations = None, [f"unreadable: {error}"]
        lines.append(model_row(path.relative_to(LOOSE).as_posix(), bounds, animations))
    lines.append("")
    return lines


def content_section():
    lines = ["## The project's own assets", "", "In `Content/`, referenced by their path under it.", "",
             "| Kind | Asset |", "|---|---|"]
    kinds = (("Prefab", "*.manaprefab"), ("Sprite sheet", "*.manasheet"), ("9-slice", "*.manaslice"),
             ("Grid map preset", "*.managridpreset"), ("Generator rules", "*.manawfc"), ("Level", "*.manalevel"))
    for title, pattern in kinds:
        for f in sorted(CONTENT.rglob(pattern)):
            lines.append(f"| {title} | `{f.relative_to(CONTENT).as_posix()}` |")
    lines.append("")
    return lines


def main():
    sheets = "--sheets" in sys.argv
    zips = sorted((ROOT / "Assets").glob("*.zip"), key=lambda p: p.name.lower())
    out = ["# Asset catalog", "",
           "Generated by `python Tools/Assets/catalog_assets.py` -- do not edit by hand; run it again after adding a pack",
           "(`--sheets` also draws each pack's previews into `Library/AssetPreviews/<pack>.png`, to look at).",
           "Sizes are width x height x depth (X, Y, Z) in the model's own metres, before any scale it is placed at, and ranges",
           "are where it sits about the point it is placed at (a model's lone root node's own offset is dropped when placed).",
           ""]
    sections, index = [], []
    for path in zips:
        print("reading", path.name)
        lines, models, pictures = pack_section(path, sheets)
        sections += lines
        def count(n, word):
            return f"{n} {word}{'' if n == 1 else 's'}" if n else ""
        counts = ", ".join(c for c in (count(models, "model"), count(pictures, "picture")) if c)
        index.append(f"- [{path.stem}](#{path.stem.lower().replace('.', '')}) -- {counts or 'nothing to place'}")
    out += ["## Packs", ""] + index + ["- [Loose models](#loose-models)", "- [The project's own assets](#the-projects-own-assets)", ""]
    out += sections
    out += loose_section()
    out += content_section()
    OUT.write_text("\n".join(out).rstrip() + "\n", encoding="utf-8", newline="\n")
    print(f"wrote {OUT.relative_to(ROOT)} ({len(zips)} packs)")


if __name__ == "__main__":
    main()
