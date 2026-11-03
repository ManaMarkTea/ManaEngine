using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Projectiles.ProjectilesLib;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/homing-missile.js: a missile kicks off, arcs up, rolls over and dives onto its target (the
    /// origin), its smoke trail left hanging in the air, and hits in a fiery blast of flame, sparks and rocks. The blast's
    /// particle recipe is adapted from Sprite Fusion's open-source explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class HomingMissile : PixelEffect
    {
        private const double Duration = 3.8;
        private const double FlyTime = 1.15;

        // the path: up out of the launcher, over the top, and down onto the target, faster all the way
        private static readonly (double X, double Y)[] Path = [(-130, -22), (-114, -110), (-30, -120), (0, 0)];

        // the missile, nose right: fins, a body lit on top, a red warhead with a white tip
        private static readonly string[] Missile = ["ff..........", ".fhhhhhhhre.", "nmmmmmmmmrrw", ".fsssssssrd.", "ff.........."];

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/homing-missile",
            Name = "Homing missile",
            Description = "A missile kicks off, arcs up, rolls over and dives onto its target (the origin), its smoke trail left hanging in the air, and hits in a fiery blast of flame, sparks and rocks.",
            Size = (248, 140),
            Origin = (160, 120),
            Duration = Duration,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["metal"] = new[] { "#25243a", "#3e4058", "#61667f", "#8e97b0", "#c4cce0" },
                ["smoke"] = new[] { "#26222e", "#3b3644", "#544e5e", "#6f6979", "#8c8596", "#aca5b5", "#cbc4d2" },
                ["stone"] = "#4a3f69",
                ["seed"] = 8.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        private static (double X, double Y) At(double u)
        {
            var v = 1 - u;
            var a = v * v * v;
            var b = 3 * v * v * u;
            var c = 3 * v * u * u;
            var d = u * u * u;
            return (a * Path[0].X + b * Path[1].X + c * Path[2].X + d * Path[3].X, a * Path[0].Y + b * Path[1].Y + c * Path[2].Y + d * Path[3].Y);
        }

        private static double Heading(double u)
        {
            var v = 1 - u;
            var dx = 3 * v * v * (Path[1].X - Path[0].X) + 6 * v * u * (Path[2].X - Path[1].X) + 3 * u * u * (Path[3].X - Path[2].X);
            var dy = 3 * v * v * (Path[1].Y - Path[0].Y) + 6 * v * u * (Path[2].Y - Path[1].Y) + 3 * u * u * (Path[3].Y - Path[2].Y);
            return Math.Atan2(dy, dx);
        }

        private static double Progress(double t) => Math.Pow(Clamp(t / FlyTime), 1.45);

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _f;
            private readonly uint[] _m;
            private readonly uint[] _sm;
            private readonly (uint Light, uint Body, uint Shade, uint Dust) _stone;
            private readonly PixelRng _r;
            private readonly Particles _ps;
            private readonly Smoke _smoke;
            private readonly Bits _rocks = new(_ => 0);
            private readonly Lights _lights = new();
            private readonly Dictionary<char, uint> _pal;
            private double _t;
            private double _emit;
            private bool _hit;

            public Run(PixelFxOptions o)
            {
                _f = Fit(o.Ramp("palette"), 9);
                _m = Fit(o.Ramp("metal"), 5);
                _sm = Fit(o.Ramp("smoke"), 7);
                var f = _f;
                var m = _m;
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                var st = o.Color("stone");
                _stone = (Mix(Scale(st, 1.35), White, 0.08), st, Scale(st, 0.64), Mix(Scale(st, 1.2), Hex("#968c96"), 0.3));
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [f[7], f[6], f[4], f[3]]);
                _smoke = new Smoke(r);
                _pal = new Dictionary<char, uint> { ['f'] = m[1], ['h'] = m[4], ['m'] = m[3], ['s'] = m[1], ['n'] = m[0], ['r'] = f[2], ['e'] = f[3], ['d'] = f[1], ['w'] = White };
                _lights.Add(Path[0].X, Path[0].Y, 30, f[4], 0.7, 0.25);
                for (var i = 0; i < 6; i++)
                {
                    _smoke.Add(new SmokePuff { X = Path[0].X + 2, Y = Path[0].Y + 2, Vx = r.Range(-30, 20), Vy = r.Range(-10, 15), R0 = 2, R1 = r.Range(5, 7.5), Life = r.Range(1.2, 1.8), A = 0.6, Warm = 1, Rise = 8 });
                }
            }

            public override bool HasBack => true;

            private void Blast()
            {
                var r = _r;
                var ps = _ps;
                var f = _f;
                var stone = _stone;
                const double y = -6;
                _lights.Add(0, y - 2, 52, Hex("#ffb060"), 0.8, 0.8);
                _lights.Add(0, y - 2, 30, White, 0.75, 0.1);
                for (var i = 0; i < 26; i++)
                {
                    var a = r.Range(0, Tau);
                    var d = r.Range(0, 6);
                    var sp = r.Range(30, 170);
                    ps.Spawn(P.Fire, Math.Cos(a) * d, y + Math.Sin(a) * d, Math.Cos(a) * sp, Math.Sin(a) * sp * 0.75 - 40, r.Range(0.45, 0.95), 0, r.Range(3.2, 6.5));
                }
                for (var i = 0; i < 5; i++)
                {
                    ps.Spawn(P.Fire, r.Range(-3, 3), y, r.Range(-10, 10), -r.Range(90, 150), r.Range(0.7, 1.1), 0, r.Range(4, 5.5), r.Range(0, 0.08));
                }
                for (var i = 0; i < 70; i++)
                {
                    var a = r.Range(Math.PI + 0.12, Tau - 0.12);
                    var sp = r.Range(110, 280);
                    ps.Spawn(P.Spark, 0, y, Math.Cos(a) * sp, Math.Sin(a) * sp - 40, r.Range(0.2, 0.65));
                }
                for (var i = 0; i < 22; i++)
                {
                    var a = r.Range(Math.PI, Tau);
                    var sp = r.Range(30, 110);
                    ps.Spawn(P.Ember, Math.Cos(a) * 4, y + Math.Sin(a) * 4, Math.Cos(a) * sp, Math.Sin(a) * sp - 50, r.Range(0.9, 1.6), f[4]);
                }
                for (var i = 0; i < 16; i++)
                {
                    _rocks.Add(new Bit
                    {
                        Kind = B.Chunk, X = r.Range(-5, 5), Y = -2, Vx = r.Range(-110, 110), Vy = -r.Range(140, 290), G = 1, Drag = 0.4, Bounce = 0.3,
                        Life = r.Range(1.5, 2.2), A = r.Range(0, Tau), Va = r.Range(-16, 16), S = r.Int(1, 2),
                    });
                }
                for (var i = 0; i < 60; i++)
                {
                    var u = r.Range(-1, 1);
                    var a = -Math.PI / 2 + u * 0.55;
                    var sp = r.Range(80, 220) * (1 - 0.4 * Math.Abs(u));
                    var life = r.Range(1, 1.8);
                    var col = r.Pick([stone.Light, stone.Body, stone.Body, stone.Shade]);
                    double size = 1;
                    if (r.Chance(0.4))
                    {
                        size = 2;
                    }
                    ps.Spawn(P.Grit, u * 4, -1, Math.Cos(a) * sp, Math.Sin(a) * sp, life, col, size);
                }
                // the smoke: a column off the fire and dust rolling out along the floor, lit by the blast at first
                for (var i = 0; i < 18; i++)
                {
                    var a = r.Range(Math.PI * 1.15, Math.PI * 1.85);
                    var sp = r.Range(20, 90);
                    _smoke.Add(new SmokePuff
                    {
                        X = Math.Cos(a) * 4, Y = y + Math.Sin(a) * 4, Vx = Math.Cos(a) * sp * 0.7, Vy = Math.Sin(a) * sp - 45 * (i / 18.0), R0 = 3, R1 = r.Range(8, 14), Life = r.Range(1.7, 2.2),
                        A = 0.62, B = 0.88, Warm = 1, Rise = 30 + i * 2, Drag = 1.8, Delay = r.Range(0.05, 0.35),
                    });
                }
                for (var i = 0; i < 12; i++)
                {
                    var s = r.Sign();
                    _smoke.Add(new SmokePuff { X = s * r.Range(0, 6), Y = -3, Vx = s * r.Range(60, 150), Vy = -r.Range(3, 12), R0 = 2.5, R1 = r.Range(6, 9), Life = r.Range(1.1, 1.7), A = 0.5, B = 0.75, Warm = 0.3, Rise = 6, Drag = 3 });
                }
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                _t += dt;
                var t = _t;
                if (t < FlyTime)
                {
                    // the trail: puffs left behind along the path, hanging where they are dropped, swelling and thinning
                    var u = Progress(t);
                    var (x, y) = At(u);
                    var h = Heading(u);
                    var cx = Math.Cos(h);
                    var cy = Math.Sin(h);
                    var (px, py) = At(Progress(Math.Max(0, t - dt)));
                    // one puff every pixel and a half of flight, wherever along this step it fell, so the trail has no beads
                    for (_emit += Hypot(x - px, y - py) / 1.5; _emit > 1; _emit--)
                    {
                        var f = (_emit - 1) / Math.Max(1e-6, Hypot(x - px, y - py) / 1.5);
                        var ex = x + (px - x) * f;
                        var ey = y + (py - y) * f;
                        _smoke.Add(new SmokePuff
                        {
                            X = ex - cx * 7 + r.Range(-0.6, 0.6), Y = ey - cy * 7 + r.Range(-0.6, 0.6), Vx = -cx * r.Range(10, 30) + r.Range(-6, 6), Vy = -cy * r.Range(10, 30) + r.Range(-6, 6),
                            R0 = 1.4, R1 = r.Range(3.5, 6), Life = r.Range(1.5, 2.3), A = 0.5, B = 0.9, Warm = 0.8, Rise = 4, Drag = 3, Grow = 1.2,
                        });
                    }
                    if (r.Chance(0.35))
                    {
                        var a = h + Math.PI + r.Range(-0.5, 0.5);
                        var sp = r.Range(40, 140);
                        ps.Spawn(P.Spark, x - cx * 7, y - cy * 7, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.15, 0.35));
                    }
                }
                else if (!_hit)
                {
                    _hit = true;
                    Blast();
                }
                foreach (var b in _rocks.List)
                {
                    if (b.Rest && !b.Landed)
                    {
                        b.Landed = true;
                        for (var i = 0; i < 2; i++)
                        {
                            ps.Spawn(P.Dust, b.X + r.Range(-1, 1), -0.5, r.Sign() * r.Range(8, 30), -r.Range(2, 14), r.Range(0.3, 0.6), _stone.Dust, r.Range(1.2, 2.2));
                        }
                    }
                }
                ps.Update(dt);
                _smoke.Update(dt);
                _rocks.Update(dt);
                _lights.Update(dt);
            }

            private void Wave(PixelPainter p, bool far)
            {
                var q = (_t - FlyTime) / 0.45;
                if (q >= 1)
                {
                    return;
                }
                Func<double, bool> keep = g => Math.Sin(g) >= 0;
                if (far)
                {
                    keep = g => Math.Sin(g) < 0;
                }
                Sim.Band(p, 0, 0.5, 60 * (1 - Math.Pow(1 - q, 3)), 0.16, Math.Max(1, 4 * (1 - q)), 1.15 - q, [_stone.Body, _stone.Light, _f[4], _f[7]], keep);
            }

            public override void DrawBack(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var f = _f;
                // the scorch: white-hot where it hit, cooling through the fire to soot, fading late
                if (_hit)
                {
                    var k = t - FlyTime;
                    var fade = 1 - Smooth(Duration - 1, Duration - 0.2, t);
                    var rx = 16 * Math.Min(1, k / 0.06);
                    for (var y = 0; y <= 2; y++)
                    {
                        for (var x = Math.Floor(-rx); x <= Math.Ceiling(rx); x++)
                        {
                            var d = Hypot((x + 0.5) / rx, (y + 0.5) / 4);
                            if (d > 1)
                            {
                                continue;
                            }
                            var heat = Math.Pow(Clamp(1 - k / 1.4), 1.4) * (1.25 - d) - Rand(x * 31 + y, 3) * 0.35;
                            uint c;
                            if (heat > 0.06)
                            {
                                c = f[(int)Math.Max(1, Math.Min(8, Round(heat * 8)))];
                            }
                            else if (d < 0.5)
                            {
                                c = Hex("#0d0a12");
                            }
                            else
                            {
                                c = Hex("#17121d");
                            }
                            p.Dot(x, y, c, fade);
                        }
                    }
                    Wave(p, true);
                }
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var f = _f;
                var sm = _sm;
                var stone = _stone;
                var list = _smoke.Puffs();
                Cloud(p, list, sm, [sm[0], Mix(f[1], sm[1], 0.5), Mix(f[2], sm[2], 0.4), f[3], f[4], f[6], f[7]], 1.4, 0.82, null, 1.4);
                if (_hit)
                {
                    Wave(p, false);
                }
                foreach (var b in _rocks.List)
                {
                    var r = 0.6 + b.S * 0.75;
                    var fade = Math.Min(1, b.Life / 0.5);
                    var lx = Math.Cos(b.A) * r * 0.4;
                    var ly = Math.Sin(b.A) * r * 0.4;
                    p.Shade(b.X - r - 1, b.Y - r - 1, b.X + r + 1, b.Y + r + 1, (x, y, o2) =>
                    {
                        var dx = x - b.X;
                        var dy = y - b.Y;
                        var d = Hypot(dx * 1.15, dy);
                        if (d > r + 0.9)
                        {
                            return 0;
                        }
                        var lit = dx * lx + dy * ly;
                        if (d > r)
                        {
                            o2.C = Hex("#1a1422");
                        }
                        else if (lit < -0.4)
                        {
                            o2.C = stone.Light;
                        }
                        else if (lit > 0.6)
                        {
                            o2.C = stone.Shade;
                        }
                        else
                        {
                            o2.C = stone.Body;
                        }
                        o2.A = fade;
                        return 0;
                    });
                }
                _ps.Draw(p);
                if (t < FlyTime)
                {
                    var u = Progress(t);
                    var (x, y) = At(u);
                    var h = Heading(u);
                    var cx = Math.Cos(h);
                    var cy = Math.Sin(h);
                    var fr = Math.Floor(t * 30);
                    // the motor: a short white-hot jet that flickers, the missile riding its heading
                    var jet = 5 + 3 * Rand(fr, 7);
                    for (double d = 0; d <= jet; d += 0.5)
                    {
                        var v = d / jet;
                        var c = f[4];
                        if (v < 0.3)
                        {
                            c = White;
                        }
                        else if (v < 0.6)
                        {
                            c = f[6];
                        }
                        p.Dot(x - cx * (6.5 + d), y - cy * (6.5 + d), c);
                        if (v < 0.5)
                        {
                            p.Dot(x - cx * (6.5 + d) - cy * 0.8, y - cy * (6.5 + d) + cx * 0.8, f[5]);
                        }
                    }
                    TurnSprite(p, Missile, _pal, x, y, h, 6, 2.5);
                    p.Glow(x - cx * 8, y - cy * 8, 18, f[4], 0.5);
                    p.Glow(x - cx * 7, y - cy * 7, 6, f[7], 0.5);
                }
                // the hit's flash: a white ball for two frames and a ring of flame
                var hk = t - FlyTime;
                if (hk >= 0 && hk < 0.07)
                {
                    var rr = 14 * (0.8 + 0.2 * (1 - hk / 0.07));
                    p.Circle(0, -6, rr + 1.2, f[3]);
                    var mid = f[6];
                    var core = f[7];
                    if (hk < 0.035)
                    {
                        mid = f[7];
                        core = White;
                    }
                    p.Circle(0, -6, rr, mid);
                    p.Circle(0, -6, rr * 0.6, core);
                }
                if (hk >= 0 && hk < 0.3)
                {
                    var rr = 8 + 30 * (1 - Math.Pow(1 - hk / 0.3, 3));
                    p.Ring(0, -6, rr, Math.Max(1, 2.5 * (1 - hk / 0.3) + 0.4), f[5], (_, ang) =>
                    {
                        if (-6 + Math.Sin(ang) * rr > 0)
                        {
                            return 0;
                        }
                        return Math.Min(1, (1 - hk / 0.3) * 1.6);
                    });
                }
                _lights.Draw(p);
            }
        }
    }
}
