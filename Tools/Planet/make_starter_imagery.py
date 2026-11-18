"""Shrinks the planet's painted picture (Library/PlanetImagery/*-8192-*.jpg, made by a desktop run of Planet
Showcase) into Assets/Planet/earth_starter.jpg: what the globe wears at once while the sharp one is fetched.

    python Tools/Planet/make_starter_imagery.py [width]   (2048 by default)
"""
import glob
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def main():
    width = int(sys.argv[1]) if len(sys.argv) > 1 else 2048
    painted = [p for p in glob.glob(os.path.join(ROOT, "Library", "PlanetImagery", "*-8192-*.jpg")) if ".source." not in p]
    if not painted:
        print("No painted 8192 picture in Library/PlanetImagery: play Planet Showcase on desktop once first.")
        return 1
    source = max(painted, key=os.path.getmtime)
    picture = Image.open(source).convert("RGB").resize((width, width // 2), Image.LANCZOS)
    out = os.path.join(ROOT, "Assets", "Planet", "earth_starter.jpg")
    picture.save(out, quality=80, optimize=True, progressive=False)
    print(f"{os.path.relpath(out, ROOT)}: {width} x {width // 2}, {os.path.getsize(out) // 1024} KB, from {os.path.basename(source)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
