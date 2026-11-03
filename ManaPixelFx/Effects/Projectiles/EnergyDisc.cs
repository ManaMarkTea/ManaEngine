using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Projectiles.ProjectilesLib;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/energy-disc.js: a sawblade of crackling energy spinning flat out as it flies -- hooked teeth
    /// bright on their leading edges, a white hub, spiral arms churning, sparks flung off the teeth. The tangent spark
    /// streaks are adapted from Sprite Fusion's open-source energy and impact effects, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class EnergyDisc : PixelEffect
    {
        private const double D = 0.5;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/energy-disc",
            Name = "Energy disc",
            Description = "A sawblade of crackling energy spinning flat out as it flies: hooked teeth bright on their leading edges, a white hub, spiral arms churning, sparks flung off the teeth.",
            Size = (120, 52),
            Origin = (92, 26),
            Duration = D,
            Loop = true,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#0a0f2e", "#172a78", "#2550d0", "#3f8cff", "#7cc8ff", "#c8f0ff", "#ffffff" },
                ["size"] = 9.0,
                ["teeth"] = 8.0,
                ["speed"] = 220.0,
                ["seed"] = 5.0,
            },
        };

        public override void Draw(PixelPainter p, double t, PixelFxOptions o)
        {
            var c = Fit(o.Ramp("palette"), 7);
            var big = o.Number("size");
            var teeth = o.Number("teeth");
            var v = o.Number("speed");
            var s = o.Number("seed");
            var left = -Meta.Origin.X + 2;
            var rot = (Tau * 3 * t) / D;
            var seg = Tau / teeth;

            // speed lines peeling off the rim, top and bottom, stretching back and cooling
            Beat(t, D, 8, s + 4, r => r.Range(0.12, 0.22), (age, life, r, _) =>
            {
                var k = age / life;
                double side = 1;
                if (r.Chance(0.5))
                {
                    side = -1;
                }
                var x0 = -2 - age * r.Range(180, 260);
                var len = r.Range(8, 16) * (0.5 + k);
                var y = side * (big + r.Range(0.5, 3));
                if (x0 - len < left)
                {
                    return;
                }
                var col = c[3];
                if (k < 0.35)
                {
                    col = c[5];
                }
                else if (k < 0.7)
                {
                    col = c[4];
                }
                p.Line(x0, y, x0 - len, y, col, u =>
                {
                    if (u < 0.65)
                    {
                        return 1;
                    }
                    return (1 - u) / 0.35;
                });
            });
            p.GlowLine(-2, 0, -26, 0, 8, c[3], 0.35);

            // sparks flung off the tooth tips along the spin, then left behind in the air
            uint[] sp = [White, c[5], c[4], c[3]];
            Beat(t, D, 20, s, r => r.Range(0.12, 0.3), (age, life, r, _) =>
            {
                // pixelfx draws and discards one number here: kept, so the rest line up
                var a = rot - r.Range(0, Tau) * 0 + r.Range(0, Tau);
                var tip = big + 3.5;
                var x0 = Math.Cos(a) * tip;
                var y0 = Math.Sin(a) * tip;
                var speed = r.Range(120, 230);
                var (x, y, vx, vy) = Wake(x0, y0, -Math.Sin(a) * speed, Math.Cos(a) * speed, age, v * 0.6, 2.4, 60);
                if (x > left + 8 && Math.Abs(y) < 23)
                {
                    Spark(p, x, y, vx, vy, 1 - age / life, sp, 0.45, 0.014, 7);
                }
            });

            // the disc: a white hub in a dark collar, spiral arms churning, a bright rim; past it hooked teeth that run white
            // on their leading faces, and a dim blur ring where the teeth have just been
            p.Shade(-big - 5, -big - 5, big + 5, big + 5, (x, y, _) =>
            {
                var r = Hypot(x, y);
                var th = Math.Atan2(y, x);
                if (r > big + 4.6)
                {
                    return 0;
                }
                if (r <= big)
                {
                    if (r < 1.8)
                    {
                        return White;
                    }
                    if (r < 2.9)
                    {
                        return c[1];
                    }
                    if (r > big - 1.1)
                    {
                        return c[5];
                    }
                    var arm = Math.Sin(3 * (th - rot * 0.5) - r * 0.85);
                    if (arm > 0.65)
                    {
                        return c[5];
                    }
                    if (arm > 0.2)
                    {
                        return c[4];
                    }
                    if (arm > -0.3)
                    {
                        return c[3];
                    }
                    return c[2];
                }
                var ph = (th - rot) / seg - Math.Floor((th - rot) / seg);
                var h = 4.2 * Math.Pow(ph, 1.4);
                if (r - big < h)
                {
                    if (ph > 0.86)
                    {
                        return White;
                    }
                    if (ph > 0.6)
                    {
                        return c[6];
                    }
                    if (ph > 0.35)
                    {
                        return c[5];
                    }
                    return c[4];
                }
                if (r - big < 1.6)
                {
                    return c[2];
                }
                return 0;
            });
            // filaments of energy crackling from the hub to the rim, new every frame
            var fr = Math.Floor(t * 30 + 1e-6);
            for (var j = 0; j < 3; j++)
            {
                var g = Rng(fr, j, s);
                var a0 = g.Range(0, Tau);
                var pts = new List<(double X, double Y)>();
                for (var i = 0; i <= 4; i++)
                {
                    var rr = 2.5 + ((big - 3) * i) / 4;
                    var a = a0 + i * 0.25;
                    if (i != 0)
                    {
                        a += g.Range(-0.35, 0.35);
                    }
                    pts.Add((Math.Cos(a) * rr, Math.Sin(a) * rr));
                }
                var col = White;
                if (j != 0)
                {
                    col = c[6];
                }
                p.Polyline(pts, col);
            }
            p.Ring(0, 0, big + 0.5, 1, c[6]);
            // light: a soft pool, a hot hub, a glint riding the top of the rim
            p.Glow(0, 0, 30, c[3], 0.4);
            p.Glow(0, 0, 6, c[5], 0.35);
            p.Glow(Math.Cos(rot) * big, Math.Sin(rot) * big, 5, c[6], 0.4);
        }
    }
}
