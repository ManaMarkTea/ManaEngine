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

        /// <summary>
        /// True when the font has no glyph for this codepoint and what rasterised is the .notdef box.
        /// <para>
        /// Distinct from <see cref="HasPixels"/>, and the distinction matters: .notdef is a hollow
        /// rectangle, so it has pixels like any other glyph. Every icon in the library had a "fall back to
        /// a drawn shape if the font lacks this character" path keyed on HasPixels, and not one of them
        /// ever fired -- the tofu box sailed through as a real glyph.
        /// </para>
        /// </summary>
        public readonly bool IsMissing;

        public ManaGlyph(
            Vector4 atlasUv,
            Vector2 offset,
            Vector2 size,
            float advance,
            bool hasPixels,
            bool isMissing = false)
        {
            AtlasUv = atlasUv;
            Offset = offset;
            Size = size;
            Advance = advance;
            HasPixels = hasPixels;
            IsMissing = isMissing;
        }

        /// <summary>True when there is a real glyph with something to draw.</summary>
        public bool IsDrawable => HasPixels && !IsMissing;

        public static ManaGlyph Blank(float advance, bool isMissing = false)
        {
            return new ManaGlyph(Vector4.Zero, Vector2.Zero, Vector2.Zero, advance, false, isMissing);
        }
    }
}
