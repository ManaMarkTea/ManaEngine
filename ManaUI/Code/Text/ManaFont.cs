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
        private readonly float _scale;

        private GCHandle _dataPin;
        private int _atlasGeneration;
        private bool _disposed;

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

        private ManaGlyph Rasterize(int codepoint)
        {
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
                return ManaGlyph.Blank(advancePixels);
            }

            if (!_atlas.TryPack(width, height, out var atlasX, out var atlasY))
            {
                return ManaGlyph.Blank(advancePixels);
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
                hasPixels: true);
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
