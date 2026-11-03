using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Fire.FireLib;

namespace ManaPixelFx.Effects.Fire
{
    /// <summary>
    /// pixelfx's fire/torch.js: a wall torch on an iron bracket, a licking flame off a pitch-soaked wrap. The tongue,
    /// lick, puff, smoke and ember recipe is adapted from Sprite Fusion's open-source fire, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class Torch : PixelEffect
    {
        private const double D = 2;
        private const double Base = -16;
        private static readonly TimeNoise NL = NoiseFor(D);

        private static readonly Tongue[] Tongues =
        [
            new(-1.9, -1, 0.74, 2.0, true),
            new(2.1, -1, 0.7, 2.0, true),
            new(-3.4, 0.5, 0.42, 1.6, false),
            new(3.6, 0.5, 0.46, 1.6, false),
            new(0, 0, 1, 2.9, false),
        ];

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "fire/torch",
            Name = "Torch",
            Description = "A wall torch on an iron bracket: a licking flame leaping off a pitch-soaked wrap, licks and puffs torn off its tips, smoke, embers, a flickering light on the wall.",
            Size = (72, 120),
            Origin = (36, 94),
            Duration = D,
            Loop = true,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["wood"] = new[] { "#0e080a", "#24130f", "#3b2216", "#5a3520", "#8a5a34", "#b9884f" },
                ["iron"] = new[] { "#0d0b12", "#1e1b26", "#363242", "#5b5566", "#8a8396" },
                ["seed"] = 4.0,
            },
        };

        // the bracket: a bevelled iron plate on the wall with two rivets
        private static void Plate(PixelPainter p, uint[] iron, uint[] hot, double fl)
        {
            p.Shade(-5.5, -5.5, 5.5, 11.5, (x, y, _) =>
            {
                if (x < -5 || x > 5 || y < -5 || y > 11)
                {
                    return iron[0];
                }
                var rivet = Math.Abs(x) < 0.6 && (Math.Abs(y + 3.5) < 0.6 || Math.Abs(y - 9.5) < 0.6);
                if (rivet)
                {
                    return Mix(iron[4], hot[3], 0.35 * fl);
                }
                if (y < -4 || x < -4)
                {
                    return Mix(iron[3], hot[4], 0.3 * fl);
                }
                if (y > 10 || x > 4)
                {
                    return iron[1];
                }
                if (y < 2)
                {
                    return iron[2];
                }
                return iron[1];
            });
        }

        private static void Handle(PixelPainter p, uint[] wood)
        {
            p.Shade(-3, -12, 3, 15, (x, y, _) =>
            {
                var half = 1.3 + Clamp((14 - y) / 26) * 0.9;
                if (Math.Abs(x) > half + 0.5 || y > 14.5)
                {
                    return 0;
                }
                if (Math.Abs(x) > half - 0.5 || y > 13.5)
                {
                    return wood[0];
                }
                var g = Rand(Math.Floor(y / 3), Round(x), 9);
                if (x < -0.3)
                {
                    if (g < 0.3)
                    {
                        return wood[2];
                    }
                    return wood[4];
                }
                if (g < 0.4)
                {
                    return wood[1];
                }
                return wood[2];
            });
        }

        private static void Band(PixelPainter p, uint[] iron, uint[] hot, double fl)
        {
            p.Rect(-3, -2, 7, 4, iron[0]);
            p.Rect(-2, -1, 5, 1, Mix(iron[4], hot[3], 0.3 * fl));
            p.Rect(-2, 0, 5, 1, iron[2]);
        }

        // the head: rags bound with cord and soaked in pitch, flaring into a cup, charred and breathing embers at the top
        private static void Wrap(PixelPainter p, uint[] wood, uint[] iron, uint[] hot, double t)
        {
            p.Shade(-6, Base - 2, 6, -9, (x, y, o) =>
            {
                var v = (y - (Base - 1.5)) / 7.5;
                var half = 4.6 - v * 1.9;
                if (Math.Abs(x) > half + 0.5 || y < Base - 1.5 || y > -9.5)
                {
                    return 0;
                }
                if (Math.Abs(x) > half - 0.5 || y > -10.5)
                {
                    return wood[0];
                }
                var cord = Math.Floor(y - x * 0.6 + 40) % 3 == 0;
                var h = Rand(Math.Floor(x), Math.Floor(y), 31);
                if (v < 0.45)
                {
                    if (h < 0.42)
                    {
                        var g = 0.5 + 0.5 * Math.Sin((t / D) * Math.PI * 4 + h * 40);
                        o.K = 0.3 * g;
                        if (g > 0.66)
                        {
                            return hot[2];
                        }
                        if (g > 0.33)
                        {
                            return hot[4];
                        }
                        return hot[5];
                    }
                    if (h < 0.75)
                    {
                        return wood[0];
                    }
                    return wood[1];
                }
                if (cord)
                {
                    return iron[2];
                }
                if (x < -1)
                {
                    return wood[3];
                }
                if (x < 1.5)
                {
                    return wood[2];
                }
                return wood[1];
            });
        }

        public override void Draw(PixelPainter p, double t, PixelFxOptions o)
        {
            var hot = HotFirst(Fit(o.Ramp("palette"), 9));
            var wood = Fit(o.Ramp("wood"), 6);
            var iron = Fit(o.Ramp("iron"), 5);
            var s = o.Number("seed");
            var fl0 = 0.74 + 0.16 * NL(t, 7.3, s + 1) + 0.1 * NL(t, 19, s + 2);
            Plate(p, iron, hot, fl0);
            Handle(p, wood);
            Band(p, iron, hot, fl0);
            Burn(p, t, Tongues, new BurnOptions
            {
                H = hot, D = D, NL = NL, S = s, X = 0, Y = Base, Height = 20, Lean = 0.12, Spread = 4,
                Puffs = 10, Licks = 5, Smoke = 7, SmokeSize = 2.3, SmokeLife = (1.3, 2.1), Embers = 7, GlowR = 48, Glow = 1.25, Wind = 2,
                Mid = _ => Wrap(p, wood, iron, hot, t),
            });
        }
    }
}
