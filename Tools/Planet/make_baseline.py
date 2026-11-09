"""Writes Assets/Planet/baseline.tif: a coarse whole-globe height grid made from Natural Earth's land and lakes.

It stands in for the 466 MB ETOPO file the planet used to start from. Land is a few hundred metres up, sea a few
thousand down, lakes just below sea level, with the coast blended over a cell -- enough for the planet's first
colours and ocean mask. Real relief arrives as the planet streams Terrarium tiles at higher zooms.

    python Tools/Planet/make_baseline.py [cells-per-degree, default 4]

Needs numpy, Pillow and pyshp (pip install pyshp). Natural Earth is public domain.
"""
import io
import struct
import sys
import zipfile
import zlib
from pathlib import Path

import numpy as np
import shapefile
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
SUPER = 4  # draw at 4x and average down, so a coast cell is a fraction rather than all or nothing


def read_shapes(name):
    path = ROOT / "Assets" / "GeoData" / name
    with zipfile.ZipFile(path) as z:
        parts = {Path(n).suffix: z.read(n) for n in z.namelist() if Path(n).suffix in (".shp", ".dbf", ".shx")}
    return shapefile.Reader(shp=io.BytesIO(parts[".shp"]), dbf=io.BytesIO(parts[".dbf"]), shx=io.BytesIO(parts[".shx"])).shapes()


def mask_of(shapes, width, height):
    image = Image.new("L", (width * SUPER, height * SUPER), 0)
    draw = ImageDraw.Draw(image)
    sx = width * SUPER / 360.0
    sy = height * SUPER / 180.0
    for shape in shapes:
        points = shape.points
        starts = list(shape.parts) + [len(points)]
        for i in range(len(shape.parts)):
            ring = [((x + 180.0) * sx, (90.0 - y) * sy) for x, y in points[starts[i]:starts[i + 1]]]
            if len(ring) < 3:
                continue
            # Outer rings run clockwise in shapefiles (y up), holes anticlockwise.
            area = sum(ring[k][0] * ring[(k + 1) % len(ring)][1] - ring[(k + 1) % len(ring)][0] * ring[k][1] for k in range(len(ring)))
            draw.polygon(ring, fill=255 if area > 0 else 0)
    small = image.resize((width, height), Image.BOX)
    return np.asarray(small, dtype=np.float32) / 255.0


def write_geotiff(path, heights, per_degree):
    """Tiled, deflated 32-bit float: the layout ManaGeoTools' reader takes signed heights in."""
    height, width = heights.shape
    tile = 256
    strips = []
    for top in range(0, height, tile):
        for left in range(0, width, tile):
            block = np.zeros((tile, tile), dtype="<f4")
            part = heights[top:top + tile, left:left + tile]
            block[:part.shape[0], :part.shape[1]] = part
            strips.append(zlib.compress(block.tobytes(), 9))
    entries = []  # (tag, type, count, value-or-bytes)
    data = bytearray()

    def short(tag, value):
        entries.append((tag, 3, 1, struct.pack("<HH", value, 0)))

    def long(tag, value):
        entries.append((tag, 4, 1, struct.pack("<I", value)))

    def blob(tag, kind, count, payload):
        entries.append((tag, kind, count, payload))

    header = 8
    offsets, counts, cursor = [], [], header
    for strip in strips:
        offsets.append(cursor)
        counts.append(len(strip))
        cursor += len(strip)
    pixel_data = b"".join(strips)

    long(256, width)
    long(257, height)
    short(258, 32)
    short(259, 8)
    short(262, 1)
    short(277, 1)
    short(284, 1)
    long(322, tile)
    long(323, tile)
    blob(324, 4, len(offsets), struct.pack(f"<{len(offsets)}I", *offsets))
    blob(325, 4, len(counts), struct.pack(f"<{len(counts)}I", *counts))
    short(339, 3)
    blob(33550, 12, 3, struct.pack("<3d", 1.0 / per_degree, 1.0 / per_degree, 0.0))
    blob(33922, 12, 6, struct.pack("<6d", 0, 0, 0, -180.0, 90.0, 0.0))
    blob(34735, 3, 12, struct.pack("<12H", 1, 1, 0, 2, 1024, 0, 1, 2, 2048, 0, 1, 4326))
    entries.sort(key=lambda e: e[0])

    ifd_at = cursor
    extra_at = ifd_at + 2 + 12 * len(entries) + 4
    extra = bytearray()
    ifd = bytearray(struct.pack("<H", len(entries)))
    for tag, kind, count, value in entries:
        if len(value) <= 4:
            ifd += struct.pack("<HHI", tag, kind, count) + value.ljust(4, b"\0")
        else:
            ifd += struct.pack("<HHII", tag, kind, count, extra_at + len(extra))
            extra += value
    ifd += struct.pack("<I", 0)
    Path(path).write_bytes(b"II*\0" + struct.pack("<I", ifd_at) + pixel_data + bytes(ifd) + bytes(extra))


def main():
    per_degree = int(sys.argv[1]) if len(sys.argv) > 1 else 4
    width, height = 360 * per_degree, 180 * per_degree
    land = mask_of(read_shapes("ne_10m_land.zip"), width, height)
    lakes = mask_of(read_shapes("ne_10m_lakes.zip"), width, height)
    blended = np.where(land >= 0.5, 400.0 * (2 * land - 1), 4000.0 * (2 * land - 1))
    # Lakes sit just under sea level, so the planet's ocean fills them like any water.
    heights = np.where(lakes >= 0.5, np.minimum(blended, -30.0), blended)
    out = ROOT / "Assets" / "Planet" / "baseline.tif"
    write_geotiff(out, np.round(heights).astype(np.float32), per_degree)
    print(f"{out.relative_to(ROOT)}: {width}x{height}, {out.stat().st_size // 1024} KB, land {np.mean(land >= 0.5):.1%}")


main()
