using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Core.PixelParticles;
using static ManaPixelFx.Effects.Projectiles.ProjectilesLib;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/muzzle-smg.js: sustained fire at twelve rounds a second -- each shot a different spiky flash
    /// with a white core, sparks, smoke wisps piling up and drifting, brass casings bouncing on the floor. The flash lobes,
    /// spark, smoke wisp and casing recipes are adapted from Sprite Fusion's open-source muzzle-flash, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class MuzzleSmg : PixelEffect
    {
        private const double D = 1;
        private const int Shots = 12;
        private const double F1 = 1 / 30.0;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/muzzle-smg",
            Name = "SMG burst",
            Description = "Sustained fire at twelve rounds a second: each shot a different spiky flash with a white core, sparks, smoke wisps piling up and drifting, brass casings bouncing on the floor.",
            Size = (112, 72),
            Origin = (56, 66),
            Duration = D,
            Loop = true,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["brass"] = new[] { "#4a2e10", "#8c5c1c", "#d9a441", "#ffe39a" },
                ["smoke"] = new[] { "#26222e", "#3b3644", "#544e5e", "#6f6979", "#8c8596", "#aca5b5", "#cbc4d2" },
                ["height"] = 26.0,
                ["seed"] = 6.0,
            },
        };

        // every live copy of every shot: fn(s, age) for shot s fired at s * D / Shots, while age < life
        private static void EachShot(double t, double life, Action<int, double> fn)
        {
            for (var s = 0; s < Shots; s++)
            {
                for (var age = Wrap(t - (s * D) / Shots + 1e-7, D); age < life; age += D)
                {
                    fn(s, age);
                }
            }
        }

        public override void Draw(PixelPainter p, double t, PixelFxOptions o)
        {
            var f = Fit(o.Ramp("palette"), 9);
            var b = Fit(o.Ramp("brass"), 4);
            var sm = Fit(o.Ramp("smoke"), 7);
            var seed = o.Number("seed");
            var h = o.Number("height");
            const double mx = 0;
            var my = -h;
            uint[] sp = [f[7], f[6], f[4], f[3]];
            var top = -Meta.Origin.Y + 1;
            // the latest shot's pop of light, for the smoke it lights up
            double pop = 0;
            EachShot(t, 0.2, (_, age) => pop = Math.Max(pop, Math.Exp(-(age + 0.008) / 0.035)));

            // smoke: every shot leaves a few wisps that push forward, slow, swell and rise, piling into a drifting haze
            var puffs = new List<CloudPuff>();
            EachShot(t, 2.2, (s, age) =>
            {
                for (var j = 0; j < 3; j++)
                {
                    var r = Rng(seed, s, j, 3);
                    var life = r.Range(1.5, 2.2);
                    var dl = j * 0.03;
                    if (age < dl || age - dl >= life)
                    {
                        continue;
                    }
                    var a = age - dl;
                    var k = a / life;
                    var port = j == 2;
                    // a push forward that dies fast, then the warm gas lifts it and the air drifts it back over the gun
                    double vx;
                    double vy;
                    if (port)
                    {
                        vx = r.Range(-12, 6);
                        vy = -r.Range(14, 26);
                    }
                    else
                    {
                        vx = r.Range(45, 100) * (1 - j * 0.4);
                        vy = -r.Range(4, 16);
                    }
                    var startX = mx + 3;
                    var startY = my;
                    double size = 1.2;
                    double share = 0.34;
                    if (port)
                    {
                        startX = -7;
                        startY = my - 2;
                        size = 1;
                        share = 0.24;
                    }
                    var x = startX + (vx * (1 - Math.Exp(-3.2 * a))) / 3.2 - r.Range(3, 7) * a * a + Math.Sin(a * 2.1 + r.Range(0, Tau)) * a * 2.5;
                    var y = startY + (vy * (1 - Math.Exp(-2.4 * a))) / 2.4 - 0.5 * r.Range(9, 16) * a * a;
                    var rad = size + r.Range(2, 4.5) * (1 - Math.Exp(-1.4 * a));
                    if (y - rad < top + 1)
                    {
                        continue;
                    }
                    var edge = Math.Min(1, (y - rad - top) / 12);
                    puffs.Add(new CloudPuff { X = x, Y = y, R = rad, A = Math.Min(1, a * 10) * Math.Pow(1 - k, 1.6) * share * edge, B = Clamp(0.92 - k * 0.6), W = pop * Math.Exp(-Hypot(x - mx, y - my) / 9) });
                }
            });
            Cloud(p, puffs, sm, [sm[0], Mix(f[1], sm[1], 0.5), Mix(f[2], sm[2], 0.4), f[3], f[4], f[5], f[7]], 1.4, 0.7, null, 1.3);

            // casings: thrown up and back out of the ejection port, tumbling, bouncing on the floor, then lying a moment
            EachShot(t, D, (s, age) =>
            {
                var r = Rng(seed, s, 7);
                var vx = -r.Range(18, 48);
                var vy = -r.Range(85, 135);
                var va = r.Sign() * r.Range(20, 32);
                var k = Casing(-7, my - 1, vx, vy, va, age, 850, -0.5, 0.42, r.Range(0, Tau));
                DrawCasing(p, k, b, Clamp((D - age) / 0.25));
            });

            // the flash: a hero frame with a white core, then a cooler, smaller one; every shot its own shape
            EachShot(t, 2 * F1, (s, age) =>
            {
                var r = Rng(seed, s);
                var frame = 2;
                if (age < F1)
                {
                    frame = 1;
                }
                var tilt = r.Range(-0.06, 0.06);
                var lobes = FlashShape(r, frame, r.Range(0.95, 1.2), (12, 21), (6, 11), (4, 7.5), (2.5, 5));
                uint[] c = [f[7], f[6], f[4], f[2]];
                if (frame == 1)
                {
                    c = [White, f[7], f[6], f[3]];
                }
                Flash(p, mx, my, tilt, lobes, c, s * 7 + frame);
            });

            // sparks and unburnt grains spat forward with each shot, dropping as they slow
            EachShot(t, 0.3, (s, age) =>
            {
                for (var j = 0; j < 5; j++)
                {
                    var r = Rng(seed, s, j, 11);
                    var life = r.Range(0.05, 0.16);
                    if (j > 2)
                    {
                        life *= 1.8;
                    }
                    if (age >= life)
                    {
                        continue;
                    }
                    var a = r.Range(-0.3, 0.3);
                    var speed = r.Range(170, 380);
                    if (j > 2)
                    {
                        speed *= 0.5;
                    }
                    var (x, y) = Fly(mx + 2, my, Math.Cos(a) * speed, Math.Sin(a) * speed, age, 420, 2.2);
                    var (vx, vy) = FlyV(Math.Cos(a) * speed, Math.Sin(a) * speed, age, 420, 2.2);
                    if (j > 2)
                    {
                        var c = f[4];
                        if (age / life < 0.5)
                        {
                            c = f[6];
                        }
                        p.Px(x, y, c);
                    }
                    else
                    {
                        Spark(p, x, y, vx, vy, 1 - age / life, sp, 0.45, 0.011);
                    }
                }
            });

            // light: a hot pop at the muzzle on every shot, the floor and smoke catching it
            EachShot(t, 0.15, (_, age) =>
            {
                var k = Math.Exp(-(age + 0.008) / 0.03);
                p.Glow(mx + 4, my, 26, f[4], 0.8 * k);
                p.Glow(mx + 2, my, 7, White, 0.45 * k);
                p.Glow(mx + 2, 0, 12, f[4], 0.16 * k);
            });
        }
    }
}
