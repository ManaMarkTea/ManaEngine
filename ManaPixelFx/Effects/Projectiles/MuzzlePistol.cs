using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Projectiles.ProjectilesLib;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/muzzle-pistol.js: one pistol shot from a muzzle height px above the floor -- a spiky
    /// white-cored flash, sparks, a curl of smoke hooking up and back, a casing that bounces and settles. The flash lobes,
    /// spark, smoke and casing recipes are adapted from Sprite Fusion's open-source muzzle-flash, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class MuzzlePistol : PixelEffect
    {
        private const double Duration = 1.8;
        private const double F1 = 1 / 30.0;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/muzzle-pistol",
            Name = "Pistol shot",
            Description = "One pistol shot from a muzzle `height` px above the floor (the origin): a spiky white-cored flash, sparks, a curl of smoke hooking up and back, a casing that bounces and settles.",
            Size = (96, 72),
            Origin = (44, 66),
            Duration = Duration,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["brass"] = new[] { "#4a2e10", "#8c5c1c", "#d9a441", "#ffe39a" },
                ["smoke"] = new[] { "#26222e", "#3b3644", "#544e5e", "#6f6979", "#8c8596", "#aca5b5", "#cbc4d2" },
                ["height"] = 26.0,
                ["seed"] = 3.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _f;
            private readonly uint[] _b;
            private readonly uint[] _sm;
            private readonly double _my;
            private readonly double _seed;
            private readonly Particles _ps;
            private readonly Smoke _smoke;
            private readonly double _shapeSeed;
            private readonly double _tilt;
            private readonly (double Vx, double Vy, double Va, double A0) _shell;
            private double _t;

            public Run(PixelFxOptions o)
            {
                _f = Fit(o.Ramp("palette"), 9);
                _b = Fit(o.Ramp("brass"), 4);
                _sm = Fit(o.Ramp("smoke"), 7);
                var f = _f;
                _my = -o.Number("height");
                var my = _my;
                _seed = o.Number("seed");
                var r = new PixelRng(_seed);
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [f[7], f[6], f[4], f[3]]);
                _smoke = new Smoke(r);
                // the flash's shape, fixed for this shot
                _shapeSeed = r.Int(0, 1e6);
                _tilt = r.Range(-0.05, 0.05);
                // sparks spat forward, a few grains of unburnt powder that drop
                for (var i = 0; i < 6; i++)
                {
                    var a = r.Range(-0.3, 0.3);
                    var sp = r.Range(180, 400);
                    _ps.Spawn(P.Spark, 3, my, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.06, 0.16));
                }
                for (var i = 0; i < 5; i++)
                {
                    var a = r.Range(-0.35, 0.25);
                    var sp = r.Range(60, 160);
                    _ps.Spawn(P.Ember, 3, my, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.25, 0.5), f[5]);
                }
                // a puff of gas forward, then a chain of wisps that push out, slow and curl up and back over the muzzle
                for (var i = 0; i < 3; i++)
                {
                    _smoke.Add(new SmokePuff { X = 3, Y = my, Vx = r.Range(70, 120), Vy = r.Range(-12, 6), R0 = 1.6, R1 = r.Range(4.5, 6), Life = r.Range(0.8, 1.05), A = 0.55, Drag = 3.5, Rise = 10, Warm = 0.9, Delay = i * 0.012 });
                }
                for (var i = 0; i < 14; i++)
                {
                    var k = i / 13.0;
                    _smoke.Add(new SmokePuff
                    {
                        X = 2, Y = my, Vx = 115 - 75 * k + r.Range(-12, 12), Vy = -6 - 10 * k + r.Range(-5, 5), R0 = 1.4, R1 = (3.6 + k * 2.6) * r.Range(0.75, 1.25),
                        Life = 1.15 + k * 0.3, A = 0.62, B = r.Range(0.85, 1), Drag = 2.2, Rise = 6, Curl = -4.4 + k * 1.8 + r.Range(-0.5, 0.5), Grow = 1.6, Warm = 0.7, Delay = 0.02 + i * 0.022,
                    });
                }
                _smoke.Add(new SmokePuff { X = -7, Y = my - 2, Vx = -6, Vy = -18, R0 = 1.2, R1 = 4, Life = 1.05, A = 0.4, Rise = 8, Delay = 0.03 });
                var vx = -r.Range(25, 45);
                var vy = -r.Range(95, 130);
                var va = r.Sign() * r.Range(22, 32);
                _shell = (vx, vy, va, r.Range(0, 6.28));
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
                var my = _my;
                var pop = Math.Exp(-(t + 0.008) / 0.035);
                var list = _smoke.Puffs();
                foreach (var q in list)
                {
                    q.W *= 0.4 + pop;
                }
                Cloud(p, list, sm, [sm[0], Mix(f[1], sm[1], 0.5), Mix(f[2], sm[2], 0.4), f[3], f[4], f[5], f[7]], 1.5, 0.8, null, 1.4);
                // the casing: out of the port, up and back, tumbling, a bounce or two, then it lies there and fades
                if (t < Duration)
                {
                    DrawCasing(p, Casing(-7, my - 1, _shell.Vx, _shell.Vy, _shell.Va, t, 850, -0.5, 0.42, _shell.A0), _b, Math.Min(1, (Duration - 0.05 - t) / 0.3));
                }
                // the flash: the hero frame with a white core, then one cooler, smaller frame
                if (t < 2 * F1)
                {
                    var frame = 2;
                    if (t < F1)
                    {
                        frame = 1;
                    }
                    var lobes = FlashShape(Rng(_shapeSeed), frame, 1.15, (15, 22), (7, 12), (4.5, 8), (3, 5.5));
                    uint[] c = [f[7], f[6], f[4], f[2]];
                    if (frame == 1)
                    {
                        c = [White, f[7], f[6], f[3]];
                    }
                    Flash(p, 0, my, _tilt, lobes, c, frame + _seed);
                }
                _ps.Draw(p);
                // light: the pop, its warm pool, a touch on the floor below
                p.Glow(4, my, 30, f[4], 0.85 * pop);
                p.Glow(2, my, 8, White, 0.5 * pop);
                p.Glow(4, 0, 16, f[4], 0.2 * pop);
            }
        }
    }
}
