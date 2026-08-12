namespace ManaUI
{
    /// <summary>
    /// A rasterised glyph, positioned relative to the pen origin on the baseline.
    /// </summary>
    public readonly struct ManaGlyph
    {
        /// <summary>Normalised u0, v0, u1, v1 into the shared glyph atlas.</summary>
        public readonly Vector4 AtlasUv;

        /// <summary>Pen-origin to quad top-left, in pixels. Y is negative for the part above the baseline.</summary>
        public readonly Vector2 Offset;

        /// <summary>Quad size in pixels.</summary>
        public readonly Vector2 Size;

        /// <summary>How far the pen moves after this glyph.</summary>
        public readonly float Advance;

        /// <summary>False for whitespace and anything that rasterised empty -- skip the quad entirely.</summary>
        public readonly bool HasPixels;

        public ManaGlyph(Vector4 atlasUv, Vector2 offset, Vector2 size, float advance, bool hasPixels)
        {
            AtlasUv = atlasUv;
            Offset = offset;
            Size = size;
            Advance = advance;
            HasPixels = hasPixels;
        }

        public static ManaGlyph Blank(float advance)
        {
            return new ManaGlyph(Vector4.Zero, Vector2.Zero, Vector2.Zero, advance, false);
        }
    }
}
