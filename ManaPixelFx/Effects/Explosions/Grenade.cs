using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Explosions.ExplosionsLib;

namespace ManaPixelFx.Effects.Explosions
{
    /// <summary>
    /// pixelfx's explosions/grenade.js: a grenade is lobbed in, bounces, rolls to a stop with its fuse fizzing, shudders,
    /// then blows -- a white flash, shrapnel streaks, sparks, a small fireball and a scorch. The blast is adapted from
    /// Sprite Fusion's open-source explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class Grenade : PixelEffect
    {
        private const double Duration = 4.2;
        private const double Blow = 1.62;
        private const double Rb = 6.2;
        private static readonly uint Dust = Hex("#7d7280");
        private static readonly uint[] Metal = [Hex("#3a3c46"), Hex("#6a6e7c"), Hex("#a8acb8")];
        private static readonly uint[] Steel = [Hex("#2a2c36"), Hex("#5e6270"), Hex("#9a9eac"), Hex("#d0d4de")];

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "explosions/grenade",
            Name = "Grenade",
            Description = "A grenade is lobbed in, bounces, rolls to a stop with its fuse fizzing, shudders, then blows: a white flash, shrapnel streaks, sparks, a small fireball and a scorch.",
            Size = (300, 200),
            Origin = (160, 170),
            Duration = Duration,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["body"] = new[] { "#141a10", "#2c3a1e", "#4a5c30", "#6e8446", "#9ab06a" },
                ["seed"] = 6.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o, Meta);

        private sealed class Body
        {
            public double X = -140, Y = -70, Vx = 168, Vy = -150, A, Va = -9;
            public bool Rest;
        }

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _f;
            private readonly uint[] _fr;
            private readonly uint[] _hr;
            private readonly uint[] _g;
            private readonly PixelRng _r;
            private readonly double _x0, _x1, _y0, _y1;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly Bits _bits = new(_ => 0);
            private readonly Body _body = new();
            private double _t;
            private bool _blown;
            private double _bx;

            public Run(PixelFxOptions o, PixelEffectMeta meta)
            {
                _f = Fit(o.Ramp("palette"), 8);
                _fr = FireRamp(_f);
                _hr = HeatRamp(_fr);
                _g = Fit(o.Ramp("body"), 5);
                _r = new PixelRng(o.Number("seed"));
                var (w, h) = meta.Size;
                var (ox, oy) = meta.Origin;
                _x0 = -ox + 3;
                _x1 = w - ox - 3;
                _y0 = -oy + 3;
                _y1 = h - oy - 3;
                _ps = new Particles(rng: _r, ground: _ => 0, sparks: [_f[6], _f[5], _f[3], _f[2]]);
            }

            public override bool HasBack => true;

            private void Blast()
            {
                var r = _r;
                var ps = _ps;
                var f = _f;
                _blown = true;
                _bx = _body.X;
                var bx = _bx;
                const double y = -5;
                _lights.Add(bx, y - 10, 70, Mix(f[3], f[6], 0.45), 0.85, 0.5);
                _lights.Add(bx, y, 30, White, 0.7, 0.07);
                // shrapnel: hot streaks flying straight and far, sparks that fall and bounce
                for (var i = 0; i < 38; i++)
                {
                    var a = r.Range(-Math.PI + 0.04, -0.04);
                    var sp = r.Range(380, 640);
                    ps.Spawn(P.Mote, bx, y, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.12, 0.26), r.Pick([f[7], f[6], f[5]]), r.Range(0.6, 0.9));
                }
                for (var i = 0; i < 60; i++)
                {
                    var a = r.Range(0, Tau);
                    var sp = r.Range(140, 360);
                    ps.Spawn(P.Spark, bx, y, Math.Cos(a) * sp, Math.Sin(a) * sp - 60, r.Range(0.2, 0.55));
                }
                // a small fireball that cools into smoke
                for (var i = 0; i < 4; i++)
                {
                    ps.Spawn(P.Fire, bx + r.Range(-2, 2), y + r.Range(-2, 2), r.Range(-15, 15), r.Range(-30, -8), r.Range(0.16, 0.26), 0, r.Range(6, 8));
                }
                for (var i = 0; i < 16; i++)
                {
                    var a = r.Range(-Math.PI - 0.2, 0.2);
                    var d = r.Range(0, 6);
                    var sp = r.Range(40, 150);
                    var life = r.Range(0.45, 0.9);
                    var size = r.Range(3.5, 7);
                    var flag = 0;
                    if (r.Chance(0.5))
                    {
                        flag = PF.Smokes;
                    }
                    ps.Spawn(P.Fire, bx + Math.Cos(a) * d, y + Math.Sin(a) * d, Math.Cos(a) * sp, Math.Sin(a) * sp * 0.8 - 30, life, 0, size, 0, flag);
                }
                for (var i = 0; i < 9; i++)
                {
                    var a = r.Range(-Math.PI, 0);
                    var d = r.Range(2, 10);
                    ps.Spawn(P.Smoke, bx + Math.Cos(a) * d, y + Math.Sin(a) * d, Math.Cos(a) * 25, -r.Range(20, 60), r.Range(1.4, 2.1), 0, r.Range(6, 10), r.Range(0.05, 0.3), PF.Big);
                }
                for (var i = 0; i < 30; i++)
                {
                    var a = r.Range(0, Tau);
                    var sp = r.Range(30, 120);
                    ps.Spawn(P.Ember, bx, y, Math.Cos(a) * sp, Math.Sin(a) * sp - 50, r.Range(0.8, 1.8), f[3]);
                }
                // the casing in pieces, and the floor's grit and dust
                for (var i = 0; i < 12; i++)
                {
                    var a = r.Range(-Math.PI + 0.2, -0.2);
                    var sp = r.Range(150, 330);
                    _bits.Add(new Bit
                    {
                        Kind = B.Shard, X = bx, Y = y, Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp, G = 1, Drag = 0.5, Bounce = 0.3,
                        Life = r.Range(1.4, 2.1), A = r.Range(0, Tau), Va = r.Range(-24, 24), S = r.Range(2, 3.5), C = r.Pick(Metal),
                    });
                }
                for (var i = 0; i < 70; i++)
                {
                    var u = r.Range(-1, 1);
                    var a = -Math.PI / 2 + u * 0.8;
                    var sp = r.Range(100, 300) * (1 - 0.4 * Math.Abs(u));
                    var life = r.Range(1, 1.8);
                    var col = r.Pick([Hex("#5a4f6b"), Hex("#3e3650"), Hex("#776c88")]);
                    double size = 1;
                    if (r.Chance(0.35))
                    {
                        size = 2;
                    }
                    ps.Spawn(P.Grit, bx + u * 4, -1, Math.Cos(a) * sp, Math.Sin(a) * sp, life, col, size);
                }
                for (var i = 0; i < 10; i++)
                {
                    var s = r.Sign();
                    ps.Spawn(P.Smoke, bx + s * r.Range(0, 6), -r.Range(1, 4), s * r.Range(60, 180), -r.Range(2, 10), r.Range(1, 1.7), Dust, r.Range(5, 8), r.Range(0, 0.05), PF.Big);
                }
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                var g = _body;
                _t += dt;
                var t = _t;
                if (!_blown)
                {
                    // the lob, the bounces, the roll
                    if (!g.Rest)
                    {
                        g.Vy += 900 * dt;
                        g.X += g.Vx * dt;
                        g.Y += g.Vy * dt;
                        if (g.Y >= -Rb && g.Vy > 0)
                        {
                            g.Y = -Rb;
                            if (g.Vy > 90)
                            {
                                // a clink, a puff of dust
                                for (var i = 0; i < 4; i++)
                                {
                                    ps.Spawn(P.Dust, g.X + r.Range(-2, 2), -0.5, r.Sign() * r.Range(10, 40), -r.Range(4, 16), r.Range(0.3, 0.6), Dust, r.Range(1.5, 2.6));
                                }
                                for (var i = 0; i < 3; i++)
                                {
                                    ps.Spawn(P.Spark, g.X, -1, r.Range(-60, 60), -r.Range(40, 120), r.Range(0.08, 0.16));
                                }
                                g.Vy = -g.Vy * 0.36;
                                g.Vx *= 0.8;
                            }
                            else
                            {
                                g.Vy = 0;
                            }
                        }
                        if (g.Y >= -Rb - 0.01 && g.Vy == 0)
                        {
                            g.Vx -= g.Vx * 2.4 * dt;
                            g.Va = -g.Vx / Rb;
                            if (Math.Abs(g.Vx) < 3)
                            {
                                g.Rest = true;
                                g.Vx = 0;
                            }
                        }
                        g.A += g.Va * dt;
                    }
                    // the fuse: sparks spitting off its tip, a thread of smoke, faster as it runs down
                    var (tx, ty) = Fuse(g.X, g.Y, g.A);
                    var hurry = Smooth(Blow - 0.5, Blow, t);
                    if (r.Chance((24 + 50 * hurry) * dt))
                    {
                        ps.Spawn(P.Spark, tx, ty, r.Range(-50, 50), -r.Range(30, 110), r.Range(0.08, 0.2));
                    }
                    if (r.Chance(9 * dt))
                    {
                        ps.Spawn(P.Vapour, tx, ty - 1, r.Range(-4, 4), -r.Range(10, 20), r.Range(0.5, 0.9), Hex("#8a8494"), r.Range(1.2, 2));
                    }
                    if (t >= Blow)
                    {
                        Blast();
                    }
                }
                _bits.Update(dt);
                for (var i = _bits.List.Count - 1; i >= 0; i--)
                {
                    var b = _bits.List[i];
                    if (b.X < _x0 + 3 || b.X > _x1 - 3 || b.Y < _y0 + 3)
                    {
                        _bits.List.RemoveAt(i);
                    }
                }
                ps.Update(dt);
                Cull(ps, _x0, _y0, _x1, _y1);
                FadeEdges(ps, _x0, _x1, 24, _y1);
                _lights.Update(dt);
            }

            private void Wave(PixelPainter p, bool far)
            {
                var k = (_t - Blow) / 0.4;
                if (k >= 1)
                {
                    return;
                }
                Func<double, bool> keep = g => Math.Sin(g) >= 0;
                if (far)
                {
                    keep = g => Math.Sin(g) < 0;
                }
                Sim.Band(p, _bx, 0.5, 70 * Ease.OutCubic(k), 0.16, Math.Max(1, 3.5 * (1 - k)), 1.15 - k, [Hex("#4a3f69"), Hex("#6e6390"), _f[3], _f[6]], keep);
            }

            public override void DrawBack(PixelPainter p, double time, PixelFxOptions o)
            {
                if (_blown)
                {
                    var u = _t - Blow;
                    Scorch(p, _hr, u, 22, 1.6, 1 - Smooth(Duration - Blow - 0.8, Duration - Blow - 0.15, u), 2);
                    Wave(p, true);
                }
                DrawPs(p, _ps, "back", _fr);
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var f = _f;
                var t = _t;
                var g = _body;
                if (!_blown)
                {
                    // the grenade, shuddering in its last half second, the fuse glowing
                    var shake = Smooth(Blow - 0.45, Blow, t);
                    var jx = shake * Math.Sin(t * 90) * 0.8;
                    DrawGrenade(p, _g, g.X + jx, g.Y, g.A);
                    var (tx, ty) = Fuse(g.X + jx, g.Y, g.A);
                    var c = f[5];
                    if (Math.Sin(t * 50) > 0)
                    {
                        c = f[6];
                    }
                    p.Px(tx, ty, c);
                    p.Glow(tx, ty, 6 + 3 * shake, f[4], 0.5 + 0.3 * Math.Sin(t * 37));
                }
                else
                {
                    Wave(p, false);
                }
                _bits.Draw(p);
                DrawPs(p, _ps, "front", _fr);
                if (_blown)
                {
                    var u = t - Blow;
                    var bx = _bx;
                    // the flash: a hot disc for two frames, a flare, a ring of light
                    if (u < 0.07)
                    {
                        var k2 = 1 - u / 0.07;
                        var rr = 11 * (0.8 + 0.2 * k2);
                        p.Circle(bx, -6, rr + 1.2, f[2]);
                        var mid = f[5];
                        var core = f[6];
                        if (k2 > 0.45)
                        {
                            mid = f[6];
                            core = White;
                        }
                        p.Circle(bx, -6, rr, mid);
                        p.Circle(bx, -6, rr * 0.6, core);
                    }
                    if (u < 0.2)
                    {
                        Sim.Flare(p, bx, -6, 40 * Math.Pow(1 - u / 0.2, 1.4), 1 - u / 0.2, f);
                    }
                    if (u < 0.3)
                    {
                        var k3 = u / 0.3;
                        var e = 1 - Math.Pow(1 - k3, 3);
                        var rr = 8 + 40 * e;
                        p.Ring(bx, -6, rr, Math.Max(1, 2.5 * (1 - k3) + 0.4), f[5], (_, an) =>
                        {
                            if (Math.Sin(an) * rr > 9)
                            {
                                return 0;
                            }
                            return Math.Min(1, (1 - k3) * 1.6);
                        });
                    }
                    Pool(p, bx, 1, 70, 12, f[3], 0.55 * Math.Max(0, 1 - u / 0.6));
                }
                _lights.Draw(p);
            }
        }

        // where the fuse's tip is: on top of the grenade, turning with it
        private static (double X, double Y) Fuse(double x, double y, double a) => (x + Math.Sin(a) * 10.4, y - Math.Cos(a) * 10.4);

        // a pineapple grenade turned by a: an oval body with grooves lit from the top left, an outline, a metal cap with a
        // ring pin, a lever down its side
        private static void DrawGrenade(PixelPainter p, uint[] g, double x, double y, double a)
        {
            var ca = Math.Cos(a);
            var sa = Math.Sin(a);
            var m = Steel;
            p.Shade(x - 13, y - 13, x + 13, y + 13, (px, py, _) =>
            {
                var dx = px - x;
                var dy = py - y;
                var u = dx * ca + dy * sa;
                var v = -dx * sa + dy * ca;
                // the cap, its ring, and the lever pressed down the side
                if (v < -5 && v > -10.2 && Math.Abs(u) < 2.4)
                {
                    if (v < -9.4 || Math.Abs(u) > 1.7)
                    {
                        return m[0];
                    }
                    if (u < -0.6)
                    {
                        return m[3];
                    }
                    return m[2];
                }
                if (Hypot(u + 3.4, v + 8) < 1.9 && Hypot(u + 3.4, v + 8) > 0.9)
                {
                    return m[2];
                }
                if (u > 2.2 && u < 3.6 && v > -8.6 && v < 2)
                {
                    if (u > 3)
                    {
                        return m[0];
                    }
                    return m[1];
                }
                var e = Math.Pow(u / 5.1, 2) + Math.Pow((v - 0.6) / 6.4, 2);
                if (e > 1.2)
                {
                    return 0;
                }
                if (e > 1)
                {
                    return Outline;
                }
                // grooves cut the body into knobbly segments; light from the top left in world space
                var lit = -(dx * 0.6 + dy * 0.8) / 6;
                var gv = Math.Abs(((v + 30) % 3.1) - 1.55) < 0.45;
                var gu = Math.Abs(((u + 30) % 3.4) - 1.7) < 0.45;
                double groove = 0;
                if (gv || gu)
                {
                    groove = 1;
                }
                double knob = 0;
                if (e < 0.3 && lit > 0.3)
                {
                    knob = 1;
                }
                var i = (int)Math.Max(0, Math.Min(4, Round(2 + lit * 1.7 - groove + knob)));
                return g[i];
            });
        }
    }
}
