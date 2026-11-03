using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Core.PixelParticles;
using static ManaPixelFx.Effects.Projectiles.ProjectilesLib;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/plasma-shot.js: a glowing plasma blob -- hot knots churning in its core, a corona crackling
    /// over its skin, its tail necking into droplets that tear off, cool and wink out behind. The layered head, rim rays
    /// and trail light follow Sprite Fusion's open-source energy bolt and orb, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class PlasmaShot : PixelEffect
    {
        private const double D = 0.8;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/plasma-shot",
            Name = "Plasma shot",
            Description = "A glowing plasma blob: hot knots churning in its core, a corona crackling over its skin, its tail necking into droplets that tear off, cool and wink out behind.",
            Size = (144, 60),
            Origin = (108, 30),
            Duration = D,
            Loop = true,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#061a1c", "#0b3b34", "#0f6a4a", "#18a05a", "#3fd46a", "#8cf57e", "#d6ffc4", "#ffffff" },
                ["size"] = 6.0,
                ["speed"] = 200.0,
                ["seed"] = 3.0,
            },
        };

        private readonly record struct Ball(double X, double Y, double R, double H, double Core);

        private readonly record struct Drop(double X, double Y, double R, double K);

        public override void Draw(PixelPainter p, double t, PixelFxOptions o)
        {
            var c = Fit(o.Ramp("palette"), 8);
            var big = o.Number("size");
            var v = o.Number("speed");
            var s = o.Number("seed");
            var w = (Tau * t) / D;
            var fr = Math.Floor(t * 30 + 1e-6);
            var left = -Meta.Origin.X + 2;

            // an ion streak left in the air behind it, rippling and cooling in steps
            var t0 = -big * 3.2;
            double Tail(double x) => Math.Sin(x * 0.2 + w * 2) * (0.3 + ((t0 - x) / 44) * 1.8);
            for (var x = t0; x > t0 - 44; x -= 1)
            {
                var u = (t0 - x) / 44;
                var y = Tail(x);
                var col = c[4];
                if (u < 0.2)
                {
                    col = c[6];
                }
                else if (u < 0.45)
                {
                    col = c[5];
                }
                var a = (1 - u) / 0.45;
                if (u < 0.55)
                {
                    a = 1;
                }
                p.Dot(x, y, col, a);
                if (u < 0.3)
                {
                    var under = c[4];
                    if (u < 0.15)
                    {
                        under = c[5];
                    }
                    p.Px(x, y + 1, under);
                }
            }

            // the blob as a field of balls: a body, three hot knots churning inside it at their own speeds, a wagging tail,
            // and droplets that cling to the tail's end until the air tears them off
            var balls = new List<Ball> { new(0, 0, big, 1, 0.8) };
            double[] spins = [2, -3, 5];
            for (var i = 0; i < 3; i++)
            {
                var a = w * spins[i] + (i * Tau) / 3;
                var d = big * (0.5 + 0.1 * Math.Sin(w * 3 + i * 2));
                balls.Add(new Ball(Math.Cos(a) * d - 0.4, Math.Sin(a) * d * 0.8, big * 0.32, 1, 1));
            }
            // the tail: thick and hot where it leaves the body, tapering and cooling to a point, its axis still glowing
            for (var i = 1; i <= 6; i++)
            {
                var x = -big * (0.5 + i * 0.42);
                var wag = Math.Sin(w * 2 - i * 0.9) * (0.3 + i * 0.28);
                var r = big * (0.62 - i * 0.08);
                balls.Add(new Ball(x, wag, r, 0.96 - i * 0.05, Math.Sqrt((5.2 - i * 0.65) * 0.3) / r));
            }
            var drops = new List<Drop>();
            Cycle(t, D, 11, s, r => r.Range(0.34, 0.5), (age, life, r, _) =>
            {
                var k = age / life;
                var pull = r.Range(450, 800);
                var x = -big * 2.9 - 0.5 * pull * age * age - r.Range(0, 16) * age;
                var y = Math.Sin(w * 2 - 5.4 + age * 12) * 2 + r.Range(-30, 30) * age * (0.4 + age);
                var rad = big * r.Range(0.3, 0.48) * (1 - k * 0.6);
                balls.Add(new Ball(x, y, rad, 0.97 * (1 - k * 0.75), Math.Sqrt(Math.Max(0, 1 - k * 1.3) * 7 * 0.3) / rad));
                drops.Add(new Drop(x, y, rad, k));
            });
            // the skin comes from a compact field (so the tail tapers and droplets neck cleanly as they part); the hot bands
            // inside come only from the body and its churning knots
            p.Shade(-big * 2.6 - 60, -big * 2 - 8, big * 1.6, big * 2 + 8, (x, y, _) =>
            {
                double f = 0;
                double heat = 0;
                double hh = 0;
                foreach (var b in balls)
                {
                    var dx = x - b.X;
                    var dy = y - b.Y;
                    var d2 = dx * dx + dy * dy;
                    var q = d2 / (4 * b.R * b.R);
                    if (q < 1)
                    {
                        var cc = ((1 - q) * (1 - q)) / 0.5625;
                        f += cc;
                        heat += cc * b.H;
                    }
                    if (b.Core != 0)
                    {
                        hh += (b.Core * b.Core * b.R * b.R) / (d2 + 0.3);
                    }
                }
                if (f < 1)
                {
                    return 0;
                }
                int lvl;
                if (hh > 10)
                {
                    lvl = 7;
                }
                else if (hh > 4.6)
                {
                    lvl = 6;
                }
                else if (hh > 2.2)
                {
                    lvl = 5;
                }
                else if (f > 1.4)
                {
                    lvl = 4;
                }
                else
                {
                    lvl = 3;
                }
                var cap = (int)Round(2 + (heat / f) * 5.4);
                if (x > big * 0.3 && lvl < 5)
                {
                    lvl++;
                }
                return c[Math.Max(2, Math.Min(lvl, cap))];
            });

            // the corona: jagged arcs leaping off the skin and back, and rays flickering round the rim, new every frame
            for (var j = 0; j < 3; j++)
            {
                var g = Rng(fr, j, s);
                var a0 = g.Range(-2.3, 2.3);
                var span = g.Range(0.45, 0.9) * g.Sign();
                const int n = 6;
                var hgt = g.Range(1.5, 3.5);
                var pts = new List<(double X, double Y)>();
                for (var i = 0; i <= n; i++)
                {
                    var a = a0 + (span * i) / n;
                    var rr = big + 0.6 + hgt * Math.Sin((Math.PI * i) / n);
                    if (i % n != 0)
                    {
                        rr += g.Range(-0.5, 0.5);
                    }
                    pts.Add((Math.Cos(a) * rr - 0.4, Math.Sin(a) * rr * 0.92));
                }
                var col = c[6];
                if (j != 0)
                {
                    col = c[5];
                }
                p.Polyline(pts, col);
                p.Px(pts[3].X, pts[3].Y, White);
                p.Glow(pts[3].X, pts[3].Y, 6, c[5], 0.4);
            }
            for (var j = 0; j < 4; j++)
            {
                var g = Rng(fr, j + 9, s);
                var a = g.Range(-2.2, 2.2);
                var l = g.Range(1.5, 4);
                var r0 = big + 1;
                var x0 = Math.Cos(a) * r0 - 0.4;
                var y0 = Math.Sin(a) * r0 * 0.92;
                var col = c[5];
                if (j % 2 != 0)
                {
                    col = c[4];
                }
                p.Line(x0, y0, x0 + Math.Cos(a) * l, y0 + Math.Sin(a) * l, col);
            }
            // motes flung off the corona, streaking back
            uint[] sp = [White, c[6], c[5], c[3]];
            Beat(t, D, 14, s + 5, r => r.Range(0.12, 0.3), (age, life, r, _) =>
            {
                var a = r.Range(-2.4, 2.4);
                var speed = r.Range(60, 130);
                var (x, y, vx, vy) = Wake(Math.Cos(a) * (big + 1), Math.Sin(a) * (big + 1), Math.Cos(a) * speed, Math.Sin(a) * speed, age, v * 0.7, 4, 0);
                if (x > left + 4 && Math.Abs(y) < 26)
                {
                    Spark(p, x, y, vx, vy, 1 - age / life, sp, 0.4, 0.015, 6);
                }
            });

            // light: a big soft pool, a hot heart that pulses with the churn, a point on every droplet
            var pulse = 0.85 + 0.15 * Math.Sin(w * 4);
            p.Glow(-2, 0, 34, c[3], 0.8 * pulse);
            p.Glow(0, 0, 16, c[4], 0.35 * pulse);
            p.Glow(1, 0, 7, c[6], 0.3 * pulse);
            p.GlowLine(-big, 0, -big * 4.6, 0, 7, c[3], 0.3);
            foreach (var d in drops)
            {
                if (d.X < left + 4)
                {
                    continue;
                }
                if (d.K < 0.6 && d.X < -big * 3.5)
                {
                    var col = c[6];
                    if (d.K < 0.3)
                    {
                        col = c[7];
                    }
                    p.Px(d.X, d.Y, col);
                }
                p.Glow(d.X, d.Y, 3 + d.R * 2, c[2], 0.6 * (1 - d.K));
            }
        }
    }
}
