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

        /// <summary>
        /// Only the meshes its slot marks (<see cref="ManaPostEffects.Retro"/>) go through it, the rest of the picture shown
        /// as usual; screen-wide parts (curvature) are left out then. False filters the whole screen.
        /// </summary>
        public bool Scoped { get; init; }

        /// <summary>The looks the <c>retro</c> script command names, Default first.</summary>
        public static readonly string[] PresetNames = { "Default", "VHS", "Console", "CRT" };

        /// <summary>
        /// A named look: Default (as a new RetroFilter script), VHS (a worn tape: fringes, wobble, grain), Console (a
        /// clean 1990s console picture, chunky and dithered) or CRT (a TV's curved glass and strong scanlines). Null for
        /// any other name.
        /// </summary>
        public static ManaRetroFilterSettings? Preset(string? name)
        {
            switch (name?.Trim().ToLowerInvariant())
            {
                case null:
                case "":
                case "default":
                    return new ManaRetroFilterSettings();
                case "vhs":
                    return new ManaRetroFilterSettings
                    {
                        Lines = 240, ColorLevels = 12, Dither = 0.8f, Scanlines = 0.25f, ChromaticAberration = 1.0f,
                        Noise = 0.35f, Glitch = 0.6f, Vignette = 0.4f, Saturation = 0.8f
                    };
                case "console":
                    return new ManaRetroFilterSettings
                    {
                        Lines = 200, ColorLevels = 8, Dither = 1.0f, Scanlines = 0.0f, ChromaticAberration = 0.0f,
                        Noise = 0.0f, Glitch = 0.0f, Vignette = 0.0f, Saturation = 1.0f
                    };
                case "crt":
                    return new ManaRetroFilterSettings
                    {
                        Lines = 270, ColorLevels = 16, Dither = 0.6f, Scanlines = 0.6f, ChromaticAberration = 0.5f,
                        Noise = 0.15f, Glitch = 0.1f, Vignette = 0.5f, Curvature = 0.5f, Saturation = 0.95f
                    };
                default:
                    return null;
            }
        }

        /// <summary>Screen pixels across one big pixel for a window <paramref name="height"/> tall: whole, so every block is alike.</summary>
        public int PixelSize(uint height)
        {
            var lines = Math.Max(Lines, 16);
            return Math.Max(1, (int)MathF.Round(height / (float)lines, MidpointRounding.AwayFromZero));
        }
    }
}
