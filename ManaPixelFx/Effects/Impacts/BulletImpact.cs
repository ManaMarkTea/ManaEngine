using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Impacts.ImpactsLib;

namespace ManaPixelFx.Effects.Impacts
{
    /// <summary>
    /// pixelfx's impacts/bullet-impact.js: a bullet hits a wall on the right -- a hot half-star, sparks sprayed back in a
    /// cone, a puff of grit, chips that fall and bounce, a pock mark cooling to soot. The open-sided star, spark cone,
    /// chips, dust and smoke are adapted from Sprite Fusion's open-source impact, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class BulletImpact : PixelEffect
    {
        private const double Duration = 1.4;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "impacts/bullet-impact",
            Name = "Bullet impact",
            Description = "A bullet hits a wall on the right: a hot half-star, sparks sprayed back in a cone, a puff of grit, chips that fall and bounce, a pock mark cooling to soot.",
            Size = (152, 100),
            Origin = (116, 84),
            Duration = Duration,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#c9281f", "#f2561d", "#ff8a2a", "#ffe26b", "#fff3a6", "#ffffff" },
                ["stone"] = "#5b5079",
                ["height"] = 24.0,
                ["seed"] = 6.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        private sealed class Run : PixelSimulation
        {
            // the wall's face looks left; the bullet came in level, a touch downward, and the spray bounces back off it
            private const double N = Math.PI;
            private const double In = 0.08;
            private const double Back = Math.PI - In;

            private readonly uint[] _c;
            private readonly Stone _st;
            private readonly double _y;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly List<Spike> _sp;
            private readonly List<(int X, int Y, int K)> _rim = [];
            private readonly List<(double X0, double Y0, double X1, double Y1)> _cracks = [];
            private double _t;

            private static bool Open(double a) => Math.Cos(a - N) > -0.25;

            public Run(PixelFxOptions o)
            {
                _c = Fit(o.Ramp("palette"), 7);
                var c = _c;
                _st = Stone.Of(o.Color("stone"));
                var st = _st;
                var seed = o.Number("seed");
                var r = new PixelRng(seed);
                const double x = 0;
                _y = -o.Number("height");
                var y = _y;
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [c[6], c[5], c[4], c[2]]);
                _sp = Spikes(r, 9, Back, len: (7, 10.5), w: (1.6, 2.1), fwd: 1.8, back: 0.4);
                foreach (var s in _sp)
                {
                    if (Math.Cos(s.A - N) < -0.2)
                    {
                        s.Len *= 0.3;
                    }
                }
                // the pock mark: a ragged hole with a rim that glows and cools
                for (var py = -2; py <= 2; py++)
                {
                    for (var px = 0; px <= 3; px++)
                    {
                        var d = Hypot((px - 1) / 1.6, py / 2.1) + Rand(px, py, seed) * 0.35;
                        if (d < 0.75)
                        {
                            _rim.Add((px, py, 0));
                        }
                        else if (d < 1.25)
                        {
                            _rim.Add((px, py, 1));
                        }
                    }
                }
                for (var i = 0; i < 3; i++)
                {
                    var a = r.Range(-1.2, 1.2);
                    var l = r.Range(2, 4.5);
                    _cracks.Add((Math.Cos(a) * 2.2 + 1, Math.Sin(a) * 2.2, Math.Cos(a) * (2.2 + l) + 1, Math.Sin(a) * (2.2 + l)));
                }

                _lights.Add(x - 4, y, 40, c[4], 0.6, 0.22);
                _lights.Add(x - 2, y, 14, White, 0.6, 0.06);
                // sparks bounce back off the wall in a cone
                for (var i = 0; i < 18; i++)
                {
                    var a = Back + r.Range(-1, 1) * 0.8;
                    var v = r.Range(170, 450);
                    var life = r.Range(0.12, 0.4);
                    double delay = 0;
                    if (i > 13)
                    {
                        delay = r.Range(0.01, 0.04);
                    }
                    _ps.Spawn(P.Spark, x - 1, y, Math.Cos(a) * v, Math.Sin(a) * v - 30, life, White, 1, delay);
                }
                // chips of the wall: flung out, falling, bouncing once and settling
                for (var i = 0; i < 8; i++)
                {
                    var a = N + r.Range(-0.8, 0.6);
                    var v = r.Range(60, 190);
                    var cy = y + r.Range(-1, 1);
                    var vy = Math.Sin(a) * v - r.Range(20, 70);
                    var life = r.Range(0.9, 1.3);
                    var col = r.Pick([st.Light, st.Body, st.Body, st.Lip]);
                    double size = 1;
                    if (i < 3)
                    {
                        size = 2;
                    }
                    _ps.Spawn(P.Grit, x - 1, cy, Math.Cos(a) * v, vy, life, col, size);
                }
                // a puff of grit dust and a wisp of smoke drifting off
                for (var i = 0; i < 9; i++)
                {
                    var a = N + r.Range(-1.1, 1.1);
                    var v = r.Range(15, 60);
                    var cy = y + r.Range(-1.5, 1.5);
                    var life = r.Range(0.3, 0.55);
                    var col = Mix(st.Dust, White, 0.25);
                    if (r.Chance(0.5))
                    {
                        col = st.Dust;
                    }
                    _ps.Spawn(P.Dust, x - 2, cy, Math.Cos(a) * v, Math.Sin(a) * v, life, col, r.Range(1.2, 2.3));
                }
                _ps.Spawn(P.Smoke, x - 4, y, r.Range(-10, -4), r.Range(-16, -8), r.Range(0.8, 1.2), st.Dust, r.Range(1.8, 2.4), 0.05);
            }

            public override bool HasBack => true;

            public override void Update(double dt, PixelFxOptions o)
            {
                _t += dt;
                _ps.Update(dt);
                _lights.Update(dt);
            }

            public override void DrawBack(PixelPainter p, double time, PixelFxOptions o) => _ps.Draw(p, "back");

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                var st = _st;
                const double x = 0;
                var y = _y;
                // the pock mark: white-hot, cooling through the ramp to a dark rim, fading at the very end
                var heat = Math.Pow(Clamp(1 - t / 0.9), 1.3);
                var fade = 1 - Smooth(Duration - 0.45, Duration - 0.1, t);
                if (fade > 0)
                {
                    foreach (var (x0, y0, x1, y1) in _cracks)
                    {
                        var col = st.Shade;
                        var a = fade * 0.85;
                        if (heat > 0.5)
                        {
                            col = c[3];
                            a = fade;
                        }
                        p.Line(x + x0, y + y0, x + x1, y + y1, col, a);
                    }
                    foreach (var (rx, ry, k) in _rim)
                    {
                        var h = heat * 1.15;
                        if (k != 0)
                        {
                            h = heat * 0.85;
                        }
                        uint col;
                        if (k == 0 && h < 0.75)
                        {
                            col = Soot;
                        }
                        else if (h > 0.85)
                        {
                            col = White;
                        }
                        else if (h > 0.6)
                        {
                            col = c[5];
                        }
                        else if (h > 0.4)
                        {
                            col = c[4];
                        }
                        else if (h > 0.22)
                        {
                            col = c[3];
                        }
                        else if (h > 0.1)
                        {
                            col = c[2];
                        }
                        else
                        {
                            col = st.Shade;
                        }
                        p.Dot(x + rx, y + ry, col, fade);
                    }
                    if (heat > 0)
                    {
                        p.Glow(x + 1, y, 6 + 6 * heat, c[3], 0.5 * heat);
                    }
                }
                // a half ring opening away from the wall
                var u = (t - 0.03) / 0.18;
                if (u >= 0 && u < 1)
                {
                    var r = 5 + 12 * Ease.OutCubic(u);
                    var col = c[3];
                    if (u < 0.3)
                    {
                        col = c[5];
                    }
                    else if (u < 0.6)
                    {
                        col = c[4];
                    }
                    for (int j = 0, n = (int)Math.Ceiling(r * Tau * 1.3); j < n; j++)
                    {
                        var g = (j / (double)n) * Tau;
                        if (Open(g))
                        {
                            p.Dot(x + Math.Cos(g) * r, y + Math.Sin(g) * r, col, 1.15 - u);
                        }
                    }
                }
                _ps.Draw(p, "front");
                if (t < 0.03)
                {
                    Star(p, x, y, _sp, 1, 1, [c[2], c[5], c[4], White], 1, 2.2);
                }
                else if (t < 0.06)
                {
                    Star(p, x, y, _sp, 1.25, 0.55, [c[1], c[4], c[3], c[5]], 1, 2.2);
                }
                var pop = Math.Exp(-t / 0.045);
                if (pop > 0.02)
                {
                    p.Glow(x - 2, y, 22, c[3], 0.6 * pop);
                    p.Glow(x - 1, y, 8, White, 0.5 * pop);
                    // light grazing the wall's face
                    p.GlowLine(x + 1, y - 16, x + 1, y + 16, 4, c[4], 0.4 * pop);
                }
                _lights.Draw(p);
            }
        }
    }
}
