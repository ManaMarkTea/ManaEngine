using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Explosions.ExplosionsLib;

namespace ManaPixelFx.Effects.Explosions
{
    /// <summary>
    /// pixelfx's explosions/firework-rocket.js: a rocket fizzes off the ground on a sparkling gold trail, coasts, and
    /// bursts into a palm whose heavy gold fronds arc out, droop on glittering trails and crackle at the tips. The flare,
    /// glitter and crackle are adapted from Sprite Fusion's open-source sparkle, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class FireworkRocket : PixelEffect
    {
        private const double Fly = 1.05;
        private const double High = 192;
        private const double Drag = 1.35;
        private const double G = 105;
        private const double TrailLife = 1.15;
        private static readonly (int X, int Y)[] Studs = [(-1, 0), (2, 0), (0, -1), (0, 2)];

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "explosions/firework-rocket",
            Name = "Firework rocket",
            Description = "A rocket fizzes off the ground on a sparkling gold trail, coasts, and bursts into a palm: heavy gold fronds arc out, droop on glittering trails and crackle at the tips.",
            Size = (300, 300),
            Origin = (150, 288),
            Duration = 3.8,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#2a1406", "#6a3a0c", "#b8701a", "#f0a830", "#ffd860", "#fff2b0", "#ffffff" },
                ["tips"] = new[] { "#0c2a3a", "#1a5a78", "#36a2c8", "#7cd8f0", "#c8f4ff", "#ffffff" },
                ["seed"] = 12.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o, Meta);

        private sealed class Frond
        {
            public double X, Y, Vx, Vy, Age, Life, Acc, Seed;
            public List<double> Pts = [];
        }

        private static (double X, double Y) At(double t) => (Math.Sin(t * 8) * 1.6 * (t / Fly), -8 - High * Ease.OutQuad(Clamp(t / Fly)));

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly uint[] _tips;
            private readonly PixelRng _r;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly List<Frond> _fronds = [];
            private readonly List<FireworkBurst.Pop> _pops = [];
            private readonly PixelEffectMeta _meta;
            private double _t;
            private bool _burst;
            private double _bx;
            private double _by;
            private double _acc;

            public Run(PixelFxOptions o, PixelEffectMeta meta)
            {
                _meta = meta;
                _c = Fit(o.Ramp("palette"), 7);
                _tips = Fit(o.Ramp("tips"), 6);
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                _ps = new Particles(rng: r, sparks: [_c[6], _c[5], _c[4], _c[3]]);
                _lights.Add(0, -14, 24, _c[4], 0.7, 0.35);
                // ignition: a puff of smoke and a spray of sparks off the ground
                for (var i = 0; i < 18; i++)
                {
                    var a = -Math.PI / 2 + r.Range(-1.2, 1.2);
                    var sp = r.Range(60, 180);
                    _ps.Spawn(P.Spark, 0, -2, Math.Cos(a) * sp, Math.Sin(a) * sp * 0.6, r.Range(0.15, 0.4));
                }
                for (var i = 0; i < 5; i++)
                {
                    _ps.Spawn(P.Vapour, r.Range(-3, 3), -2, r.Range(-20, 20), -r.Range(2, 10), r.Range(0.6, 1), Hex("#5a5468"), r.Range(3, 5));
                }
            }

            public override bool HasBack => true;

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                var c = _c;
                _t += dt;
                var t = _t;
                if (t < Fly)
                {
                    // the climb: a hot tail spitting gold sparks, glitter left hanging, a thread of smoke
                    var (x, y) = At(t);
                    var (x2, y2) = At(Math.Max(0, t - 0.02));
                    var vy = (y - y2) / 0.02;
                    var vx = (x - x2) / 0.02;
                    for (_acc += dt * 90; _acc > 1; _acc--)
                    {
                        var a = Math.PI / 2 + r.Range(-0.45, 0.45);
                        var sp = r.Range(30, 110);
                        ps.Spawn(P.Spark, x, y + 4, Math.Cos(a) * sp + vx * 0.2, Math.Sin(a) * sp + vy * 0.15, r.Range(0.15, 0.4));
                        if (r.Chance(0.25))
                        {
                            var sx = x + r.Range(-1.5, 1.5);
                            var sy = y + r.Range(2, 6);
                            var svx = r.Range(-6, 6);
                            var svy = r.Range(5, 20);
                            var life = r.Range(0.4, 0.9);
                            var col = White;
                            if (r.Chance(0.5))
                            {
                                col = c[5];
                            }
                            var size = 0.5;
                            if (r.Chance(0.15))
                            {
                                size = 1.2;
                            }
                            ps.Spawn(P.Star, sx, sy, svx, svy, life, col, size);
                        }
                        if (r.Chance(0.18))
                        {
                            ps.Spawn(P.Vapour, x, y + 5, r.Range(-4, 4), r.Range(4, 12), r.Range(0.6, 1.1), Hex("#4a4458"), r.Range(1.2, 2.2));
                        }
                    }
                }
                else if (!_burst)
                {
                    _burst = true;
                    (_bx, _by) = At(Fly);
                    var bx = _bx;
                    var by = _by;
                    _lights.Add(bx, by, 82, c[4], 0.8, 1.6);
                    _lights.Add(bx, by, 40, White, 0.6, 0.08);
                    // the palm: heavy comets thrown up and out, a few low ones to the sides
                    for (var i = 0; i < 9; i++)
                    {
                        var a = -Math.PI / 2 + ((i - 4) / 4.0) * 1.4 + r.Range(-0.1, 0.1);
                        var sp = r.Range(180, 210) * (1 - 0.12 * Math.Abs(i - 4) / 4);
                        var frond = new Frond { X = bx, Y = by, Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp, Age = 0 };
                        frond.Life = r.Range(1.8, 2.15);
                        frond.Acc = r.Next();
                        frond.Seed = r.Range(0, 100);
                        _fronds.Add(frond);
                    }
                    for (var i = 0; i < 6; i++)
                    {
                        ps.Spawn(P.Vapour, bx + r.Range(-4, 4), by + r.Range(-4, 4), r.Range(-20, 20), r.Range(-15, 15), r.Range(1.8, 2.6), Hex("#4a4458"), r.Range(6, 10));
                    }
                    for (var i = 0; i < 30; i++)
                    {
                        var a = r.Range(0, Tau);
                        var sp = r.Range(40, 160);
                        ps.Spawn(P.Spark, bx, by, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.15, 0.35));
                    }
                }
                for (var i = _fronds.Count - 1; i >= 0; i--)
                {
                    var f = _fronds[i];
                    f.Age += dt;
                    var k = f.Age / f.Life;
                    if (k >= 1)
                    {
                        for (var j = 0; j < 4; j++)
                        {
                            var x = f.X + r.Range(-5, 5);
                            var y = f.Y + r.Range(-5, 5);
                            var t0 = t + r.Range(0, 0.2);
                            var life = r.Range(0.06, 0.12);
                            var col = _tips[3];
                            if (r.Chance(0.5))
                            {
                                col = White;
                            }
                            _pops.Add(new FireworkBurst.Pop(x, y, t0, life, col));
                        }
                        _fronds.RemoveAt(i);
                        continue;
                    }
                    f.Vx -= f.Vx * Drag * dt;
                    f.Vy -= f.Vy * Drag * dt - G * dt;
                    f.X += f.Vx * dt;
                    f.Y += f.Vy * dt;
                    f.Pts.Add(f.X);
                    f.Pts.Add(f.Y);
                    f.Pts.Add(t);
                    while (f.Pts.Count > 3 && t - f.Pts[2] > TrailLife)
                    {
                        f.Pts.RemoveRange(0, 3);
                    }
                    // glitter shed all along: specks that hang, twinkle and drift down
                    for (f.Acc += dt * 34; f.Acc > 1; f.Acc--)
                    {
                        var x = f.X + r.Range(-1, 1);
                        var y = f.Y + r.Range(-1, 1);
                        var vx = f.Vx * 0.1 + r.Range(-5, 5);
                        var vy = r.Range(0, 14);
                        var life = r.Range(0.5, 1.1);
                        var col = White;
                        if (r.Chance(0.6))
                        {
                            col = c[5];
                        }
                        var size = 0.5;
                        if (r.Chance(0.12))
                        {
                            size = r.Range(1, 1.5);
                        }
                        ps.Spawn(P.Star, x, y, vx, vy, life, col, size);
                    }
                }
                for (var i = _pops.Count - 1; i >= 0; i--)
                {
                    if (t > _pops[i].T0 + _pops[i].Life)
                    {
                        _pops.RemoveAt(i);
                    }
                }
                ps.Update(dt);
                var (w, h) = _meta.Size;
                var (ox, oy) = _meta.Origin;
                Cull(ps, -ox + 2, -oy + 2, w - ox - 2, h - oy - 2);
                _lights.Update(dt);
            }

            public override void DrawBack(PixelPainter p, double time, PixelFxOptions o) => _ps.Draw(p, "back");

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                if (t < Fly)
                {
                    // the rocket: a little tube on a stick, a white-hot tail flickering under it
                    var (x, y) = At(t);
                    p.Line(x, y + 4, x, y + 12, Hex("#5a4030"));
                    p.Rect(x - 1, y - 3, 3, 7, Hex("#b8303a"));
                    p.Px(x - 1, y - 3, Hex("#e86070"));
                    p.Px(x, y - 4, Hex("#d8d0c0"));
                    double fl = 2;
                    if (Math.Sin(t * 70) > 0)
                    {
                        fl = 3;
                    }
                    p.Line(x, y + 4, x, y + 4 + fl, u =>
                    {
                        if (u < 0.4)
                        {
                            return White;
                        }
                        return c[5];
                    });
                    p.Glow(x, y + 5, 9, c[4], 0.6);
                }
                foreach (var f in _fronds)
                {
                    var k = f.Age / f.Life;
                    var pts = f.Pts;
                    var n = pts.Count / 3;
                    // the frond: a thick gold trail, hot near the head, cooling and dithering out behind
                    for (var j = n - 1; j > 0; j--)
                    {
                        var u = (t - pts[j * 3 + 2]) / TrailLife;
                        if (u >= 1)
                        {
                            break;
                        }
                        uint col;
                        if (u < 0.08)
                        {
                            col = c[6];
                        }
                        else if (u < 0.25)
                        {
                            col = c[5];
                        }
                        else if (u < 0.5)
                        {
                            col = c[4];
                        }
                        else if (u < 0.75)
                        {
                            col = c[3];
                        }
                        else
                        {
                            col = c[2];
                        }
                        var a = Math.Pow(1 - u, 0.6) * (1.25 - k * 0.45);
                        p.Line(pts[j * 3], pts[j * 3 + 1], pts[j * 3 - 3], pts[j * 3 - 2], col, a);
                        if (u < 0.7)
                        {
                            var side = c[2];
                            if (u < 0.25)
                            {
                                side = c[5];
                            }
                            else if (u < 0.5)
                            {
                                side = c[3];
                            }
                            p.Line(pts[j * 3] + 1, pts[j * 3 + 1], pts[j * 3 - 3] + 1, pts[j * 3 - 2], side, a * 0.85);
                        }
                        if (u < 0.35)
                        {
                            p.Line(pts[j * 3], pts[j * 3 + 1] + 1, pts[j * 3 - 3], pts[j * 3 - 2] + 1, c[3], a * 0.6);
                        }
                    }
                    if (k > 0.8 && Math.Sin(t * 55 + f.Seed) < 0)
                    {
                        continue;
                    }
                    var head = c[6];
                    if (k < 0.5)
                    {
                        head = White;
                    }
                    p.Rect(f.X - 0.5, f.Y - 0.5, 2, 2, head);
                    if (k < 0.6)
                    {
                        foreach (var (dx, dy) in Studs)
                        {
                            p.Px(f.X + dx, f.Y + dy, c[5]);
                        }
                    }
                    p.Glow(f.X, f.Y, 10, c[4], 0.6 * (1 - k * 0.5));
                }
                foreach (var q in _pops)
                {
                    var u = (t - q.T0) / q.Life;
                    if (u < 0 || u > 1)
                    {
                        continue;
                    }
                    Sim.Sparkle(p, q.X, q.Y, 2.8 * Math.Sin(Math.PI * u), q.C, _tips[2], 0.5);
                }
                // the burst: a white heart for two frames, a flare turning
                var b = t - Fly;
                if (b >= 0 && b < 0.26)
                {
                    var u = b / 0.26;
                    var l = 30 * Math.Pow(1 - u, 1.3);
                    for (var i = 0; i < 8; i++)
                    {
                        var diag = i % 2 == 1;
                        var an = u * 0.5 + (i * Tau) / 8;
                        var len = l;
                        if (diag)
                        {
                            len = l * 0.45;
                        }
                        var ca = Math.Cos(an);
                        var sa = Math.Sin(an);
                        for (double d = 0; d <= len; d += 0.5)
                        {
                            var v = d / len;
                            var col = c[4];
                            if (v < 0.3)
                            {
                                col = White;
                            }
                            else if (v < 0.65)
                            {
                                col = c[5];
                            }
                            var a = 1 - (v - 0.7) / 0.35;
                            if (v < 0.7)
                            {
                                a = 1;
                            }
                            p.Dot(_bx + ca * d, _by + sa * d, col, a);
                        }
                    }
                    if (b < 0.07)
                    {
                        p.Circle(_bx, _by, 2.5, White);
                    }
                    p.Glow(_bx, _by, 4 + l * 1.2, c[4], 0.35);
                }
                _ps.Draw(p, "front");
                _lights.Draw(p);
            }
        }
    }
}
