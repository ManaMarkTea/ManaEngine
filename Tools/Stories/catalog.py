"""What a story can use, as compact text for the story generator (generate.py) -- or a person -- to read.

    python Tools/Stories/catalog.py            # print it
    python Tools/Stories/catalog.py --stats    # how big it is

Levels (where their camera sits, what is in them and where), every model the project can place with its size in metres
(from Assets/Catalog.md), the characters' animation clips, prefabs, music, sounds and fonts.
"""
import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import story as story_tool  # noqa: E402

ROOT = story_tool.ROOT
# Packs of 2D pictures, cursors and UI have no models worth placing in a scene.
SKIPPED_PACKS = ("cursor-pack", "input-prompts", "ui-pack", "new-platformer-pack", "hexagon-pack")
CHARACTER_PACKS = ("kenney_blocky-characters_20", "kenney_mini-characters")
LEVEL_FOLDERS_SKIPPED = ("Tests", "Downloaded")


def levels():
    lines = []
    for path in sorted((ROOT / "Content" / "Levels").rglob("*.manalevel")):
        relative = path.relative_to(ROOT).as_posix()
        if any(part in LEVEL_FOLDERS_SKIPPED for part in path.relative_to(ROOT / "Content" / "Levels").parts[:-1]):
            continue
        try:
            level = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        nodes = level.get("nodes", [])
        positions = [node.get("position") for node in nodes if story_tool.is_point(node.get("position"))]
        line = f"- `{relative}` ({level.get('name', path.stem)})"
        camera = level.get("camera")
        if camera:
            line += f"; its camera at {fmt(camera.get('position'))}"
        if positions:
            low = [min(p[i] for p in positions) for i in range(3)]
            high = [max(p[i] for p in positions) for i in range(3)]
            line += f"; things placed between {fmt(low)} and {fmt(high)}"
        sky = level.get("sky")
        if sky and sky.get("mode"):
            line += f"; sky {sky['mode']}"
            if sky.get("dateTime"):
                line += f" at {sky['dateTime'][11:16]}"
        named = [f"{node.get('name')} {fmt(node.get('position'))}" for node in nodes
                 if node.get("name") and story_tool.is_point(node.get("position"))][:14]
        if named:
            line += ". Notable: " + "; ".join(named)
        lines.append(line)
    return lines


def fmt(point):
    if not point:
        return "?"
    return "[" + ", ".join(f"{value:g}" for value in point) + "]"


def models():
    """Per pack: its path pattern and each model's W x H x D (and offset when off centre), from Assets/Catalog.md."""
    text = (ROOT / "Assets" / "Catalog.md").read_text(encoding="utf-8")
    sections = re.split(r"^## ", text, flags=re.MULTILINE)
    lines = []
    for section in sections:
        heading, _, body = section.partition("\n")
        pack = heading.strip()
        if not pack.startswith("kenney_") or any(skip in pack for skip in SKIPPED_PACKS):
            continue
        pattern = re.search(r"placed as `([^`]+)`", body)
        if not pattern:
            continue
        rows = re.findall(r"^\| `([^`]+)` \| ([\d.]+) \| ([\d.]+) \| ([\d.]+) \| ([^|]*)\|", body, flags=re.MULTILINE)
        if not rows:
            continue
        entries = []
        for name, width, height, depth, offset in rows:
            entry = f"{name} {width}x{height}x{depth}"
            if offset.strip():
                entry += f" (at {offset.strip()})"
            entries.append(entry)
        lines.append(f"### {pack}\nPath: `{pattern.group(1)}`. Models (W x H x D metres):\n" + "; ".join(entries))
    return lines


def characters():
    lines = []
    for pack in CHARACTER_PACKS:
        zip_path = ROOT / "Assets" / f"{pack}.zip"
        if not zip_path.exists():
            continue
        glbs = sorted(entry for entry in story_tool.zip_entries(str(zip_path)) if entry.endswith(".glb"))
        clips = ()
        for entry in glbs:
            clips = story_tool.model_clips(f"zip:{pack}.zip/{entry}")
            if clips:
                break
        names = ", ".join(Path(entry).stem for entry in glbs)
        lines.append(f"- `zip:{pack}.zip/{Path(glbs[0]).parent.as_posix()}/<name>.glb` -- {names}. "
                     f"Clips: {', '.join(clips) or 'none'}")
    return lines


def prefabs():
    found = []
    for path in sorted((ROOT / "Content" / "Prefabs").rglob("*.manaprefab")):
        relative = path.relative_to(ROOT / "Content").as_posix()
        if "2D" in path.stem or path.stem == "New Prefab":
            continue
        found.append(f"`{relative}`")
    return found


def audio():
    music = sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / "Assets" / "Audio" / "Music").glob("*.wav"))
    sounds = sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / "Assets" / "Audio").rglob("*.wav")
                    if "Music" not in p.parts)
    return music, sounds


def fonts():
    return sorted(p.relative_to(ROOT).as_posix() for p in (ROOT / "Assets" / "Fonts").rglob("*.ttf"))


def build():
    music, sounds = audio()
    parts = [
        "# What a story can use",
        "World axes: +Y is up; the ground of most levels is y = 0. Levels sit at x and z of 32 and above. Kenney models "
        "face +Z, so yaw 0 faces a camera placed at larger z looking toward smaller z; yaw 90 faces +X, 180 faces -Z, "
        "-90 faces -X.",
        "## Levels (level paths)",
        *levels(),
        "## Characters (they animate; place with scale ~0.65 for a 1.75 m person -- they are 2.7 m tall as authored)",
        *characters(),
        "## Prefabs (asset is the path under Content/)",
        ", ".join(prefabs()),
        "## Models by pack (asset is the pack's path with <model> filled in; scale to taste, sizes are as authored)",
        *models(),
        "## Music (story music.path)",
        *[f"- `{path}`" for path in music],
        "## Sounds (shot sounds[].path)",
        ", ".join(f"`{path}`" for path in sounds),
        "## Fonts (story font / titleFont)",
        ", ".join(f"`{path}`" for path in fonts()),
    ]
    return "\n\n".join(parts)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--stats", action="store_true")
    options = parser.parse_args()
    text = build()
    if options.stats:
        print(f"{len(text):,} characters, roughly {len(text) // 4:,} tokens")
        return
    print(text)


if __name__ == "__main__":
    main()
