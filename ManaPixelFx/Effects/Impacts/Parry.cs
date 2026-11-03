using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Impacts.ImpactsLib;

namespace ManaPixelFx.Effects.Impacts
{
    /// <summary>
    /// pixelfx's impacts/parry.js: two blades clash -- their edges flash in an X, a white four-point star rings out, a
    /// ring breaks into arcs and a fan of gold sparks sprays up and rains down. The star's key frames and the spark spray
    /// are adapted from Sprite Fusion's open-source impact, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class Parry : PixelEffect
    {
        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "impacts/parry",
            Name = "Parry",
            Description = "Two blades clash: their edges flash in an X, a white four-point star rings out, a ring breaks into arcs and a fan of gold sparks sprays up and rains down.",
            Size = (200, 112),
            Origin = (100, 96),
            Duration = 1,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#2a1a08", "#6a4410", "#b07a1a", "#e8b030", "#ffe066", "#fff6c4", "#ffffff" },
                ["height"] = 30.0,
                ["seed"] = 11.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly double _y;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly List<Spike> _sp = [];
            private readonly List<RingSegment> _segs = [];
            private readonly double[] _edges;
            private double _t;

            public Run(PixelFxOptions o)
            {
                _c = Fit(o.Ramp("palette"), 7);
                var c = _c;
                var r = new PixelRng(o.Number("seed"));
                const double x = 0;
                _y = -o.Number("height");
                var y = _y;
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [c[4], c[4], c[3], c[2]]);
                // a hard four-point star: long arms on the axes, short ones between
                for (var i = 0; i < 8; i++)
                {
                    var a = (i * Math.PI) / 4 + r.Range(-0.04, 0.04);
                    double len;
                    var w = 2.2;
                    if (i % 2 != 0)
                    {
                        len = r.Range(6, 8);
                        w = 1.2;
                    }
                    else if (i % 4 == 0)
                    {
                        len = r.Range(19, 22);
                    }
                    else
                    {
                        len = r.Range(14, 17);
                    }
                    _sp.Add(new Spike(a, len, w));
                }
                for (var i = 0; i < 6; i++)
                {
                    var a = (i / 6.0) * Tau + r.Range(-0.1, 0.1);
                    var gap = r.Range(0.55, 0.8);
                    _segs.Add(new RingSegment(a, gap, r.Range(0.1, 0.35)));
                }
                // the two edges that met: lines crossing in an X
                _edges = [-0.62 + r.Range(-0.08, 0.08), Math.PI + 0.62 + r.Range(-0.08, 0.08)];

                _lights.Add(x, y, 60, c[4], 0.65, 0.4);
                _lights.Add(x, y, 22, White, 0.8, 0.08);
                // the fan: sparks thrown up and out to both sides, arcing over and raining down to bounce on the floor
                for (var i = 0; i < 24; i++)
                {
                    double s = -1;
                    if (i % 2 != 0)
                    {
                        s = 1;
                    }
                    var a = -Math.PI / 2 + s * r.Range(0.3, 1.3);
                    var v = r.Range(130, 290);
                    var sx = x + s * r.Range(1, 3);
                    var life = r.Range(0.45, 0.85);
                    double delay = 0;
                    if (i > 17)
                    {
                        delay = r.Range(0.02, 0.07);
                    }
                    _ps.Spawn(P.Spark, sx, y, Math.Cos(a) * v, Math.Sin(a) * v, life, White, 1, delay);
                }
                for (var i = 0; i < 8; i++)
                {
                    var a = r.Range(0, Tau);
                    var v = r.Range(40, 110);
                    _ps.Spawn(P.Mote, x, y, Math.Cos(a) * v, Math.Sin(a) * v - 20, r.Range(0.3, 0.55), r.Pick([c[5], c[6]]), 0.6, r.Range(0, 0.04));
                }
                for (var i = 0; i < 3; i++)
                {
                    var a = r.Range(0, Tau);
                    var d = r.Range(12, 26);
                    _ps.Spawn(P.Star, x + Math.Cos(a) * d, y + Math.Sin(a) * d, 0, -3, r.Range(0.22, 0.34), White, r.Range(1.6, 2.6), r.Range(0.1, 0.3));
                }
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                _t += dt;
                _ps.Update(dt);
                _lights.Update(dt);
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                const double x = 0;
                var y = _y;
                // the ring: rings out off the star and breaks into arcs that fly on and cool
                var u = (t - 0.03) / 0.36;
                if (u >= 0 && u < 1)
                {
                    var r = 8 + 24 * Ease.OutCubic(u);
                    var brk = Smooth(0.15, 0.6, u);
                    int i;
                    if (u < 0.15)
                    {
                        i = 6;
                    }
                    else if (u < 0.35)
                    {
                        i = 5;
                    }
                    else if (u < 0.6)
                    {
                        i = 4;
                    }
                    else
                    {
                        i = 3;
                    }
                    var k = (1 - u) / 0.4;
                    if (u < 0.6)
                    {
                        k = 1;
                    }
                    BrokenRing(p, x, y, r, Math.Max(1, 2.6 * Math.Pow(1 - u, 1.2)), _segs, brk, c[i], c[Math.Max(2, i - 2)], k, 1, 0);
                    if (u < 0.5)
                    {
                        p.GlowRing(x, y, r, 5, c[4], 0.3 * (1 - u / 0.5));
                    }
                }
                _ps.Draw(p);
                // the edges flash where they met
                if (t < 0.058)
                {
                    foreach (var a in _edges)
                    {
                        var ca = Math.Cos(a);
                        var sa = Math.Sin(a);
                        double l = 32;
                        var k = 0.6;
                        if (t < 0.025)
                        {
                            l = 26;
                            k = 1;
                        }
                        for (var d = -l; d <= l; d += 0.5)
                        {
                            var q = Math.Abs(d) / l;
                            var col = c[4];
                            if (q < 0.4)
                            {
                                col = White;
                            }
                            else if (q < 0.75)
                            {
                                col = c[5];
                            }
                            var alpha = k * (1.3 - q);
                            if (q < 0.6)
                            {
                                alpha = 1;
                            }
                            p.Dot(x + ca * d, y + sa * d, col, alpha);
                        }
                        p.GlowLine(x - ca * l, y - sa * l, x + ca * l, y + sa * l, 4, c[5], 0.35 * k);
                    }
                }
                // the star: white, then bigger and gold as it thins, then just a glint
                if (t < 0.025)
                {
                    Star(p, x, y, _sp, 1, 1, [c[4], White, White, White], 1, 2.6);
                }
                else if (t < 0.058)
                {
                    Star(p, x, y, _sp, 1.25, 0.65, [c[2], c[5], c[6], White], 1, 2.6);
                }
                else if (t < 0.092)
                {
                    Star(p, x, y, _sp, 1.4, 0.3, [c[2], c[4], c[5], c[6]], 1, 2);
                }
                var pop = Math.Exp(-t / 0.05);
                if (pop > 0.02)
                {
                    p.Glow(x, y, 30, c[4], 0.5 * pop);
                    p.Glow(x, y, 10, White, 0.4 * pop);
                }
                _lights.Draw(p);
            }
        }
    }
}
