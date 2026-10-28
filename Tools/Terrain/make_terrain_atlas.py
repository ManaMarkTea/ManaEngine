"""Packs the four ground materials a terrain paints with into one 2 x 2 atlas each for colour and normals.

Layers in the terrain's order -- grass, dirt, rock, sand -- from Assets/Materials (CC0 Textures, see each zip's
LICENSE.txt). Each quarter holds its tileable picture shrunk to 448 px inside a 32 px border wrapped from its own
far edge, so bilinear filtering and the first few mips never reach a neighbour (pbr_irradiance.wgsl's terrain layers
read them so). Writes Assets/Textures/Terrain/terrain_albedo.jpg and terrain_normal.jpg.
"""
import io
import zipfile
from pathlib import Path

from PIL import Image, ImageStat

ROOT = Path(__file__).resolve().parents[2]
LAYERS = ["Ground/grass.zip", "Ground/soil.zip", "Stone/granite.zip", "Ground/sand.zip"]
# ManaTerrainNode.DefaultLayerColors: each picture's average is pulled most of the way to its layer's flat colour, so a
# textured terrain keeps the look it was painted with (the granite alone is twice as bright as a grey rock).
LAYER_COLORS = [(0.17, 0.30, 0.09), (0.34, 0.22, 0.12), (0.33, 0.32, 0.30), (0.66, 0.58, 0.40)]
MATCH = 0.8
QUARTER = 512
BORDER = 32
INNER = QUARTER - 2 * BORDER


def quarter(index, name):
    with zipfile.ZipFile(ROOT / "Assets/Materials" / LAYERS[index]) as archive:
        picture = Image.open(io.BytesIO(archive.read(f"material_{name}.png"))).convert("RGB")
    picture = picture.resize((INNER, INNER), Image.Resampling.LANCZOS)
    if name == "albedo":
        mean = [value / 255.0 for value in ImageStat.Stat(picture).mean]
        scale = [(target / max(have, 1e-3)) ** MATCH for target, have in zip(LAYER_COLORS[index], mean)]
        channels = [channel.point(lambda value, k=k: min(255, int(value * k + 0.5))) for channel, k in zip(picture.split(), scale)]
        picture = Image.merge("RGB", channels)
    # Tile it 3 x 3 and cut the middle out with a border: the border is the picture's own far edge, as wrapping is.
    tiled = Image.new("RGB", (INNER * 3, INNER * 3))
    for y in range(3):
        for x in range(3):
            tiled.paste(picture, (x * INNER, y * INNER))
    return tiled.crop((INNER - BORDER, INNER - BORDER, 2 * INNER + BORDER, 2 * INNER + BORDER))


def atlas(name):
    sheet = Image.new("RGB", (QUARTER * 2, QUARTER * 2))
    for index in range(len(LAYERS)):
        sheet.paste(quarter(index, name), ((index % 2) * QUARTER, (index // 2) * QUARTER))
    return sheet


def main():
    output = ROOT / "Assets/Textures/Terrain"
    output.mkdir(parents=True, exist_ok=True)
    atlas("albedo").save(output / "terrain_albedo.jpg", quality=90, subsampling=0)
    atlas("normal").save(output / "terrain_normal.jpg", quality=95, subsampling=0)
    print(f"Wrote {output}")


if __name__ == "__main__":
    main()
