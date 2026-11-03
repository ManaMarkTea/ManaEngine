using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Impacts.ImpactsLib;

namespace ManaPixelFx.Effects.Impacts
{
    /// <summary>
    /// pixelfx's impacts/blunt-hit.js: a heavy, soft thud -- a white-hot lumpy blob cools to cream in a warm pool of
    /// light, inked wedges jab out and snap back, a squashed ring of dust puffs swells and drifts, clods drop. The clods
    /// and their dust are adapted from Sprite Fusion's open-source explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class BluntHit : PixelEffect
    {
        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "impacts/blunt-hit",
            Name = "Blunt hit",
            Description = "A heavy, soft thud: a white-hot lumpy blob cools to cream in a warm pool of light, inked wedges jab out and snap back, a squashed ring of dust puffs swells and drifts, clods drop.",
            Size = (128, 88),
            Origin = (64, 72),
            Duration = 0.9,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#1d1620", "#4a3a3c", "#7d6a66", "#b09c90", "#dccab4", "#f6ead2", "#ffffff" },
                ["warm"] = "#ffc98a",
                ["dust"] = "#b7a8a6",
                ["clod"] = "#6e5a5a",
                ["height"] = 22.0,
                ["seed"] = 9.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        // a lumpy outline: radius at angle a for a blob of radius r
        private static double Lump(double r, double a, double ph) => r * (1 + 0.15 * Math.Sin(5 * a + ph) + 0.08 * Math.Sin(9 * a + ph * 2.3));

        private readonly record struct Wedge(double A, double Len, double W, double R0, double T0, double Out, double Back);

        private sealed class Puff
        {
            public double X, Y, Vx, Vy, R, Life, Max, T0;
            public double Lift = 6;
        }

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly PixelRng _r;
            private readonly double _y;
            private readonly double _ph;
            private readonly uint _warm;
            private readonly uint _dust;
            private readonly Stone _st;
            private readonly Lights _lights = new();
            private readonly Bits _bits = new(_ => 0);
            private readonly List<Wedge> _wedges = [];
            private readonly List<Puff> _puffs = [];
            private readonly List<Puff> _kicks = [];
            private double _t;

            public Run(PixelFxOptions o)
            {
                _c = Fit(o.Ramp("palette"), 7);
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                const double x = 0;
                _y = -o.Number("height");
                var y = _y;
                _ph = r.Range(0, Tau);
                _warm = o.Color("warm");
                _dust = o.Color("dust");
                _st = Stone.Of(o.Color("clod"));
                // a warm pool of light, a white-hot heart, and a glow on the floor below
                _lights.Add(x, y, 48, _warm, 0.6, 0.4);
                _lights.Add(x, y, 16, White, 0.6, 0.07);
                _lights.Add(x, -2, 36, _warm, 0.25, 0.4);
                // inked wedges: each jabs out at its own length and pace, then snaps back
                for (var i = 0; i < 9; i++)
                {
                    var a = (i / 9.0) * Tau + r.Range(-0.2, 0.2);
                    var len = r.Range(6, 15);
                    var w = r.Range(3.2, 5);
                    var r0 = r.Range(13, 16);
                    var t0 = r.Range(0.015, 0.04);
                    var outward = r.Range(0.04, 0.06);
                    var back = r.Range(0.09, 0.14);
                    _wedges.Add(new Wedge(a, len, w, r0, t0, outward, back));
                }
                // the squash ring: soft puffs flung out from the blob's rim, wider than tall, each its own size, pace and life
                for (var i = 0; i < 30; i++)
                {
                    var a = (i / 30.0) * Tau + r.Range(-0.12, 0.12);
                    var side = Math.Abs(Math.Cos(a));
                    var v = r.Range(40, 140) * (0.6 + 0.5 * side);
                    var puff = new Puff { X = x + Math.Cos(a) * 9, Y = y + Math.Sin(a) * 6, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v * 0.5 };
                    puff.R = r.Range(2.4, 6.2) * (0.75 + 0.35 * side);
                    puff.Life = r.Range(0.35, 0.85);
                    puff.T0 = r.Range(0.03, 0.1);
                    puff.Lift = r.Range(4, 16);
                    _puffs.Add(puff);
                }
                // and a few that billow up off the top and hang longer
                for (var i = 0; i < 5; i++)
                {
                    var puff = new Puff { X = x + r.Range(-6, 6) };
                    puff.Y = y - r.Range(3, 6);
                    puff.Vx = r.Range(-30, 30);
                    puff.Vy = -r.Range(20, 45);
                    puff.R = r.Range(3.5, 5.5);
                    puff.Life = r.Range(0.6, 0.85);
                    puff.T0 = r.Range(0.05, 0.1);
                    puff.Lift = r.Range(14, 24);
                    _puffs.Add(puff);
                }
                foreach (var q in _puffs)
                {
                    q.Max = q.Life;
                }
                // heavier clods knocked loose: they arc out, drop to the floor, bounce once and settle
                for (var i = 0; i < 5; i++)
                {
                    var s = r.Sign();
                    var a = -Math.PI / 2 + s * r.Range(0.5, 1.3);
                    var v = r.Range(70, 140);
                    var bit = new Bit { Kind = B.Chunk, X = x + s * r.Range(4, 8), Y = y + r.Range(-3, 3), Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, G = 1, Drag = 0.6, Bounce = 0.28 };
                    bit.Life = r.Range(0.65, 0.85);
                    bit.A = r.Range(0, Tau);
                    bit.Va = s * r.Range(8, 16);
                    bit.S = 1;
                    if (r.Chance(0.4))
                    {
                        bit.S = 2;
                    }
                    bit.Shape = RockShape(r);
                    bit.Pvy = 0;
                    _bits.Add(bit);
                }
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                _t += dt;
                var t = _t;
                foreach (var q in _puffs)
                {
                    if (t < q.T0)
                    {
                        continue;
                    }
                    q.Life -= dt;
                    q.Vx -= q.Vx * 4.5 * dt;
                    // they slow, and lift a little as they thin
                    q.Vy -= q.Vy * 4.5 * dt + q.Lift * dt;
                    q.X += q.Vx * dt;
                    q.Y += q.Vy * dt;
                }
                foreach (var b in _bits.List)
                {
                    b.Pvy = b.Vy;
                }
                _bits.Update(dt);
                // a clod hitting the floor kicks a little dust
                foreach (var b in _bits.List)
                {
                    if (b.Pvy > 60 && b.Vy <= 0)
                    {
                        for (var i = 0; i < 2; i++)
                        {
                            var kick = new Puff { X = b.X, Y = -1, Vx = r.Sign() * r.Range(8, 22) };
                            kick.Vy = -r.Range(3, 10);
                            kick.R = r.Range(1.2, 2);
                            kick.Life = 0.4;
                            kick.Max = 0.4;
                            kick.T0 = 0;
                            _kicks.Add(kick);
                        }
                        b.Pvy = 0;
                    }
                }
                foreach (var q in _kicks)
                {
                    q.Life -= dt;
                    q.Vx -= q.Vx * 4 * dt;
                    q.Vy -= q.Vy * 4 * dt;
                    q.X += q.Vx * dt;
                    q.Y += q.Vy * dt;
                }
                _lights.Update(dt);
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                const double x = 0;
                var y = _y;
                // dust: each puff swells and thins, warm-lit at first, its top catching the light
                foreach (var q in _puffs.Concat(_kicks))
                {
                    var k = q.Life / q.Max;
                    if (t < q.T0 || k <= 0)
                    {
                        continue;
                    }
                    var rr = q.R * (0.7 + (1 - k) * 0.85);
                    var a = Math.Pow(k, 1.25) * Math.Min(1, (t - q.T0) / 0.03);
                    var col = Mix(_dust, _warm, Clamp(1 - (t - 0.03) / 0.25) * 0.55);
                    p.Disc(q.X, q.Y, rr, col, (d, _) => 0.58 * a * (1 - d * d * 0.75));
                    p.Disc(q.X - rr * 0.22, q.Y - rr * 0.32, rr * 0.58, Mix(col, White, 0.55), (d, _) => 0.48 * a * (1 - d * d));
                }
                foreach (var b in _bits.List)
                {
                    Rock(p, b, _st);
                }
                // the blob: white-hot and lumpy for a frame, then cream and a little bigger, then gone into the dust
                if (t < 0.058)
                {
                    var f = 1;
                    if (t < 0.025)
                    {
                        f = 0;
                    }
                    var r0 = 11.5;
                    var squash = 1.1;
                    if (f == 1)
                    {
                        r0 = 13.5;
                        squash = 1.25;
                    }
                    var ph = _ph;
                    p.Shade(x - r0 * 1.4, y - r0 * 1.4, x + r0 * 1.4, y + r0 * 1.4, (px, py, _) =>
                    {
                        var dx = px - x;
                        var dy = (py - y) * squash;
                        var d = Hypot(dx, dy);
                        var ro = Lump(r0, Math.Atan2(dy, dx), ph + f * 0.6);
                        if (d > ro)
                        {
                            return 0;
                        }
                        var q = d / ro;
                        if (f == 0)
                        {
                            if (q < 0.55)
                            {
                                return White;
                            }
                            if (q < 0.86)
                            {
                                return c[5];
                            }
                            return c[4];
                        }
                        if (q < 0.3)
                        {
                            return White;
                        }
                        if (q < 0.72)
                        {
                            return c[5];
                        }
                        if (q < 0.9)
                        {
                            return c[4];
                        }
                        return c[3];
                    });
                }
                // inked wedges jab out from the blob and snap back
                foreach (var w in _wedges)
                {
                    var u = t - w.T0;
                    if (u < 0 || u > w.Out + w.Back)
                    {
                        continue;
                    }
                    double s;
                    if (u < w.Out)
                    {
                        s = Ease.OutCubic(u / w.Out);
                    }
                    else
                    {
                        s = 1 - Ease.InQuad((u - w.Out) / w.Back);
                    }
                    var l = w.Len * s;
                    if (l < 1.5)
                    {
                        continue;
                    }
                    var ca = Math.Cos(w.A);
                    var sa = Math.Sin(w.A);
                    var r0 = w.R0 + 2 * s;
                    var hw = (w.W * (0.6 + 0.4 * s)) / 2;
                    var tip = (X: x + ca * r0, Y: y + sa * r0);
                    var end = (X: x + ca * (r0 + l), Y: y + sa * (r0 + l));
                    var n = (X: -sa * hw, Y: ca * hw);
                    List<(double X, double Y)> Shape(double g) =>
                    [
                        (tip.X - ca * g, tip.Y - sa * g),
                        (end.X + n.X * (1 + g * 0.45) + ca * g, end.Y + n.Y * (1 + g * 0.45) + sa * g),
                        (end.X - n.X * (1 + g * 0.45) + ca * g, end.Y - n.Y * (1 + g * 0.45) + sa * g),
                    ];
                    p.Poly(Shape(1), c[1]);
                    var face = c[5];
                    if (u < w.Out + 0.02)
                    {
                        face = White;
                    }
                    p.Poly(Shape(0), face);
                }
                var pop = Math.Exp(-t / 0.06);
                if (pop > 0.02)
                {
                    p.Glow(x, y, 28, _warm, 0.5 * pop);
                }
                _lights.Draw(p);
            }
        }
    }
}
