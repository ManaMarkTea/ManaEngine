"""Lists the files the mobile game carries: the engine's boot set (as the web build's) plus everything the bundled
levels and the prefabs they place refer to -- model packs, models, sounds, pictures. One repo-relative path a line,
"source|bundled path". Run from anywhere: python Tools/Mobile/game_assets.py [--sizes]"""
import json, os, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
# Where an "Assets/..." reference can live, as ManaLevelLoader.ResolveAssetPath searches.
ASSET_ROOTS = [ROOT]
BOOT = [
    "Assets/Textures/DebugTexture_Grid.png", "Assets/Textures/Sky/moon_lroc_1k.jpg",
    "Assets/Fonts/Roboto/Roboto-Regular.ttf", "Assets/Fonts/Roboto/Roboto-Medium.ttf",
    "Assets/kenney_mini-characters.zip", "Assets/kenney_mini-dungeon.zip",
    # The start screen's music (GameViewController.MenuMusic).
    "Assets/Audio/Music/Monkeys Spinning Monkeys.wav",
]
CONTENT_KINDS = (".manalevel", ".manaprefab", ".managridpreset", ".manasheet", ".manaslice", ".manawfc", ".manaui")
REFERENCE = re.compile(r'"(?:zip:([^"/]+\.zip)/[^"]*|((?:Mana/)?Assets/[^";]+\.\w+))"|;((?:Mana/)?Assets/[^";]+\.\w+)')
# Pictures and sounds kept in Content itself, named by a sheet or a level (the bows' frames).
CONTENT_FILE = re.compile(r'"(Content/[^"]+\.(?:png|jpe?g|wav|ogg|mp3|glb))"', re.IGNORECASE)


def find(asset):
    for root in ASSET_ROOTS:
        if (root / asset).is_file():
            return root / asset
    return None


def find_pack(name):
    """A zip: pack by its name -- material packs live in Materials/<kind>/, not beside the model packs."""
    if (ROOT / "Assets" / name).is_file():
        return f"Assets/{name}", ROOT / "Assets" / name
    for root in ASSET_ROOTS:
        for found in sorted((root / "Assets").rglob(name)):
            return "Assets/" + found.relative_to(root / "Assets").as_posix(), found
    return f"Assets/{name}", None


def main():
    files = {}
    for asset in BOOT:
        files[asset] = find(asset)
    for shader in sorted((ROOT / "ManaGraphics/Assets/Shaders/WebGpu").rglob("*.wgsl")):
        files["Assets/Shaders/WebGpu/" + shader.relative_to(ROOT / "ManaGraphics/Assets/Shaders/WebGpu").as_posix()] = shader
    # Content/ and each game's own (Games/<Name>/Content, its content mount), bundled where they sit in the repo.
    folders = [ROOT / "Content"] + sorted(p for p in (ROOT / "Games").glob("*/Content") if p.is_dir())
    content = [p for folder in folders for p in sorted(folder.rglob("*")) if p.suffix in CONTENT_KINDS and "Tests" not in p.parts]
    # The start screen's level pictures.
    for folder in folders:
        for picture in sorted((folder / "Levels/Pictures").glob("*.jpg")):
            files[picture.relative_to(ROOT).as_posix()] = picture
    for document in content:
        files[document.relative_to(ROOT).as_posix()] = document
        text = document.read_text(encoding="utf-8-sig")
        for pack, path, listed in REFERENCE.findall(text):
            if pack:
                asset, source = find_pack(pack)
            else:
                asset = (path or listed).removeprefix("Mana/")
                source = find(asset)
            if asset not in files:
                files[asset] = source
        for asset in CONTENT_FILE.findall(text):
            source = ROOT / asset
            files.setdefault(asset, source if source.is_file() else None)
    missing = sorted(a for a, f in files.items() if f is None)
    for asset in missing:
        print(f"missing: {asset}", file=sys.stderr)
    found = {a: f for a, f in files.items() if f is not None}
    if "--sizes" in sys.argv:
        for asset, source in sorted(found.items(), key=lambda kv: kv[1].stat().st_size)[-15:]:
            print(f"{source.stat().st_size / 1e6:7.1f} MB  {asset}")
        print(f"{len(found)} files, {sum(f.stat().st_size for f in found.values()) / 1e6:.1f} MB")
        return
    for asset, source in sorted(found.items()):
        print(f"{source}|Game/{asset}")


main()
