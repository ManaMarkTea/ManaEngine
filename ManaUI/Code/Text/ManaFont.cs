using System.Runtime.InteropServices;
using StbTrueTypeSharp;

namespace ManaUI
{
    /// <summary>
    /// One typeface at one pixel size, rasterising glyphs into the shared atlas on first use.
    /// <para>
    /// Lazy rasterisation rather than a prebuilt range: an editor draws a few hundred distinct characters,
    /// so baking all of Latin-1 up front would waste most of the atlas and all of the startup time it costs.
    /// </para>
    /// This is bitmap rasterisation, not SDF -- at UI sizes, hinted-position bitmaps read noticeably crisper
    /// than a distance field, and the cost is one atlas per size rather than one shared across all of them.
    /// </summary>
    public sealed unsafe class ManaFont : IDisposable
    {
        private readonly StbTrueType.stbtt_fontinfo _info;
        private readonly ManaGlyphAtlas _atlas;
        private readonly Dictionary<int, ManaGlyph> _glyphs = new();
        private readonly Dictionary<int, ManaGlyph> _colorGlyphs = new();
        private readonly float _scale;

        // What the font's tables say never changes, but asking is expensive -- kerning reads the GPOS table and
        // a glyph lookup walks the cmap -- and text layout asks for both per character, every frame. Caching the
        // answers took a large slice out of the time the editor spends drawing its own UI.
        private readonly Dictionary<long, int> _kerningUnits = new();
        private readonly Dictionary<int, bool> _hasGlyph = new();

        private readonly List<ManaFont> _fallbacks = new();

        private GCHandle _dataPin;
        private int _atlasGeneration;
        private int _imageAtlasGeneration = -1;
        private bool _disposed;

        /// <summary>
        /// The colour atlas colour glyphs are packed into. Optional, because a font without CBDT tables
        /// never needs one and the layout engine is exercised headlessly without any atlas at all.
        /// </summary>
        public ManaImageAtlas? ImageAtlas { get; set; }

        /// <summary>Embedded colour bitmaps in this font's own file. Null for every ordinary font.</summary>
        public ManaColorBitmapTable? ColorBitmaps { get; private set; }

        /// <summary>
        /// A separate colour emoji font to fall back on. Distinct from <see cref="Fallbacks"/>, which holds
        /// ManaFonts -- an emoji font cannot be one, having no outlines for stb to initialise from.
        /// </summary>
        public IManaColorGlyphSource? ColorEmoji { get; set; }

        /// <summary>True when this font can supply colour glyphs, from its own tables or the emoji font.</summary>
        public bool HasColorGlyphs => ColorBitmaps != null || ColorEmoji != null;

        public ManaFont(string name, byte[] fontData, float pixelSize, ManaGlyphAtlas atlas, float renderScale = 1.0f)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(fontData);
            ArgumentNullException.ThrowIfNull(atlas);

            Name = name;
            PixelSize = pixelSize;
            RenderScale = MathF.Max(0.1f, renderScale);
            _atlas = atlas;
            _atlasGeneration = atlas.Generation;

            // stb keeps the pointer for the font's lifetime, so the managed array has to stay put.
            _dataPin = GCHandle.Alloc(fontData, GCHandleType.Pinned);
            _info = new StbTrueType.stbtt_fontinfo();

            var dataPointer = (byte*)_dataPin.AddrOfPinnedObject();
            var offset = StbTrueType.stbtt_GetFontOffsetForIndex(dataPointer, 0);
            if (offset < 0 || StbTrueType.stbtt_InitFont(_info, dataPointer, offset) == 0)
            {
                _dataPin.Free();
                throw new InvalidOperationException($"'{name}' is not a font stb_truetype can read.");
            }

            // Only emoji fonts answer this; for everything else it is a table-directory scan and a null.
            ColorBitmaps = ManaColorBitmapTable.TryRead(fontData);

            // Glyphs are rasterised at the physical size so a HiDPI display gets genuinely sharper text
            // rather than a magnified bitmap...
            _scale = StbTrueType.stbtt_ScaleForPixelHeight(_info, pixelSize * RenderScale);

            int ascent;
            int descent;
            int lineGap;
            StbTrueType.stbtt_GetFontVMetrics(_info, &ascent, &descent, &lineGap);

            // ...but every metric this font reports is divided back down, so layout stays in logical units
            // and nothing above this class has to know the difference. stb also reports descent as
            // negative-up, which is flipped to the UI's positive-down convention here.
            Ascent = ascent * _scale / RenderScale;
            Descent = -descent * _scale / RenderScale;
            LineGap = lineGap * _scale / RenderScale;
        }

        public string Name { get; }

        /// <summary>Logical size this font was requested at.</summary>
        public float PixelSize { get; }

        /// <summary>Logical-to-physical ratio the glyph bitmaps were baked at.</summary>
        public float RenderScale { get; }

        /// <summary>Distance from the baseline up to the top of the tallest glyph.</summary>
        public float Ascent { get; }

        /// <summary>Distance from the baseline down to the bottom of the lowest descender, positive.</summary>
        public float Descent { get; }

        public float LineGap { get; }

        /// <summary>Baseline-to-baseline distance, rounded up so successive lines land on whole pixels.</summary>
        public float LineHeight => SnapUp(Ascent + Descent + LineGap);

        /// <summary>The atlas every glyph of this font lives in.</summary>
        public ManaGlyphAtlas Atlas => _atlas;

        /// <summary>
        /// Rounds a logical coordinate to the nearest whole *physical* pixel. Glyph bitmaps are sharp only
        /// when they land on the device grid, which at scale 2 is every half logical pixel -- rounding to
        /// whole logical pixels instead would visibly soften text on a HiDPI display.
        /// </summary>
        public float SnapToPixel(float logical)
        {
            return MathF.Round(logical * RenderScale) / RenderScale;
        }

        private float SnapUp(float logical)
        {
            return MathF.Ceiling(logical * RenderScale) / RenderScale;
        }

        public ManaGlyph GetGlyph(int codepoint)
        {
            return GetGlyph(codepoint, preferColor: false);
        }

        /// <summary>
        /// The glyph for a codepoint, from this font, its fallbacks, or the colour emoji source.
        /// <para>
        /// The order matters more than it looks. Colour was tried first at one point, and an emoji font
        /// claims a great deal it has no business drawing in a UI: Noto Color Emoji maps the ASCII digits
        /// and the asterisk and hash -- they are the bases of the keycap sequences -- so every number in
        /// the interface turned into a coloured tile, and U+25B6 turned a tree's collapsed arrow into a
        /// play button. The text font gets first refusal now.
        /// </para>
        /// <para>
        /// <paramref name="preferColor"/> is how a caller says the colour form was explicitly asked for:
        /// a trailing U+FE0F, which is exactly what distinguishes a red heart emoji from the black
        /// dingbat sharing its codepoint.
        /// </para>
        /// </summary>
        public ManaGlyph GetGlyph(int codepoint, bool preferColor)
        {
            // A reset atlas invalidates every cached UV, so the whole cache goes with it.
            if (_atlasGeneration != _atlas.Generation)
            {
                _glyphs.Clear();
                _colorGlyphs.Clear();
                _atlasGeneration = _atlas.Generation;
            }

            // Colour glyph UVs live in the image atlas, which mesh and texture thumbnails can reset
            // independently from the text glyph atlas. Ordinary glyphs stay valid; only colour glyphs need
            // to be decoded and packed again.
            var imageAtlasGeneration = ImageAtlas?.Generation ?? -1;
            if (_imageAtlasGeneration != imageAtlasGeneration)
            {
                _colorGlyphs.Clear();
                _imageAtlasGeneration = imageAtlasGeneration;
            }

            // Invisible by definition, and no font is obliged to have them -- so left to the normal path
            // they rasterise as .notdef and put a tofu box on screen. That is what turned a pasted heart
            // emoji into a heart followed by a box: U+2764 then U+FE0F, and the selector is not a
            // character anyone meant to see.
            if (IsDefaultIgnorable(codepoint))
            {
                var ignorable = ManaGlyph.Blank(0.0f);
                _glyphs[codepoint] = ignorable;
                return ignorable;
            }

            // Colour is the right answer when it was asked for, when the codepoint lives in a plane that
            // is emoji and nothing else, or when no text font in the chain can draw it at all.
            if (preferColor || IsEmojiPresentation(codepoint) || !CanRender(codepoint))
            {
                if (_colorGlyphs.TryGetValue(codepoint, out var cachedColor))
                {
                    return cachedColor;
                }

                if (TryRasterizeColor(codepoint, out var colorGlyph))
                {
                    _colorGlyphs[codepoint] = colorGlyph;
                    return colorGlyph;
                }
            }

            if (_glyphs.TryGetValue(codepoint, out var cached))
            {
                return cached;
            }

            // A codepoint this font lacks is rasterised by whichever fallback has it, into the same atlas
            // at the same size -- so the caller gets a real glyph rather than a tofu box and never has to
            // know which file it came from.
            if (!HasGlyph(codepoint))
            {
                foreach (var fallback in _fallbacks)
                {
                    if (!fallback.HasGlyph(codepoint))
                    {
                        continue;
                    }

                    fallback.ImageAtlas ??= ImageAtlas;
                    fallback.ColorEmoji ??= ColorEmoji;
                    var borrowed = fallback.GetGlyph(codepoint);
                    _glyphs[codepoint] = borrowed;
                    return borrowed;
                }
            }

            var glyph = Rasterize(codepoint);
            _glyphs[codepoint] = glyph;
            return glyph;
        }

        /// <summary>
        /// Whether the codepoint starting at <paramref name="index"/> is U+FE0F, the selector that asks
        /// for the emoji form of whatever precedes it. Windows inserts one after every emoji its picker
        /// produces, which is how a pasted heart arrives as two codepoints.
        /// </summary>
        public static bool HasEmojiSelector(ReadOnlySpan<char> text, int index)
        {
            return index < text.Length && text[index] == '️';
        }

        /// <summary>
        /// Codepoints that are emoji and nothing else, so the colour form is always the intended one.
        /// <para>
        /// Deliberately only the supplementary planes. The BMP is full of characters that are emoji in one
        /// context and typography in another -- U+2764 is both a red heart and a dingbat, U+25B6 both a
        /// play button and a triangle a tree view wants -- and guessing wrong there is what produced the
        /// coloured digits. Those need a variation selector to ask for colour, and get the text form
        /// otherwise.
        /// </para>
        /// </summary>
        public static bool IsEmojiPresentation(int codepoint)
        {
            return codepoint >= 0x1F000 && codepoint <= 0x1FAFF;
        }

        public float GetKerning(int left, int right)
        {
            var key = ((long)left << 32) | (uint)right;
            if (!_kerningUnits.TryGetValue(key, out var units))
            {
                units = StbTrueType.stbtt_GetCodepointKernAdvance(_info, left, right);
                _kerningUnits[key] = units;
            }

            return units * _scale / RenderScale;
        }

        /// <summary>Advance width of a string on a single line, kerning included.</summary>
        public float MeasureWidth(ReadOnlySpan<char> text)
        {
            var width = 0.0f;
            var previous = 0;

            var enumerator = new CodepointEnumerator(text);
            while (enumerator.MoveNext())
            {
                var codepoint = enumerator.Current;

                if (previous != 0)
                {
                    width += GetKerning(previous, codepoint);
                }

                // Measured with the same colour decision the renderer will make, or an emoji would be
                // laid out at the text form's advance and then drawn at the emoji's.
                width += GetGlyph(codepoint, HasEmojiSelector(text, enumerator.Position)).Advance;
                previous = codepoint;
            }

            return width;
        }

        /// <summary>
        /// Codepoints that carry meaning but no ink: variation selectors, joiners, directional marks.
        /// <para>
        /// Unicode calls these Default_Ignorable_Code_Point and says a renderer should draw nothing for
        /// them rather than fall back to a missing-glyph box. Emoji are full of them -- the difference
        /// between an emoji and its plain text form is a trailing U+FE0F, and every multi-part emoji is
        /// held together with U+200D.
        /// </para>
        /// <para>
        /// This is the list, not a full Unicode property table: the ranges below are the ones that turn up
        /// in text a UI actually receives.
        /// </para>
        /// </summary>
        public static bool IsDefaultIgnorable(int codepoint)
        {
            return codepoint switch
            {
                0x00AD => true,                             // soft hyphen
                0x034F => true,                             // combining grapheme joiner
                0x061C => true,                             // Arabic letter mark
                >= 0x115F and <= 0x1160 => true,            // Hangul fillers
                0x17B4 or 0x17B5 => true,                   // Khmer inherent vowels
                >= 0x180B and <= 0x180F => true,            // Mongolian selectors and separator
                >= 0x200B and <= 0x200F => true,            // zero-width space, joiners, bidi marks
                >= 0x202A and <= 0x202E => true,            // bidi embedding and override
                >= 0x2060 and <= 0x206F => true,            // word joiner, invisible operators, deprecated
                0x3164 => true,                             // Hangul filler
                >= 0xFE00 and <= 0xFE0F => true,            // variation selectors 1-16
                0xFEFF => true,                             // zero-width no-break space
                0xFFA0 => true,                             // halfwidth Hangul filler
                >= 0xFFF0 and <= 0xFFF8 => true,            // unassigned, specified ignorable
                >= 0x1BCA0 and <= 0x1BCA3 => true,          // shorthand format controls
                >= 0x1D173 and <= 0x1D17A => true,          // musical format controls
                >= 0xE0000 and <= 0xE0FFF => true,          // tags and variation selectors supplement
                _ => false
            };
        }

        /// <summary>
        /// Whether the font actually contains this codepoint. Glyph index 0 is .notdef -- the tofu box --
        /// which every other part of the pipeline treats as an ordinary glyph, so this is the only place
        /// the difference can be seen.
        /// </summary>
        public bool HasGlyph(int codepoint)
        {
            if (!_hasGlyph.TryGetValue(codepoint, out var has))
            {
                has = StbTrueType.stbtt_FindGlyphIndex(_info, codepoint) != 0;
                _hasGlyph[codepoint] = has;
            }

            return has;
        }

        /// <summary>
        /// Fonts consulted, in order, for codepoints this one does not have. Same pixel size and same
        /// atlas, so a fallback glyph is indistinguishable downstream from a native one.
        /// </summary>
        public IReadOnlyList<ManaFont> Fallbacks => _fallbacks;

        /// <summary>
        /// Adds a fallback. Set up by <see cref="ManaFontManager"/> when the font is created, because the
        /// chain has to be complete before anything asks for a glyph -- a font that cached a .notdef for a
        /// codepoint would keep returning it after the fallback arrived.
        /// </summary>
        internal void AddFallback(ManaFont fallback)
        {
            if (fallback == null || ReferenceEquals(fallback, this) || _fallbacks.Contains(fallback))
            {
                return;
            }

            _fallbacks.Add(fallback);
            _glyphs.Clear();
        }

        /// <summary>This font or the first fallback that has the codepoint; null when nothing does.</summary>
        public ManaFont? FindFontFor(int codepoint)
        {
            if (HasGlyph(codepoint))
            {
                return this;
            }

            foreach (var fallback in _fallbacks)
            {
                if (fallback.HasGlyph(codepoint))
                {
                    return fallback;
                }
            }

            return null;
        }

        /// <summary>True when this font or any of its fallbacks can draw the codepoint.</summary>
        public bool CanRender(int codepoint)
        {
            return FindFontFor(codepoint) != null;
        }

        /// <summary>
        /// Draws a colour glyph -- from this font's own tables first, then the shared emoji source -- and packs
        /// it into the colour atlas.
        /// </summary>
        private bool TryRasterizeColor(int codepoint, out ManaGlyph glyph)
        {
            glyph = default;

            var imageAtlas = ImageAtlas;
            if (imageAtlas == null)
            {
                return false;
            }

            // Sized against the em, not the bitmap: emoji sit on the same line as the text around them.
            var target = PixelSize * RenderScale;
            var image = ColorBitmaps?.Draw(codepoint, target) ?? ColorEmoji?.Draw(codepoint, target);
            if (image == null)
            {
                return false;
            }

            var uv = imageAtlas.Add(image.Pixels, image.Width, image.Height);
            if (uv == null)
            {
                return false;
            }

            // BearingY is measured up from the baseline; the UI's Y grows downward, hence the negation.
            glyph = new ManaGlyph(
                uv.Value,
                new Vector2(image.BearingX / RenderScale, -image.BearingY / RenderScale),
                new Vector2(image.Width / RenderScale, image.Height / RenderScale),
                image.Advance / RenderScale,
                hasPixels: true,
                isMissing: false,
                isColor: true);

            return true;
        }

        private ManaGlyph Rasterize(int codepoint)
        {
            // Whitespace has no glyph index either in some fonts, but it also rasterises empty, so the
            // flag only ends up mattering for characters that would otherwise draw a box.
            var missing = !HasGlyph(codepoint);

            int advance;
            int leftSideBearing;
            StbTrueType.stbtt_GetCodepointHMetrics(_info, codepoint, &advance, &leftSideBearing);
            var advancePixels = advance * _scale / RenderScale;

            int x0;
            int y0;
            int x1;
            int y1;
            StbTrueType.stbtt_GetCodepointBitmapBox(_info, codepoint, _scale, _scale, &x0, &y0, &x1, &y1);

            var width = x1 - x0;
            var height = y1 - y0;

            // Whitespace and unmapped codepoints rasterise empty; they still advance the pen.
            if (width <= 0 || height <= 0)
            {
                return ManaGlyph.Blank(advancePixels, missing);
            }

            if (!_atlas.TryPack(width, height, out var atlasX, out var atlasY))
            {
                return ManaGlyph.Blank(advancePixels, missing);
            }

            var bitmap = new byte[width * height];
            fixed (byte* pixels = bitmap)
            {
                StbTrueType.stbtt_MakeCodepointBitmap(_info, pixels, width, height, width, _scale, _scale, codepoint);
            }

            _atlas.Blit(bitmap, width, atlasX, atlasY, width, height);

            // The bitmap is physical-sized; the quad it fills is logical-sized. Sampling one from the other
            // is exactly what makes the glyph render at full device resolution.
            return new ManaGlyph(
                _atlas.ToUv(atlasX, atlasY, width, height),
                new Vector2(x0 / RenderScale, y0 / RenderScale),
                new Vector2(width / RenderScale, height / RenderScale),
                advancePixels,
                hasPixels: true,
                isMissing: missing);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _glyphs.Clear();

            if (_dataPin.IsAllocated)
            {
                _dataPin.Free();
            }
        }
    }

    /// <summary>
    /// Walks a UTF-16 span as Unicode codepoints, combining surrogate pairs. A struct enumerator so text
    /// measurement -- which runs on every layout pass -- allocates nothing.
    /// </summary>
    public ref struct CodepointEnumerator
    {
        private readonly ReadOnlySpan<char> _text;
        private int _index;

        public CodepointEnumerator(ReadOnlySpan<char> text)
        {
            _text = text;
            _index = 0;
            Current = 0;
        }

        public int Current { get; private set; }

        /// <summary>Index just past the character(s) that produced <see cref="Current"/>.</summary>
        public int Position => _index;

        public readonly CodepointEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_index >= _text.Length)
            {
                return false;
            }

            var high = _text[_index];
            if (char.IsHighSurrogate(high) && _index + 1 < _text.Length && char.IsLowSurrogate(_text[_index + 1]))
            {
                Current = char.ConvertToUtf32(high, _text[_index + 1]);
                _index += 2;
                return true;
            }

            Current = high;
            _index++;
            return true;
        }
    }
}
