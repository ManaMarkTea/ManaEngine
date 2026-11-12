using System.ComponentModel.DataAnnotations;
using ManaCore.Graphics;
using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// Puts the whole screen through the retro filter while its level is open -- big pixels, a few dithered colours, and
    /// a worn CRT or VHS look (see <see cref="ManaRetroFilterSettings"/>). Where it sits does not matter; one per world
    /// (the first enabled one found), in the editor as in play. The editor's panels and a game's HUD stay sharp above it.
    /// </summary>
    public sealed class ManaRetroFilterComponent : ManaComponent
    {
        /// <summary>Off, the picture is shown as usual.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>The picture's height in big pixels: 240 is a 1990s console on a TV, 120 very chunky.</summary>
        [Range(60, 1080)]
        public int Lines { get; set; } = 240;

        /// <summary>Levels each colour channel keeps: fewer is more posterized and dithered.</summary>
        [Range(2, 64)]
        public int ColorLevels { get; set; } = 10;

        /// <summary>How strongly the cut colours are dithered: 0 bands, 1 the full checkered pattern.</summary>
        [Range(0, 1)]
        public float Dither { get; set; } = 1.0f;

        /// <summary>Dark lines between rows of big pixels.</summary>
        [Range(0, 1)]
        public float Scanlines { get; set; } = 0.3f;

        /// <summary>Red and blue pulled apart sideways, in big pixels.</summary>
        [Range(0, 4)]
        public float ChromaticAberration { get; set; } = 0.6f;

        /// <summary>Flickering grain.</summary>
        [Range(0, 1)]
        public float Noise { get; set; } = 0.2f;

        /// <summary>VHS wobble: jittering lines, a rolling tracking band, a torn strip along the bottom.</summary>
        [Range(0, 1)]
        public float Glitch { get; set; } = 0.3f;

        /// <summary>Darkened corners.</summary>
        [Range(0, 1)]
        public float Vignette { get; set; } = 0.35f;

        /// <summary>The bulge of a CRT's glass; 0 is flat.</summary>
        [Range(0, 1)]
        public float Curvature { get; set; }

        /// <summary>Colour: 0 grey, 1 as drawn, above 1 more vivid.</summary>
        [Range(0, 2)]
        public float Saturation { get; set; } = 0.85f;

        public ManaRetroFilterComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Retro Filter";
        }

        /// <summary>The filter as the renderer takes it.</summary>
        public ManaRetroFilterSettings Settings() => new()
        {
            Lines = Math.Clamp(Lines, 16, 4320),
            ColorLevels = Math.Clamp(ColorLevels, 2, 256),
            Dither = Math.Clamp(Dither, 0.0f, 1.0f),
            Scanlines = Math.Clamp(Scanlines, 0.0f, 1.0f),
            ChromaticAberration = Math.Clamp(ChromaticAberration, 0.0f, 8.0f),
            Noise = Math.Clamp(Noise, 0.0f, 1.0f),
            Glitch = Math.Clamp(Glitch, 0.0f, 1.0f),
            Vignette = Math.Clamp(Vignette, 0.0f, 1.0f),
            Curvature = Math.Clamp(Curvature, 0.0f, 1.0f),
            Saturation = Math.Clamp(Saturation, 0.0f, 2.0f)
        };
    }
}
