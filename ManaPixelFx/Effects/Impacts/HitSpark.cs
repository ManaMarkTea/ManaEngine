using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Effects.Impacts
{
    /// <summary>
    /// pixelfx's impacts/hit-spark.js: the everyday hit -- a sharp, lopsided cross flare with one long ray along the blow
    /// round a white diamond core, then a cone of sparks and hot chips that fall and bounce. The spark spray and light pop
    /// are adapted from Sprite Fusion's open-source impact, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class HitSpark : PixelEffect
    {
        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "impacts/hit-spark",
            Name = "Hit spark",
            Description = "The everyday hit: a sharp, lopsided cross flare with one long ray along the blow round a white diamond core, then a cone of sparks and hot chips that fall and bounce.",
            Size = (136, 80),
            Origin = (44, 64),
            Duration = 0.5,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#c9281f", "#f2561d", "#ff8a2a", "#ffe26b", "#fff3a6", "#ffffff" },
                ["height"] = 20.0,
                ["aim"] = 0.1,
                ["seed"] = 4.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        private readonly record struct Ray(double A, double L, double W);

        // one ray of the cross: fat at the root, a hair at the tip, white cooling outward; from draws only past that share
        private static void DrawRay(PixelPainter p, double x, double y, double a, double l, double w, uint[] c, double from = 0)
        {
            var cs = Math.Cos(a);
            var s = Math.Sin(a);
            for (var d = from * l; d <= l; d += 0.5)
            {
                var u = d / l;
                var hw = w * Math.Pow(1 - u, 1.6);
                uint col;
                if (u < 0.3)
                {
                    col = White;
                }
                else if (u < 0.62)
                {
                    col = c[5];
                }
                else if (u < 0.88)
                {
                    col = c[4];
                }
                else
                {
                    col = c[3];
                }
                p.Px(x + cs * d, y + s * d, col);
                if (hw > 0.45)
                {
                    for (var o = 0.5; o <= hw; o += 0.5)
                    {
                        foreach (var e in new[] { -o, o })
                        {
                            var side = c[4];
                            if (u < 0.3 && o < 1)
                            {
                                side = c[5];
                            }
                            p.Px(x + cs * d - s * e, y + s * d + cs * e, side);
                        }
                    }
                }
            }
        }

        // the core: a crisp white diamond with a pale rim
        private static void Diamond(PixelPainter p, double x, double y, int r, uint[] c)
        {
            for (var dy = -r - 1; dy <= r + 1; dy++)
            {
                for (var dx = -r - 1; dx <= r + 1; dx++)
                {
                    var m = Math.Abs(dx) + Math.Abs(dy);
                    if (m <= r)
                    {
                        p.Px(x + dx, y + dy, White);
                    }
                    else if (m == r + 1)
                    {
                        p.Px(x + dx, y + dy, c[3]);
                    }
                }
            }
        }

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly double _y;
            private readonly double _aim;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly Ray[] _rays;
            private double _t;

            public Run(PixelFxOptions o)
            {
                _c = Fit(o.Ramp("palette"), 7);
                var c = _c;
                var r = new PixelRng(o.Number("seed"));
                const double x = 0;
                _y = -o.Number("height");
                var y = _y;
                _aim = o.Number("aim");
                var aim = _aim;
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [c[6], c[5], c[4], c[2]]);
                // the cross: a long thin ray along the blow, a stub behind, two unequal arms across, and a glint between
                var tilt = r.Range(-0.08, 0.08);
                _rays =
                [
                    new(aim + tilt, r.Range(33, 37), 2.2),
                    new(aim + Math.PI + tilt, r.Range(8, 10), 2.2),
                    new(aim - Math.PI / 2 + tilt, r.Range(15, 18), 2.6),
                    new(aim + Math.PI / 2 + tilt, r.Range(10, 12), 2.3),
                    new(aim - Math.PI / 4 + tilt, r.Range(5, 7), 1),
                ];
                _lights.Add(x, y, 38, c[4], 0.35, 0.18);
                _lights.Add(x + Math.Cos(aim) * 10, y + Math.Sin(aim) * 10, 22, c[5], 0.45, 0.1);
                _lights.Add(x, -2, 26, c[3], 0.22, 0.22);
                // a cone of sparks sprayed on along the blow: they fall, bounce off the floor and die
                for (var i = 0; i < 9; i++)
                {
                    var a = aim + r.Range(-0.6, 0.6) - 0.15;
                    var v = r.Range(170, 360);
                    var life = r.Range(0.25, 0.45);
                    double delay = 0;
                    if (i > 6)
                    {
                        delay = r.Range(0.01, 0.03);
                    }
                    _ps.Spawn(P.Spark, x + 1, y, Math.Cos(a) * v, Math.Sin(a) * v - 40, life, White, 1, delay);
                }
                // hot pixel chips knocked off, tumbling down to tick off the floor
                for (var i = 0; i < 6; i++)
                {
                    var a = aim + r.Range(-1.1, 0.5) - 0.3;
                    var v = r.Range(70, 170);
                    var vy = Math.Sin(a) * v - r.Range(30, 80);
                    _ps.Spawn(P.Grit, x + 1, y, Math.Cos(a) * v, vy, r.Range(0.35, 0.45), r.Pick([c[5], c[4], c[4], c[3]]), 1);
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
                _ps.Draw(p);
                if (t < 0.025)
                {
                    // the flash: the whole cross round the diamond
                    foreach (var ray in _rays)
                    {
                        DrawRay(p, x, y, ray.A, ray.L, ray.W, c);
                    }
                    Diamond(p, x, y, 4, c);
                }
                else if (t < 0.058)
                {
                    // reaching further and thinning, the diamond still crisp
                    foreach (var ray in _rays)
                    {
                        DrawRay(p, x, y, ray.A, ray.L * 1.2, ray.W * 0.55, c, 0.12);
                    }
                    Diamond(p, x, y, 3, c);
                }
                else if (t < 0.092)
                {
                    // only the long ray lingers, its root burning away
                    var ray = _rays[0];
                    DrawRay(p, x, y, ray.A, ray.L * 1.3, 0.6, [c[0], c[1], c[2], c[3], c[3], c[4]], 0.4);
                }
                var pop = Math.Exp(-t / 0.045);
                if (pop > 0.02)
                {
                    p.Glow(x, y, 22, c[4], 0.3 * pop);
                    p.Glow(x, y, 7, White, 0.15 * pop);
                    p.GlowLine(x, y, x + Math.Cos(_aim) * 26, y + Math.Sin(_aim) * 26, 4, c[5], 0.25 * pop);
                }
                _lights.Draw(p);
            }
        }
    }
}
