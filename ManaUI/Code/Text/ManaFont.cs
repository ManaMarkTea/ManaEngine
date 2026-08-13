using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Processing;
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
        private readonly float _scale;

        private readonly List<ManaFont> _fallbacks = new();

        private GCHandle _dataPin;
        private int _atlasGeneration;
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
        public ManaColorBitmapTable? ColorEmoji { get; set; }

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
            // A reset atlas invalidates every cached UV, so the whole cache goes with it.
            if (_atlasGeneration != _atlas.Generation)
            {
                _glyphs.Clear();
                _atlasGeneration = _atlas.Generation;
            }

            if (_glyphs.TryGetValue(codepoint, out var cached))
            {
                return cached;
            }

            if (TryRasterizeColor(codepoint, out var colorGlyph))
            {
                _glyphs[codepoint] = colorGlyph;
                return colorGlyph;
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

        public float GetKerning(int left, int right)
        {
            return StbTrueType.stbtt_GetCodepointKernAdvance(_info, left, right) * _scale / RenderScale;
        }

        /// <summary>Advance width of a string on a single line, kerning included.</summary>
        public float MeasureWidth(ReadOnlySpan<char> text)
        {
            var width = 0.0f;
            var previous = 0;

            foreach (var codepoint in new CodepointEnumerator(text))
            {
                if (previous != 0)
                {
                    width += GetKerning(previous, codepoint);
                }

                width += GetGlyph(codepoint).Advance;
                previous = codepoint;
            }

            return width;
        }

        /// <summary>
        /// Whether the font actually contains this codepoint. Glyph index 0 is .notdef -- the tofu box --
        /// which every other part of the pipeline treats as an ordinary glyph, so this is the only place
        /// the difference can be seen.
        /// </summary>
        public bool HasGlyph(int codepoint)
        {
            return StbTrueType.stbtt_FindGlyphIndex(_info, codepoint) != 0;
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
        /// Decodes the glyph's embedded PNG, scales it to this font's size and packs it into the colour
        /// atlas.
        /// <para>
        /// Scaled on the way in rather than at draw time. The strike is authored at 109 pixels and a UI
        /// asks for fifteen, so storing them native would fit about forty emoji in a 1024 atlas and waste
        /// most of what it did fit.
        /// </para>
        /// </summary>
        private bool TryRasterizeColor(int codepoint, out ManaGlyph glyph)
        {
            glyph = default;

            var imageAtlas = ImageAtlas;
            if (imageAtlas == null)
            {
                return false;
            }

            // This font's own tables first -- a font that has both outlines and bitmaps should use its
            // own -- then the shared emoji font.
            var table = ColorBitmaps;
            ManaColorBitmapEntry entry = default;
            var found = false;

            if (table != null
                && table.TryGetGlyphIndex(codepoint, out var ownIndex)
                && table.TryGetEntry(ownIndex, out entry))
            {
                found = true;
            }
            else if (ColorEmoji != null
                && ColorEmoji.TryGetGlyphIndex(codepoint, out var emojiIndex)
                && ColorEmoji.TryGetEntry(emojiIndex, out entry))
            {
                table = ColorEmoji;
                found = true;
            }

            if (!found || table == null)
            {
                return false;
            }

            // Sized against the em, not the bitmap: emoji are designed to sit on the same line as text, so
            // the target height comes from the font's own size rather than the strike's.
            var target = PixelSize * RenderScale;
            var scale = target / MathF.Max(1.0f, table.PixelsPerEm);
            var width = Math.Max(1, (int)MathF.Round(entry.Width * scale));
            var height = Math.Max(1, (int)MathF.Round(entry.Height * scale));

            Vector4? uv;

            try
            {
                using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                    table.GetPng(entry));

                image.Mutate(context => context.Resize(width, height));

                var pixels = new byte[width * height * 4];
                image.CopyPixelDataTo(pixels);
                uv = imageAtlas.Add(pixels, width, height);
            }
            catch (Exception)
            {
                // A PNG this decoder cannot read is one emoji missing, not a broken font.
                return false;
            }

            if (uv == null)
            {
                return false;
            }

            // BearingY is measured up from the baseline; the UI's Y grows downward, hence the negation.
            glyph = new ManaGlyph(
                uv.Value,
                new Vector2(entry.BearingX * scale / RenderScale, -entry.BearingY * scale / RenderScale),
                new Vector2(width / RenderScale, height / RenderScale),
                entry.Advance * scale / RenderScale,
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
