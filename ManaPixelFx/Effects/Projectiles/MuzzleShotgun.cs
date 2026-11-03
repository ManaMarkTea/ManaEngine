using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Projectiles.ProjectilesLib;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/muzzle-shotgun.js: a shotgun blast from a muzzle height px above the floor -- a wide ragged
    /// fan of flame, pellet streaks, a heavy rolling smoke cloud, a red shell kicked out on the pump. The flash lobes,
    /// spark, smoke and casing recipes are adapted from Sprite Fusion's open-source muzzle-flash, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class MuzzleShotgun : PixelEffect
    {
        private const double Duration = 3.2;
        private const double F1 = 1 / 30.0;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/muzzle-shotgun",
            Name = "Shotgun blast",
            Description = "A shotgun blast from a muzzle `height` px above the floor (the origin): a wide ragged fan of flame, pellet streaks, a heavy rolling smoke cloud, a red shell kicked out on the pump.",
            Size = (160, 88),
            Origin = (44, 82),
            Duration = Duration,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["smoke"] = new[] { "#221e2a", "#36313f", "#4e4858", "#6a6474", "#888192", "#a8a1b1", "#c8c1cf" },
                ["shell"] = new[] { "#4a0f14", "#9a1e22", "#d8423a", "#8c5c1c", "#e9b54a" },
                ["height"] = 26.0,
                ["seed"] = 5.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o, Meta);

        // the fan: a fat fireball at the muzzle and a spray of ragged tongues across the cone, each its own length
        private static List<FlashLobe> Fan(double[] r, int f)
        {
            double k;
            double w = 1.25;
            if (f == 1)
            {
                k = 1;
                w = 1;
            }
            else if (f == 2)
            {
                k = 0.82;
            }
            else
            {
                k = 0.55;
            }
            var lobes = new List<FlashLobe> { new() { Off = 0.5, A = 0, Len = 11 * k, Wid = 3.6 * w, Core = 0.5 } };
            for (var i = 0; i < 9; i++)
            {
                var u = i / 8.0 - 0.5;
                var a = u * 1.15 + r[i * 3] * 0.12;
                var len = (17 + 11 * (1 - Math.Abs(u) * 1.4) + r[i * 3 + 1] * 7) * k;
                double core = 0;
                if (f == 1)
                {
                    core = 0.32;
                }
                lobes.Add(new FlashLobe { Off = 2, A = a, Len = len, Wid = (1.7 + r[i * 3 + 2]) * w, Core = core });
            }
            if (f == 1)
            {
                foreach (var s in new[] { -1, 1 })
                {
                    var pick = 27;
                    if (s > 0)
                    {
                        pick = 28;
                    }
                    lobes.Add(new FlashLobe { Off = 1, A = s * (1.6 + r[pick] * 0.4), Len = 5 + r[29] * 3, Wid = 1.2, Core = 0 });
                }
            }
            return lobes;
        }

        private readonly record struct Pellet(double A, double V, double D0);

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _f;
            private readonly uint[] _sm;
            private readonly uint[] _sh;
            private readonly double _my;
            private readonly Particles _ps;
            private readonly Smoke _smoke;
            private readonly double _right;
            private readonly double[] _jit;
            private readonly List<Pellet> _pellets = [];
            private readonly (double T0, double Vx, double Vy, double Va, double A0) _shell;
            private double _t;

            public Run(PixelFxOptions o, PixelEffectMeta meta)
            {
                _f = Fit(o.Ramp("palette"), 9);
                _sm = Fit(o.Ramp("smoke"), 7);
                _sh = Fit(o.Ramp("shell"), 5);
                var f = _f;
                _my = -o.Number("height");
                var my = _my;
                var r = new PixelRng(o.Number("seed"));
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [f[7], f[6], f[4], f[3]]);
                _smoke = new Smoke(r);
                _right = meta.Size.W - meta.Origin.X - 2;
                _jit = Enumerable.Range(0, 32).Select(_ => r.Next()).ToArray();
                // pellets: a tight spray, gone off the canvas in a few frames, each dragging a hot streak
                for (var i = 0; i < 10; i++)
                {
                    var a = r.Range(-0.13, 0.13);
                    var v = r.Range(900, 1150);
                    _pellets.Add(new Pellet(a, v, r.Range(0, 4)));
                }
                // sparks and burning wad, flung wide
                for (var i = 0; i < 16; i++)
                {
                    var a = r.Range(-0.55, 0.55);
                    var sp = r.Range(180, 460);
                    _ps.Spawn(P.Spark, 4, my, Math.Cos(a) * sp, Math.Sin(a) * sp - 20, r.Range(0.08, 0.3));
                }
                for (var i = 0; i < 7; i++)
                {
                    var a = r.Range(-0.5, 0.3);
                    var sp = r.Range(70, 200);
                    _ps.Spawn(P.Ember, 4, my, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.4, 0.9), f[5]);
                }
                // the smoke: a heavy shove of gas forward that slows, rolls, swells and lifts; the first of it lit by the blast
                for (var i = 0; i < 26; i++)
                {
                    var a = r.Range(-0.45, 0.35);
                    var sp = r.Range(60, 230);
                    if (i < 8)
                    {
                        sp *= 1.15;
                    }
                    else
                    {
                        sp *= 0.8;
                    }
                    _smoke.Add(new SmokePuff
                    {
                        X = 4 + r.Range(0, 4), Y = my + r.Range(-1.5, 1.5), Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp, R0 = r.Range(2.5, 3.5), R1 = r.Range(7, 12.5), Life = r.Range(2.1, 2.9),
                        A = r.Range(0.55, 0.72), B = r.Range(0.9, 1), Drag = r.Range(2.6, 3.6), Rise = r.Range(9, 15), Grow = r.Range(1.8, 2.8), Warm = 1, Delay = r.Range(0, 0.04),
                    });
                }
                for (var i = 0; i < 6; i++)
                {
                    _smoke.Add(new SmokePuff { X = 2, Y = my, Vx = r.Range(15, 50), Vy = -r.Range(10, 25), R0 = 1.5, R1 = r.Range(4, 6), Life = r.Range(1.8, 2.4), A = 0.5, B = 1, Drag = 2, Rise = 10, Curl = -r.Range(1.5, 3), Warm = 0.6, Delay = 0.08 + i * 0.05 });
                }
                // the pump throws the spent shell a beat later
                var vx = -r.Range(30, 55);
                var vy = -r.Range(110, 150);
                var va = r.Sign() * r.Range(16, 24);
                _shell = (0.42, vx, vy, va, r.Range(0, 6.28));
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                _t += dt;
                _ps.Update(dt);
                _smoke.Update(dt);
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var f = _f;
                var sm = _sm;
                var sh = _sh;
                var my = _my;
                var pop = Math.Exp(-(t + 0.008) / 0.05);
                var list = _smoke.Puffs();
                foreach (var q in list)
                {
                    q.W *= 0.25 + pop * 1.2;
                }
                Cloud(p, list, sm, [sm[0], Mix(f[1], sm[1], 0.5), Mix(f[2], sm[2], 0.4), f[3], f[4], f[5], f[7]], 1.4, 0.86, null, 1.4);

                // the shell: red hull, brass head, tumbling up and back, bouncing, lying a moment
                if (t > _shell.T0 && t < Duration - 0.05)
                {
                    var k = Casing(-9, my, _shell.Vx, _shell.Vy, _shell.Va, t - _shell.T0, 850, -1, 0.35, _shell.A0);
                    var ca = Math.Cos(k.A);
                    var sa = Math.Sin(k.A);
                    var fade = Clamp((Duration - 0.05 - t) / 0.3);
                    for (var d = -1.5; d <= 1.5; d += 0.5)
                    {
                        uint col;
                        if (d > 0.8)
                        {
                            col = sh[3];
                            if (d > 1.2)
                            {
                                col = sh[4];
                            }
                        }
                        else if (d > -0.6)
                        {
                            col = sh[2];
                        }
                        else
                        {
                            col = sh[1];
                        }
                        p.Dot(k.X + ca * d, k.Y + sa * d - 0.5, col, fade);
                    }
                    for (double d = -1; d <= 1; d += 0.5)
                    {
                        var col = sh[0];
                        if (d > 0.6)
                        {
                            col = sh[3];
                        }
                        p.Dot(k.X + ca * d - sa, k.Y + sa * d + ca - 0.5, col, fade);
                    }
                    if (k.Hit >= 0 && k.Hit < 0.07)
                    {
                        p.Glow(k.X, k.Y, 5, sh[4], 0.5 * (1 - k.Hit / 0.07));
                    }
                }

                // the flash: a white-hot hero frame, then the fan cooling and spreading as the gas balloons
                if (t < 3 * F1)
                {
                    int frame;
                    uint[] c;
                    if (t < F1)
                    {
                        frame = 1;
                        c = [White, f[7], f[6], f[3]];
                    }
                    else if (t < 2 * F1)
                    {
                        frame = 2;
                        c = [f[7], f[6], f[4], f[2]];
                    }
                    else
                    {
                        frame = 3;
                        c = [f[5], f[4], f[3], f[1]];
                    }
                    Flash(p, 0, my, 0, Fan(_jit, frame), c, frame + 9);
                }
                // the pellets: hot heads with tails that cool to orange, clipped at the canvas edge
                if (t < 0.2)
                {
                    foreach (var q in _pellets)
                    {
                        var head = 6 + q.D0 + q.V * t;
                        var tail = Math.Max(4, head - 26);
                        var ca = Math.Cos(q.A);
                        var sa = Math.Sin(q.A);
                        for (var d = tail; d <= head; d += 0.5)
                        {
                            var x = ca * d;
                            if (x > _right)
                            {
                                break;
                            }
                            var u = (d - tail) / Math.Max(1, head - tail);
                            uint col;
                            if (u > 0.85)
                            {
                                col = White;
                            }
                            else if (u > 0.5)
                            {
                                col = f[6];
                            }
                            else
                            {
                                col = f[4];
                            }
                            var a = u / 0.3;
                            if (u > 0.3)
                            {
                                a = 1;
                            }
                            p.Dot(x, my + sa * d, col, a);
                        }
                        if (ca * head < _right)
                        {
                            p.Glow(ca * head, my + sa * head, 4, f[6], 0.5);
                        }
                    }
                }
                _ps.Draw(p);
                // light: a big pop that floods the floor, a white heart
                p.Glow(8, my, 46, f[4], 0.95 * pop);
                p.Glow(4, my, 12, White, 0.6 * pop);
                p.Glow(10, 0, 30, f[4], 0.28 * pop);
            }
        }
    }
}
