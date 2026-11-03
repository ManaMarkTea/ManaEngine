using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Projectiles.ProjectilesLib;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/laser-bolt.js: a red laser bolt -- a white-hot core in a rounded head, red bands tapering
    /// back, pulses racing down it, a tail that shimmers apart into wavering dashes. The banded body, pulses and
    /// spiralling sparks follow Sprite Fusion's open-source energy beam, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class LaserBolt : PixelEffect
    {
        private const double D = 0.5;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/laser-bolt",
            Name = "Laser bolt",
            Description = "A red laser bolt: a white-hot core in a rounded head, red bands tapering back, pulses racing down it, a tail that shimmers apart into wavering dashes.",
            Size = (128, 32),
            Origin = (108, 16),
            Duration = D,
            Loop = true,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#2a0610", "#5c0d1c", "#9a1428", "#d8243a", "#ff4d5e", "#ff8a94", "#ffd0d4", "#ffffff" },
                ["length"] = 40.0,
                ["seed"] = 2.0,
            },
        };

        public override void Draw(PixelPainter p, double t, PixelFxOptions o)
        {
            var c = Fit(o.Ramp("palette"), 8);
            var l = o.Number("length");
            var s = o.Number("seed");
            var ph = t / D;
            var fr = Math.Floor(t * 30 + 1e-6);
            var left = -Meta.Origin.X + 2;

            // the shimmering tail: dashes shed off the bolt's end, wavering, flickering and cooling as they fall behind
            Beat(t, D, 14, s, r => r.Range(0.18, 0.3), (age, life, r, _) =>
            {
                var k = age / life;
                var x0 = -l * 0.8 - age * r.Range(150, 230);
                var len = r.Range(3, 7) * (1 - k * 0.5);
                var y = 0.5 + Math.Sin(x0 * 0.25 + ph * Tau * 2) * (0.5 + k * 1.5) + r.Range(-0.6, 0.6) * k;
                if (x0 - len < left)
                {
                    return;
                }
                if (Rand(fr, r.Int(0, 999)) < k * 0.5)
                {
                    return;
                }
                var col = c[3];
                if (k < 0.3)
                {
                    col = c[5];
                }
                else if (k < 0.6)
                {
                    col = c[4];
                }
                p.Line(x0, y, x0 - len, y, col);
                if (k < 0.4)
                {
                    p.Glow(x0 - len / 2, y, 4, c[4], 0.3 * (1 - k));
                }
            });

            // the body: bands round a white core, thickest just behind the rounded head and tapering to a thread, a pulse
            // running back down it every so often; the edge breaks up a little as the light shimmers
            p.Shade(-l - 1, -5, 4, 6, (x, y, _) =>
            {
                var d = -x + 3;
                var u = d / (l + 3);
                if (u < 0 || u > 1)
                {
                    return 0;
                }
                double head = 1;
                if (d < 3)
                {
                    head = Math.Sqrt(Math.Max(0, 1 - Math.Pow((3 - d) / 3, 2)));
                }
                var w = (3.4 * Math.Pow(1 - u, 0.9) + 0.5) * head * (1 + 0.08 * Math.Sin(d * 0.9 - ph * Tau * 4));
                var v = Math.Abs(y - 0.5) / w;
                if (v > 1.2)
                {
                    return 0;
                }
                if (v > 1 && (u > 0.5 || Rand(Math.Floor(x), fr) > 0.6))
                {
                    return 0;
                }
                var pulse = 0;
                if ((((d * 0.05 - ph * 3) % 1) + 1) % 1 < 0.12)
                {
                    pulse = 1;
                }
                int lvl;
                if (v < 0.22)
                {
                    lvl = 6;
                    if (u < 0.6)
                    {
                        lvl = 7;
                    }
                }
                else if (v < 0.45)
                {
                    lvl = 6;
                    if (u > 0.5)
                    {
                        lvl = 5;
                    }
                }
                else if (v < 0.72)
                {
                    lvl = 4;
                }
                else if (v <= 1)
                {
                    lvl = 3;
                }
                else
                {
                    lvl = 2;
                }
                if (v < 0.72)
                {
                    lvl += pulse;
                }
                return c[Math.Min(7, lvl)];
            });
            // sparks spiralling round the core, a bright one on the near side and a dim one behind
            foreach (var offset in new[] { 0, Math.PI })
            {
                for (double d = 2; d < l * 0.7; d += 1)
                {
                    var a = d * 0.45 - ph * Tau * 3 + offset;
                    var y = 0.5 + Math.Sin(a) * (2.2 * (1 - d / l) + 0.6);
                    if (Math.Cos(a) > 0.3)
                    {
                        var col = c[6];
                        if (Math.Cos(a) > 0.8)
                        {
                            col = White;
                        }
                        p.Dot(-d, y, col);
                    }
                }
            }
            // the head's glint, flickering, and the light
            var g = 0.75 + 0.25 * Math.Sin(ph * Tau * 2);
            Sim.Sparkle(p, 1, 0.5, 3 + Round(g * 2), c[6], c[4], 0.5);
            p.GlowLine(1, 0.5, -l, 0.5, 9, c[3], 0.4);
            p.Glow(-2, 0.5, 20, c[3], 0.5 * g);
            p.Glow(1, 0.5, 6, White, 0.25);
        }
    }
}
