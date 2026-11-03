using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Effects.Explosions
{
    /// <summary>
    /// pixelfx's explosions/firework-burst.js: a shell climbs, slows and bursts into a sphere of coloured trailing stars
    /// round a gold core; they droop, crackle into white pops and fade, leaving glitter. The flare, twinkling crosses and
    /// glitter are adapted from Sprite Fusion's open-source sparkle, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class FireworkBurst : PixelEffect
    {
        private const double Rise = 0.42;
        private const double Y0 = 72;
        private const double Drag = 2.6;
        private const double G = 88;
        private const double TrailLife = 0.34;
        private const double Turn = 0.22;
        private static readonly (int X, int Y)[] Cross = [(1, 0), (-1, 0), (0, 1), (0, -1)];

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "explosions/firework-burst",
            Name = "Firework burst",
            Description = "A shell climbs, slows and bursts into a sphere of coloured trailing stars round a gold core; they droop, crackle into white pops and fade, leaving glitter.",
            Size = (240, 248),
            Origin = (120, 100),
            Duration = 3.6,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#3d0a33", "#701560", "#b0228f", "#f53cd1", "#ff86e6", "#ffd2f6", "#ffffff" },
                ["core"] = new[] { "#4a2c08", "#86520c", "#c48412", "#f0b424", "#ffe066", "#fff6c4", "#ffffff" },
                ["seed"] = 9.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        internal sealed class Star
        {
            public double X, Y, Vx, Vy, Life, Age, Acc, Seed;
            public uint[] Ramp = [];
            public uint[] First = [];
            public bool Big;
            public List<double> Pts = [];
        }

        internal readonly record struct Pop(double X, double Y, double T0, double Life, uint C);

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly uint[] _k;
            private readonly PixelRng _r;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly List<Star> _stars = [];
            private readonly List<Pop> _pops = [];
            private double _t;
            private bool _burst;

            public Run(PixelFxOptions o)
            {
                _c = Fit(o.Ramp("palette"), 7);
                _k = Fit(o.Ramp("core"), 7);
                _r = new PixelRng(o.Number("seed"));
                _ps = new Particles(rng: _r, sparks: [_k[6], _k[5], _k[4], _k[3]]);
            }

            public override bool HasBack => true;

            // a shell of stars thrown over a sphere: seen side-on, the ones flying at us or away look slower; the outer
            // stars burn gold for a beat, then turn to their colour
            private void Shell(int n, double speed, uint[] ramp, double life0, double life1, uint[] first)
            {
                var r = _r;
                for (var i = 0; i < n; i++)
                {
                    var z = r.Range(-1, 1);
                    var a = r.Range(0, Tau);
                    var s = Math.Sqrt(1 - z * z);
                    var v = speed * r.Range(0.93, 1.05);
                    var star = new Star { X = 0, Y = 0, Vx = Math.Cos(a) * s * v, Vy = Math.Sin(a) * s * v, Life = r.Range(life0, life1), Age = 0, Ramp = ramp, First = first };
                    star.Big = r.Chance(0.2);
                    star.Acc = r.Next();
                    star.Seed = r.Range(0, 100);
                    _stars.Add(star);
                }
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                var c = _c;
                var k5 = _k;
                _t += dt;
                var t = _t;
                // the shell climbs, slowing, shedding a fizz of gold sparks
                if (t < Rise)
                {
                    var y = Y0 * (1 - Ease.OutCubic(t / Rise));
                    if (r.Chance(50 * dt))
                    {
                        ps.Spawn(P.Spark, r.Range(-0.5, 0.5), y + 2, r.Range(-25, 25), r.Range(20, 70), r.Range(0.15, 0.35));
                    }
                    if (r.Chance(14 * dt))
                    {
                        ps.Spawn(P.Vapour, r.Range(-1, 1), y + 3, r.Range(-3, 3), r.Range(5, 15), r.Range(0.5, 0.8), Hex("#4a4458"), r.Range(1.5, 2.5));
                    }
                }
                if (!_burst && t >= Rise)
                {
                    _burst = true;
                    _lights.Add(0, 0, 110, c[4], 0.6, 1.5);
                    _lights.Add(0, 0, 30, White, 0.5, 0.07);
                    Shell(66, 236, c, 1.35, 1.9, k5);
                    Shell(26, 120, k5, 1.0, 1.4, k5);
                    // the burst charge's smoke, drifting where the shell broke
                    for (var i = 0; i < 7; i++)
                    {
                        var a = r.Range(0, Tau);
                        var d = r.Range(0, 8);
                        ps.Spawn(P.Vapour, Math.Cos(a) * d, Math.Sin(a) * d, Math.Cos(a) * r.Range(10, 30), Math.Sin(a) * r.Range(10, 30), r.Range(2.2, 3), Hex("#4a4458"), r.Range(6, 11));
                    }
                    for (var i = 0; i < 24; i++)
                    {
                        var a = r.Range(0, Tau);
                        var sp = r.Range(40, 140);
                        ps.Spawn(P.Spark, 0, 0, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.15, 0.35));
                    }
                }
                for (var i = _stars.Count - 1; i >= 0; i--)
                {
                    var s = _stars[i];
                    s.Age += dt;
                    var k = s.Age / s.Life;
                    if (k >= 1)
                    {
                        // the star crackles out in a few white pops
                        var pops = 2;
                        if (s.Big)
                        {
                            pops = 3;
                        }
                        for (var j = 0; j < pops; j++)
                        {
                            var x = s.X + r.Range(-3, 3);
                            var y = s.Y + r.Range(-3, 3);
                            var t0 = t + r.Range(0, 0.18);
                            var life = r.Range(0.06, 0.12);
                            var col = s.Ramp[5];
                            if (r.Chance(0.6))
                            {
                                col = White;
                            }
                            _pops.Add(new Pop(x, y, t0, life, col));
                        }
                        _stars.RemoveAt(i);
                        continue;
                    }
                    s.Vx -= s.Vx * Drag * dt;
                    s.Vy -= s.Vy * Drag * dt - G * dt;
                    s.X += s.Vx * dt;
                    s.Y += s.Vy * dt;
                    s.Pts.Add(s.X);
                    s.Pts.Add(s.Y);
                    s.Pts.Add(t);
                    while (s.Pts.Count > 3 && t - s.Pts[2] > TrailLife)
                    {
                        s.Pts.RemoveRange(0, 3);
                    }
                    // glitter shed along the way: specks that hang, twinkle and fall
                    var rate = 11.0;
                    if (k < 0.25)
                    {
                        rate = 4;
                    }
                    for (s.Acc += dt * rate; s.Acc > 1; s.Acc--)
                    {
                        var vx = s.Vx * 0.15 + r.Range(-6, 6);
                        var vy = s.Vy * 0.15 + r.Range(0, 10);
                        var life = r.Range(0.35, 0.8);
                        var col = White;
                        if (r.Chance(0.5))
                        {
                            col = k5[5];
                        }
                        double size = 0.5;
                        if (r.Chance(0.15))
                        {
                            size = r.Range(1, 1.6);
                        }
                        ps.Spawn(P.Star, s.X, s.Y, vx, vy, life, col, size);
                    }
                }
                for (var i = _pops.Count - 1; i >= 0; i--)
                {
                    if (t > _pops[i].T0 + _pops[i].Life)
                    {
                        _pops.RemoveAt(i);
                    }
                }
                ps.Update(dt);
                _lights.Update(dt);
            }

            public override void DrawBack(PixelPainter p, double time, PixelFxOptions o) => _ps.Draw(p, "back");

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                var k5 = _k;
                if (t < Rise)
                {
                    // the shell: a hot dot with a short tail
                    var u = t / Rise;
                    var y = Y0 * (1 - Ease.OutCubic(u));
                    p.Line(0, y, 0, y + 3 + 6 * (1 - u), q =>
                    {
                        if (q < 0.3)
                        {
                            return k5[5];
                        }
                        return k5[3];
                    });
                    p.Px(0, y, White);
                    p.Glow(0, y, 7, k5[4], 0.5);
                }
                foreach (var s in _stars)
                {
                    var k = s.Age / s.Life;
                    var pts = s.Pts;
                    var n = pts.Count / 3;
                    // the trail: a line from the head back along where it has been, cooling and dithering out; the part laid
                    // while the star burned gold stays gold
                    for (var j = n - 1; j > 0; j--)
                    {
                        var u = (t - pts[j * 3 + 2]) / TrailLife;
                        if (u >= 1)
                        {
                            break;
                        }
                        var ramp = s.Ramp;
                        if (s.Age - (t - pts[j * 3 + 2]) < Turn)
                        {
                            ramp = s.First;
                        }
                        var col = ramp[3];
                        if (u < 0.3)
                        {
                            col = ramp[5];
                        }
                        else if (u < 0.6)
                        {
                            col = ramp[4];
                        }
                        p.Line(pts[j * 3], pts[j * 3 + 1], pts[j * 3 - 3], pts[j * 3 - 2], col, Math.Pow(1 - u, 0.8) * (1.3 - k * 0.6));
                    }
                    // the head: white-hot, then its colour; late on it flickers before it crackles out
                    if (k > 0.72 && Math.Sin(t * 60 + s.Seed) < -0.2)
                    {
                        continue;
                    }
                    var headRamp = s.Ramp;
                    if (s.Age < Turn)
                    {
                        headRamp = s.First;
                    }
                    uint head;
                    if (s.Age < 0.04)
                    {
                        head = White;
                    }
                    else if (k < 0.55)
                    {
                        head = headRamp[6];
                        if (s.Age < Turn)
                        {
                            head = headRamp[5];
                        }
                    }
                    else
                    {
                        head = headRamp[5];
                    }
                    p.Px(s.X, s.Y, head);
                    if (s.Big && k < 0.6 && s.Age > 0.06)
                    {
                        foreach (var (dx, dy) in Cross)
                        {
                            p.Px(s.X + dx, s.Y + dy, headRamp[5]);
                        }
                    }
                    double radius = 5;
                    var strength = 0.5;
                    if (s.Big)
                    {
                        radius = 7;
                        strength = 0.7;
                    }
                    p.Glow(s.X, s.Y, radius, headRamp[4], strength * (1 - k * 0.6) * Math.Min(1, s.Age / 0.2));
                }
                foreach (var q in _pops)
                {
                    var u = (t - q.T0) / q.Life;
                    if (u < 0 || u > 1)
                    {
                        continue;
                    }
                    Sim.Sparkle(p, q.X, q.Y, 2.6 * Math.Sin(Math.PI * u), q.C, Mix(q.C, c[3], 0.5), 0.5);
                }
                // the burst: a flare and a white heart, the sky lit up behind it
                var b = t - Rise;
                if (b >= 0 && b < 0.28)
                {
                    var u = b / 0.28;
                    var l = 32 * Math.Pow(1 - u, 1.3);
                    FlareRays(p, 0, 0, l, u * 0.6, c, k5);
                    if (b < 0.07)
                    {
                        p.Circle(0, 0, 2.6 - b * 20, White);
                    }
                }
                _ps.Draw(p, "front");
                _lights.Draw(p);
            }
        }

        // an 8-ray flare: long rays on the axes with a white core and tinted ends, short gold diagonals, turning slowly
        private static void FlareRays(PixelPainter p, double x, double y, double l, double ang, uint[] c, uint[] k)
        {
            if (l < 1)
            {
                return;
            }
            for (var i = 0; i < 8; i++)
            {
                var diag = i % 2 == 1;
                var an = ang + (i * Tau) / 8;
                var len = l;
                if (diag)
                {
                    len = l * 0.42;
                }
                var ca = Math.Cos(an);
                var sa = Math.Sin(an);
                for (double d = 0; d <= len; d += 0.5)
                {
                    var u = d / len;
                    uint col;
                    if (diag)
                    {
                        col = k[4];
                        if (u < 0.5)
                        {
                            col = k[5];
                        }
                    }
                    else if (u < 0.3)
                    {
                        col = White;
                    }
                    else if (u < 0.65)
                    {
                        col = c[5];
                    }
                    else
                    {
                        col = c[4];
                    }
                    var a = 1 - (u - 0.7) / 0.35;
                    if (u < 0.7)
                    {
                        a = 1;
                    }
                    p.Dot(x + ca * d, y + sa * d, col, a);
                }
            }
            p.Glow(x, y, 4 + l * 1.2, c[4], 0.3);
            p.Glow(x, y, 2 + l * 0.3, White, 0.25);
        }
    }
}
