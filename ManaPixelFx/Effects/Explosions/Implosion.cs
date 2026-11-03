using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Explosions.ExplosionsLib;

namespace ManaPixelFx.Effects.Explosions
{
    /// <summary>
    /// pixelfx's explosions/implosion.js: streaks and grit rush inward, rings contract and a dark sphere collapses onto a
    /// crushed white point; a beat of stillness, then a sharp pop. The gathering streaks, pulse rings and release are
    /// adapted from Sprite Fusion's open-source energy charge, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class Implosion : PixelEffect
    {
        private const double Suck = 0.8;
        private const double Still = 1.02;
        private static readonly uint Void = Hex("#05040a");

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "explosions/implosion",
            Name = "Implosion",
            Description = "Everything is sucked in: streaks and grit rush inward, rings contract and a dark sphere collapses onto a crushed white point; a beat of stillness, then a sharp pop.",
            Size = (208, 184),
            Origin = (104, 92),
            Duration = 1.65,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#0a0614", "#1c1236", "#3a2470", "#6a44c0", "#a080f0", "#d8c8ff", "#ffffff" },
                ["seed"] = 2.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o, Meta);

        private readonly record struct Speck(double A, double D, double T0, double T, double Spin, uint C);

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly PixelRng _r;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly List<Speck> _grit = [];
            private readonly double[] _rays;
            private readonly PixelEffectMeta _meta;
            private double _t;
            private bool _popped;

            public Run(PixelFxOptions o, PixelEffectMeta meta)
            {
                _meta = meta;
                _c = Fit(o.Ramp("palette"), 7);
                var c = _c;
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                _ps = new Particles(rng: r, sparks: [c[6], c[5], c[4], c[3]]);
                // grit pulled in: specks that spiral toward the middle, faster and faster
                for (var i = 0; i < 70; i++)
                {
                    var t0 = r.Range(0, 0.4);
                    var a = r.Range(0, Tau);
                    var d = r.Range(40, 86);
                    var tt = r.Range(0.3, Suck - 0.04 - t0);
                    var spin = r.Sign() * r.Range(0.6, 1.6);
                    var col = r.Pick([c[2], c[3], c[4], Hex("#4a4458"), Hex("#6e6880")]);
                    _grit.Add(new Speck(a, d, t0, tt, spin, col));
                }
                _rays = Enumerable.Range(0, 4).Select(i => (i * Tau) / 4 + Math.PI / 4).ToArray();
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var c = _c;
                _t += dt;
                var t = _t;
                if (t < Suck - 0.26)
                {
                    // streaks of light rushing in from all round, accelerating, more of them as it goes
                    var k = t / Suck;
                    for (var n = 0; n < 1 + Math.Floor(k * 4); n++)
                    {
                        if (!r.Chance(0.9))
                        {
                            continue;
                        }
                        var a = r.Range(0, Tau);
                        var d = r.Range(34, 82) * (1 - 0.3 * k);
                        var life = r.Range(0.14, 0.24);
                        var v = (d * 3) / (Math.Exp(3 * life) - 1);
                        var col = c[4];
                        if (r.Chance(0.5))
                        {
                            col = c[5];
                        }
                        _ps.Spawn(P.Streak, Math.Cos(a) * d, Math.Sin(a) * d, -Math.Cos(a) * v, -Math.Sin(a) * v, life, col);
                    }
                    // vapour drawn in along the way
                    if (r.Chance(14 * dt))
                    {
                        var a = r.Range(0, Tau);
                        var d = r.Range(50, 80);
                        _ps.Spawn(P.Vapour, Math.Cos(a) * d, Math.Sin(a) * d, -Math.Cos(a) * 90, -Math.Sin(a) * 90, r.Range(0.4, 0.6), c[3], r.Range(3, 5));
                    }
                }
                if (!_popped && t >= Still)
                {
                    _popped = true;
                    _lights.Add(0, 0, 90, c[4], 1, 0.4);
                    _lights.Add(0, 0, 30, White, 0.9, 0.08);
                    for (var i = 0; i < 34; i++)
                    {
                        var a = r.Range(0, Tau);
                        var sp = r.Range(120, 330);
                        var kind = P.Star;
                        if (r.Chance(0.6))
                        {
                            kind = P.Mote;
                        }
                        _ps.Spawn(kind, 0, 0, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.2, 0.45), r.Pick([c[6], c[5], c[4]]), r.Range(0.8, 1.8));
                    }
                }
                _ps.Update(dt);
                var (w, h) = _meta.Size;
                var (ox, oy) = _meta.Origin;
                Cull(_ps, -ox + 3, -oy + 3, w - ox - 3, h - oy - 3);
                _lights.Update(dt);
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var c = _c;
                var t = _t;
                var k = Clamp(t / Suck);
                if (t < Still)
                {
                    // the void: a dark sphere edged with light, collapsing onto the core
                    var vr = 64 * (1 - Ease.InCubic(k)) + 3;
                    if (k < 1)
                    {
                        p.Disc(0, 0, vr, Void, 0.35 + 0.35 * k);
                        var edge = c[4];
                        if (k < 0.5)
                        {
                            edge = c[3];
                        }
                        p.Ring(0, 0, vr + 1, 1, edge, 0.5 + 0.5 * k);
                        p.GlowRing(0, 0, vr, 5, c[4], 0.18 + 0.3 * k);
                    }
                    // rings pulled in after it, one after another, brighter as they go
                    for (var i = 0; i < 4; i++)
                    {
                        var u = (t - i * 0.17) / 0.34;
                        if (u <= 0 || u >= 1)
                        {
                            continue;
                        }
                        var rr = (70 - i * 8) * (1 - Ease.InQuad(u)) + 2;
                        double width = 1;
                        if (u > 0.7)
                        {
                            width = 2;
                        }
                        var col = c[3];
                        if (u > 0.6)
                        {
                            col = c[5];
                        }
                        p.Ring(0, 0, rr, width, col, 0.4 + 0.6 * u);
                    }
                    // the grit, spiralling in
                    foreach (var q in _grit)
                    {
                        var u = (t - q.T0) / q.T;
                        if (u <= 0 || u >= 1)
                        {
                            continue;
                        }
                        var d = q.D * (1 - Ease.InCubic(u));
                        var a = q.A + q.Spin * Ease.InQuad(u) * 2;
                        var x = Math.Cos(a) * d;
                        var y = Math.Sin(a) * d;
                        var x2 = Math.Cos(a - q.Spin * 0.1) * (d + 4 * u * u + 0.5);
                        var y2 = Math.Sin(a - q.Spin * 0.1) * (d + 4 * u * u + 0.5);
                        var col = q.C;
                        p.Line(x2, y2, x, y, v =>
                        {
                            if (v < 0.5)
                            {
                                return Mix(col, Void, 0.4);
                            }
                            return col;
                        });
                    }
                    // the core: growing hotter as it is crushed, then a white point trembling in the stillness
                    var still = t > Suck;
                    double jit = 0;
                    var cr = 1 + 3.5 * (1 - k) * k * 4;
                    if (still)
                    {
                        jit = -0.5;
                        if (Math.Floor(t * 40) % 2 != 0)
                        {
                            jit = 0.5;
                        }
                        cr = 1.6;
                    }
                    p.Disc(jit, 0, cr + 3.5, Void, 0.85);
                    p.Circle(jit, 0, cr + 1.2, c[4]);
                    p.Circle(jit, 0, cr, White);
                    p.Glow(0, 0, 10 + 26 * k, c[4], 0.3 + 0.5 * k);
                    if (still)
                    {
                        p.Glow(0, 0, 8, White, 0.4 + 0.2 * Math.Sin(t * 60));
                    }
                }
                else
                {
                    // the pop: a white point bursting, a cross flare, a fast thin ring and a slower one
                    var q = (t - Still) / 0.5;
                    var e = Ease.OutCubic(Clamp(q));
                    if (q < 0.08)
                    {
                        p.Circle(0, 0, 6 * (1 - q / 0.08) + 1, White);
                    }
                    if (q < 0.35)
                    {
                        var l = (1 - q / 0.35) * 34 + 3;
                        foreach (var a in _rays)
                        {
                            for (double d = 0; d <= l; d += 0.5)
                            {
                                var col = c[5];
                                if (d < l * 0.4)
                                {
                                    col = White;
                                }
                                var alpha = 1 - (d / l - 0.7) / 0.3;
                                if (d < l * 0.7)
                                {
                                    alpha = 1;
                                }
                                p.Dot(Math.Cos(a) * d, Math.Sin(a) * d, col, alpha);
                            }
                        }
                        foreach (var a in _rays)
                        {
                            for (double d = 0; d <= l * 0.45; d += 0.5)
                            {
                                p.Dot(Math.Cos(a + Math.PI / 4) * d, Math.Sin(a + Math.PI / 4) * d, c[5], 1 - d / (l * 0.45));
                            }
                        }
                    }
                    if (q < 1)
                    {
                        var col = c[5];
                        if (q < 0.2)
                        {
                            col = White;
                        }
                        p.Ring(0, 0, 4 + 74 * e, Math.Max(1, 2.5 * (1 - q)), col, 1.15 - q);
                        p.Ring(0, 0, 2 + 40 * Ease.OutCubic(Clamp(q * 1.4)), 1, c[4], 1 - q);
                    }
                    if (q < 0.6)
                    {
                        Sim.Sparkle(p, 0, 0, 5 * (1 - q / 0.6), White, c[5], 0.6);
                    }
                }
                _ps.Draw(p);
                _lights.Draw(p);
            }
        }
    }
}
