using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Explosions.ExplosionsLib;

namespace ManaPixelFx.Effects.Explosions
{
    /// <summary>
    /// pixelfx's explosions/nuke.js: a blinding white frame, then a fireball lifts on a thin stem into a wide rolling cap
    /// that glows from inside; a white skirt flashes and a dust surge sweeps the floor. The fire puffs, sparks and embers
    /// are adapted from Sprite Fusion's open-source explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class Nuke : PixelEffect
    {
        private const double Flash = 0.05;
        private const double Lift = 0.07;
        private const double Skirt0 = 0.55;
        private const double Skirt1 = 1.75;

        // the cap's smoke: a lit crescent, two mid tones, a dark underside, the darkest hollow
        private static readonly uint[] Cap = Ramp(["#a0918c", "#7c6f6e", "#5f5458", "#40373f", "#271f27"]);

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "explosions/nuke",
            Name = "Nuke",
            Description = "A blinding white frame, then a fireball lifts on a thin stem into a wide rolling cap that glows from inside; a white skirt flashes and a dust surge sweeps the floor.",
            Size = (400, 320),
            Origin = (200, 284),
            Duration = 9,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["dust"] = "#8a7466",
                ["seed"] = 5.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o, Meta);

        private readonly record struct Veil(double A, double S, double W, double E, double Dx, double Dy);

        private readonly record struct Bell(double Ph, double V, double S, double W, double E, double Dx, double Dy);

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _f;
            private readonly uint[] _fr;
            private readonly PixelRng _r;
            private readonly uint _dust;
            private readonly uint[] _dusts;
            private readonly int _w, _h, _ox, _oy;
            private readonly double _x0, _x1, _y0, _y1;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly Plume _plume;
            private readonly Column _column;
            private readonly List<Veil> _veil = [];
            private readonly List<Bell> _skirt = [];
            private double _t;
            private bool _boomed;

            public Run(PixelFxOptions o, PixelEffectMeta meta)
            {
                _f = Fit(o.Ramp("palette"), 8);
                _fr = FireRamp(_f);
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                _dust = o.Color("dust");
                var dust = _dust;
                _dusts = [Mix(dust, White, 0.22), dust, Mix(dust, Hex("#3a3040"), 0.35), Mix(dust, Hex("#2a2330"), 0.6), Mix(dust, Hex("#1c1720"), 0.8)];
                (_w, _h) = meta.Size;
                (_ox, _oy) = meta.Origin;
                _x0 = -_ox + 3;
                _x1 = _w - _ox - 3;
                _y0 = -_oy + 3;
                _y1 = _h - _oy - 3;
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [_f[6], _f[5], _f[3], _f[2]]);
                // a wide, flat cap rolling like a torus: lobes go up the middle, out over the top and curl under at the rim
                _plume = new Plume(r, new PlumeOptions
                {
                    X = 0, Y = -26, Rise = 152, Tau = 1.7, Drift = 3, Tube = (14, 26), Ring = (4, 46), Grow = 1.6, N = 50, Size = (9.5, 14.5), Swell = 0.45,
                    Spin = 2.4, SpinTau = 2.4, Heat = 1.25, Cool = 0.42, Under = 0.6, Tilt = 0.16, Squash = 0.48, Fade = (6.0, 8.3), Spread = 26,
                    Style = 1, Inner = true, Hollow = true, Fray = (4, 1.6, 6.4),
                });
                _plume.OnFray = (x, y, rr, ux, uy) =>
                {
                    var vx = ux * r.Range(10, 26);
                    var vy = uy * r.Range(4, 12) - r.Range(2, 8);
                    var life = r.Range(1, 1.7);
                    var col = Cap[(int)r.Int(0, 1)];
                    _ps.Spawn(P.Smoke, x, y, vx, vy, life, col, rr * r.Range(0.3, 0.45));
                };
                // a thin stem that keeps feeding the cap, then breaks up from the bottom
                _column = new Column(r, new ColumnOptions { W = 3.5, Foot = 22, Speed = 125, Size = (5.5, 7), N = 84, Sway = 1.4, Style = 1 });
                // the condensation skirt: a veil arching over the cap and a bell flaring round the stem
                for (var i = 0; i < 72; i++)
                {
                    var a = -Math.PI * ((i + r.Range(0.2, 0.8)) / 72);
                    var s = r.Range(6, 10);
                    var w = r.Range(0.97, 1.06);
                    var e = r.Range(0.6, 1.3);
                    var dx = r.Range(-1, 1);
                    var dy = r.Range(-1, 0.2);
                    _veil.Add(new Veil(a, s, w, e, dx, dy));
                }
                for (var i = 0; i < 110; i++)
                {
                    var ph = r.Range(0, Tau);
                    var v = r.Range(0, 1);
                    var s = r.Range(5, 9);
                    var w = r.Range(0.9, 1.06);
                    var e = r.Range(0.6, 1.3);
                    var dx = r.Range(-1, 1);
                    var dy = r.Range(-1, 0.4);
                    _skirt.Add(new Bell(ph, v, s, w, e, dx, dy));
                }
            }

            public override bool HasBack => true;

            private double Top()
            {
                var s = _plume.Shape();
                return s.Y + s.Tube * 0.3;
            }

            private void Boom()
            {
                var r = _r;
                var ps = _ps;
                var f = _f;
                _lights.Add(0, -60, 150, f[4], 0.7, 1.6);
                // the fireball's first fire, sparks and embers
                for (var i = 0; i < 26; i++)
                {
                    var a = r.Range(-Math.PI, 0);
                    var d = r.Range(4, 22);
                    var sp = r.Range(40, 160);
                    var life = r.Range(0.6, 1.1);
                    var size = r.Range(7, 12);
                    var flag = 0;
                    if (r.Chance(0.5))
                    {
                        flag = PF.Smokes | PF.Big;
                    }
                    ps.Spawn(P.Fire, Math.Cos(a) * d, -26 + Math.Sin(a) * d, Math.Cos(a) * sp, Math.Sin(a) * sp * 0.7 - 30, life, 0, size, 0, flag);
                }
                for (var i = 0; i < 90; i++)
                {
                    var a = r.Range(-Math.PI - 0.3, 0.3);
                    var sp = r.Range(200, 420);
                    ps.Spawn(P.Spark, Math.Cos(a) * 18, -26 + Math.Sin(a) * 18, Math.Cos(a) * sp, Math.Sin(a) * sp - 60, r.Range(0.3, 0.8));
                }
                for (var i = 0; i < 50; i++)
                {
                    var a = r.Range(-Math.PI, 0);
                    var sp = r.Range(30, 140);
                    ps.Spawn(P.Ember, Math.Cos(a) * 20, -26 + Math.Sin(a) * 20, Math.Cos(a) * sp, Math.Sin(a) * sp - 60, r.Range(1.6, 3.4), f[3]);
                }
                // the base surge: dust thrown out along the floor in every direction, racing to the edges; a slower ring behind
                for (var i = 0; i < 120; i++)
                {
                    var slow = i >= 92;
                    var a = r.Range(0, Tau);
                    var c = Math.Cos(a);
                    var s = Math.Sin(a);
                    double sp;
                    if (slow)
                    {
                        sp = r.Range(110, 200);
                    }
                    else
                    {
                        sp = r.Range(240, 440);
                    }
                    var y = s * 14 * 0.16 - r.Range(1, 3);
                    var flat = 0.14;
                    if (s > 0)
                    {
                        flat = 0.06;
                    }
                    double lift;
                    if (slow)
                    {
                        lift = r.Range(0, 6);
                    }
                    else
                    {
                        lift = r.Range(0, 3);
                    }
                    var vy = s * sp * flat - lift;
                    var life = r.Range(1.8, 3);
                    double size;
                    if (slow)
                    {
                        size = r.Range(8, 11);
                    }
                    else
                    {
                        size = r.Range(5, 9);
                    }
                    ps.Spawn(P.Smoke, c * 14, y, c * sp, vy, life, _dust, size, r.Range(0.05, 0.2));
                }
                for (var i = 0; i < 40; i++)
                {
                    var u = r.Range(-1, 1);
                    var a = -Math.PI / 2 + u * 0.7;
                    var sp = r.Range(150, 380) * (1 - 0.4 * Math.Abs(u));
                    var life = r.Range(1.2, 2);
                    var col = r.Pick(_dusts.Take(3).ToArray());
                    double size = 1;
                    if (r.Chance(0.4))
                    {
                        size = 2;
                    }
                    ps.Spawn(P.Grit, u * 12, -1, Math.Cos(a) * sp, Math.Sin(a) * sp, life, col, size);
                }
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                _t += dt;
                var t = _t;
                if (!_boomed && t >= Lift)
                {
                    _boomed = true;
                    Boom();
                }
                if (t >= Lift)
                {
                    _plume.Update(dt);
                }
                var s = _plume.Shape();
                var hot = _plume.Hot();
                double feed = 0;
                if (t > 0.35 && t < 4.9)
                {
                    feed = 1;
                }
                _column.Update(dt, Top(), feed);
                // afterwinds: dust drawn in along the floor and up the stem
                if (t > 0.9 && t < 4.6 && r.Chance(9 * dt))
                {
                    var side = r.Sign();
                    ps.Spawn(P.Vapour, side * r.Range(50, 160), -r.Range(2, 6), -side * r.Range(20, 45), -r.Range(0, 6), r.Range(1.2, 1.8), _dusts[1], r.Range(5, 8));
                }
                if (t > 0.3 && t < 3.4 && r.Chance(14 * hot * dt))
                {
                    var a = r.Range(0, Tau);
                    ps.Spawn(P.Ember, s.X + Math.Cos(a) * (s.Ring + s.Tube), s.Y + Math.Sin(a) * s.Tube * 0.5, Math.Cos(a) * r.Range(30, 80), -r.Range(10, 50), r.Range(1.4, 2.6), _f[3]);
                }
                Draft(ps, dt, x: 0, top: s.Y + s.Tube * 0.3, w: 9, up: 160, pull: 120, low: 14, reach: 170, k: 1 - Smooth(4.4, 5.4, t));
                ps.Update(dt);
                Cull(ps, _x0, _y0, _x1, _y1);
                FadeEdges(ps, _x0, _x1, 34, _y1);
                _lights.Update(dt);
            }

            private void Shock(PixelPainter p, bool far)
            {
                var k = (_t - Lift) / 0.8;
                if (k <= 0 || k >= 1)
                {
                    return;
                }
                Func<double, bool> keep = g => Math.Sin(g) >= 0;
                if (far)
                {
                    keep = g => Math.Sin(g) < 0;
                }
                Sim.Band(p, 0, 0.5, 196 * Ease.OutCubic(k), 0.16, Math.Max(1, 5 * (1 - k)), 1.15 - k, [_dusts[2], _dusts[0], _f[5], _f[7]], keep);
            }

            public override void DrawBack(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                if (t < Flash)
                {
                    return;
                }
                var s = _plume.Shape();
                // the ground shock: a ring racing out along the floor ahead of the dust (far half)
                Shock(p, true);
                DrawPs(p, _ps, "back", _fr);
                SkirtDraw(p, t, s, true);
                _plume.Draw(p, _fr, Cap);
                // the dark hollow under the cap where the stem goes in, its heart still warm while the inside glows
                var ember = _plume.Ember(_plume.T);
                var gone = Smooth(_plume.O.Fade.Start, _plume.O.Fade.End - 0.6, t - Lift);
                if (gone < 1)
                {
                    var hy = s.Y + s.Tube * 0.42;
                    var rx = s.Ring * 0.7 + 5;
                    var ry = s.Tube * 0.28 + 2;
                    var fr = _fr;
                    Haze(p, 0, hy, rx, ry, d =>
                    {
                        if (d < 0.55 * ember)
                        {
                            var warm = fr[6];
                            if (d < 0.3 * ember)
                            {
                                warm = fr[5];
                            }
                            return Mix(Cap[4], warm, 0.75 * ember);
                        }
                        return Cap[4];
                    }, d =>
                    {
                        var a = 0.22;
                        if (d < 0.6)
                        {
                            a = 0.8;
                        }
                        else if (d < 0.85)
                        {
                            a = 0.5;
                        }
                        return a * (1 - gone);
                    });
                }
                _column.Draw(p, Top(), _fr, _dusts, Clamp(1 - t / 1.8), ember * 0.8);
                SkirtDraw(p, t, s, false);
                Pool(p, 0, 1, 140, 16, _f[3], 0.3 * _plume.Hot());
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var f = _f;
                // the blinding frame: a white-hot core in a wash of white light that fades out before the canvas edges
                if (t < Flash)
                {
                    Wash(p, 0, -40, White, 1.6);
                    p.Circle(0, -40, 64, White);
                    return;
                }
                Shock(p, false);
                // out of the white: a crisp white-hot sphere, cooling into the fireball
                var fk = (t - Flash) / 0.13;
                if (fk < 1)
                {
                    var r = 22 + 8 * fk;
                    p.Circle(0, -26, r + 1.5, f[2]);
                    var mid = f[5];
                    var core = f[6];
                    if (fk < 0.5)
                    {
                        mid = f[6];
                        core = White;
                    }
                    p.Circle(0, -26, r, mid);
                    p.Circle(0, -26, r * 0.62, core);
                }
                DrawPs(p, _ps, "front", _fr);
                // the shock in the air: a thin bright shell racing out from the fireball, cut off by the floor and the canvas
                var u = (t - Flash) / 0.7;
                if (u < 1)
                {
                    var r = 30 + 250 * Ease.OutCubic(u);
                    const double cy = -26;
                    var ox = _ox;
                    var oy = _oy;
                    bool Out(double a) => cy + Math.Sin(a) * r > 0 || Math.Abs(Math.Cos(a) * r) > ox - 3 || cy + Math.Sin(a) * r < 3 - oy;
                    var col = f[6];
                    if (u < 0.3)
                    {
                        col = White;
                    }
                    p.Ring(0, cy, r, Math.Max(1, 2.5 * (1 - u)), col, (_, a) =>
                    {
                        if (Out(a))
                        {
                            return 0;
                        }
                        return 1.1 - u;
                    });
                    GlowDome(p, 0, cy, r, 7, f[5], 0.3 * (1 - u), 3 - ox, 3 - oy, _w - ox - 4, 0);
                }
                // the light of it: everything washed white, falling away fast, then the fireball's own warm light
                var w = Math.Exp(-(t - Flash) / 0.13);
                if (w > 0.02)
                {
                    Wash(p, 0, -40, White, 0.9 * w);
                }
                _lights.Draw(p);
            }

            // light over most of the canvas, brightest near (cx, cy), falling to nothing just inside the canvas edges so
            // it never shows the canvas as a box
            private void Wash(PixelPainter p, double cx, double cy, uint c, double k)
            {
                var rx = Math.Min(_ox + cx, _w - _ox - cx) - 4;
                var up = _oy + cy - 4;
                var down = _h - _oy - cy - 4;
                p.Shade(-_ox, -_oy, _w - _ox - 1, _h - _oy - 1, (x, y, o) =>
                {
                    var ry = down;
                    if (y < cy)
                    {
                        ry = up;
                    }
                    var d = Hypot((x - cx) / rx, (y - cy) / ry);
                    o.L = c;
                    o.K = k * (1 - Smooth(0, 1, d));
                    return 0;
                });
            }

            // the condensation skirt: a white veil flashing over the cap and a bell flaring round the stem, edged with a
            // crisp white rim, evaporating into wisps; far half behind the cloud, near in front
            private void SkirtDraw(PixelPainter p, double t, (double X, double Y, double Tube, double Ring, double G) s, bool back)
            {
                var u = (t - Skirt0) / (Skirt1 - Skirt0);
                if (u <= 0 || u >= 1)
                {
                    return;
                }
                var a = Math.Min(1, u / 0.06) * Math.Pow(1 - u, 1.5) * 0.6;
                var capR = (s.Ring + s.Tube) * 1.1;
                var y0 = s.Y + s.Tube * 0.3;
                var y1 = s.Y * 0.55;
                if (!back)
                {
                    foreach (var q in _veil)
                    {
                        var ev = u * u * q.E;
                        var rr = capR * q.W * (1 + ev * 0.2);
                        p.Disc(Math.Cos(q.A) * rr + q.Dx * ev * 10, s.Y + Math.Sin(q.A) * (s.Tube * 1.15 + 6) * q.W + q.Dy * ev * 8, q.S * (1 - 0.5 * u), White, a * 0.38);
                    }
                }
                foreach (var q in _skirt)
                {
                    var z = Math.Sin(q.Ph);
                    if ((z < 0) != back)
                    {
                        continue;
                    }
                    var rr = Lerp(capR * 0.82, 13, Math.Pow(q.V, 0.6)) * q.W;
                    var ev = u * u * q.E;
                    var x = Math.Cos(q.Ph) * rr * (1 + ev * 0.25) + q.Dx * ev * 10;
                    var y = Lerp(y0, y1, q.V) + z * rr * 0.18 + q.Dy * ev * 8;
                    var share = 0.55;
                    if (back)
                    {
                        share = 0.3;
                    }
                    p.Disc(x, y, q.S * (1 - 0.55 * u), White, a * share * (1 - q.V * 0.3));
                }
                // the bell's crisp lower edge, dithering away as it evaporates
                if (u < 0.7)
                {
                    const double rr = 13 * 1.25;
                    var k = 1 - u / 0.7;
                    for (var i = 0; i < 64; i++)
                    {
                        var g = (i / 64.0) * Tau;
                        var z = Math.Sin(g);
                        if ((z < 0) != back)
                        {
                            continue;
                        }
                        var share = 1.0;
                        if (back)
                        {
                            share = 0.6;
                        }
                        p.Dot(Math.Cos(g) * rr * (1 + u * 0.6), y1 + 2 + z * rr * 0.22, White, k * share);
                    }
                }
            }
        }
    }
}
