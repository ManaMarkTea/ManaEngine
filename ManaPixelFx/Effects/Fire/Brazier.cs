using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Fire.FireLib;

namespace ManaPixelFx.Effects.Fire
{
    /// <summary>
    /// pixelfx's fire/brazier.js: an iron bowl on a tripod heaped with glowing coals and a roaring fire, sparks streaming
    /// up off it. The fire recipe is adapted from Sprite Fusion's open-source fire, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class Brazier : PixelEffect
    {
        private const double D = 3;
        private const double Rim = -25;
        private const double Rx = 15;
        private const double Ry = 3;
        private static readonly TimeNoise NL = NoiseFor(D);

        private static readonly Tongue[] Tongues =
        [
            new(-7, -1.5, 0.5, 2.0, true),
            new(7.2, -1.5, 0.55, 2.0, true),
            new(-3, -2.5, 0.84, 2.2, true),
            new(3.3, -2.5, 0.86, 2.2, true),
            new(-11.5, 0.5, 0.2, 1.5, false),
            new(11.5, 0.5, 0.22, 1.5, false),
            new(-8.3, 0, 0.34, 2.0, false),
            new(8.5, 0, 0.38, 2.0, false),
            new(-4.7, -0.5, 0.6, 2.8, false),
            new(4.9, -0.5, 0.64, 2.8, false),
            new(0, -1, 1, 3.6, false),
        ];

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "fire/brazier",
            Name = "Brazier",
            Description = "An iron bowl on a tripod heaped with glowing coals and a big roaring fire, sparks and embers streaming up off it, light on the iron and the floor.",
            Size = (112, 148),
            Origin = (56, 136),
            Duration = D,
            Loop = true,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["iron"] = new[] { "#0b0910", "#1c1924", "#302c3a", "#4c4658", "#78708a" },
                ["seed"] = 8.0,
            },
        };

        private readonly record struct Streamer(double X, double Y, double Vx, double Vy, double Life, double A, double W, double M, bool Big) : ILooped;

        // the tripod: two legs splayed out and one coming at us, curled feet, a ring holding them together
        private static void Stand(PixelPainter p, uint[] iron, uint[] hot, double fl)
        {
            void Leg(double x0, double y0, double x1, double y1)
            {
                p.Line(x0, y0, x1, y1, iron[0]);
                p.Line(x0 + 1, y0, x1 + 1, y1, iron[2]);
                p.Line(x0 + 2, y0, x1 + 2, y1, iron[0]);
            }
            // the ring: its far half behind the front leg
            p.Ellipse(0, -9, 9, 1.8, (d, _) =>
            {
                if (d > 0.6)
                {
                    return iron[2];
                }
                return 0;
            });
            Leg(-6, -17, -12, -1);
            Leg(4, -17, 10, -1);
            p.Rect(-14, -1, 5, 1, iron[2]);
            p.Rect(-15, 0, 6, 1, iron[0]);
            p.Px(-15, -1, iron[3]);
            p.Rect(9, -1, 5, 1, iron[2]);
            p.Rect(9, 0, 6, 1, iron[0]);
            p.Px(13, -1, iron[3]);
            p.Rect(-1, -16, 3, 15, iron[0]);
            p.Rect(0, -16, 1, 15, Mix(iron[3], hot[4], 0.15 * fl));
            p.Rect(-2, -1, 5, 1, iron[2]);
            p.Rect(-2, 0, 5, 1, iron[0]);
            p.Ellipse(0, -9, 9, 1.8, (d, a) =>
            {
                if (Math.Sin(a) > 0 && d > 0.55)
                {
                    if (d > 0.85)
                    {
                        return iron[0];
                    }
                    return iron[3];
                }
                return 0;
            });
        }

        // the bowl, front half: a band of rivets under a rim lit by the fire, rounding down to a dark belly
        private static void Bowl(PixelPainter p, uint[] iron, uint[] hot, double fl)
        {
            p.Shade(-Rx - 1, Rim - 1, Rx + 1, Rim + 9, (x, y, _) =>
            {
                var v = (y - Rim) / 9;
                var hw = Rx * Math.Sqrt(Math.Max(0, 1 - v * v * 0.9));
                if (v > 0.5)
                {
                    hw -= (v - 0.5) * 6;
                }
                if (Math.Abs(x) > hw + 0.5 || y < Rim)
                {
                    return 0;
                }
                if (Math.Abs(x) > hw - 0.5 || v > 0.92)
                {
                    return iron[0];
                }
                if (y < Rim + 1)
                {
                    return Mix(iron[3], hot[4], 0.5 * fl);
                }
                if (y < Rim + 2)
                {
                    return iron[2];
                }
                if (y < Rim + 3)
                {
                    if (Round(x) % 4 == 0)
                    {
                        return Mix(iron[4], hot[4], 0.25 * fl);
                    }
                    return iron[0];
                }
                if (x < -Rx * 0.45)
                {
                    return iron[2];
                }
                if (x < Rx * 0.25)
                {
                    return iron[1];
                }
                return iron[0];
            });
        }

        // coals heaped in the bowl, showing over the rim, breathing in the heat
        private static void Coals(PixelPainter p, uint[] iron, uint[] hot, double t, double s)
        {
            p.Shade(-Rx, Rim - 6, Rx, Rim + 1, (x, y, o) =>
            {
                // a mound: highest in the middle, standing proud of the rim
                var top = Rim - 1 - 4 * Math.Max(0, 1 - Math.Pow(x / (Rx - 1), 2));
                if (y < top || y > Rim + 0.5 || Math.Abs(x) > Rx - 0.5)
                {
                    return 0;
                }
                var lump = Rand(Math.Floor((x + Math.Floor(y) * 1.5) / 3), Math.Floor(y / 2), s + 20);
                var edge = y < top + 1;
                // the gaps between lumps stay dark; the lumps breathe, hottest deep in the heap
                if (Rand(Math.Floor(x), Math.Floor(y), s + 21) < 0.16 || (edge && lump < 0.3))
                {
                    return iron[0];
                }
                var g = 0.5 + 0.5 * Math.Sin((t / D) * Math.PI * 4 + lump * 40);
                if (edge)
                {
                    g *= 0.7;
                }
                o.K = 0.3 * g;
                if (g > 0.72)
                {
                    return hot[2];
                }
                if (g > 0.5)
                {
                    return hot[3];
                }
                if (g > 0.3)
                {
                    return hot[4];
                }
                if (g > 0.15)
                {
                    return hot[5];
                }
                return hot[6];
            });
        }

        public override void Draw(PixelPainter p, double t, PixelFxOptions o)
        {
            var hot = HotFirst(Fit(o.Ramp("palette"), 9));
            var iron = Fit(o.Ramp("iron"), 5);
            var s = o.Number("seed");
            var fl = 0.74 + 0.16 * NL(t, 7.3, s + 1) + 0.1 * NL(t, 19, s + 2);
            var glow = Mix(Mix(hot[3], hot[4], 0.5), White, 0.25);
            uint[] sparks = [hot[1], hot[2], hot[4], hot[5]];
            uint[] emb = [hot[1], hot[2], hot[4], hot[6]];
            FloorLight(p, 0, 0.5, 40, 7, glow, 0.32 * fl);
            Stand(p, iron, hot, fl);
            // the back of the rim, behind the fire
            p.Ellipse(0, Rim, Rx, Ry, (d, a) =>
            {
                if (Math.Sin(a) < 0 && d > 0.7)
                {
                    if (d > 0.88)
                    {
                        return iron[0];
                    }
                    return Mix(iron[3], hot[4], 0.3 * fl);
                }
                return 0;
            });
            Burn(p, t, Tongues, new BurnOptions
            {
                H = hot, D = D, NL = NL, S = s, X = 0, Y = Rim, Height = 32, Lean = 0.1, Spread = 7,
                Puffs = 8, Licks = 4, Smoke = 14, SmokeSize = 3, SmokeLife = (1.6, 2.4), Embers = 10, GlowR = 58, Glow = 1.4, Wind = 3,
                Mid = _ =>
                {
                    Coals(p, iron, hot, t, s);
                    Bowl(p, iron, hot, fl);
                },
            });
            // sparks streaming up in the draught: fast streaks that slow, wander and cool to embers high above
            Looped(t, D, 60, s + 600, (r, _) =>
            {
                var side = r.Range(-1, 1);
                var x = side * 8;
                var y = Rim - r.Range(4, 18);
                var vx = side * r.Range(10, 40);
                var vy = -r.Range(70, 150);
                var life = r.Range(0.7, 1.7);
                var a = r.Range(0, 6.3);
                var w = r.Range(1.8, 3.2);
                var m = r.Range(3, 8);
                var big = r.Chance(0.12);
                return new Streamer(x, y, vx, vy, life, a, w, m, big);
            }, (q, age, _) =>
            {
                var at = Drift(q.X, q.Y, q.Vx, q.Vy, age, 0, -22, 1.9, 1.9);
                var k = 1 - age / q.Life;
                // they bend and wander as the draught slows them
                var wob = q.M * (Math.Sin(age * q.W + q.A) - Math.Sin(q.A)) * Math.Min(1, age * 1.5);
                var wv = q.M * q.W * Math.Cos(age * q.W + q.A) * Math.Min(1, age * 1.5);
                if (Hypot(at.Vx + wv, at.Vy) > 40)
                {
                    Spark(p, at.X + wob, at.Y, at.Vx + wv, at.Vy, k, sparks);
                }
                else
                {
                    double size = 1;
                    if (q.Big)
                    {
                        size = 2;
                    }
                    Ember(p, at.X + wob, at.Y, k, size, emb, age * 40 + q.A);
                }
            });
        }
    }
}
