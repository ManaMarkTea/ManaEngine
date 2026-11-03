using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Impacts.ImpactsLib;

namespace ManaPixelFx.Effects.Impacts
{
    /// <summary>
    /// pixelfx's impacts/crit-hit.js: a critical hit -- a jagged starburst slams in white and overshoots, a ring breaks
    /// into flying arcs, sparks streak off, shards tumble to the floor under a big flare. The star, its key frames and
    /// the spark spray are adapted from Sprite Fusion's open-source impact, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class CritHit : PixelEffect
    {
        // the star's key frames: from t, scale, thickness, alpha, colours (rim, body, inner, core) as steps of the ramp
        private static readonly (double T, double Sc, double Th, double A, int[] Cols)[] Keys =
        [
            (0, 1, 1, 1, [6, 7, 7, 7]),
            (0.025, 1.3, 1, 1, [2, 5, 6, 7]),
            (0.058, 1.05, 0.72, 1, [2, 4, 5, 6]),
            (0.092, 1.22, 0.3, 1, [1, 2, 3, 4]),
            (0.125, 0, 0, 0, []),
        ];

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "impacts/crit-hit",
            Name = "Critical hit",
            Description = "A critical hit: a jagged starburst slams in white and overshoots, a ring breaks into flying arcs, sparks streak off, shards tumble to the floor under a big flare.",
            Size = (216, 136),
            Origin = (84, 106),
            Duration = 0.9,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#2a0510", "#5c0a1c", "#a3142a", "#e0302c", "#ff7a2a", "#ffc23d", "#fff09a", "#ffffff" },
                ["height"] = 26.0,
                ["aim"] = 0.12,
                ["seed"] = 3.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        // a lens flare: a long level streak, thick and white at the heart, a shorter upright one, on a bed of light
        private static void Flare(PixelPainter p, double x, double y, double l, double k, uint[] c)
        {
            foreach (var (dx, dy, len) in new[] { (1.0, 0.0, l), (0.0, 1.0, l * 0.45), (0.7, 0.7, l * 0.16), (0.7, -0.7, l * 0.16) })
            {
                foreach (var s in new[] { -1, 1 })
                {
                    for (var d = 1; d <= len; d++)
                    {
                        var u = d / len;
                        var a = k * (1.6 - u);
                        if (u < 0.7)
                        {
                            a = 1;
                        }
                        var col = c[5];
                        if (u < 0.35)
                        {
                            col = White;
                        }
                        else if (u < 0.7)
                        {
                            col = c[6];
                        }
                        p.Dot(x + s * dx * d, y + s * dy * d, col, a);
                        if (dy == 0 && u < 0.42)
                        {
                            foreach (var e in new[] { -1, 1 })
                            {
                                var edge = c[5];
                                if (u < 0.2)
                                {
                                    edge = c[6];
                                }
                                var ea = k;
                                if (u < 0.3)
                                {
                                    ea = 1;
                                }
                                p.Dot(x + s * d, y + e, edge, ea);
                            }
                        }
                    }
                }
            }
            p.GlowLine(x - l, y, x + l, y, 6, c[6], 0.5 * k);
            p.GlowLine(x, y - l * 0.45, x, y + l * 0.45, 4, c[6], 0.3 * k);
        }

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly double _y;
            private readonly Particles _ps;
            private readonly Bits _bits = new(_ => 0);
            private readonly Lights _lights = new();
            private readonly List<Spike> _sp;
            private readonly List<RingSegment> _segs = [];
            private double _t;

            public Run(PixelFxOptions o)
            {
                _c = Fit(o.Ramp("palette"), 8);
                var c = _c;
                var r = new PixelRng(o.Number("seed"));
                const double x = 0;
                _y = -o.Number("height");
                var y = _y;
                var aim = o.Number("aim");
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [c[7], c[6], c[5], c[3]]);
                _sp = Spikes(r, 14, aim, len: (11, 15), w: (1.8, 2.5), fwd: 1.4, back: 0.6, jag: 0.5);
                foreach (var s in _sp.OrderByDescending(s => s.Len).Take(2).ToList())
                {
                    s.Len *= 1.3;
                }
                for (var i = 0; i < 7; i++)
                {
                    var a = aim + (i / 7.0) * Tau;
                    var gap = r.Range(0.55, 0.85);
                    var dr = r.Range(0.08, 0.4) + 0.15 * Math.Max(0, Math.Cos(a - aim));
                    _segs.Add(new RingSegment(a, gap, dr, 0.7 + 0.5 * Math.Max(0, Math.Cos(a - aim))));
                }

                _lights.Add(x, y, 84, c[5], 0.6, 0.5);
                _lights.Add(x, y, 30, White, 0.7, 0.1);
                _lights.Add(x, -4, 44, c[4], 0.3, 0.45);
                // sparks: most sprayed on along the blow, a few kicked back
                for (var i = 0; i < 36; i++)
                {
                    var back = r.Chance(0.22);
                    var dir = aim;
                    var spread = 0.8;
                    var speed = 1.0;
                    if (back)
                    {
                        dir = aim + Math.PI;
                        spread = 0.6;
                        speed = 0.6;
                    }
                    var a = dir + r.Range(-1, 1) * spread;
                    var v = r.Range(170, 430) * speed;
                    var life = r.Range(0.15, 0.5);
                    double delay = 0;
                    if (r.Chance(0.3))
                    {
                        delay = r.Range(0, 0.04);
                    }
                    _ps.Spawn(P.Spark, x, y, Math.Cos(a) * v, Math.Sin(a) * v - 50, life, White, 1, delay);
                }
                // shards: bright splinters that tumble down, bounce and settle
                for (var i = 0; i < 10; i++)
                {
                    var a = aim + r.Range(-1, 1) * 1.1 - 0.35;
                    var v = r.Range(110, 280);
                    _bits.Add(new Bit
                    {
                        Kind = B.Shard, X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v - 40, G = 0.8, Drag = 0.7, Bounce = 0.35,
                        Life = r.Range(0.55, 0.85), A = r.Range(0, Tau), Va = r.Range(-25, 25), S = r.Range(2, 4.5), C = r.Pick([c[7], c[6], c[6], c[5]]),
                    });
                }
                // embers of light drifting up and off, and glints that pop round the ring after the hit
                for (var i = 0; i < 12; i++)
                {
                    var a = aim - Math.PI / 2 + r.Range(-1.4, 1.4);
                    var v = r.Range(35, 110);
                    _ps.Spawn(P.Mote, x + Math.Cos(a) * 4, y + Math.Sin(a) * 4, Math.Cos(a) * v, Math.Sin(a) * v - 15, r.Range(0.35, 0.7), r.Pick([c[6], c[5], c[5]]), 0.6, r.Range(0, 0.05));
                }
                for (var i = 0; i < 5; i++)
                {
                    var a = r.Range(0, Tau);
                    var d = r.Range(14, 34);
                    var life = r.Range(0.22, 0.38);
                    var col = White;
                    if (r.Chance(0.5))
                    {
                        col = c[6];
                    }
                    _ps.Spawn(P.Star, x + Math.Cos(a) * d, y + Math.Sin(a) * d * 0.8, 0, -4, life, col, r.Range(1.6, 3), r.Range(0.08, 0.32));
                }
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                _t += dt;
                _ps.Update(dt);
                _bits.Update(dt);
                _lights.Update(dt);
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                const double x = 0;
                var y = _y;
                // the ring: it swells off the star, then breaks into arcs that fly on and cool
                var u = Clamp((t - 0.03) / 0.37);
                if (t >= 0.03 && u < 1)
                {
                    var r = 10 + 30 * Ease.OutCubic(u);
                    var brk = Smooth(0.12, 0.6, u);
                    var th = Math.Max(1, 3.6 * Math.Pow(1 - u, 1.2));
                    int i;
                    if (u < 0.12)
                    {
                        i = 7;
                    }
                    else if (u < 0.3)
                    {
                        i = 6;
                    }
                    else if (u < 0.5)
                    {
                        i = 5;
                    }
                    else if (u < 0.75)
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
                    BrokenRing(p, x, y, r, th, _segs, brk, c[i], c[Math.Max(2, i - 2)], k, 1, 0);
                    if (u < 0.6)
                    {
                        p.GlowRing(x, y, r, 6, c[5], 0.35 * Math.Pow(1 - u / 0.6, 2));
                    }
                }
                _bits.Draw(p);
                _ps.Draw(p);
                // the flare blooms as the star collapses
                var f = (t - 0.04) / 0.28;
                if (f > 0 && f < 1)
                {
                    Flare(p, x, y, 64 * Math.Pow(1 - f, 1.3), 1 - f, c);
                    p.Glow(x, y, 56, c[5], 0.4 * (1 - f));
                }
                // the starburst slams in: a white frame, an overshoot, a settle, then it breaks up
                for (var i = 0; i < Keys.Length - 1; i++)
                {
                    if (t >= Keys[i].T && t < Keys[i + 1].T)
                    {
                        var key = Keys[i];
                        Star(p, x, y, _sp, key.Sc, key.Th, key.Cols.Select(j => c[j]).ToArray(), key.A, 2.6);
                    }
                }
                var pop = Math.Exp(-t / 0.05);
                if (pop > 0.02)
                {
                    p.Glow(x, y, 44, c[5], 0.4 * pop);
                    p.Glow(x, y, 12, White, 0.6 * pop);
                }
                _lights.Draw(p);
            }
        }
    }
}
