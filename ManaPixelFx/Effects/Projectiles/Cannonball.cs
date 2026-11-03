using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Projectiles.ProjectilesLib;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/cannonball.js: a cannon fires from a muzzle height px above the floor -- a white flash, a
    /// tongue of flame, the ball arcing off trailing smoke, a big gunsmoke cloud rolling out. The flash, spark and smoke
    /// recipes are adapted from Sprite Fusion's open-source muzzle-flash and explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class Cannonball : PixelEffect
    {
        private const double Ang = -0.32;
        private const double F1 = 1 / 30.0;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/cannonball",
            Name = "Cannon shot",
            Description = "A cannon fires from a muzzle `height` px above the floor (the origin): a white flash, a tongue of flame, the ball arcing off trailing smoke, a big gunsmoke cloud rolling out.",
            Size = (208, 116),
            Origin = (48, 102),
            Duration = 3.8,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["smoke"] = new[] { "#3a3542", "#55505e", "#736d7c", "#938d9b", "#b3adba", "#d2cdd6", "#ece8ee" },
                ["iron"] = new[] { "#141218", "#2c2a34", "#4c4a58", "#8a8898" },
                ["height"] = 12.0,
                ["seed"] = 4.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o, Meta);

        private sealed class Ball
        {
            public double X, Y, Vx, Vy;
            public bool Out;
        }

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _f;
            private readonly uint[] _sm;
            private readonly uint[] _ir;
            private readonly double _h;
            private readonly double _my;
            private readonly PixelRng _r;
            private readonly Particles _ps;
            private readonly Smoke _smoke;
            private readonly Lights _lights = new();
            private readonly double _right;
            private readonly double _top;
            private readonly Ball _ball;
            private readonly double[] _jit;
            private double _t;
            private double _wisp;

            public Run(PixelFxOptions o, PixelEffectMeta meta)
            {
                _f = Fit(o.Ramp("palette"), 9);
                _sm = Fit(o.Ramp("smoke"), 7);
                _ir = Fit(o.Ramp("iron"), 4);
                var f = _f;
                _h = o.Number("height");
                _my = -_h;
                var my = _my;
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [f[7], f[6], f[4], f[3]]);
                _smoke = new Smoke(r);
                var ca = Math.Cos(Ang);
                var sa = Math.Sin(Ang);
                _right = meta.Size.W - meta.Origin.X - 3;
                _top = -meta.Origin.Y + 3;
                _ball = new Ball { X = 4, Y = my, Vx = ca * 330, Vy = sa * 330 };
                _jit = Enumerable.Range(0, 16).Select(_ => r.Next()).ToArray();
                _lights.Add(12, my - 2, 44, Hex("#ffb060"), 1, 0.35);
                _lights.Add(6, my, 18, White, 0.9, 0.08);
                // burning wadding and sparks thrown out with the shot
                for (var i = 0; i < 22; i++)
                {
                    var a = Ang + r.Range(-0.45, 0.45);
                    var sp = r.Range(140, 380);
                    _ps.Spawn(P.Spark, 6, my, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.1, 0.4));
                }
                for (var i = 0; i < 10; i++)
                {
                    var a = Ang + r.Range(-0.3, 0.3);
                    var sp = r.Range(70, 170);
                    _ps.Spawn(P.Spark, 6, my, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.5, 0.9));
                }
                for (var i = 0; i < 8; i++)
                {
                    var a = Ang + r.Range(-0.5, 0.3);
                    var sp = r.Range(40, 110);
                    _ps.Spawn(P.Ember, 8, my, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.7, 1.4), f[5], 2);
                }
                // the gunsmoke: a great white shove of it out of the bore that slows, rolls over itself, swells and lifts
                for (var i = 0; i < 34; i++)
                {
                    var a = Ang + r.Range(-0.4, 0.35);
                    var sp = r.Range(40, 260);
                    if (i < 12)
                    {
                        sp *= 1.1;
                    }
                    else
                    {
                        sp *= 0.75;
                    }
                    _smoke.Add(new SmokePuff
                    {
                        X = 6 + r.Range(0, 6), Y = my + r.Range(-2, 2), Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp, R0 = r.Range(3, 4.5), R1 = r.Range(9, 16), Life = r.Range(2.7, 3.5),
                        A = r.Range(0.6, 0.75), B = r.Range(0.9, 1), Drag = r.Range(2.2, 3.2), Rise = r.Range(7, 13), Grow = r.Range(1.5, 2.4), Curl = r.Range(-1.2, 0.4), Warm = 1, Delay = r.Range(0, 0.05),
                    });
                }
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ball = _ball;
                _t += dt;
                var t = _t;
                if (!ball.Out)
                {
                    var x0 = ball.X;
                    var y0 = ball.Y;
                    ball.Vy += 260 * dt;
                    ball.X += ball.Vx * dt;
                    ball.Y += ball.Vy * dt;
                    // the ball leaves a thin wisp of smoke off its hot skin, one puff every two pixels of flight
                    for (_wisp += Hypot(ball.X - x0, ball.Y - y0) / 2; _wisp > 1; _wisp--)
                    {
                        var f = r.Next();
                        var x = x0 + (ball.X - x0) * f;
                        var y = y0 + (ball.Y - y0) * f;
                        if (x < _right - 8 && x > 24 && t < 0.6)
                        {
                            _smoke.Add(new SmokePuff { X = x - 3, Y = y, Vx = -ball.Vx * 0.04, Vy = -ball.Vy * 0.04, R0 = 1, R1 = r.Range(2.5, 3.6) * (1 - t), Life = r.Range(0.7, 1.1), A = 0.5 * (1 - t), B = 0.95, Drag = 2, Rise = 5, Grow = 2 });
                        }
                    }
                    if (ball.X > _right || ball.Y < _top)
                    {
                        ball.Out = true;
                    }
                }
                _ps.Update(dt);
                _smoke.Update(dt);
                _lights.Update(dt);
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var f = _f;
                var sm = _sm;
                var ir = _ir;
                var my = _my;
                var pop = Math.Exp(-(t + 0.008) / 0.06);
                var list = _smoke.Puffs();
                foreach (var q in list)
                {
                    q.W *= 0.2 + pop * 1.3;
                }
                Cloud(p, list, sm, [sm[0], Mix(f[1], sm[1], 0.5), Mix(f[2], sm[2], 0.45), f[3], f[4], f[6], f[7]], 1.35, 0.86, null, 1.3);
                _ps.Draw(p);
                // the ball: dark iron, a lit crescent, a hot glint where the flash still burns on it
                if (!_ball.Out)
                {
                    var x = _ball.X;
                    var y = _ball.Y;
                    p.Circle(x, y, 3.2, ir[0]);
                    p.Circle(x, y, 2.6, ir[1]);
                    p.Circle(x - 0.7, y - 0.7, 1.8, ir[2]);
                    p.Px(x - 1, y - 1.5, ir[3]);
                    p.Px(x - 2, y - 1, ir[3]);
                    var glint = ir[2];
                    if (t < 0.3)
                    {
                        glint = f[5];
                    }
                    p.Px(x + 1.5, y + 1, glint);
                    p.Glow(x, y, 6, f[4], 0.45 * Clamp(1 - t / 0.5));
                }
                // the flash: a white ball with a tongue of flame for a frame, then a cooler, bigger burst as the gas spills
                if (t < 3 * F1)
                {
                    int frame;
                    double k;
                    if (t < F1)
                    {
                        frame = 1;
                        k = 1;
                    }
                    else if (t < 2 * F1)
                    {
                        frame = 2;
                        k = 1.15;
                    }
                    else
                    {
                        frame = 3;
                        k = 0.8;
                    }
                    k *= 1.3;
                    var lobes = new List<FlashLobe>
                    {
                        new() { Off = 0, A = 0, Len = 12 * k, Wid = 5 * k, Core = 0.55 },
                        new() { Off = 2, A = 0, Len = (24 + _jit[0] * 8) * k, Wid = 2.2 * k, Core = 0.4 },
                    };
                    for (var i = 0; i < 6; i++)
                    {
                        double side = 1;
                        if (i < 3)
                        {
                            side = -1;
                        }
                        var a = side * (0.4 + (i % 3) * 0.45 + _jit[i + 1] * 0.2);
                        var down = Math.Sin(Ang + a);
                        var len = (8 + _jit[i + 7] * 7) * k;
                        if (frame == 3)
                        {
                            len *= 0.7;
                        }
                        if (down > 0.1)
                        {
                            len = Math.Min(len, (_h - 3) / down);
                        }
                        lobes.Add(new FlashLobe { Off = 2, A = a, Len = len, Wid = 1.6 * k, Core = 0.25 });
                    }
                    uint[] c;
                    if (frame == 1)
                    {
                        c = [White, f[7], f[6], f[3]];
                    }
                    else if (frame == 2)
                    {
                        c = [f[7], f[6], f[4], f[2]];
                    }
                    else
                    {
                        c = [f[5], f[4], f[3], f[1]];
                    }
                    Flash(p, 4, my, Ang, lobes, c, frame);
                    if (frame == 1)
                    {
                        p.Circle(7, my - 1, 6.5, White);
                        p.Ring(7, my - 1, 7.5, 1, f[7]);
                    }
                }
                _lights.Draw(p);
                p.Glow(14, 0, 22, f[4], 0.25 * pop);
            }
        }
    }
}
