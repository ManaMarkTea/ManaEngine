"""Draws the bow and arrow sprites in Content/Sprites/Items/Bows in Kenney's flat, dark-outlined style.
Run with Pillow available: python Tools/Sprites/make_bows.py"""
import math, os
from PIL import Image, ImageDraw

S = 8                      # supersampling
OUT = (53, 53, 65, 255)    # Kenney's outline
STRING = (236, 236, 226, 255)
STRING_EDGE = (110, 110, 122, 255)
GRIP = (120, 78, 60, 255)
OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "..", "Content", "Sprites", "Items", "Bows")

# name: (limb, limb highlight, arrow shaft, arrowhead, fletching)
THEMES = {
    "Wooden":  ((218, 164, 98, 255), (240, 200, 140, 255), (214, 164, 98, 255), (196, 202, 214, 255), (226, 84, 70, 255)),
    "Golden":  ((250, 186, 45, 255), (255, 224, 120, 255), (250, 186, 45, 255), (248, 250, 252, 255), (240, 122, 40, 255)),
    "Crystal": ((92, 178, 232, 255), (170, 224, 248, 255), (170, 224, 248, 255), (125, 220, 187, 255), (93, 212, 170, 255)),
}


def px(v):
    return [(x * S, y * S) for x, y in v]


def stroke(draw, pts, width, fill):
    r = width * S / 2
    p = [(x * S, y * S) for x, y in pts]
    draw.line(p, fill=fill, width=int(width * S), joint="curve")
    for x, y in p:
        draw.ellipse([x - r, y - r, x + r, y + r], fill=fill)


def canvas(w, h):
    im = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
    return im, ImageDraw.Draw(im)


def finish(im, w, h, name):
    im.resize((w, h), Image.LANCZOS).save(os.path.join(OUT_DIR, name))


def arrow_shapes(d, x0, x1, y, theme, outline=True):
    """An arrow lying along y from the nock x0 to the tip x1, drawn onto d (supersampled)."""
    _, _, shaft, head, fletch = theme
    head_len, head_h = 9, 4.6
    shaft_end = x1 - head_len + 2
    # fletching: two feathers swept back from the shaft
    for sign in (-1, 1):
        feather = [(x0, y), (x0 + 3, y + sign * 4.2), (x0 + 9, y + sign * 4.2), (x0 + 7, y)]
        d.polygon(px(feather), fill=OUT)
        stroke(d, feather + [feather[0]], 1.6, OUT)
    for sign in (-1, 1):
        feather = [(x0 + 0.8, y), (x0 + 3.2, y + sign * 3.0), (x0 + 7.4, y + sign * 3.0), (x0 + 6.0, y)]
        d.polygon(px(feather), fill=fletch)
    stroke(d, [(x0, y), (shaft_end, y)], 4.2, OUT)
    stroke(d, [(x0 + 1, y), (shaft_end, y)], 2.2, shaft)
    tip = [(shaft_end - 1, y - head_h), (x1, y), (shaft_end - 1, y + head_h)]
    d.polygon(px(tip), fill=OUT)
    stroke(d, tip + [tip[0]], 2.2, OUT)
    inner = [(shaft_end, y - head_h + 1.6), (x1 - 2.6, y), (shaft_end, y + head_h - 1.6)]
    d.polygon(px(inner), fill=head)


def bow_frame(theme, pull, name):
    limb, light, *_ = theme
    w = h = 64
    im, d = canvas(w, h)
    gx, gy = 32, 32
    # limbs: a shallow arc, belly toward the target (right), tips drawn back toward the archer
    arc = []
    for i in range(-20, 21):
        t = i / 20
        arc.append((gx - 13 * t * t + 1, gy + t * 26))
    tips = (arc[0], arc[-1])
    # string first, behind the limbs
    nock = (gx - pull, gy)
    string = [tips[0], nock, tips[1]] if pull else [tips[0], tips[1]]
    stroke(d, string, 2.6, STRING_EDGE)
    stroke(d, string, 1.2, STRING)
    if pull >= 17:
        arrow_shapes(d, nock[0] - 2, 62, gy, theme)
    stroke(d, arc, 7.4, OUT)
    stroke(d, arc, 4.2, limb)
    stroke(d, [(x + 0.6, y) for x, y in arc[3:-3]], 1.2, light)
    # grip wrap
    stroke(d, [(gx + 1, gy - 5), (gx + 1, gy + 5)], 7.4, OUT)
    stroke(d, [(gx + 1, gy - 4), (gx + 1, gy + 4)], 4.4, GRIP)
    finish(im, w, h, name)


def arrow_picture(theme, name):
    w, h = 64, 16
    im, d = canvas(w, h)
    arrow_shapes(d, 3, 62, 8, theme)
    finish(im, w, h, name)


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    for label, theme in THEMES.items():
        slug = label.lower()
        for pull, tag in ((0, "idle"), (17, "draw"), (26, "full")):
            bow_frame(theme, pull, f"{slug}_bow_{tag}.png")
        arrow_picture(theme, f"{slug}_arrow.png")
    print("wrote", len(THEMES) * 4, "pictures to", os.path.normpath(OUT_DIR))
