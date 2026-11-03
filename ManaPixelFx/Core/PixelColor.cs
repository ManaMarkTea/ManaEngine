using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Core
{
    /// <summary>
    /// pixelfx's color.js: colours are 32-bit numbers laid out like image bytes (R, G, B, A in memory, so R is the
    /// low byte), and palettes are ramps of them, dark to bright.
    /// </summary>
    public static class PixelColor
    {
        public static uint Rgb(double r, double g, double b) =>
            0xff000000u | ((uint)(I32(b) & 255) << 16) | ((uint)(I32(g) & 255) << 8) | (uint)(I32(r) & 255);

        public static int Red(uint c) => (int)(c & 255);
        public static int Green(uint c) => (int)((c >> 8) & 255);
        public static int Blue(uint c) => (int)((c >> 16) & 255);

        /// <summary>"#rgb" or "#rrggbb" to a colour.</summary>
        public static uint Hex(string h)
        {
            var s = h.Replace("#", "");
            if (s.Length == 3)
            {
                s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
            }
            var v = Convert.ToInt32(s[..Math.Min(6, s.Length)], 16);
            return Rgb((v >> 16) & 255, (v >> 8) & 255, v & 255);
        }

        public static string ToHex(uint c) => $"#{Red(c):x2}{Green(c):x2}{Blue(c):x2}";

        /// <summary>A straight mix of two colours.</summary>
        public static uint Mix(uint a, uint b, double t)
        {
            t = Clamp(t);
            return Rgb(Red(a) + (Red(b) - Red(a)) * t, Green(a) + (Green(b) - Green(a)) * t, Blue(a) + (Blue(b) - Blue(a)) * t);
        }

        /// <summary>A colour's brightness times k.</summary>
        public static uint Scale(uint c, double k) => Rgb(Math.Min(255, Red(c) * k), Math.Min(255, Green(c) * k), Math.Min(255, Blue(c) * k));

        /// <summary>Perceived brightness 0..1.</summary>
        public static double Luma(uint c) => (0.299 * Red(c) + 0.587 * Green(c) + 0.114 * Blue(c)) / 255;

        public static uint[] Ramp(IEnumerable<string> list) => list.Select(Hex).ToArray();

        /// <summary>A ramp resampled to exactly n steps, keeping its ends, so an effect can index steps by number.</summary>
        public static uint[] Fit(IReadOnlyList<uint> r, int n)
        {
            if (r.Count == n)
            {
                return r.ToArray();
            }
            var fitted = new uint[n];
            for (var i = 0; i < n; i++)
            {
                fitted[i] = r[(int)Round((i * (r.Count - 1)) / (double)Math.Max(1, n - 1))];
            }
            return fitted;
        }

        public static uint[] Fit(IReadOnlyList<string> r, int n) => Fit(Ramp(r), n);

        /// <summary>The ramp step for t in 0..1, unblended: pixel art picks a colour, it does not fade between two.</summary>
        public static uint At(IReadOnlyList<uint> r, double t) => r[(int)Clamp(Math.Floor(t * r.Count), 0, r.Count - 1)];

        /// <summary>A blended colour along a ramp, for light, where smooth is right.</summary>
        public static uint Blend(IReadOnlyList<uint> r, double t)
        {
            var x = Clamp(t) * (r.Count - 1);
            var i = (int)Math.Min(r.Count - 2, Math.Floor(x));
            return Mix(r[i], r[i + 1], x - i);
        }

        /// <summary>Hue-shifted ramps, dark to bright; any of them recolours an effect as its palette.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> Pal = new Dictionary<string, string[]>
        {
            ["fire"] = ["#1f0b10", "#4a1118", "#8c1d1c", "#cc3a1a", "#f2701f", "#ffae35", "#ffe27a", "#fff8d6"],
            ["ember"] = ["#1a0a0e", "#3d1012", "#701a14", "#a8301a", "#e0561e", "#ff8a2a", "#ffc46a"],
            ["lava"] = ["#14060a", "#380a10", "#6e1210", "#ac2410", "#e8501a", "#ff8c2a", "#ffd25a"],
            ["smoke"] = ["#0e0c12", "#1d1a22", "#2e2a35", "#433e4b", "#5c5664", "#7b7484", "#a39cab"],
            ["dust"] = ["#1c1610", "#33281c", "#4f3f2c", "#6e5a40", "#927a58", "#b89f78", "#dcc8a2"],
            ["ice"] = ["#0c1430", "#162c5c", "#1f4f94", "#2f7fcc", "#58b4ec", "#9bdcff", "#d8f4ff", "#ffffff"],
            ["water"] = ["#081830", "#0e2e5a", "#14508c", "#1c76bc", "#36a2e0", "#78ccf4", "#c4ecff", "#ffffff"],
            ["electric"] = ["#0a0f2e", "#172a78", "#2550d0", "#3f8cff", "#7cc8ff", "#c8f0ff", "#ffffff"],
            ["volt"] = ["#24160a", "#5e3a0c", "#a8680f", "#e8a218", "#ffd23c", "#fff08a", "#ffffff"],
            ["poison"] = ["#0d1a0c", "#173a14", "#265e18", "#3f8a1e", "#6cb82a", "#a8e04a", "#e0ff8a"],
            ["holy"] = ["#2a1c08", "#5a3e10", "#9a6c18", "#d8a428", "#ffd84a", "#fff09a", "#fffbe6", "#ffffff"],
            ["heal"] = ["#08201a", "#0f3f2c", "#17663e", "#24924e", "#3ec466", "#7ee88a", "#c8ffc4", "#ffffff"],
            ["arcane"] = ["#140a26", "#2b1252", "#481c88", "#6b2cc0", "#9a48ec", "#c684ff", "#ecc8ff", "#ffffff"],
            ["shadow"] = ["#050308", "#0f0a18", "#1c1230", "#2c1a4a", "#432468", "#5e3290", "#8048b8", "#a970e0"],
            ["blood"] = ["#14040a", "#3a0a14", "#6a101c", "#a01824", "#d42a30", "#ff5a50", "#ff9a8a"],
            ["earth"] = ["#140e0a", "#2a1e14", "#47321e", "#69492a", "#8e6638", "#b88a52", "#dcb47c"],
            ["stone"] = ["#0e0e12", "#1c1c24", "#2c2c38", "#42424f", "#5c5c6b", "#7d7d8c", "#a4a4b2", "#d0d0da"],
            ["gold"] = ["#1e1206", "#4a2c08", "#86520c", "#c48412", "#f0b424", "#ffe066", "#fff6c4", "#ffffff"],
            ["leaf"] = ["#0c1a0e", "#163418", "#225224", "#357530", "#4f9a3a", "#7cc048", "#b4e070", "#e4ffb0"],
            ["wood"] = ["#160c06", "#2e1a0c", "#4a2c14", "#6a421e", "#8e5c2c", "#b47c40", "#d8a464"],
            ["metal"] = ["#0c0e14", "#1c2230", "#2e3648", "#4a5468", "#6e7a90", "#9aa6ba", "#d0d8e6", "#ffffff"],
            ["bone"] = ["#1a1814", "#3a352c", "#5e5648", "#8a806c", "#b8ad96", "#e4dcc8", "#fffaf0"],
            ["neon"] = ["#1a0516", "#3d0a33", "#701560", "#b0228f", "#f53cd1", "#ff86e6", "#ffd2f6", "#ffffff"],
            ["cyber"] = ["#041418", "#08303a", "#0c5260", "#108088", "#18b4b0", "#4ce8d8", "#b4fff0", "#ffffff"],
            ["matrix"] = ["#020a04", "#05200c", "#0a3c16", "#106624", "#1a9a34", "#3cd650", "#a4ffa8", "#e8ffe8"],
            ["candy"] = ["#ff4a5a", "#ff9a2a", "#ffe03c", "#5ad84a", "#3cc8ff", "#7a6cff", "#f53cd1"],
            ["night"] = ["#07060d", "#0c0a18", "#131026", "#1b1734", "#241f44", "#2f2956"],
        };

        public static readonly uint White = Hex("#ffffff");
        public static readonly uint Black = Hex("#000000");
        public static readonly uint Ink = Hex("#0b0a10");
        public static readonly uint Patch = Hex("#f53cd1");

        /// <summary>The simulation's own ramps (sim.js), hot first.</summary>
        public static readonly uint[] FireRamp = Ramp(["#ffffff", "#fff3a6", "#ffe26b", "#ffc23d", "#ff8a2a", "#f2561d", "#c9281f", "#8a1f17", "#4a1712"]);
        public static readonly uint[] SmokeRamp = Ramp(["#7b6f72", "#5d5660", "#433d48", "#2b2630", "#1a171e"]);
    }
}
