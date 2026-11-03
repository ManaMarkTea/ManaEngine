using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Explosions.ExplosionsLib;

namespace ManaPixelFx.Effects.Explosions
{
    /// <summary>
    /// pixelfx's explosions/barrel-explosion.js: an explosive barrel shudders, its lid bulging and spitting flame, then
    /// blows -- a fireball column, staves and the lid flying, burning fuel splashed over the floor. The blast is adapted
    /// from Sprite Fusion's open-source explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class BarrelExplosion : PixelEffect
    {
        private const double Duration = 5.2;
        private const double Blow = 0.8;
        private const double Bw = 8;
        private const double Bh = 22;
        private static readonly uint Oil = Hex("#221a22");
        private static readonly uint SootSmoke = Hex("#1f1a24");
        private static readonly uint Dust = Hex("#7d7280");

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "explosions/barrel-explosion",
            Name = "Barrel explosion",
            Description = "An explosive barrel shudders, its lid bulging and spitting flame, then blows: a fireball column, staves and the lid flying, burning fuel splashed over the floor.",
            Size = (300, 240),
            Origin = (150, 204),
            Duration = Duration,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["barrel"] = new[] { "#1c0a0e", "#4a1218", "#7c1a1e", "#b02a26", "#d8483a", "#f08a70" },
                ["seed"] = 8.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o, Meta);

        private sealed class Lid
        {
            public double X, Y = -Bh, Vx, Vy, A, Va;
            public bool On, Rest;
        }

        private readonly record struct Puddle(double X, double W);

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _f;
            private readonly uint[] _fr;
            private readonly uint[] _hr;
            private readonly uint[] _c;
            private readonly PixelRng _r;
            private readonly double _x0, _x1, _y0, _y1;
            private readonly Particles _ps;
            private readonly Blazes _blazes;
            private readonly Lights _lights = new();
            private readonly Bits _staves = new(_ => 0);
            private readonly Lid _lid = new();
            private readonly List<Puddle> _pools = [];
            private double _t;
            private bool _blown;

            public Run(PixelFxOptions o, PixelEffectMeta meta)
            {
                _f = Fit(o.Ramp("palette"), 8);
                _fr = FireRamp(_f);
                _hr = HeatRamp(_fr);
                _c = Fit(o.Ramp("barrel"), 6);
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                var (w, h) = meta.Size;
                var (ox, oy) = meta.Origin;
                _x0 = -ox + 3;
                _x1 = w - ox - 3;
                _y0 = -oy + 3;
                _y1 = h - oy - 3;
                Blazes? blazes = null;
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [_f[6], _f[5], _f[3], _f[2]], onLand: (x, y) => blazes!.Add(x, y - 0.25, r.Range(1.2, 2.2), r.Range(0.6, 1)));
                blazes = new Blazes(_ps);
                _blazes = blazes;
            }

            public override bool HasBack => true;

            private static int Smokes(PixelRng r, double chance)
            {
                if (r.Chance(chance))
                {
                    return PF.Smokes | PF.Big;
                }
                return 0;
            }

            private void DoBlow()
            {
                var r = _r;
                var ps = _ps;
                var f = _f;
                _blown = true;
                const double y = -Bh * 0.5;
                _lights.Add(0, y - 20, 90, Mix(f[3], f[6], 0.45), 0.85, 0.9);
                _lights.Add(0, y, 44, White, 0.7, 0.09);
                // the fireball: bursting out, then a column of fire driven up out of the drum
                for (var i = 0; i < 5; i++)
                {
                    ps.Spawn(P.Fire, r.Range(-3, 3), y + r.Range(-3, 3), r.Range(-20, 20), r.Range(-40, -10), r.Range(0.18, 0.28), 0, r.Range(9, 12));
                }
                for (var i = 0; i < 30; i++)
                {
                    var a = r.Range(-Math.PI - 0.15, 0.15);
                    var d = r.Range(0, 8);
                    var sp = r.Range(50, 190);
                    ps.Spawn(P.Fire, Math.Cos(a) * d, y + Math.Sin(a) * d, Math.Cos(a) * sp, Math.Sin(a) * sp * 0.8 - 40, r.Range(0.55, 1.05), 0, r.Range(5, 10), 0, Smokes(r, 0.5));
                }
                for (var i = 0; i < 12; i++)
                {
                    ps.Spawn(P.Fire, r.Range(-6, 6), y - r.Range(0, 10), r.Range(-14, 14), -r.Range(170, 300), r.Range(0.7, 1.15), 0, r.Range(6, 10), r.Range(0, 0.12), Smokes(r, 0.7));
                }
                // oily black smoke
                for (var i = 0; i < 12; i++)
                {
                    var a = r.Range(-Math.PI, 0);
                    var d = r.Range(4, 16);
                    ps.Spawn(P.Smoke, Math.Cos(a) * d, y + Math.Sin(a) * d, Math.Cos(a) * 30, -r.Range(30, 90), r.Range(1.6, 2.6), SootSmoke, r.Range(8, 13), r.Range(0.1, 0.4), PF.Big);
                }
                for (var i = 0; i < 90; i++)
                {
                    var a = r.Range(0, Tau);
                    var sp = r.Range(160, 400);
                    ps.Spawn(P.Spark, 0, y, Math.Cos(a) * sp, Math.Sin(a) * sp - 70, r.Range(0.2, 0.6));
                }
                for (var i = 0; i < 40; i++)
                {
                    var a = r.Range(0, Tau);
                    var sp = r.Range(30, 140);
                    ps.Spawn(P.Ember, Math.Cos(a) * 8, y + Math.Sin(a) * 8, Math.Cos(a) * sp, Math.Sin(a) * sp - 70, r.Range(1.2, 2.6), f[3]);
                }
                // burning fuel flung out in arcs: it lands and keeps burning
                for (var i = 0; i < 14; i++)
                {
                    var a = -Math.PI / 2 + r.Range(-1.2, 1.2);
                    var sp = r.Range(120, 300);
                    ps.Spawn(P.Brand, r.Range(-4, 4), y, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.8, 1.4));
                }
                // the staves: the drum's plates torn off and thrown out, tumbling
                for (var i = 0; i < 9; i++)
                {
                    var side = -1;
                    if (i % 2 != 0)
                    {
                        side = 1;
                    }
                    var a = -Math.PI / 2 + side * r.Range(0.25, 1.15);
                    var sp = r.Range(170, 330);
                    _staves.Add(new Bit
                    {
                        Kind = B.Chunk, X = side * r.Range(2, 6), Y = -r.Range(4, Bh - 4), Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp, G = 1, Drag = 0.5, Bounce = 0.35,
                        Life = r.Range(3, 3.8), A = r.Range(0, Tau), Va = r.Range(-16, 16), S = r.Range(9, 13), Heat = r.Range(0.4, 0.9),
                    });
                }
                // the lid, flung high and spinning
                _lid.On = true;
                _lid.X = 0;
                _lid.Y = -Bh;
                _lid.Vx = r.Range(-40, 40);
                _lid.Vy = -r.Range(430, 480);
                _lid.A = 0;
                _lid.Va = r.Sign() * r.Range(10, 14);
                // the fuel on the floor, and the floor's own dust and grit
                for (var i = 0; i < 7; i++)
                {
                    var x = r.Range(-34, 34);
                    _pools.Add(new Puddle(x, r.Range(5, 10)));
                    _blazes.Add(x, -0.25, r.Range(2.2, 3), r.Range(0.8, 1.3));
                }
                for (var i = 0; i < 12; i++)
                {
                    var s = r.Sign();
                    ps.Spawn(P.Smoke, s * r.Range(0, 10), -r.Range(1, 4), s * r.Range(80, 220), -r.Range(2, 12), r.Range(1.1, 1.8), Dust, r.Range(6, 9), r.Range(0, 0.05), PF.Big);
                }
                for (var i = 0; i < 60; i++)
                {
                    var u = r.Range(-1, 1);
                    var a = -Math.PI / 2 + u * 0.7;
                    var sp = r.Range(120, 330) * (1 - 0.4 * Math.Abs(u));
                    var life = r.Range(1.1, 1.9);
                    var col = r.Pick([Hex("#5a4f6b"), Hex("#3e3650"), Hex("#776c88")]);
                    double size = 1;
                    if (r.Chance(0.4))
                    {
                        size = 2;
                    }
                    ps.Spawn(P.Grit, u * 6, -1, Math.Cos(a) * sp, Math.Sin(a) * sp, life, col, size);
                }
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                var lid = _lid;
                _t += dt;
                var t = _t;
                if (!_blown)
                {
                    // the drum strains: flame and smoke spitting from the seam under the lid, quicker as it goes
                    var k = t / Blow;
                    if (r.Chance((6 + 30 * k * k) * dt))
                    {
                        var s = r.Sign();
                        ps.Spawn(P.Fire, s * r.Range(4, 7), -Bh + 1, s * r.Range(20, 60), -r.Range(20, 60), r.Range(0.15, 0.3), 0, r.Range(1.5, 3));
                        ps.Spawn(P.Spark, s * 6, -Bh + 1, s * r.Range(40, 140), -r.Range(20, 120), r.Range(0.1, 0.25));
                    }
                    if (r.Chance(10 * dt))
                    {
                        ps.Spawn(P.Smoke, r.Range(-5, 5), -Bh, r.Range(-8, 8), -r.Range(15, 35), r.Range(0.8, 1.3), 0, r.Range(2.5, 4));
                    }
                    if (t >= Blow)
                    {
                        DoBlow();
                    }
                }
                if (lid.On && !lid.Rest)
                {
                    lid.Vy += 950 * dt;
                    lid.Vx -= lid.Vx * 0.4 * dt;
                    lid.X += lid.Vx * dt;
                    lid.Y += lid.Vy * dt;
                    lid.A += lid.Va * dt;
                    if (lid.Y >= -1 && lid.Vy > 0)
                    {
                        // a clank: sparks and dust where it lands, then it bounces flat and settles
                        for (var i = 0; i < 6; i++)
                        {
                            ps.Spawn(P.Spark, lid.X, -1, r.Range(-90, 90), -r.Range(40, 140), r.Range(0.1, 0.25));
                        }
                        for (var i = 0; i < 5; i++)
                        {
                            ps.Spawn(P.Dust, lid.X + r.Range(-4, 4), -0.5, r.Sign() * r.Range(15, 60), -r.Range(4, 18), r.Range(0.35, 0.7), Dust, r.Range(1.8, 3));
                        }
                        lid.Y = -1;
                        if (lid.Vy > 160)
                        {
                            lid.Vy *= -0.3;
                            lid.Vx *= 0.5;
                            lid.Va *= 0.5;
                        }
                        else
                        {
                            lid.Rest = true;
                            lid.A = Math.PI / 2;
                        }
                    }
                }
                _staves.Update(dt);
                for (var i = _staves.List.Count - 1; i >= 0; i--)
                {
                    var b = _staves.List[i];
                    if (b.X < _x0 + 8 || b.X > _x1 - 8)
                    {
                        _staves.List.RemoveAt(i);
                    }
                    else if (b.Heat > 0)
                    {
                        b.Heat = Math.Max(0, b.Heat - dt * 0.35);
                    }
                }
                _blazes.Update(dt);
                ps.Update(dt);
                Cull(ps, _x0, _y0, _x1, _y1);
                FadeEdges(ps, _x0, _x1, 24, _y1);
                _lights.Update(dt);
            }

            private void Wave(PixelPainter p, bool far)
            {
                var k = (_t - Blow) / 0.45;
                if (k >= 1)
                {
                    return;
                }
                Func<double, bool> keep = g => Math.Sin(g) >= 0;
                if (far)
                {
                    keep = g => Math.Sin(g) < 0;
                }
                Sim.Band(p, 0, 0.5, 100 * Ease.OutCubic(k), 0.16, Math.Max(1, 4.5 * (1 - k)), 1.15 - k, [Hex("#4a3f69"), Hex("#6e6390"), _f[3], _f[6]], keep);
            }

            public override void DrawBack(PixelPainter p, double time, PixelFxOptions o)
            {
                var f = _f;
                var t = _t;
                if (_blown)
                {
                    var u = t - Blow;
                    var fade = 1 - Smooth(Duration - Blow - 1, Duration - Blow - 0.2, u);
                    Scorch(p, _hr, u, 30, 2.2, fade, 3);
                    // the spilled fuel: a dark slick on the floor, lit by its own fire, shrinking as it burns off
                    var left = 1 - Smooth(1.6, 3.4, u);
                    if (left > 0)
                    {
                        foreach (var q in _pools)
                        {
                            for (var x = Math.Floor(-q.W * left); x <= Math.Ceiling(q.W * left); x++)
                            {
                                var e = Math.Abs(x) / (q.W * left + 0.01);
                                var c = Oil;
                                if (Rand(Math.Floor(q.X + x), Math.Floor(t * 12)) < 0.25)
                                {
                                    c = f[4];
                                }
                                var a = fade * 0.6;
                                if (e < 0.8)
                                {
                                    a = fade;
                                }
                                p.Dot(q.X + x, 1, c, a);
                                if (e < 0.6)
                                {
                                    p.Dot(q.X + x, 2, Oil, fade * 0.8);
                                }
                            }
                        }
                    }
                    Wave(p, true);
                }
                DrawPs(p, _ps, "back", _fr);
                _blazes.DrawBack(p);
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var f = _f;
                var t = _t;
                if (!_blown)
                {
                    // the barrel, shaking harder, its lid bulging
                    var k = t / Blow;
                    var jx = Math.Sin(t * 70) * k * k * 1.4;
                    var jy = Math.Abs(Math.Sin(t * 53)) * k * k * 0.8;
                    Barrel(p, _c, jx, -jy, k);
                    p.Glow(0, -Bh, 10 + 8 * k, f[4], 0.25 + 0.35 * k * k * (0.6 + 0.4 * Math.Sin(t * 40)));
                }
                else
                {
                    Wave(p, false);
                }
                DrawPs(p, _ps, "front", _fr);
                foreach (var b in _staves.List)
                {
                    Stave(p, b, _c, _fr);
                }
                if (_lid.On)
                {
                    DrawLid(p, _c, _lid, Math.Min(1, (Duration - t) / 0.4));
                }
                if (_blown)
                {
                    var u = t - Blow;
                    if (u < 0.08)
                    {
                        var k2 = 1 - u / 0.08;
                        var r = 18 * (0.85 + 0.15 * k2);
                        const double y = -Bh * 0.5;
                        p.Circle(0, y, r + 1.4, f[2]);
                        var mid = f[5];
                        var core = f[6];
                        if (k2 > 0.45)
                        {
                            mid = f[6];
                            core = White;
                        }
                        p.Circle(0, y, r, mid);
                        p.Circle(0, y, r * 0.6, core);
                    }
                    if (u < 0.22)
                    {
                        Sim.Flare(p, 0, -Bh * 0.5, 54 * Math.Pow(1 - u / 0.22, 1.4), 1 - u / 0.22, f);
                    }
                    // the burning fuel lights the floor
                    Pool(p, 0, 1, 90, 14, f[3], 0.5 * Math.Max(0, 1 - u / 0.7) + 0.22 * (1 - Smooth(1.6, 3.4, u)));
                }
                _lights.Draw(p);
            }
        }

        // a steel drum on the floor at x: two ribs, a lit left side, a dark right side, an outline, a pale band; the lid bulges with k
        private static void Barrel(PixelPainter p, uint[] c, double x, double y, double k)
        {
            var bulge = Round(k * k * 2);
            p.Shade(x - Bw - 1, y - Bh - 3, x + Bw + 1, y + 0.5, (px, py, _) =>
            {
                var dx = (px - x) / Bw;
                var v = (y - py) / Bh;
                if (Math.Abs(dx) > 1.1 || v < 0 || v > 1.05)
                {
                    return 0;
                }
                var edge = Math.Abs(dx) > 0.92 || v > 0.97 || v < 0.04;
                if (edge)
                {
                    return Outline;
                }
                var rib = Math.Abs(v - 0.33) < 0.035 || Math.Abs(v - 0.67) < 0.035;
                var ribLit = Math.Abs(v - 0.31) < 0.03 || Math.Abs(v - 0.65) < 0.03;
                var band = v > 0.44 && v < 0.56;
                var lit = -dx;
                int i;
                if (lit > 0.55)
                {
                    i = 5;
                }
                else if (lit > 0.15)
                {
                    i = 4;
                }
                else if (lit > -0.35)
                {
                    i = 3;
                }
                else if (lit > -0.75)
                {
                    i = 2;
                }
                else
                {
                    i = 1;
                }
                if (band)
                {
                    if (lit > 0.3)
                    {
                        return Hex("#e8d8b0");
                    }
                    if (lit > -0.4)
                    {
                        return Hex("#b8a888");
                    }
                    return Hex("#6e6050");
                }
                if (rib)
                {
                    i = Math.Max(1, i - 2);
                }
                else if (ribLit)
                {
                    i = Math.Min(5, i + 1);
                }
                return c[i];
            });
            // the lid: a dark ellipse with a lit rim, pushed up as the drum swells
            p.Ellipse(x, y - Bh - bulge * 0.5, Bw - 0.5, 2.2 + bulge * 0.4, (d, a) =>
            {
                if (d > 0.7)
                {
                    if (Math.Sin(a) < 0)
                    {
                        return c[4];
                    }
                    return c[2];
                }
                return c[1];
            });
        }

        // a stave: a curved plate torn from the drum, its painted face and its dark inside showing as it turns; hot edges glow
        private static void Stave(PixelPainter p, Bit b, uint[] c, uint[] fr)
        {
            var fade = Math.Min(1, b.Life / 0.5);
            var ca = Math.Cos(b.A);
            var sa = Math.Sin(b.A);
            var l = b.S;
            var face = Math.Cos(b.A * 1.7) > 0;
            for (var d = -l / 2; d <= l / 2; d += 0.5)
            {
                var bow = (1 - Math.Pow(2 * d / l, 2)) * 1.2;
                var x = b.X + ca * d - sa * bow;
                var y = b.Y + sa * d + ca * bow;
                var hot = b.Heat > 0.3 && Math.Abs(d) > l / 2 - 1;
                p.Dot(x + sa, y - ca, Outline, fade);
                uint mid;
                if (hot)
                {
                    mid = fr[5];
                    if (b.Heat > 0.6)
                    {
                        mid = fr[3];
                    }
                }
                else if (face)
                {
                    mid = c[4];
                }
                else
                {
                    mid = c[1];
                }
                p.Dot(x, y, mid, fade);
                var inner = Outline;
                if (face)
                {
                    inner = c[2];
                }
                p.Dot(x - sa, y + ca, inner, fade);
            }
            if (b.Heat > 0.3)
            {
                p.Glow(b.X, b.Y, 6, fr[4], 0.25 * b.Heat * fade);
            }
        }

        // the lid: a disc turning as it flies (an ellipse whose width follows its spin), lying flat once it lands
        private static void DrawLid(PixelPainter p, uint[] c, Lid lid, double a)
        {
            var w = Math.Max(0.8, Math.Abs(Math.Cos(lid.A)) * 7.5);
            var h = Math.Max(1.2, Math.Abs(Math.Sin(lid.A)) * 7.5);
            var rx = w;
            var ry = h;
            double lift = 0;
            if (lid.Rest)
            {
                rx = 7.5;
                ry = 1.6;
                lift = 0.5;
            }
            p.Ellipse(lid.X, lid.Y - lift, rx + 0.8, ry + 0.8, Outline, a);
            p.Ellipse(lid.X, lid.Y - lift, rx, ry, (d, an) =>
            {
                if (d > 0.65)
                {
                    if (Math.Sin(an) < 0)
                    {
                        return c[4];
                    }
                    return c[2];
                }
                return c[3];
            }, a);
        }
    }
}
