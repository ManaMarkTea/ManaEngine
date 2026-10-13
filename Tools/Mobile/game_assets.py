"""Lists the files the mobile game carries: the engine's boot set (as the web build's) plus everything the bundled
levels and the prefabs they place refer to -- model packs, models, sounds, pictures. One repo-relative path a line,
"source|bundled path". Run from anywhere: python Tools/Mobile/game_assets.py [--sizes]"""
import json, os, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
# Where an "Assets/..." reference can live, as ManaLevelLoader.ResolveAssetPath searches.
ASSET_ROOTS = [ROOT, ROOT / "Mana"]
BOOT = [
    "Assets/Textures/DebugTexture_Grid.png", "Assets/Textures/Sky/moon_lroc_1k.jpg",
    "Assets/Fonts/Roboto/Roboto-Regular.ttf", "Assets/Fonts/Roboto/Roboto-Medium.ttf",
    "Assets/kenney_mini-characters.zip", "Assets/kenney_mini-dungeon.zip",
]
# The planet's height map is the editor's (466 MB); a level showing the planet comes up without it, as on the web.
EXCLUDED = {"Assets/Planet/ETOPO_2022_v1_60s_N90W180_surface.tif"}
CONTENT_KINDS = (".manalevel", ".manaprefab", ".managridpreset", ".manasheet", ".manaslice", ".manawfc", ".manaui")
REFERENCE = re.compile(r'"(?:zip:([^"/]+\.zip)/[^"]*|((?:Mana/)?Assets/[^";]+\.\w+))"|;((?:Mana/)?Assets/[^";]+\.\w+)')


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
    content = [p for p in sorted((ROOT / "Content").rglob("*")) if p.suffix in CONTENT_KINDS and "Tests" not in p.parts]
    for document in content:
        files["Content/" + document.relative_to(ROOT / "Content").as_posix()] = document
        text = document.read_text(encoding="utf-8-sig")
        for pack, path, listed in REFERENCE.findall(text):
            if pack:
                asset, source = find_pack(pack)
            else:
                asset = (path or listed).removeprefix("Mana/")
                source = find(asset)
            if asset not in files and asset not in EXCLUDED:
                files[asset] = source
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
