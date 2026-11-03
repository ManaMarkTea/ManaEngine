using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Projectiles.ProjectilesLib;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/arrow.js: an arrow in flight -- a steel broadhead catching the light, a wooden shaft,
    /// fletching fluttering in the wind, streaks of air peeling off its head and swirling off its feathers.
    /// </summary>
    public sealed class Arrow : PixelEffect
    {
        private const double D = 0.6;
        private const double Len = 36;

        // the arrow, nose right, tip at (0, 0): fletching (three poses for the flutter), shaft, binding, broadhead
        private static readonly string[] Head = ["..LL....", "..sLLL..", "bbsSSSSW", "..dDDD..", "..DD...."];

        private static readonly string[][] Fletch =
        [
            ["AAa.........", ".aAAa.......", "..aaAAa.....", "nkkkkkkkkkkk", "..cCCc......", ".cCCc.......", "CCc........."],
            [".AAa........", "..aAAa......", "..aaAAAa....", "nkkkkkkkkkkk", "..cCCCc.....", "..cCCc......", ".CCc........"],
            ["AAAa........", "aAAa........", ".aaAAa......", "nkkkkkkkkkkk", "..ccCc......", "cCCc........", "CCc........."],
        ];

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/arrow",
            Name = "Arrow",
            Description = "An arrow in flight: a steel broadhead catching the light, a wooden shaft, fletching fluttering in the wind, streaks of air peeling off its head and swirling off its feathers.",
            Size = (112, 32),
            Origin = (92, 16),
            Duration = D,
            Loop = true,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#7a1f2c", "#b8323c", "#e8e2d6", "#ffffff" },
                ["wood"] = new[] { "#3a2414", "#6e4422", "#a8723c", "#d8a868" },
                ["steel"] = new[] { "#2e3648", "#5a6680", "#9aa6ba", "#dfe7f2" },
                ["wind"] = "#b8c8e8",
                ["seed"] = 3.0,
            },
        };

        public override void Draw(PixelPainter p, double t, PixelFxOptions o)
        {
            var pl = Fit(o.Ramp("palette"), 4);
            var w = Fit(o.Ramp("wood"), 4);
            var st = Fit(o.Ramp("steel"), 4);
            var wind = o.Color("wind");
            var s = o.Number("seed");
            var fr = Math.Floor(t * 30 + 1e-6);
            var ph = t / D;
            var left = -Meta.Origin.X + 2;

            // streaks of air peeling off the head and the fletching, stretching back and thinning
            Beat(t, D, 9, s, r => r.Range(0.14, 0.26), (age, life, r, _) =>
            {
                var k = age / life;
                double top = 1;
                if (r.Chance(0.5))
                {
                    top = -1;
                }
                var from = -Len + 6;
                if (r.Chance(0.6))
                {
                    from = -3;
                }
                var x0 = from - age * r.Range(160, 240);
                var len = r.Range(8, 16) * (0.5 + k);
                var y = 0.5 + top * (r.Range(3, 5) + k * r.Range(1, 2.5));
                if (x0 - len < left)
                {
                    return;
                }
                var c = Mix(wind, Hex("#2a2540"), (k - 0.4) * 1.2);
                if (k < 0.4)
                {
                    c = wind;
                }
                p.Line(x0, y, x0 - len, y, u =>
                {
                    if (u < 0.25 && k < 0.4)
                    {
                        return Mix(wind, White, 0.5);
                    }
                    return c;
                }, u =>
                {
                    if (u < 0.65)
                    {
                        return 1;
                    }
                    return (1 - u) / 0.35;
                });
            });
            // little eddies spinning off the feathers
            Beat(t, D, 6, s + 3, r => r.Range(0.15, 0.25), (age, life, r, _) =>
            {
                var k = age / life;
                var cx = -Len - 2 - age * r.Range(100, 150);
                var cy = 0.5 + r.Sign() * r.Range(2, 4);
                var a = age * 30 + r.Range(0, Tau);
                var rr = 1.2 + k * 1.5;
                if (cx < left + 4)
                {
                    return;
                }
                for (var i = 0; i < 4; i++)
                {
                    p.Dot(cx + Math.Cos(a + i * 0.5) * rr, cy + Math.Sin(a + i * 0.5) * rr, wind, (1 - k) * (1 - i * 0.2));
                }
            });

            // the shaft with a lit top line
            p.Line(-Len + 11, 0, -7, 0, w[2]);
            for (var x = -Len + 14; x < -8; x += 6)
            {
                p.Px(x, 0, w[3]);
            }
            // the fletching flutters through its poses, faster than the frame rate can quite catch
            var pose = Fletch[(int)Math.Floor(Rand(fr, s) * 3)];
            p.Sprite(pose, new Dictionary<char, uint> { ['a'] = pl[2], ['A'] = pl[3], ['c'] = pl[0], ['C'] = pl[1], ['k'] = w[1], ['n'] = w[0] }, -Len, -3);
            // the binding and the broadhead: a lit upper bevel, a shaded lower one, a white point
            p.Sprite(Head, new Dictionary<char, uint> { ['L'] = st[3], ['s'] = st[2], ['S'] = st[2], ['d'] = st[0], ['D'] = st[1], ['b'] = w[0], ['W'] = White }, -7, -2);
            // a glint runs down the blade now and then
            var g = (ph * 2) % 1;
            if (g < 0.25)
            {
                Sim.Sparkle(p, -5 + g * 16, -1, 1 + Round(2 * Math.Sin((g / 0.25) * Math.PI)), White, st[3], 0.25);
            }
        }
    }
}
