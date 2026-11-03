using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Impacts.ImpactsLib;

namespace ManaPixelFx.Effects.Impacts
{
    /// <summary>
    /// pixelfx's impacts/ground-slam.js: something lands hard -- the floor flashes and cracks, two crescent shock waves
    /// race out along it, a dust wave and a tall dust cloud rise, rocks erupt, bounce and settle. The rocks, grit,
    /// rolling floor dust and dust column are adapted from Sprite Fusion's open-source explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class GroundSlam : PixelEffect
    {
        private const double Duration = 3.2;

        // seconds for the shock fronts to run out
        private const double Wave = 0.6;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "impacts/ground-slam",
            Name = "Ground slam",
            Description = "Something lands hard: the floor flashes and cracks, two crescent shock waves race out along it, a dust wave and a tall dust cloud rise, rocks erupt, bounce and settle.",
            Size = (288, 208),
            Origin = (144, 164),
            Duration = Duration,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#2a1712", "#5e2c1c", "#a4502a", "#e8893a", "#ffc764", "#fff0b4", "#ffffff" },
                ["stone"] = "#4a3f69",
                ["reach"] = 104.0,
                ["seed"] = 5.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        // the dent: the floor's edge caves into a shallow dish, its bottom glowing while hot, lips thrown up either side
        private static void Crater(PixelPainter p, Stone st, double hot, uint[] c, double k)
        {
            for (var x = -12; x <= 11; x++)
            {
                var q = Math.Abs(x + 0.5) / 12;
                var dep = Round(2.6 * (1 - q * q));
                if (q < 0.84)
                {
                    for (var y = 0; y < dep; y++)
                    {
                        p.Dot(x, y, Soot, k);
                    }
                    var bottom = st.Lip;
                    if (hot > 0.5)
                    {
                        bottom = c[5];
                    }
                    else if (hot > 0.2)
                    {
                        bottom = c[4];
                    }
                    p.Dot(x, dep, bottom, k);
                }
                else
                {
                    var lip = st.Lip;
                    if (q > 0.93)
                    {
                        lip = st.Light;
                    }
                    p.Dot(x, -1, lip, k);
                    if (q < 0.9)
                    {
                        p.Dot(x, -2, st.Light, k * 0.8);
                    }
                }
            }
        }

        private readonly record struct Glint(double X, double Y, double D, double Life, double Ph);

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly Stone _st;
            private readonly double _reach;
            private readonly PixelRng _r;
            private readonly Particles _ps;
            private readonly Bits _bits = new(_ => 0);
            private readonly Lights _lights = new();
            private readonly uint[] _hot;
            private readonly uint _dustLit;
            private readonly List<Crack> _cracks;
            private readonly List<Spike> _burst;
            private readonly List<Glint> _embers = [];
            private readonly double[] _acc;
            private double _t;

            public Run(PixelFxOptions o)
            {
                _c = Fit(o.Ramp("palette"), 7);
                var c = _c;
                _st = Stone.Of(o.Color("stone"));
                var st = _st;
                _reach = o.Number("reach");
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [c[6], c[5], c[4], c[3]]);
                var ps = _ps;
                _hot = [White, c[5], c[4], c[3]];
                _dustLit = Mix(st.Dust, White, 0.2);
                var dustLit = _dustLit;
                _cracks = MakeCracks(r, 10, len: (22, 58), dive: 0.55, maxY: 22, forks: 0.7, x0: (2, 9));
                // the flash: a flat half-star hugging the floor, long rays along it, short ones up
                _burst =
                [
                    new(-0.07, r.Range(34, 40), 1.9),
                    new(Math.PI + 0.07, r.Range(34, 40), 1.9),
                    new(-0.42 + r.Range(-0.08, 0.08), r.Range(15, 20), 1.5),
                    new(-Math.PI + 0.42 + r.Range(-0.08, 0.08), r.Range(15, 20), 1.5),
                    new(-Math.PI / 2 + r.Range(-0.15, 0.15), r.Range(18, 24), 1.7),
                    new(-1.05 + r.Range(-0.1, 0.1), r.Range(10, 14), 1.2),
                    new(-Math.PI + 1.05 + r.Range(-0.1, 0.1), r.Range(10, 14), 1.2),
                ];
                // embers caught in the cracks, flickering as they cool
                foreach (var crack in _cracks)
                {
                    foreach (var (x, y, d) in crack.Pts)
                    {
                        if (d > 3 && d < 32 && r.Chance(0.25))
                        {
                            var life = r.Range(0.8, 2.2);
                            _embers.Add(new Glint(x, y, d, life, r.Range(0, Tau)));
                        }
                    }
                }

                // contact: light floods out, sparks fly off the stone and skitter along the floor
                _lights.Add(0, -12, 96, c[4], 0.6, 0.6);
                _lights.Add(0, -4, 50, White, 0.9, 0.14);
                for (var i = 0; i < 56; i++)
                {
                    var low = i < 14;
                    var s = r.Sign();
                    double a;
                    if (low)
                    {
                        if (s > 0)
                        {
                            a = -r.Range(0.04, 0.6);
                        }
                        else
                        {
                            a = Math.PI + r.Range(0.04, 0.6);
                        }
                    }
                    else
                    {
                        a = -Math.PI / 2 + r.Range(-1.15, 1.15);
                    }
                    double sp;
                    if (low)
                    {
                        sp = r.Range(150, 380);
                    }
                    else
                    {
                        sp = r.Range(200, 500);
                    }
                    var x = r.Range(-4, 4);
                    double life;
                    if (low)
                    {
                        life = r.Range(0.2, 0.5);
                    }
                    else
                    {
                        life = r.Range(0.25, 0.65);
                    }
                    double delay = 0;
                    if (!low)
                    {
                        delay = r.Range(0, 0.05);
                    }
                    ps.Spawn(P.Spark, x, -1, Math.Cos(a) * sp, Math.Sin(a) * sp, life, White, 1, delay);
                }
                // rocks erupt from the dent: three heavy ones that barely clear it, the rest thrown high
                for (var i = 0; i < 20; i++)
                {
                    double s = 4;
                    if (i >= 3)
                    {
                        s = r.Int(1, 3);
                    }
                    var a = -Math.PI / 2 + r.Range(-0.5, 0.5);
                    double sp;
                    if (s > 3)
                    {
                        sp = r.Range(110, 190);
                    }
                    else
                    {
                        sp = r.Range(170, 380);
                        if (s > 2)
                        {
                            sp *= 0.8;
                        }
                    }
                    var bit = new Bit { Kind = B.Chunk, X = r.Range(-7, 7), Y = -2, Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp, G = 1, Drag = 0.4, Bounce = 0.38 };
                    bit.Life = r.Range(1.9, 2.7);
                    bit.A = r.Range(0, Tau);
                    bit.Va = r.Range(-14, 14);
                    bit.S = s;
                    bit.Shape = RockShape(r);
                    bit.Pvy = 0;
                    _bits.Add(bit);
                }
                // grit: a fan of floor bits that bounce once and settle
                for (var i = 0; i < 110; i++)
                {
                    var u = r.Range(-1, 1);
                    var a = -Math.PI / 2 + u * 0.65;
                    var sp = r.Range(110, 380) * (1 - 0.35 * Math.Abs(u));
                    var life = r.Range(1, 2.2);
                    var col = r.Pick([st.Light, st.Body, st.Body, st.Shade, st.Shade, st.Lip]);
                    double size = 1;
                    if (r.Chance(0.35))
                    {
                        size = 2;
                    }
                    ps.Spawn(P.Grit, u * 6, -1, Math.Cos(a) * sp, Math.Sin(a) * sp, life, col, size);
                }
                // the dust cloud: a column that shoots up and spreads at the top, lit on top, and a heavy bank at its foot
                for (var i = 0; i < 28; i++)
                {
                    var core = i < 14;
                    var s = r.Sign();
                    double x;
                    if (core)
                    {
                        x = r.Range(-5, 5);
                    }
                    else
                    {
                        x = s * r.Range(4, 12);
                    }
                    var y = -r.Range(2, 10);
                    double vx;
                    if (core)
                    {
                        vx = r.Range(-12, 12);
                    }
                    else
                    {
                        vx = s * r.Range(14, 38);
                    }
                    var vy = -r.Range(50, 130);
                    if (!core)
                    {
                        vy *= r.Range(0.3, 0.6);
                    }
                    var life = r.Range(1.9, 2.7);
                    var col = st.Dust;
                    if (r.Chance(0.35))
                    {
                        col = dustLit;
                    }
                    double size;
                    if (core)
                    {
                        size = r.Range(4.5, 9);
                    }
                    else
                    {
                        size = r.Range(4, 7.5);
                    }
                    ps.Spawn(P.Smoke, x, y, vx, vy, life, col, size, r.Range(0.02, 0.22), PF.Big);
                }
                for (var i = 0; i < 10; i++)
                {
                    var s = r.Sign();
                    ps.Spawn(P.Smoke, s * r.Range(2, 14), -r.Range(2, 6), s * r.Range(20, 70), -r.Range(4, 20), r.Range(1.4, 2.3), st.Dust, r.Range(6, 9), r.Range(0, 0.08), PF.Big);
                }
                // fine dust settling out of the cloud while it hangs
                for (var i = 0; i < 26; i++)
                {
                    var x = r.Range(-22, 22);
                    var y = -r.Range(14, 80);
                    var vx = r.Range(-12, 12);
                    var vy = r.Range(-10, 4);
                    var life = r.Range(1.2, 2);
                    var col = dustLit;
                    if (r.Chance(0.5))
                    {
                        col = st.Lip;
                    }
                    ps.Spawn(P.Flake, x, y, vx, vy, life, col, r.Range(0.5, 1.2), r.Range(0.25, 1));
                }
                _acc = [r.Next(), r.Next()];
            }

            public override bool HasBack => true;

            private double Front(double t) => 7 + (_reach - 7) * Ease.OutCubic(Clamp((t - 0.015) / Wave));

            private static double Heat(double t) => Math.Pow(Clamp(1 - t / 0.8), 1.4);

            // a front's height and thickness
            private static (double H, double W) Crest(double u) => (6 + 28 * Math.Pow(1 - u, 0.75), 1.5 + 5 * Math.Pow(1 - u, 0.9));

            private void Ring(PixelPainter p, bool back)
            {
                var t = _t;
                var c = _c;
                var u = Clamp((t - 0.015) / (Wave + 0.1));
                if (t < 0.015 || u >= 0.9)
                {
                    return;
                }
                var k = (0.9 - u) / 0.35;
                if (u < 0.55)
                {
                    k = 1.1;
                }
                uint[] ramp = [_st.Body, _st.Light, c[4], c[5]];
                if (u < 0.35)
                {
                    ramp = [c[3], c[4], c[5], c[6]];
                }
                Sim.Band(p, 0, 0.5, Front(t) + 1, 0.16, Math.Max(1, 4 * Math.Pow(1 - u, 0.8)), k, ramp, g => (Math.Sin(g) < 0) == back, t < 0.09);
            }

            private void Fronts(PixelPainter p)
            {
                var t = _t;
                var c = _c;
                var st = _st;
                var u = Clamp((t - 0.03) / (Wave + 0.16));
                if (t < 0.03 || u >= 1)
                {
                    return;
                }
                var fr = Front(t);
                var (h, w) = Crest(u);
                var k = (1 - u) / 0.3;
                if (u < 0.7)
                {
                    k = 1;
                }
                uint[] cols;
                if (u < 0.14)
                {
                    cols = [White, c[6], c[5]];
                }
                else if (u < 0.34)
                {
                    cols = [c[6], c[5], c[4]];
                }
                else if (u < 0.55)
                {
                    cols = [c[5], c[4], st.Lip];
                }
                else
                {
                    cols = [st.Lip, st.Light, st.Dust];
                }
                foreach (var s in new[] { -1, 1 })
                {
                    FrontCrescent(p, s * fr, s, h, w, cols, k);
                    p.Glow(s * fr, -h * 0.3, 8 + h * 0.7, c[4], 0.5 * (1 - u) * k);
                }
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                var st = _st;
                var dustLit = _dustLit;
                _t += dt;
                var t = _t;
                // dust kicked up behind the racing fronts, and spray off their crests
                var u = Clamp((t - 0.015) / Wave);
                if (u > 0 && u < 1)
                {
                    var fr = Front(t);
                    var (h, _) = Crest(u);
                    for (var k = 0; k < 2; k++)
                    {
                        double s = -1;
                        if (k != 0)
                        {
                            s = 1;
                        }
                        for (_acc[k] += dt * 120 * (1 - u * 0.7); _acc[k] > 1; _acc[k]--)
                        {
                            {
                                var x = s * (fr - r.Range(1, 7));
                                var y = -r.Range(0.5, 3);
                                var vx = s * r.Range(15, 70) * (1 - u);
                                var vy = -r.Range(2, 12);
                                var life = r.Range(1, 2.1);
                                var col = st.Dust;
                                if (r.Chance(0.3))
                                {
                                    col = dustLit;
                                }
                                ps.Spawn(P.Smoke, x, y, vx, vy, life, col, r.Range(2.5, 6.5) * (1 - 0.4 * u), 0, PF.Big);
                            }
                            if (r.Chance(0.5))
                            {
                                var x = s * (fr - r.Range(0, 3));
                                var y = -r.Range(0.3, 0.9) * h;
                                var vx = s * r.Range(-15, 25);
                                var vy = -r.Range(8, 35);
                                var life = r.Range(0.3, 0.6);
                                var col = st.Dust;
                                if (r.Chance(0.5))
                                {
                                    col = dustLit;
                                }
                                ps.Spawn(P.Dust, x, y, vx, vy, life, col, r.Range(1.2, 2.4));
                            }
                            if (r.Chance(0.4))
                            {
                                ps.Spawn(P.Grit, s * fr, -1, s * r.Range(10, 60), -r.Range(40, 140) * (1 - u), r.Range(0.5, 1), r.Pick([st.Light, st.Body, st.Lip]), 1);
                            }
                            if (u < 0.45 && r.Chance(0.2))
                            {
                                ps.Spawn(P.Spark, s * fr, -1, s * r.Range(60, 200), -r.Range(30, 160), r.Range(0.12, 0.3));
                            }
                        }
                    }
                }
                foreach (var b in _bits.List)
                {
                    b.Pvy = b.Vy;
                }
                _bits.Update(dt);
                // rocks that land hard kick up dust
                foreach (var b in _bits.List)
                {
                    if (b.Pvy > 70 && b.Vy <= 0)
                    {
                        var n = 2;
                        if (b.Pvy > 200)
                        {
                            n = 3;
                        }
                        for (var i = 0; i < n; i++)
                        {
                            ps.Spawn(P.Dust, b.X + r.Range(-1.5, 1.5), -0.5, r.Sign() * r.Range(8, 35), -r.Range(2, 16), r.Range(0.3, 0.65), st.Dust, r.Range(1.3, 2.4) * (0.7 + b.S * 0.25));
                        }
                        b.Pvy = 0;
                    }
                }
                ps.Update(dt);
                _lights.Update(dt);
            }

            public override void DrawBack(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                var fade = 1 - Smooth(Duration - 0.9, Duration - 0.15, t);
                var h = Heat(t);
                if (t > 0.02)
                {
                    Crater(p, _st, h, c, fade);
                }
                DrawCracks(p, _cracks, crack => crack.Len * Ease.OutCubic(Clamp((t - crack.T0) / 0.1)), d => h * Clamp(1.2 - d / 40), _hot, Soot, _st.Light, fade);
                foreach (var e in _embers)
                {
                    var k = 1 - (t - 0.1) / e.Life;
                    if (t < 0.1 || k <= 0)
                    {
                        continue;
                    }
                    var f = 0.6 + 0.4 * Math.Sin(t * 23 + e.Ph);
                    var col = c[3];
                    if (k * f > 0.55)
                    {
                        col = c[5];
                    }
                    else if (k * f > 0.25)
                    {
                        col = c[4];
                    }
                    p.Px(e.X, e.Y, col);
                    p.Glow(e.X, e.Y, 4, c[4], 0.35 * k * f);
                }
                Ring(p, true);
                _ps.Draw(p, "back");
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                Ring(p, false);
                Fronts(p);
                foreach (var b in _bits.List)
                {
                    Rock(p, b, _st);
                }
                _ps.Draw(p, "front");
                if (t < 0.035)
                {
                    p.Ellipse(0, 0, 30, 4.5, (d, _) =>
                    {
                        if (d > 0.8)
                        {
                            return c[4];
                        }
                        if (d > 0.5)
                        {
                            return c[6];
                        }
                        return White;
                    });
                    Star(p, 0, -1, _burst, 1, 1, [c[4], c[6], White, White]);
                }
                else if (t < 0.07)
                {
                    Star(p, 0, -1, _burst, 1.25, 0.5, [c[3], c[5], c[6], White]);
                }
                // the floor flashes along its length, then the dent glows while the cracks are hot
                if (t < 0.14)
                {
                    p.GlowLine(-80, 0, 80, 0, 10, c[5], 0.55 * (1 - t / 0.14));
                }
                var h = Heat(t);
                if (h > 0)
                {
                    p.Glow(0, 1, 46, c[4], 0.35 * h);
                }
                _lights.Draw(p);
            }
        }
    }
}
