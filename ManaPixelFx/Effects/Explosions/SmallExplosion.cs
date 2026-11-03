using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Effects.Explosions
{
    /// <summary>
    /// pixelfx's explosions/small-explosion.js: a ground blast -- a white flash and shock rings, fire cooling into a
    /// rolling smoke column, sparks, burning brands that land and keep burning, tumbling rocks and a glowing scorch.
    /// The particle recipe is adapted from Sprite Fusion's open-source explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class SmallExplosion : PixelEffect
    {
        private const double Duration = 5.4;
        private static readonly uint Soot = Hex("#0d0a12");
        private static readonly uint Soot2 = Hex("#17121d");
        private static readonly uint Outline = Hex("#1a1422");
        private static readonly uint Warm = Rgb(255, 176, 96);

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "explosions/small-explosion",
            Name = "Explosion",
            Description = "A ground blast: a white flash and shock rings, fire that cools into a rolling smoke column, sparks, burning brands that land and keep burning, tumbling rocks and a glowing scorch.",
            Size = (300, 240),
            Origin = (150, 196),
            Duration = Duration,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["stone"] = "#4a3f69",
                ["size"] = 1.0,
                ["seed"] = 7.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        private sealed class Flash
        {
            public double R, Life, Max;
        }

        private sealed class Ring
        {
            public double R0, R1, Life, Max, Thick;
            public uint C;
        }

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _f;
            private readonly double _s;
            private readonly double _rad;
            private readonly Stone _st;
            private readonly PixelRng _r;
            private readonly Particles _ps;
            private readonly Blazes _blazes;
            private readonly Bits _rocks;
            private readonly Lights _lights = new();
            private readonly List<Flash> _flashes = [];
            private readonly List<Ring> _rings = [];
            private readonly double _y;
            private double _t;

            public Run(PixelFxOptions o)
            {
                _f = Fit(o.Ramp("palette"), 8);
                var f = _f;
                _s = o.Number("size");
                var s = _s;
                _rad = 18 * s;
                var rad = _rad;
                var power = 0.9 * Math.Pow(s, 0.8);
                const double earth = 1.6;
                _st = Stone.Of(o.Color("stone"));
                var st = _st;
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                Blazes? blazes = null;
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [f[6], f[5], f[3], f[2]], onLand: (x, y) => blazes!.Add(x, y - 0.25, r.Range(0.9, 1.5), r.Range(0.55, 0.85)));
                blazes = new Blazes(_ps);
                _blazes = blazes;
                _rocks = new Bits(_ => 0);
                var ps = _ps;
                _y = -rad * 0.3;
                var y0 = _y;

                // the fireball
                _flashes.Add(new Flash { R = rad * 1.05, Life = 0.06, Max = 0.06 });
                _rings.Add(new Ring { R0 = rad * 0.45, R1 = rad * 2.7, Life = 0.34, Max = 0.34, Thick = 3, C = f[5] });
                _rings.Add(new Ring { R0 = rad * 0.3, R1 = rad * 1.6, Life = 0.22, Max = 0.22, Thick = 1.8, C = f[3] });
                _lights.Add(0, y0, rad * 3.4, Warm, 0.6 * power, 0.7);
                _lights.Add(0, y0, rad * 1.6, White, 0.6, 0.08);
                for (var i = 0; i < 5; i++)
                {
                    ps.Spawn(P.Fire, r.Range(-2.5, 2.5), y0 + r.Range(-2.5, 2.5), r.Range(-20, 20), r.Range(-40, -10), r.Range(0.18, 0.3), 0, r.Range(rad * 0.4, rad * 0.55));
                }
                for (var i = 0; i < Round(28 * power); i++)
                {
                    var a = r.Range(0, Tau);
                    var d = r.Range(0, rad * 0.55);
                    var sp = r.Range(40, 210) * power;
                    ps.Spawn(P.Fire, Math.Cos(a) * d, y0 + Math.Sin(a) * d, Math.Cos(a) * sp, Math.Sin(a) * sp * 0.8 - 40, r.Range(0.55, 1.15), 0, r.Range(rad * 0.22, rad * 0.46), 0, Smokes(r, 0.45));
                }
                for (var i = 0; i < 5; i++)
                {
                    var a = r.Range(0, Tau);
                    var d = r.Range(rad * 0.1, rad * 0.5);
                    ps.Spawn(P.Smoke, Math.Cos(a) * d, y0 + Math.Sin(a) * d, Math.Cos(a) * 30, Math.Sin(a) * 20 - 30, r.Range(0.9, 1.5), Rgb(34, 29, 38), r.Range(rad * 0.15, rad * 0.26), r.Range(0.06, 0.2), PF.Big);
                }
                for (var i = 0; i < 5; i++)
                {
                    ps.Spawn(P.Fire, r.Range(-rad * 0.25, rad * 0.25), y0, r.Range(-12, 12), r.Range(-180, -100), r.Range(0.8, 1.3), 0, r.Range(rad * 0.24, rad * 0.36), r.Range(0, 0.1), Smokes(r, 0.6));
                }
                for (var i = 0; i < Round(10 * power); i++)
                {
                    var a = r.Range(0, Tau);
                    var d = r.Range(0, rad * 0.7);
                    var sp = r.Range(15, 100);
                    ps.Spawn(P.Smoke, Math.Cos(a) * d, y0 + Math.Sin(a) * d, Math.Cos(a) * sp, Math.Sin(a) * sp * 0.6 - 35, r.Range(1.8, 2.8), 0, r.Range(rad * 0.26, rad * 0.44), r.Range(0.12, 0.45), PF.Big);
                }
                for (var i = 0; i < Round(80 * power); i++)
                {
                    var a = r.Range(0, Tau);
                    var sp = r.Range(140, 380) * power;
                    ps.Spawn(P.Spark, 0, y0, Math.Cos(a) * sp, Math.Sin(a) * sp - 70, r.Range(0.18, 0.6));
                }
                for (var i = 0; i < Round(40 * power); i++)
                {
                    var a = r.Range(0, Tau);
                    var sp = r.Range(30, 150);
                    ps.Spawn(P.Ember, Math.Cos(a) * rad * 0.4, y0 + Math.Sin(a) * rad * 0.4, Math.Cos(a) * sp, Math.Sin(a) * sp - 75, r.Range(1.2, 2.6), f[3]);
                }
                for (var i = 0; i < Round(r.Range(9, 14) * Math.Max(0, power - 0.55)); i++)
                {
                    var a = -Math.PI / 2 + r.Range(-1.35, 1.35);
                    var sp = r.Range(170, 330);
                    ps.Spawn(P.Brand, 0, y0, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.7, 1.3));
                }
                // the ground's part: dust rolling out along the floor, rocks and grit thrown up, a dust column
                for (var i = 0; i < Round(rad * 0.5); i++)
                {
                    var side = r.Sign();
                    ps.Spawn(P.Smoke, side * r.Range(0, rad * 0.6), -r.Range(1, 5 + rad * 0.04), side * r.Range(3, 9.5) * rad, -r.Range(2, 15), r.Range(1.1, 2.0), st.Dust, r.Range(4, 7) + rad / 6, r.Range(0, 0.06), PF.Big);
                }
                for (var i = 0; i < 26 * Math.Min(1.5, s); i++)
                {
                    _rocks.Add(new Bit
                    {
                        Kind = B.Chunk, X = r.Range(-0.4, 0.4) * rad, Y = -3, Vx = r.Range(-9, 9) * rad * 0.25, Vy = -r.Range(7, 16) * rad * 0.9,
                        G = 1, Drag = 0.4, Bounce = 0.3, Life = r.Range(1.8, 2.6), A = r.Range(0, Tau), Va = r.Range(-16, 16), S = r.Int(1, 3),
                    });
                }
                for (var i = 0; i < Round(earth * rad * 4); i++)
                {
                    var u = r.Range(-1, 1);
                    var a = -Math.PI / 2 + u * 0.5;
                    var sp = r.Range(5, 15) * rad * (1 - 0.45 * Math.Abs(u));
                    ps.Spawn(P.Grit, u * 0.25 * rad, -1, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(1.2, 2.2), r.Pick([st.Light, st.Body, st.Body, st.Shade]), Chip(r));
                }
                for (var i = 0; i < Round(earth * 12); i++)
                {
                    ps.Spawn(P.Smoke, r.Range(-0.3, 0.3) * rad, -r.Range(2, rad * 0.6), r.Range(-15, 15), -r.Range(60, 190) * earth * 0.6, r.Range(1.6, 2.6), st.Dust, r.Range(5, 9) * Math.Sqrt(s) + rad / 7, r.Range(0, 0.15), PF.Big);
                }
            }

            private static int Smokes(PixelRng r, double chance)
            {
                if (r.Chance(chance))
                {
                    return PF.Smokes | PF.Big;
                }
                return 0;
            }

            private static double Chip(PixelRng r)
            {
                if (r.Chance(0.45))
                {
                    return 2;
                }
                return 1;
            }

            public override bool HasBack => true;

            public override void Update(double dt, PixelFxOptions o)
            {
                _t += dt;
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
                _rocks.Update(dt);
                // rocks that hit hard kick up dust
                foreach (var b in _rocks.List)
                {
                    if (b.Rest && !b.Landed)
                    {
                        b.Landed = true;
                        for (var i = 0; i < 3; i++)
                        {
                            _ps.Spawn(P.Dust, b.X + _r.Range(-1.5, 1.5), -0.5, _r.Sign() * _r.Range(10, 55) * 0.6, -_r.Range(2, 22), _r.Range(0.3, 0.65), _st.Dust, _r.Range(1.3, 2.8));
                        }
                    }
                }
                _blazes.Update(dt);
                _ps.Update(dt);
                _lights.Update(dt);
            }

            // the floor under the blast: white-hot, cooling through the fire ramp to soot
            private void Scorch(PixelPainter p, double rx0, double cool, double fade)
            {
                if (fade <= 0)
                {
                    return;
                }
                var t = _t;
                var rx = rx0 * Math.Min(1, t / 0.08);
                const double ry = 4;
                var heat = _f.Reverse().ToArray();
                for (var y = 0; y <= 2; y++)
                {
                    for (var x = Math.Floor(-rx); x <= Math.Ceiling(rx); x++)
                    {
                        var dx = (x + 0.5) / rx;
                        var dy = (y + 0.5) / ry;
                        var d = Math.Sqrt(dx * dx + dy * dy);
                        if (d > 1)
                        {
                            continue;
                        }
                        var k = Rand(x * 31 + y, 3);
                        var tt = Math.Pow(Clamp(1 - t / cool), 1.4) * (1.25 - d) - k * 0.35;
                        if (tt > 0.06)
                        {
                            var c = heat[(int)Math.Min(heat.Length - 1, Math.Floor((1 - Math.Min(1, tt)) * (heat.Length - 1)))];
                            p.Dot(x, y, c, fade);
                            if ((I32(x) & 3) == 0)
                            {
                                p.Glow(x + 0.5, y, 6, c, 0.18 * tt * fade);
                            }
                        }
                        else
                        {
                            var c = Soot2;
                            if (d < 0.5)
                            {
                                c = Soot;
                            }
                            var a = 1 - d;
                            if (d < 0.95 - k * 0.3)
                            {
                                a = 1;
                            }
                            p.Dot(x, y, c, a * fade);
                        }
                    }
                }
            }

            // a rock: a lit side, a shaded side, an outline, spinning
            private static void Rock(PixelPainter p, Bit b, Stone st)
            {
                var r = 0.6 + b.S * 0.75;
                var fade = Math.Min(1, b.Life / 0.5);
                var lx = Math.Cos(b.A) * r * 0.4;
                var ly = Math.Sin(b.A) * r * 0.4;
                p.Shade(b.X - r - 1, b.Y - r - 1, b.X + r + 1, b.Y + r + 1, (x, y, o) =>
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
                        o.C = Outline;
                    }
                    else if (lit < -0.4)
                    {
                        o.C = st.Light;
                    }
                    else if (lit > 0.6)
                    {
                        o.C = st.Shade;
                    }
                    else
                    {
                        o.C = st.Body;
                    }
                    o.A = fade;
                    return 0;
                });
            }

            private void Wave(PixelPainter p, bool far)
            {
                var k = _t / 0.5;
                if (k >= 1)
                {
                    return;
                }
                uint[] ramp = [_st.Body, _st.Light, _f[3], _f[6]];
                Func<double, bool> keep = g => Math.Sin(g) >= 0;
                if (far)
                {
                    keep = g => Math.Sin(g) < 0;
                }
                Sim.Band(p, 0, 0.5, 80 * _s * Ease.OutCubic(k), 0.16, Math.Max(1, (2 + _rad / 9) * (1 - k)), 1.15 - k, ramp, keep);
            }

            public override void DrawBack(PixelPainter p, double t, PixelFxOptions o)
            {
                Scorch(p, 22 * _s, 1.2 + _rad / 14, 1 - Smooth(Duration - 1.2, Duration - 0.2, _t));
                _ps.Draw(p, "back");
                _blazes.DrawBack(p);
                Wave(p, true);
            }

            public override void Draw(PixelPainter p, double t, PixelFxOptions o)
            {
                Wave(p, false);
                foreach (var b in _rocks.List)
                {
                    Rock(p, b, _st);
                }
                _ps.Draw(p, "front");
                foreach (var f in _flashes)
                {
                    var k2 = f.Life / f.Max;
                    var r = f.R * (0.8 + 0.2 * k2);
                    p.Circle(0, _y, r + 1.2, _f[2]);
                    var mid = _f[5];
                    var core = _f[6];
                    if (k2 > 0.45)
                    {
                        mid = _f[6];
                        core = White;
                    }
                    p.Circle(0, _y, r, mid);
                    p.Circle(0, _y, r * 0.6, core);
                }
                foreach (var ring in _rings)
                {
                    var k2 = ring.Life / ring.Max;
                    var e = 1 - Math.Pow(k2, 3);
                    p.Ring(0, _y, ring.R0 + (ring.R1 - ring.R0) * e, Math.Max(1, ring.Thick * k2 + 0.4), ring.C, Math.Min(1, k2 * 1.6));
                }
                _lights.Draw(p);
            }
        }
    }
}
