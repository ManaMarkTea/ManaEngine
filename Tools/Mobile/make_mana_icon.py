"""Mana's iOS icon: ManaLidar's wireframe cube, filled with a rainbow so the two apps tell apart on the home screen.
Run from the repo root: python Tools/Mobile/make_mana_icon.py"""
import colorsys, math
from PIL import Image, ImageDraw

src = Image.open("ManaLidar/Resources/Assets.xcassets/AppIcon.appiconset/icon-1024.png").convert("RGB")
w, h = src.size
top = [(512, 173), (874, 383), (512, 595), (150, 383)]
left = [(150, 383), (512, 595), (512, 905), (150, 723)]
right = [(512, 595), (874, 383), (874, 723), (512, 905)]
# Each face a different light, so it still reads as a solid cube.
faces = [(top, 1.0), (left, 0.78), (right, 0.6)]
masks = []
for poly, shade in faces:
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).polygon(poly, fill=255)
    masks.append((m.load(), shade))

px, out = src.load(), Image.new("RGB", (w, h))
o = out.load()
for y in range(h):
    for x in range(w):
        r, g, b = px[x, y]
        face = next(((s) for m, s in masks if m[x, y]), None)
        # The cyan lines (the only bright green in the picture) turn white, inside the cube and on its outline.
        line = max(0.0, min(1.0, (g - 60) / 180))
        if face is None:
            cr, cg, cb = r / 255, g / 255, b / 255
        else:
            # A conic rainbow round the cube's centre.
            hue = (math.atan2(y - 540, x - 512) / (2 * math.pi) + 0.25) % 1.0
            cr, cg, cb = colorsys.hsv_to_rgb(hue, 0.72, 0.95 * face)
        o[x, y] = tuple(int(255 * (c * (1 - line) + 0.97 * line)) for c in (cr, cg, cb))
out.save("ManaVoxel.iOS/Resources/Assets.xcassets/AppIcon.appiconset/icon-1024.png")
