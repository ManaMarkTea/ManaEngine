using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Projectiles.ProjectilesLib;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/bullet-tracer.js: a white-hot slug tearing along, its tracer burning behind it in a long
    /// streak that sputters and cools from white through gold to red, shedding sparks and a thread of smoke. The spark
    /// and streak recipes are adapted from Sprite Fusion's open-source impact ricochet, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class BulletTracer : PixelEffect
    {
        private const double D = 0.6;
        private static readonly string[] Slug = ["..oyyp..", ".ypwwwwp", "ywwwwwww", ".ypwwwwp", "..oyyp.."];

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/bullet-tracer",
            Name = "Bullet tracer",
            Description = "A white-hot slug tearing along, its tracer burning behind it in a long streak that sputters and cools from white through gold to red, shedding sparks and a thread of smoke.",
            Size = (200, 40),
            Origin = (184, 18),
            Duration = D,
            Loop = true,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["smoke"] = new[] { "#2e2a36", "#46404f", "#625b6b", "#817a8a", "#a39caa" },
                ["length"] = 120.0,
                ["speed"] = 520.0,
                ["seed"] = 4.0,
            },
        };

        public override void Draw(PixelPainter p, double t, PixelFxOptions o)
        {
            var f = Fit(o.Ramp("palette"), 9);
            var sm = Fit(o.Ramp("smoke"), 5);
            var s = o.Number("seed");
            var l = o.Number("length");
            var v = o.Number("speed");
            var fr = Math.Floor(t * 30 + 1e-6);
            var ph = (Tau * t) / D;
            var left = -Meta.Origin.X + 2;

            // a thread of smoke off the cool end of the trace, wavering as it thins
            var puffs = new List<CloudPuff>();
            Beat(t, D, 18, s + 7, r => r.Range(0.5, 0.7), (age, life, r, _) =>
            {
                var k = age / life;
                var x = -l * r.Range(0.45, 0.8) - age * 90;
                var y = 0.5 + Math.Sin(age * 6 + r.Range(0, Tau)) * age * 3 - age * age * 8;
                if (x < left + 6)
                {
                    return;
                }
                puffs.Add(new CloudPuff { X = x, Y = y, R = 1.2 + age * 4, A = 0.55 * (1 - k) * Math.Min(1, age * 8) * Smooth(left, left + 30, x), B = 0.95 - k * 0.4, W = 0 });
            });
            Cloud(p, puffs, sm, sm, 1.2, 0.6);

            // the trace: a tapering body of burning gas near the slug -- banded white core, gold, an orange rim -- and past
            // it a one-pixel line cooling to red; the compound sputters, so brightness ripples back along it, and the cold
            // end breaks into dashes
            double Heat(double d) => 1 - Math.Pow(d / l, 0.8) + Noise(d * 0.12 - t * 40, s) * 0.12 + 0.08 * Math.Sin(d * 0.35 - ph * 9);
            double YAt(double d) => 0.5 + Math.Sin(d * 0.09 + ph * 2) * (d / l) * 0.8;
            var hot = l * 0.32;
            p.Shade(-hot - 1, -4, 1, 5, (x, y, _) =>
            {
                var d = -x;
                var u = d / hot;
                if (u < 0 || u > 1)
                {
                    return 0;
                }
                var w = 0.7 + 2.3 * Math.Pow(1 - u, 1.4) * (0.9 + 0.1 * Math.Sin(d * 0.8 - ph * 12));
                var vv = Math.Abs(y - YAt(d)) / w;
                if (vv > 1)
                {
                    return 0;
                }
                var h = Heat(d);
                if (vv < 0.38)
                {
                    if (h > 0.8)
                    {
                        return White;
                    }
                    return f[7];
                }
                if (vv < 0.7)
                {
                    if (h > 0.75)
                    {
                        return f[7];
                    }
                    if (h > 0.6)
                    {
                        return f[6];
                    }
                    return f[5];
                }
                if (u < 0.5)
                {
                    return f[5];
                }
                return f[4];
            });
            for (var d = hot; d <= l; d += 0.5)
            {
                var u = d / l;
                var h = Heat(d);
                if (u > 0.55 && Rand(Math.Floor(d / 3), fr, s) < (u - 0.55) * 1.6)
                {
                    continue;
                }
                uint c;
                if (h > 0.55)
                {
                    c = f[6];
                }
                else if (h > 0.42)
                {
                    c = f[5];
                }
                else if (h > 0.3)
                {
                    c = f[4];
                }
                else if (h > 0.18)
                {
                    c = f[3];
                }
                else if (h > 0.08)
                {
                    c = f[2];
                }
                else
                {
                    c = f[1];
                }
                var a = (1 - u) / 0.15;
                if (u < 0.85)
                {
                    a = 1;
                }
                p.Dot(-d, YAt(d), c, a);
            }
            p.GlowLine(-2, 1, -l * 0.45, 1, 6, f[4], 0.35);
            p.GlowLine(-2, 1, -l * 0.2, 1, 3, f[7], 0.3);

            // sparks of burning compound shed off the trace, dropping and cooling as they fall behind
            uint[] sp = [f[7], f[6], f[4], f[3]];
            Beat(t, D, 16, s + 2, r => r.Range(0.1, 0.28), (age, life, r, _) =>
            {
                var x0 = -r.Range(4, l * 0.5);
                var y0 = 0.5 + r.Range(-0.5, 1);
                var vx0 = -r.Range(10, 80);
                var vy0 = r.Range(-60, 50);
                var (x, y, vx, vy) = Wake(x0, y0, vx0, vy0, age, v * 0.35, 3, 260);
                if (x > left + 8)
                {
                    Spark(p, x, y, vx, vy, 1 - age / life, sp, 0.45, 0.012, 6);
                }
            });

            // the slug: a white-hot pointed round, pale-rimmed, its nose a point of light
            p.Sprite(Slug, new Dictionary<char, uint> { ['w'] = White, ['p'] = f[7], ['y'] = f[6], ['o'] = f[5] }, -1, -1);
            p.Glow(2, 1, 24, f[4], 0.5);
            p.Glow(3, 1, 8, White, 0.55);
        }
    }
}
