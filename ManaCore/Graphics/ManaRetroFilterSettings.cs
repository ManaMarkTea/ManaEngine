namespace ManaCore.Graphics
{
    /// <summary>
    /// The retro screen filter (the RetroFilter script, retro_filter.wgsl): the finished picture cut into big pixels,
    /// its colours cut to a few levels with an ordered (Bayer) dither, then shown as an old CRT or a VHS tape would show
    /// it -- scanlines, colour fringes, grain, a rolling tracking band and darkened corners. Every strength is 0 to 1
    /// (0 leaves that part out) unless it says otherwise.
    /// </summary>
    public sealed record ManaRetroFilterSettings
    {
        /// <summary>The picture's height in big pixels: 240 is a 1990s console on a TV, 480 finer, 120 very chunky.</summary>
        public int Lines { get; init; } = 240;

        /// <summary>Levels each colour channel keeps (2 to 256): 6 to 8 dithers visibly, 32 and up hardly bands at all.</summary>
        public int ColorLevels { get; init; } = 10;

        /// <summary>How strongly the cut colours are dithered: 0 bands, 1 is the full checkered pattern.</summary>
        public float Dither { get; init; } = 1.0f;

        /// <summary>Dark lines between rows of big pixels.</summary>
        public float Scanlines { get; init; } = 0.3f;

        /// <summary>Red and blue pulled apart sideways, in big pixels: the fringes on a worn tape.</summary>
        public float ChromaticAberration { get; init; } = 0.6f;

        /// <summary>Flickering grain.</summary>
        public float Noise { get; init; } = 0.2f;

        /// <summary>VHS wobble: lines jittering sideways, a tracking band rolling down the screen, a torn strip at the bottom.</summary>
        public float Glitch { get; init; } = 0.3f;

        /// <summary>Darkened corners.</summary>
        public float Vignette { get; init; } = 0.35f;

        /// <summary>The bulge of a CRT's glass: 0 flat, 1 a deep curve with the corners rounded off.</summary>
        public float Curvature { get; init; }

        /// <summary>Colour: 0 grey, 1 as drawn, above 1 more vivid.</summary>
        public float Saturation { get; init; } = 0.85f;

        /// <summary>Screen pixels across one big pixel for a window <paramref name="height"/> tall: whole, so every block is alike.</summary>
        public int PixelSize(uint height)
        {
            var lines = Math.Max(Lines, 16);
            return Math.Max(1, (int)MathF.Round(height / (float)lines, MidpointRounding.AwayFromZero));
        }
    }
}
