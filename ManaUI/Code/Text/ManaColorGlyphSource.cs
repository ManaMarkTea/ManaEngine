namespace ManaUI
{
    /// <summary>
    /// One colour glyph drawn at the size asked for: RGBA pixels plus where they sit, all in output pixels.
    /// </summary>
    public sealed class ManaColorGlyphImage
    {
        public ManaColorGlyphImage(byte[] pixels, int width, int height, float bearingX, float bearingY, float advance)
        {
            Pixels = pixels;
            Width = width;
            Height = height;
            BearingX = bearingX;
            BearingY = bearingY;
            Advance = advance;
        }

        /// <summary>Straight (not premultiplied) RGBA, rows top to bottom.</summary>
        public byte[] Pixels { get; }
        public int Width { get; }
        public int Height { get; }

        /// <summary>Pen origin to the image's left edge.</summary>
        public float BearingX { get; }

        /// <summary>Baseline to the image's top edge, positive upwards.</summary>
        public float BearingY { get; }

        public float Advance { get; }
    }

    /// <summary>
    /// Somewhere colour emoji come from: a CBDT font (<see cref="ManaColorBitmapTable"/>) on desktop, the
    /// browser's own emoji font on the web, where shipping a 10 MB font would be most of the download.
    /// </summary>
    public interface IManaColorGlyphSource
    {
        /// <summary>The glyph drawn <paramref name="pixelsPerEm"/> pixels to the em, or null when there is none.</summary>
        ManaColorGlyphImage? Draw(int codepoint, float pixelsPerEm);
    }
}
