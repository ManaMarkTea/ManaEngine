"""Writes Assets/Fonts/Kenney/KenneyFuture.ttf and KenneyFutureNarrow.ttf from Kenney's UI Pack (CC0).

The pack's fonts have no kerning table and set every advance well past the ink (the narrow cut's A is 512 units wide
in an 844 advance), so letters sit far apart. This keeps the outlines and sets each advance to the glyph's own width
plus a fixed gap. Run from the repo root: python Tools/Fonts/tighten_kenney.py   (needs fonttools)
"""
import io
import zipfile
from pathlib import Path

from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont

PACK = Path("Assets/kenney_ui-pack.zip")
OUT = Path("Assets/Fonts/Kenney")

# Units are 1/1024 em; the fonts' stems are 128 wide. The wide cut keeps more air than the narrow one.
FONTS = {"Kenney Future.ttf": ("KenneyFuture.ttf", 170), "Kenney Future Narrow.ttf": ("KenneyFutureNarrow.ttf", 130)}

with zipfile.ZipFile(PACK) as pack:
    for source, (target, gap) in FONTS.items():
        font = TTFont(io.BytesIO(pack.read("Font/" + source)))
        glyphs = font.getGlyphSet()
        metrics = font["hmtx"].metrics
        for name, (advance, side) in list(metrics.items()):
            pen = BoundsPen(glyphs)
            glyphs[name].draw(pen)
            if pen.bounds is None:
                continue  # a space or other blank: its advance is the whole point
            left, _, right, _ = pen.bounds
            metrics[name] = (int(right - left + gap), side)
        font.save(OUT / target)
        print(f"{target}: gap {gap}")
