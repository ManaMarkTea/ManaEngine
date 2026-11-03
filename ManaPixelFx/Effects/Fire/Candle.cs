using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Fire.FireLib;

namespace ManaPixelFx.Effects.Fire
{
    /// <summary>
    /// pixelfx's fire/candle.js: a wax candle in a brass dish, a calm flame with a blue root, a halo, motes and a bead
    /// of wax running down a drip. The layered tongue is adapted from Sprite Fusion's open-source fire, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class Candle : PixelEffect
    {
        private const double D = 4;
        private const double Top = -16;
        private const double Wick = Top - 3;
        private static readonly TimeNoise NL = NoiseFor(D);

        // a calm flame: an orange rim round gold, yellow, a pale heart and a small white core low down
        private static readonly FlameLayer[] Calm = [new(1, 1, 5), new(0.8, 0.88, 3), new(0.6, 0.72, 2), new(0.38, 0.52, 1), new(0.18, 0.3, 0)];

        // the bead runs down this drip, from the rim to its bulb
        private const double DripX = 4;
        private const double DripY0 = Top + 2;
        private const double DripY1 = -7;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "fire/candle",
            Name = "Candle",
            Description = "A wax candle in a brass dish: a calm flame with a blue root that sways and stretches, a halo of light, motes drifting in it, a bead of wax running down a drip.",
            Size = (56, 76),
            Origin = (28, 64),
            Duration = D,
            Loop = true,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["root"] = new[] { "#1b2a8a", "#3a62e0", "#7fb4ff" },
                ["wax"] = new[] { "#2e201e", "#5e4436", "#8e6e58", "#b8957a", "#d8b896", "#f0dcc0" },
                ["brass"] = new[] { "#2a1a10", "#6a4a1e", "#b4862e", "#e8c060", "#fff0b0" },
                ["seed"] = 2.0,
            },
        };

        private readonly record struct SmokeThread(double Vx, double Vy, double Life, double S) : ILooped;

        private readonly record struct Mote(double X, double Y, double Vx, double Vy, double Life, double Ph) : ILooped;

        private static void Dish(PixelPainter p, uint[] brass, uint[] hot, double fl)
        {
            p.Ellipse(0, 1, 10, 2.6, (d, a) =>
            {
                if (d > 0.82)
                {
                    return brass[0];
                }
                if (Math.Sin(a) < -0.2)
                {
                    if (d > 0.6)
                    {
                        return Mix(brass[3], hot[3], 0.3 * fl);
                    }
                    return brass[2];
                }
                if (d > 0.55)
                {
                    return brass[1];
                }
                return brass[2];
            });
            p.Rect(-8, 2, 17, 1, brass[1]);
            p.Rect(-7, 3, 15, 1, brass[0]);
        }

        // a wax pillar: a dark outline, a lit band, a shadow side, a pool of melted wax lit by the flame, a glossy
        // drip down the right ending in a bulb and a short one on the left
        private static void Pillar(PixelPainter p, uint[] wax, uint[] hot, double fl)
        {
            const double l = Top;
            uint[] cols = [wax[1], wax[4], wax[5], wax[4], wax[4], wax[3], wax[3], wax[2], wax[0]];
            for (var i = 0; i < 9; i++)
            {
                var x = i - 4;
                p.Rect(x, l + 1, 1, -l - 2, cols[i]);
                if (i == 2)
                {
                    p.Rect(x, -8, 1, 7, wax[4]);
                }
            }
            p.Rect(-3, -1, 7, 1, wax[2]);
            p.Px(-4, -1, wax[0]);
            p.Px(4, -1, wax[0]);
            // the rim and the pool round the wick, lit by the flame
            p.Rect(-3, l, 7, 1, Mix(wax[5], hot[2], 0.25 * fl));
            p.Px(-4, l, wax[1]);
            p.Px(4, l, wax[1]);
            p.Rect(-2, l + 1, 5, 1, Mix(wax[5], hot[1], 0.5 * fl));
            p.Rect(-1, l + 1, 3, 1, Mix(wax[4], hot[2], 0.45 * fl));
            // the long drip: a lighter, glossy run down the right that bulges past the side into a bulb
            const double y1 = DripY1;
            p.Rect(3, l + 1, 1, y1 - l - 1, wax[4]);
            p.Rect(4, l + 2, 1, y1 - l - 2, wax[3]);
            p.Px(5, l + 3, wax[0]);
            p.Rect(5, l + 4, 1, y1 - l - 4, wax[0]);
            p.Rect(3, y1, 2, 2, wax[4]);
            p.Px(5, y1, wax[3]);
            p.Px(5, y1 + 1, wax[2]);
            p.Px(6, y1, wax[0]);
            p.Px(6, y1 + 1, wax[0]);
            p.Rect(3, y1 + 2, 3, 1, wax[0]);
            p.Px(3, l + 2, wax[5]);
            // the short drip on the left
            p.Rect(-4, l + 1, 1, 4, wax[4]);
            p.Px(-5, l + 3, wax[1]);
            p.Px(-5, l + 4, wax[1]);
            p.Px(-4, l + 5, wax[3]);
        }

        public override void Draw(PixelPainter p, double t, PixelFxOptions o)
        {
            var hot = HotFirst(Fit(o.Ramp("palette"), 9));
            var root = Fit(o.Ramp("root"), 3);
            var wax = Fit(o.Ramp("wax"), 6);
            var brass = Fit(o.Ramp("brass"), 5);
            var s = o.Number("seed");
            // calm: everything moves slowly, the flame now and then stretches tall
            double N(double rate, double seed = 0, double off = 0) => NL(t, rate * 0.35, seed, off);
            var fl = 0.86 + 0.08 * NL(t, 2, s + 1) + 0.06 * NL(t, 7, s + 2);
            var stretch = Math.Max(0, NL(t, 0.75, s + 3) - 0.3) * 0.6;
            var h = 12 * (1 + stretch) * (0.94 + 0.06 * fl);
            var sway = NL(t, 0.5, s + 4) * 1.6 + NL(t, 1.5, s + 5) * 0.5;
            var glow = Mix(Mix(hot[3], hot[4], 0.5), White, 0.3);
            var cy = Wick - h * 0.4;

            // a pool of warm light on the floor and the halo behind the flame
            FloorLight(p, 0, 1, 26, 5, glow, 0.3 * fl);
            p.Glow(sway * 0.3, cy, 38 * fl, glow, 0.42 * fl);
            p.GlowRing(sway * 0.3, cy, 10, 4, hot[2], 0.12 * fl);
            Dish(p, brass, hot, fl);
            Pillar(p, wax, hot, fl);
            // the bead of wax: it swells at the rim, runs down the drip and sets into its bulb
            var run = (t % D) / D;
            var swell = Smooth(0, 0.3, run);
            var slide = Smooth(0.3, 0.85, run);
            var set = Smooth(0.85, 1, run);
            if (set < 1 && swell > 0.15)
            {
                var beadY = Round(DripY0 + (DripY1 - DripY0) * slide);
                p.Px(DripX, beadY, Mix(wax[5], hot[1], 0.4 * fl));
                if (swell > 0.6 && set < 0.5)
                {
                    p.Px(DripX + 1, beadY, wax[4]);
                    p.Px(DripX + 1, beadY + 1, wax[2]);
                }
            }
            // the wick, glowing at its tip
            p.Line(0, Top, 0, Wick + 1, wax[0]);
            p.Px(0, Wick + 1, hot[4]);
            // the flame and its blue root
            FlameTongue(p, 0, Wick + 1, h, 2.6, sway, N, s * 31, hot, Calm, 0.25, 0.6);
            for (var dx = -2; dx <= 2; dx++)
            {
                var a = 1 - Math.Abs(dx) * 0.2;
                var c = root[1];
                if (Math.Abs(dx) == 2)
                {
                    c = root[0];
                }
                p.Dot(dx, Wick + 1, c, a);
                if (Math.Abs(dx) <= 1)
                {
                    p.Dot(dx, Wick + 2, root[0], 0.7);
                }
            }
            p.Px(0, Wick, root[2]);
            // a thread of smoke now and then off the tip
            Looped(t, D, 6, s + 300, (r, _) =>
            {
                var vx = r.Range(-1.5, 1.5);
                var vy = -r.Range(8, 12);
                var life = r.Range(1.4, 2.2);
                var size = r.Range(0.8, 1.2);
                return new SmokeThread(vx, vy, life, size);
            }, (q, age, _) =>
            {
                var at = Drift(sway, Wick - h, q.Vx, q.Vy, age, 1, -6, 1, 0.8);
                SmokePuff(p, at.X + NL(t - age, 0.6, s + 60) * (1 + age * 2), at.Y, 1 - age / q.Life, q.S, 0, false, 0.6);
            });
            // motes drifting in the candlelight, catching it as they pass close
            Looped(t, D, 10, s + 400, (r, _) =>
            {
                var x = r.Range(-20, 20);
                var y = r.Range(-44, -8);
                var vx = r.Range(-3, 3);
                var vy = r.Range(-4, 2);
                var life = r.Range(2.5, 4);
                var ph = r.Range(0, 6.3);
                return new Mote(x, y, vx, vy, life, ph);
            }, (q, age, _) =>
            {
                var x = q.X + q.Vx * age + Math.Sin(age * 1.3 + q.Ph) * 2;
                var y = q.Y + q.Vy * age;
                var d = Hypot(x, y - cy);
                var k = Math.Min(1, Math.Min(age * 2, (q.Life - age) * 2)) * Clamp(1.2 - d / 26);
                if (k > 0.15)
                {
                    var c = hot[3];
                    if (k > 0.6)
                    {
                        c = hot[1];
                    }
                    p.Dot(x, y, c, k);
                    p.Glow(x, y, 3, hot[2], 0.2 * k);
                }
            });
            // the hot core of the light
            p.Glow(sway * 0.5, Wick - h * 0.3, 10, hot[1], 0.35 * fl);
        }
    }
}
