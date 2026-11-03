using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Explosions.ExplosionsLib;

namespace ManaPixelFx.Effects.Explosions
{
    /// <summary>
    /// pixelfx's explosions/plasma-explosion.js: a blue-violet energy blast with no smoke -- a white flash, a ball of
    /// plasma swelling into a thinning shell, shock rings in and out of depth, lightning arcs crackling off it. The release
    /// and the arcs are adapted from Sprite Fusion's open-source energy, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class PlasmaExplosion : PixelEffect
    {
        private const double Ball = 0.55;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "explosions/plasma-explosion",
            Name = "Plasma explosion",
            Description = "A blue-violet energy blast with no smoke: a white flash, a ball of plasma swelling into a thinning shell, shock rings in and out of depth, lightning arcs crackling off it.",
            Size = (224, 200),
            Origin = (112, 100),
            Duration = 1.7,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#0a0828", "#1a1c6e", "#2c3cc0", "#4a7cf0", "#7cc0ff", "#c8ecff", "#ffffff" },
                ["accent"] = new[] { "#1c0a30", "#4a1a8c", "#8a3ce0", "#c48cff", "#f0d8ff" },
                ["seed"] = 7.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o, Meta);

        // the ball's radius: punched out fast, then easing
        private static double Shell(double t) => 5 + 30 * Ease.OutCubic(Clamp(t / Ball));

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly uint[] _v;
            private readonly PixelRng _r;
            private readonly double _seed;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly PixelEffectMeta _meta;
            private double _t;

            public Run(PixelFxOptions o, PixelEffectMeta meta)
            {
                _meta = meta;
                _c = Fit(o.Ramp("palette"), 7);
                _v = Fit(o.Ramp("accent"), 5);
                var c = _c;
                var v = _v;
                _seed = o.Number("seed");
                _r = new PixelRng(_seed);
                var r = _r;
                _ps = new Particles(rng: r, sparks: [c[6], c[5], c[4], v[3]]);
                _lights.Add(0, 0, 96, c[4], 1, 0.6);
                _lights.Add(0, 0, 40, White, 0.9, 0.1);
                _lights.Add(0, 0, 70, v[2], 0.5, 1.1);
                for (var i = 0; i < 46; i++)
                {
                    var a = r.Range(0, Tau);
                    var sp = r.Range(140, 360);
                    _ps.Spawn(P.Mote, Math.Cos(a) * 4, Math.Sin(a) * 4, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.2, 0.42), r.Pick([c[6], c[5], c[4], v[3]]), r.Range(0.7, 1.1));
                }
                for (var i = 0; i < 26; i++)
                {
                    var a = r.Range(0, Tau);
                    var sp = r.Range(40, 140);
                    _ps.Spawn(P.Flake, Math.Cos(a) * 10, Math.Sin(a) * 10, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.8, 1.5), r.Pick([c[5], v[3], v[4]]), r.Range(0.6, 1.6));
                }
            }

            public override bool HasBack => true;

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                _t += dt;
                var t = _t;
                // the shell sheds sparkles and motes as it thins
                if (t > 0.12 && t < 0.9 && r.Chance(30 * dt))
                {
                    var a = r.Range(0, Tau);
                    var rs = Shell(t);
                    var vx = Math.Cos(a) * r.Range(10, 40);
                    var vy = Math.Sin(a) * r.Range(10, 40);
                    var life = r.Range(0.3, 0.6);
                    var col = _v[4];
                    if (r.Chance(0.5))
                    {
                        col = _c[5];
                    }
                    _ps.Spawn(P.Star, Math.Cos(a) * rs, Math.Sin(a) * rs, vx, vy, life, col, r.Range(0.5, 1.8));
                }
                _ps.Update(dt);
                var (w, h) = _meta.Size;
                var (ox, oy) = _meta.Origin;
                Cull(_ps, -ox + 3, -oy + 3, w - ox - 3, h - oy - 3);
                _lights.Update(dt);
            }

            // the tilted ring's far half, behind the ball
            public override void DrawBack(PixelPainter p, double time, PixelFxOptions o) => Tilted(p, _t, _c, _v, true);

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                var v = _v;
                // the ball: a white heart, cyan and blue bands, a violet rim boiling at the edge; it swells, then hollows out
                if (t < Ball)
                {
                    var u = t / Ball;
                    var r = Shell(t);
                    var hollow = Smooth(0.35, 1, u);
                    var boil = Math.Floor(t * 20);
                    p.Shade(-r - 3, -r - 3, r + 3, r + 3, (x, y, o) =>
                    {
                        var d = Hypot(x, y);
                        var a = Math.Atan2(y, x);
                        var edge = r * (1 + 0.1 * Noise(a * 3 + boil * 0.7, 3));
                        if (d > edge)
                        {
                            return 0;
                        }
                        var vv = d / edge;
                        if (vv < hollow * 0.92)
                        {
                            return 0;
                        }
                        // turbulent bands swirling round the heart, and bright filaments twisting through them
                        var n = Noise(a * 4 + d * 0.25 + boil * 0.5, 5);
                        var w = vv + n * 0.14;
                        var fil = Math.Abs(Math.Sin(a * 5 + d * 0.32 - t * 14 + n * 2.5)) < 0.1;
                        o.K = 0.25 * (1 - u);
                        if (vv > 0.9)
                        {
                            return v[2];
                        }
                        if (fil && vv > 0.3)
                        {
                            if (u < 0.5)
                            {
                                return White;
                            }
                            return c[5];
                        }
                        if (w < 0.32 - u * 0.25)
                        {
                            return White;
                        }
                        if (w < 0.52)
                        {
                            return c[5];
                        }
                        if (w < 0.72)
                        {
                            return c[4];
                        }
                        if (w < 0.86)
                        {
                            if (u < 0.5)
                            {
                                return c[3];
                            }
                            return v[3];
                        }
                        return v[2];
                    });
                    p.Glow(0, 0, r * 1.6 + 6, c[4], 0.6 * (1 - u));
                }
                if (t < 0.07)
                {
                    p.Circle(0, 0, 9 - t * 60, White);
                }
                if (t < 0.25)
                {
                    Sim.Flare(p, 0, 0, 52 * Math.Pow(1 - t / 0.25, 1.3), 1 - t / 0.25, c);
                }
                // shock rings: a fast bright one, a slower violet one
                var u1 = t / 0.42;
                if (u1 < 1)
                {
                    var col = c[5];
                    if (u1 < 0.25)
                    {
                        col = White;
                    }
                    p.Ring(0, 0, 8 + 84 * Ease.OutCubic(u1), Math.Max(1, 3 * (1 - u1)), col, 1.15 - u1);
                    p.GlowRing(0, 0, 8 + 84 * Ease.OutCubic(u1), 5, c[4], 0.35 * (1 - u1));
                }
                var u2 = (t - 0.06) / 0.6;
                if (u2 > 0 && u2 < 1)
                {
                    p.Ring(0, 0, 6 + 60 * Ease.OutCubic(u2), Math.Max(1, 2 * (1 - u2)), v[3], 1.1 - u2);
                }
                // arcs: lightning crackling out of the heart, re-struck every few frames, shorter as the charge runs out
                var left = 1 - Smooth(0.15, 0.85, t);
                var strike = Math.Floor(t * 18);
                if (left > 0)
                {
                    var r = Rng(_seed, strike, 17);
                    var n = 2 + Round(4 * left);
                    for (var i = 0; i < n; i++)
                    {
                        var a = r.Range(0, Tau);
                        var l = r.Range(26, 58) * (0.45 + 0.55 * left);
                        var path = PixelShapes.Bolt(r, 0, 0, Math.Cos(a) * l, Math.Sin(a) * l, 0.28, 4);
                        Arc(p, path, c, v, 0.8 * left);
                        if (r.Chance(0.5))
                        {
                            foreach (var fork in PixelShapes.Forks(r, path, 1, 14, 0.35))
                            {
                                Arc(p, fork, c, v, 0.5 * left);
                            }
                        }
                    }
                    // and little arcs crawling over the shell
                    var rs = Shell(t);
                    for (var i = 0; i < 3; i++)
                    {
                        var a = r.Range(0, Tau);
                        var b = a + r.Range(0.3, 0.6);
                        Arc(p, PixelShapes.Bolt(r, Math.Cos(a) * rs, Math.Sin(a) * rs, Math.Cos(b) * rs, Math.Sin(b) * rs, 0.4, 3), c, v, 0.6 * left);
                    }
                }
                Tilted(p, t, c, v, false);
                _ps.Draw(p);
                _lights.Draw(p);
            }
        }

        // a ring on a tilted plane racing out round the blast: far half behind, near half in front
        private static void Tilted(PixelPainter p, double t, uint[] c, uint[] v, bool back)
        {
            var u = (t - 0.03) / 0.5;
            if (u <= 0 || u >= 1)
            {
                return;
            }
            Sim.Band(p, 0, 4, 12 + 86 * Ease.OutCubic(u), 0.28, Math.Max(1, 4 * (1 - u)), 1.15 - u, [v[1], v[2], c[4], c[5]], g => (Math.Sin(g) < 0) == back, u < 0.15);
        }

        // one arc: a soft glow along it, a cyan halo line, a white core
        private static void Arc(PixelPainter p, List<(double X, double Y)> path, uint[] c, uint[] v, double k)
        {
            if (k <= 0.05)
            {
                return;
            }
            for (var i = 1; i < path.Count; i++)
            {
                var (ax, ay) = path[i - 1];
                var (bx, by) = path[i];
                p.GlowLine(ax, ay, bx, by, 5, v[2], 0.18 * k);
                p.Line(ax + 1, ay, bx + 1, by, c[4], k * 0.8);
                var col = c[5];
                if (i < path.Count * 0.6)
                {
                    col = White;
                }
                p.Line(ax, ay, bx, by, col, Math.Min(1, k * 1.4));
            }
        }
    }
}
