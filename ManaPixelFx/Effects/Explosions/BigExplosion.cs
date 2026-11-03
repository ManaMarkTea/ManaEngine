using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Explosions.ExplosionsLib;

namespace ManaPixelFx.Effects.Explosions
{
    /// <summary>
    /// pixelfx's explosions/big-explosion.js: a huge ground blast in three beats -- a white dome, a rolling fireball that
    /// climbs into a mushrooming smoke column, heavy rocks, and fires left burning. The blast recipe is adapted from
    /// Sprite Fusion's open-source explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class BigExplosion : PixelEffect
    {
        private const double Duration = 7.2;
        private const double Rad = 30;
        private const double Power = 1.45;
        private const double Earth = 1.6;

        // the plume's smoke, lit on top to sooty underneath
        private static readonly uint[] Sm = Ramp(["#83777f", "#6e6470", "#575060", "#3f3947", "#29242f"]);

        private readonly record struct Beat(double T, double X, double Y, double Rad, double Power, bool Main);

        // three detonations, a beat apart, climbing as they go
        private static readonly Beat[] Blasts =
        [
            new(0, 0, -9, Rad, Power, true),
            new(0.13, -17, -30, 17, 0.9, false),
            new(0.25, 15, -46, 15, 0.8, false),
        ];

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "explosions/big-explosion",
            Name = "Big explosion",
            Description = "A huge ground blast in three beats: a white dome, a rolling fireball that climbs into a mushrooming smoke column, heavy rocks, and fires left burning.",
            Size = (360, 300),
            Origin = (180, 262),
            Duration = Duration,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["stone"] = "#4a3f69",
                ["seed"] = 11.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o, Meta);

        private sealed class Flash
        {
            public double X, Y, R, Life, Max;
        }

        private sealed class Ring
        {
            public double X, Y, R0, R1, Life, Max, Thick;
            public uint C;
        }

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _f;
            private readonly uint[] _fr;
            private readonly uint[] _hr;
            private readonly Stone _st;
            private readonly PixelRng _r;
            private readonly uint _warm;
            private readonly double _x0, _x1, _y0, _y1;
            private readonly Particles _ps;
            private readonly Blazes _blazes;
            private readonly Bits _rocks = new(_ => 0);
            private readonly Lights _lights = new();
            private readonly List<Flash> _flashes = [];
            private readonly List<Ring> _rings = [];
            private readonly Plume _plume;
            private readonly Column _column;
            private double _t;
            private int _next;

            public Run(PixelFxOptions o, PixelEffectMeta meta)
            {
                _f = Fit(o.Ramp("palette"), 8);
                _fr = FireRamp(_f);
                _hr = HeatRamp(_fr);
                _st = Stone.Of(o.Color("stone"));
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                _warm = Mix(_f[3], _f[6], 0.45);
                var (w, h) = meta.Size;
                var (ox, oy) = meta.Origin;
                _x0 = -ox + 3;
                _x1 = w - ox - 3;
                _y0 = -oy + 3;
                _y1 = h - oy - 3;
                Blazes? blazes = null;
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [_f[6], _f[5], _f[3], _f[2]], onLand: (x, y) => blazes!.Add(x, y - 0.25, r.Range(3, 4.4), r.Range(0.7, 1.25)));
                blazes = new Blazes(_ps);
                _blazes = blazes;
                _plume = new Plume(r, new PlumeOptions
                {
                    X = 0, Y = -34, Rise = 118, Tau = 1.25, Drift = 4, Tube = (9, 21), Ring = (2, 19), Grow = 1.0, N = 42, Size = (6, 11), Swell = 0.9,
                    Spin = 2.6, SpinTau = 1.7, Heat = 1.2, Cool = 0.42, Under = 0.5, Tilt = 0.22, Squash = 0.72, Fade = (4.4, 6.4), Spread = 22,
                });
                _column = new Column(r, new ColumnOptions { W = 5, Foot = 18, Speed = 85, Size = (7, 11), N = 28, Sway = 2.5 });
            }

            public override bool HasBack => true;

            private double Top()
            {
                var s = _plume.Shape();
                return s.Y + s.Tube * 0.45;
            }

            private void Blast(Beat b)
            {
                var r = _r;
                var ps = _ps;
                var f = _f;
                var (x, y, rad, power, main) = (b.X, b.Y, b.Rad, b.Power, b.Main);
                // the later beats flash and ring in the air; the first is a dome on the floor (drawn from t)
                if (!main)
                {
                    _flashes.Add(new Flash { X = x, Y = y, R = rad * 1.05, Life = 0.06, Max = 0.06 });
                    _rings.Add(new Ring { X = x, Y = y, R0 = rad * 0.45, R1 = rad * 2.7, Life = 0.36, Max = 0.36, Thick = 3, C = f[5] });
                    _rings.Add(new Ring { X = x, Y = y, R0 = rad * 0.3, R1 = rad * 1.6, Life = 0.24, Max = 0.24, Thick = 1.8, C = f[3] });
                }
                // light: a big warm flare (raised for a ground blast, so it lights the air and the floor gets a pool)
                if (main)
                {
                    _lights.Add(x, y - 24, 72, _warm, 0.6 * power, 0.8);
                    _lights.Add(x, y - 12, 44, White, 0.6, 0.1);
                }
                else
                {
                    _lights.Add(x, y, rad * 3.4, _warm, 0.6 * power, 0.8);
                    _lights.Add(x, y, rad * 1.6, White, 0.6, 0.1);
                }
                // a ground blast throws nothing into the floor
                var down = Math.PI;
                if (main)
                {
                    down = 0.25;
                }
                for (var i = 0; i < 5; i++)
                {
                    ps.Spawn(P.Fire, x + r.Range(-2.5, 2.5), y + r.Range(-2.5, 2.5), r.Range(-20, 20), r.Range(-40, -10), r.Range(0.18, 0.3), 0, r.Range(rad * 0.4, rad * 0.55));
                }
                for (var i = 0; i < Round(26 * power); i++)
                {
                    double a;
                    if (main)
                    {
                        a = r.Range(-Math.PI - down, down);
                    }
                    else
                    {
                        a = r.Range(0, Tau);
                    }
                    var d = r.Range(0, rad * 0.55);
                    var sp = r.Range(40, 200) * power;
                    var flag = 0;
                    var life = r.Range(0.55, 1.15);
                    var size = r.Range(rad * 0.22, rad * 0.46);
                    if (r.Chance(0.45))
                    {
                        flag = PF.Smokes | PF.Big;
                    }
                    ps.Spawn(P.Fire, x + Math.Cos(a) * d, y + Math.Sin(a) * d, Math.Cos(a) * sp, Math.Sin(a) * sp * 0.8 - 40, life, 0, size, 0, flag);
                }
                for (var i = 0; i < 5; i++)
                {
                    var a = r.Range(-Math.PI, 0);
                    var d = r.Range(rad * 0.1, rad * 0.5);
                    ps.Spawn(P.Smoke, x + Math.Cos(a) * d, y + Math.Sin(a) * d, Math.Cos(a) * 30, Math.Sin(a) * 20 - 30, r.Range(0.9, 1.5), Hex("#221d26"), r.Range(rad * 0.15, rad * 0.26), r.Range(0.06, 0.2), PF.Big);
                }
                for (var i = 0; i < Round(8 * power); i++)
                {
                    var a = r.Range(-Math.PI, 0);
                    var d = r.Range(0, rad * 0.7);
                    var sp = r.Range(15, 90);
                    ps.Spawn(P.Smoke, x + Math.Cos(a) * d, y + Math.Sin(a) * d, Math.Cos(a) * sp, Math.Sin(a) * sp * 0.6 - 35, r.Range(1.8, 2.8), 0, r.Range(rad * 0.26, rad * 0.4), r.Range(0.12, 0.45), PF.Big);
                }
                for (var i = 0; i < Round(80 * power); i++)
                {
                    var a = r.Range(0, Tau);
                    double fastest = 380;
                    if (main)
                    {
                        fastest = 470;
                    }
                    var sp = r.Range(170, fastest);
                    ps.Spawn(P.Spark, x, y, Math.Cos(a) * sp, Math.Sin(a) * sp - 70, r.Range(0.2, 0.65));
                }
                for (var i = 0; i < Round(30 * power); i++)
                {
                    var a = r.Range(0, Tau);
                    var sp = r.Range(30, 150);
                    ps.Spawn(P.Ember, x + Math.Cos(a) * rad * 0.4, y + Math.Sin(a) * rad * 0.4, Math.Cos(a) * sp, Math.Sin(a) * sp - 75, r.Range(1.4, 3), f[3]);
                }
                if (!main)
                {
                    return;
                }
                // burning brands that land and keep the ground on fire
                for (var i = 0; i < 14; i++)
                {
                    var a = -Math.PI / 2 + r.Range(-1.3, 1.3);
                    var sp = r.Range(160, 340);
                    ps.Spawn(P.Brand, x, y, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.8, 1.5));
                }
                // the ground's part: dust rolling out along the floor, a dust column, grit and heavy rocks thrown up
                for (var i = 0; i < 18; i++)
                {
                    var s = r.Sign();
                    ps.Spawn(P.Smoke, s * r.Range(0, Rad * 0.6), -r.Range(1, 6), s * r.Range(3, 9) * Rad, -r.Range(2, 15), r.Range(1.3, 2.2), _st.Dust, r.Range(8, 12), r.Range(0, 0.06), PF.Big);
                }
                for (var i = 0; i < Round(Earth * 12); i++)
                {
                    ps.Spawn(P.Smoke, r.Range(-0.3, 0.3) * Rad, -r.Range(2, Rad * 0.6), r.Range(-15, 15), -r.Range(60, 190) * Earth * 0.6, r.Range(1.6, 2.6), _st.Dust, r.Range(8, 12), r.Range(0, 0.15), PF.Big);
                }
                for (var i = 0; i < Round(Earth * Rad * 4); i++)
                {
                    var u = r.Range(-1, 1);
                    var a = -Math.PI / 2 + u * 0.55;
                    var sp = r.Range(5, 15) * Rad * (1 - 0.45 * Math.Abs(u));
                    var life = r.Range(1.4, 2.4);
                    var col = r.Pick([_st.Light, _st.Body, _st.Body, _st.Shade]);
                    double size = 1;
                    if (r.Chance(0.45))
                    {
                        size = 2;
                    }
                    ps.Spawn(P.Grit, u * 0.25 * Rad, -1, Math.Cos(a) * sp, Math.Sin(a) * sp, life, col, size);
                }
                for (var i = 0; i < 26; i++)
                {
                    var big = i < 10;
                    var hot = r.Chance(0.45);
                    var sx = r.Sign();
                    var bit = new Bit { Kind = B.Chunk, X = r.Range(-0.4, 0.4) * Rad, Y = -4 };
                    if (big)
                    {
                        bit.Vx = sx * r.Range(30, 120);
                        bit.Vy = -r.Range(280, 480);
                    }
                    else
                    {
                        bit.Vx = sx * r.Range(30, 150);
                        bit.Vy = -r.Range(320, 560);
                    }
                    bit.G = 1;
                    bit.Drag = 0.3;
                    bit.Bounce = 0.3;
                    bit.Life = r.Range(3.6, 5);
                    bit.A = r.Range(0, Tau);
                    bit.Va = r.Range(-14, 14);
                    if (big)
                    {
                        bit.S = r.Range(2.6, 3.8);
                    }
                    else
                    {
                        bit.S = r.Range(1.2, 2.4);
                    }
                    if (hot)
                    {
                        bit.Heat = r.Range(0.6, 1);
                    }
                    _rocks.Add(bit);
                }
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                var f = _f;
                _t += dt;
                var t = _t;
                while (_next < Blasts.Length && t >= Blasts[_next].T)
                {
                    Blast(Blasts[_next++]);
                }
                if (t > 0.08)
                {
                    _plume.Update(dt);
                }
                var s = _plume.Shape();
                var k = 1 - Smooth(2.2, 3.1, t);
                // the column: smoke and fire boil up off the floor into the fireball, dust is drawn in along the ground
                if (t > 0.2 && t < 3.1)
                {
                    if (r.Chance(8 * k * dt))
                    {
                        ps.Spawn(P.Smoke, r.Range(-10, 10), -r.Range(1, 6), r.Range(-8, 8), -r.Range(40, 80), r.Range(1.5, 2.3), _st.Dust, r.Range(5, 8), 0, PF.Big);
                    }
                    if (t < 1.5 && r.Chance(16 * dt))
                    {
                        var x = r.Range(-6, 6);
                        var y = -r.Range(4, 14);
                        var vx = r.Range(-10, 10);
                        var vy = -r.Range(90, 150);
                        var life = r.Range(0.5, 0.8);
                        var size = r.Range(4, 7.5);
                        var flag = 0;
                        if (r.Chance(0.5))
                        {
                            flag = PF.Smokes | PF.Big;
                        }
                        ps.Spawn(P.Fire, x, y, vx, vy, life, 0, size, 0, flag);
                    }
                    if (t > 0.45 && r.Chance(12 * k * dt))
                    {
                        var side = r.Sign();
                        ps.Spawn(P.Smoke, side * r.Range(40, 120), -r.Range(1, 4), -side * r.Range(10, 30), -r.Range(0, 5), r.Range(1.3, 1.9), _st.Dust, r.Range(3.5, 5.5), 0, PF.Big);
                    }
                }
                // the fireball sheds embers, then ash drifts down out of the cloud
                if (t > 0.3 && t < 3 && r.Chance(10 * _plume.Hot() * dt))
                {
                    var a = r.Range(0, Tau);
                    ps.Spawn(P.Ember, s.X + Math.Cos(a) * (s.Ring + s.Tube), s.Y + Math.Sin(a) * s.Tube * 0.5, Math.Cos(a) * r.Range(40, 90), -r.Range(10, 60), r.Range(1.2, 2.4), f[3]);
                }
                if (t > 1.4 && t < 4.6 && r.Chance(7 * dt))
                {
                    ps.Spawn(P.Flake, s.X + r.Range(-1, 1) * (s.Ring + s.Tube), s.Y + s.Tube * 0.6, r.Range(-6, 6), r.Range(0, 10), r.Range(2, 3), r.Pick([Sm[1], Sm[2], f[2]]), r.Range(0.6, 1.2));
                }
                double feed = 0;
                if (t > 0.22 && t < 2.5)
                {
                    feed = 1;
                }
                _column.Update(dt, Top(), feed);
                Draft(ps, dt, x: 0, top: s.Y + s.Tube * 0.4, w: 9 + 7 * Math.Min(1, t), up: 170, pull: 130, low: 12, reach: 130, k: 1 - Smooth(2.5, 3.8, t));
                for (var i = _flashes.Count - 1; i >= 0; i--)
                {
                    _flashes[i].Life -= dt;
                    if (_flashes[i].Life <= 0)
                    {
                        _flashes.RemoveAt(i);
                    }
                }
                for (var i = _rings.Count - 1; i >= 0; i--)
                {
                    _rings[i].Life -= dt;
                    if (_rings[i].Life <= 0)
                    {
                        _rings.RemoveAt(i);
                    }
                }
                // rocks: molten ones cool and trail smoke and flame; every hard landing kicks up dust
                foreach (var b in _rocks.List)
                {
                    b.Pvy = b.Vy;
                }
                _rocks.Update(dt);
                for (var i = _rocks.List.Count - 1; i >= 0; i--)
                {
                    var b = _rocks.List[i];
                    if (b.X < _x0 + 4 || b.X > _x1 - 4)
                    {
                        _rocks.List.RemoveAt(i);
                        continue;
                    }
                    if (b.Heat > 0)
                    {
                        b.Heat = Math.Max(0, b.Heat - dt * 0.3);
                        if (!b.Rest && b.Heat > 0.25)
                        {
                            b.Pts ??= [];
                            b.Pts.Add(new TrailPoint(b.X, b.Y, t));
                            if (r.Chance(14 * dt))
                            {
                                ps.Spawn(P.Fire, b.X, b.Y, b.Vx * 0.1, b.Vy * 0.1 - 8, r.Range(0.16, 0.3), 0, r.Range(1.2, 2.2));
                            }
                        }
                        else if (b.Rest && r.Chance(3 * b.Heat * dt))
                        {
                            ps.Spawn(P.Smoke, b.X, b.Y - 1, r.Range(-3, 3), -r.Range(5, 12), r.Range(0.8, 1.4), 0, r.Range(1.6, 2.6));
                        }
                    }
                    var landed = (b.Pvy > 0 && b.Vy <= 0) || (b.Rest && !b.Landed);
                    if (landed && b.Pvy > 60)
                    {
                        for (var j = 0; j < 2 + Round(b.S); j++)
                        {
                            ps.Spawn(P.Dust, b.X + r.Range(-1.5, 1.5), -0.5, r.Sign() * r.Range(10, 50), -r.Range(2, 20), r.Range(0.3, 0.7), _st.Dust, r.Range(1.3, 1 + b.S));
                        }
                        if (b.Heat > 0.5 && !b.Landed)
                        {
                            _blazes.Add(b.X, -0.25, r.Range(1.2, 2.2), 0.45);
                        }
                    }
                    if (b.Rest)
                    {
                        b.Landed = true;
                    }
                    if (b.Pts != null)
                    {
                        while (b.Pts.Count > 0 && t - b.Pts[0].T > 0.45)
                        {
                            b.Pts.RemoveAt(0);
                        }
                    }
                }
                _blazes.Update(dt);
                ps.Update(dt);
                Cull(ps, _x0, _y0, _x1, _y1);
                _lights.Update(dt);
            }

            private void Wave(PixelPainter p, bool far)
            {
                var k = _t / 0.6;
                if (k >= 1)
                {
                    return;
                }
                Func<double, bool> keep = g => Math.Sin(g) >= 0;
                if (far)
                {
                    keep = g => Math.Sin(g) < 0;
                }
                Sim.Band(p, 0, 0.5, 132 * Ease.OutCubic(k), 0.16, Math.Max(1, 6 * (1 - k)), 1.15 - k, [_st.Body, _st.Light, _f[3], _f[6]], keep);
            }

            public override void DrawBack(PixelPainter p, double t, PixelFxOptions o)
            {
                Scorch(p, _hr, _t, 52, 3.4, 1 - Smooth(Duration - 1.6, Duration - 0.3, _t), 3);
                Wave(p, true);
                DrawPs(p, _ps, "back", _fr);
                _column.Draw(p, Top(), _fr, Sm, 1 - _t / 1.4, _plume.Hot());
                _plume.Draw(p, _fr, Sm);
                _blazes.DrawBack(p);
            }

            public override void Draw(PixelPainter p, double t, PixelFxOptions o)
            {
                var f = _f;
                var time = _t;
                Wave(p, false);
                // the first beat: a white hero frame, a dome of white-hot air that cools into the fire, a flare, a shell of light
                if (time < 0.04)
                {
                    Dome(p, f, 0, 44, 1);
                    p.Glow(0, -30, 68, White, 0.6);
                }
                else if (time < 0.11)
                {
                    Dome(p, f, 0, 38 - (time - 0.04) * 120, 1 - (time - 0.04) / 0.07);
                }
                foreach (var b in _rocks.List)
                {
                    if (b.Pts != null)
                    {
                        Trail(p, b.Pts, time, _fr, Sm, 0.45, 0.6 + b.S * 0.3, b.Heat);
                    }
                    Rock(p, b, _st, _fr);
                }
                DrawPs(p, _ps, "front", _fr);
                if (time < 0.24)
                {
                    Sim.Flare(p, 0, -14, 70 * Math.Pow(1 - time / 0.24, 1.4), 1 - time / 0.24, f);
                }
                if (time < 0.5)
                {
                    var u = time / 0.5;
                    var rr = 34 + 80 * Ease.OutCubic(u);
                    var c = f[3];
                    if (u < 0.3)
                    {
                        c = f[6];
                    }
                    else if (u < 0.6)
                    {
                        c = f[5];
                    }
                    p.Ring(0, 0, rr, Math.Max(1, 3 * (1 - u) + 0.5), c, (_, a) =>
                    {
                        if (Math.Sin(a) > 0)
                        {
                            return 0;
                        }
                        return Math.Min(1, (1 - u) * 1.6);
                    });
                    GlowDome(p, 0, 0, rr, 6, f[4], 0.35 * (1 - u));
                }
                foreach (var fl in _flashes)
                {
                    var k2 = fl.Life / fl.Max;
                    var r = fl.R * (0.8 + 0.2 * k2);
                    p.Circle(fl.X, fl.Y, r + 1.2, f[2]);
                    var mid = f[5];
                    var core = f[6];
                    if (k2 > 0.45)
                    {
                        mid = f[6];
                        core = White;
                    }
                    p.Circle(fl.X, fl.Y, r, mid);
                    p.Circle(fl.X, fl.Y, r * 0.6, core);
                }
                foreach (var ring in _rings)
                {
                    var k2 = ring.Life / ring.Max;
                    var e = 1 - Math.Pow(k2, 3);
                    p.Ring(ring.X, ring.Y, ring.R0 + (ring.R1 - ring.R0) * e, Math.Max(1, ring.Thick * k2 + 0.4), ring.C, Math.Min(1, k2 * 1.6));
                }
                // the fireball lights the floor under it while it burns
                var hot = _plume.Hot();
                var flash = Math.Max(0, 1 - time / 0.9);
                Pool(p, 0, 1, 150, 22, f[3], 0.24 * hot + 0.5 * flash * flash);
                _lights.Draw(p);
            }
        }
    }
}
