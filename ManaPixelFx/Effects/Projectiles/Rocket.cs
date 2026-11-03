using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Projectiles.ProjectilesLib;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/rocket.js: a steel rocket roars along -- a flickering jet with shock diamonds and a white-hot
    /// nozzle, a thick billowing smoke trail lit by the flame, sparks shed behind. The spark and fire-ball recipes are
    /// adapted from Sprite Fusion's open-source explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class Rocket : PixelEffect
    {
        private const double D = 1.2;
        private static readonly uint OutlineColor = Hex("#100d17");

        // the bell's exit: the jet starts here; the jet's mean length
        private const double Nozzle = -14;
        private const double J0 = 22;

        // the rocket: a steel body lit from above with a specular line, a painted ogive nose and band, swept fins with lit
        // leading edges, a dark bell with a glowing lip
        private static readonly (string[] Rows, double X0, double Y0) Ship = PixelShape(-15, -9, 30, 19, set =>
        {
            var body = new Dictionary<int, char> { [-3] = '4', [-2] = '6', [-1] = '5', [0] = '4', [1] = '3', [2] = '2', [3] = '1' };
            var seam = new Dictionary<int, char> { [-3] = '3', [-2] = '4', [-1] = '4', [0] = '3', [1] = '2', [2] = '1', [3] = '1' };
            var paint = new Dictionary<int, char> { [-3] = 'd', [-2] = 'e', [-1] = 'e', [0] = 'd', [1] = 'c', [2] = 'b', [3] = 'a' };
            for (var y = -3; y <= 3; y++)
            {
                for (var x = -11; x <= 5; x++)
                {
                    set(x, y, body[y]);
                }
            }
            for (var y = -3; y <= 3; y++)
            {
                set(-3, y, seam[y]);
                set(-9, y, paint[y]);
                set(-8, y, paint[y]);
            }
            var nose = new (int X, int H)[] { (6, 3), (7, 3), (8, 2), (9, 2), (10, 1), (11, 1), (12, 0), (13, 0) };
            foreach (var (xs, h) in nose)
            {
                for (var y = -h; y <= h; y++)
                {
                    set(xs, y, paint[y]);
                }
            }
            set(7, -2, 'w');
            set(8, -2, 'w');
            set(9, -1, 'e');
            set(13, 0, 'e');
            // fins, the top one in the light, the bottom one in shadow; each leading edge catches a highlight
            var fins = new (int A, int B)[] { (-13, -8), (-13, -9), (-13, -10), (-13, -11) };
            for (var i = 0; i < fins.Length; i++)
            {
                var (a, b) = fins[i];
                for (var x = a; x <= b; x++)
                {
                    var upper = '3';
                    var lower = '2';
                    if (i > 1)
                    {
                        upper = '4';
                        lower = '1';
                    }
                    if (x == b)
                    {
                        upper = '5';
                        lower = '3';
                    }
                    set(x, -4 - i, upper);
                    set(x, 4 + i, lower);
                }
            }
            // the bell: a throat and a flared exit, its lip glowing
            for (var y = -2; y <= 2; y++)
            {
                var c = '2';
                if (y < 0)
                {
                    c = '3';
                }
                set(-12, y, c);
            }
            for (var y = -3; y <= 3; y++)
            {
                var c = '1';
                if (y < -1)
                {
                    c = '2';
                }
                set(-13, y, c);
            }
            set(-14, -3, 'h');
            set(-14, 3, 'h');
            for (var y = -2; y <= 2; y++)
            {
                var c = 'h';
                if (Math.Abs(y) < 2)
                {
                    c = 'x';
                }
                set(-14, y, c);
            }
        });

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/rocket",
            Name = "Rocket",
            Description = "A steel rocket roars along: a flickering jet with shock diamonds and a white-hot nozzle, a thick billowing smoke trail lit by the flame, sparks shed behind.",
            Size = (288, 76),
            Origin = (252, 42),
            Duration = D,
            Loop = true,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["metal"] = new[] { "#0f0d16", "#25243a", "#3e4058", "#61667f", "#8e97b0", "#c4cce0", "#f2f6ff" },
                ["paint"] = new[] { "#3a0d14", "#7c1a22", "#bd2f2c", "#ec5a45", "#ff9b7e" },
                ["smoke"] = new[] { "#26222e", "#3d3846", "#5a5463", "#7e7887", "#a7a1ad", "#d0cad2", "#f1ecee" },
                ["speed"] = 120.0,
                ["seed"] = 5.0,
            },
        };

        // the jet: a cone of flame out of the bell in clean bands (a red rim, orange, a gold core), white-hot at the nozzle,
        // with a chain of shock diamonds down its middle; fr changes every frame so it flickers
        private static void Jet(PixelPainter p, uint[] f, double x0, double y0, double j, double wob, double fr)
        {
            var dia = Enumerable.Range(0, 4).Select(i => 3.6 + i * 4.3 + (Rand(fr, i, 41) - 0.5) * 0.7).ToArray();
            p.Shade(x0 - j - 2, y0 - 6, x0, y0 + 6, (x, y, _) =>
            {
                var u = x0 - x;
                if (u < 0)
                {
                    return 0;
                }
                var q = u / j;
                if (q > 1.05)
                {
                    return 0;
                }
                var ady = Math.Abs(y - y0);
                var qq = Math.Min(q, 1);
                var w = (2.6 + 2.4 * Math.Pow(qq, 0.7)) * (1 - qq * qq) * wob;
                var m = w - ady + (Rand(Math.Floor(x) * 7 + Math.Floor(y) * 131, fr) - 0.5) * 0.9 * Math.Min(1, 0.25 + q * 2);
                if (m < -0.5)
                {
                    return 0;
                }
                if (m < 0.5)
                {
                    if (q < 0.45)
                    {
                        return f[3];
                    }
                    return f[2];
                }
                for (var i = 0; i < dia.Length; i++)
                {
                    if (dia[i] > j * 0.72)
                    {
                        break;
                    }
                    var d = Math.Abs(u - dia[i]) / (1.9 - i * 0.15) + ady / (1.7 - i * 0.25);
                    if (d < 1)
                    {
                        if (d < 0.55 && i < 3)
                        {
                            return f[8];
                        }
                        return f[7];
                    }
                }
                if (u < 1.6 && ady < 1.6)
                {
                    return f[8];
                }
                var lvl = (1 - ady / Math.Max(0.6, w)) * (1 - Math.Pow(qq, 1.5));
                if (lvl > 0.62)
                {
                    if (u < 3)
                    {
                        return f[7];
                    }
                    return f[5];
                }
                if (lvl > 0.38)
                {
                    return f[5];
                }
                if (lvl > 0.16)
                {
                    return f[4];
                }
                return f[3];
            });
        }

        public override void Draw(PixelPainter p, double t, PixelFxOptions o)
        {
            var f = Fit(o.Ramp("palette"), 9);
            var m = Fit(o.Ramp("metal"), 7);
            var paint = Fit(o.Ramp("paint"), 5);
            var sm = Fit(o.Ramp("smoke"), 7);
            var s = o.Number("seed");
            var v = o.Number("speed");
            var fr = Math.Floor(t * 30 + 1e-6);
            double Bob(double tt) => Round(Math.Sin((tt / D) * Tau) * 0.62);
            var by = Bob(t);
            var j = J0 * (0.84 + 0.32 * Rand(fr, s, 1));
            var wob = 0.92 + 0.16 * Rand(fr, s, 2);
            var fl = j / J0;
            var left = -Meta.Origin.X + 2;
            uint[] sp = [f[7], f[6], f[4], f[3]];

            // smoke: puffs leave the tail of the flame, stream back, swell and rise, lit by the fire while young
            var list = new List<CloudPuff>();
            Beat(t, D, 66, s + 1, r => r.Range(1.1, 1.45), (age, life, r, _) =>
            {
                var sx = Nozzle - J0 * r.Range(0.62, 0.8);
                var sy = Bob(t - age) + r.Range(-1, 1);
                var vx = -r.Range(75, 105);
                var vy = r.Range(-20, 20);
                var (x, y, _, _) = Wake(sx, sy, vx, vy, age, v, 3, 0);
                var k = age / life;
                var rise = 0.5 * r.Range(6, 16) * age * age;
                var sway = Math.Sin(age * 2.3 + r.Range(0, Tau)) * age * 2;
                var start = r.Range(2.2, 3);
                double full;
                if (r.Chance(0.25))
                {
                    full = r.Range(14, 19);
                }
                else
                {
                    full = r.Range(8, 13);
                }
                var rad = start + (full - 2.6) * (1 - Math.Exp(-2.2 * age));
                if (x - rad < left + 1)
                {
                    return;
                }
                var a = Math.Min(1, age * 16) * Math.Pow(1 - k, 1.25) * 0.68 * Smooth(left, left + 40, x - rad);
                var b = Clamp(0.95 - k * 0.75 + r.Range(-0.08, 0.08));
                list.Add(new CloudPuff { X = x, Y = y - rise + sway, R = rad, A = a, B = b, W = Math.Exp(-age * r.Range(3.5, 6)) });
            });
            Cloud(p, list, sm, [Mix(f[0], sm[0], 0.5), Mix(f[1], sm[1], 0.45), Mix(f[2], sm[2], 0.35), f[3], f[4], f[6], f[7]], 1.3, 0.86, null, 1.5);
            // the smoke right behind the flame glows from inside
            p.Glow(Nozzle - J0 - 6, by, 26, f[4], 0.24 * fl);

            // embers carried off in the smoke, cooling and winking out
            Beat(t, D, 9, s + 4, r => r.Range(0.6, 1.1), (age, life, r, _) =>
            {
                var k = 1 - age / life;
                var vx = -r.Range(40, 120);
                var vy = r.Range(-40, 30);
                var (x, y, _, _) = Wake(Nozzle - J0 * 0.6, Bob(t - age), vx, vy, age, v, 2.5, -8);
                if (x < left + 2)
                {
                    return;
                }
                if (k < 0.3 && Rand(fr, r.Int(0, 999)) < 0.5)
                {
                    return;
                }
                var c = f[2];
                if (k > 0.6)
                {
                    c = f[6];
                }
                else if (k > 0.3)
                {
                    c = f[4];
                }
                p.Px(x, y, c);
                p.Glow(x, y, 3, c, 0.35 * k);
            });

            // tongues of flame torn off the tip of the jet, cooling and shrinking as they fall behind
            Beat(t, D, 30, s + 3, r => r.Range(0.08, 0.16), (age, life, r, _) =>
            {
                var k = age / life;
                var sx = Nozzle - J0 * r.Range(0.6, 0.9);
                var sy = Bob(t - age) + r.Range(-1.5, 1.5);
                var vx = -r.Range(10, 50);
                var vy = r.Range(-12, 12);
                var (x, y, _, _) = Wake(sx, sy, vx, vy, age, v, 4, -30);
                var idx = 7 - (int)Math.Floor(Math.Pow(k, 0.7) * 4);
                var rad = r.Range(1.2, 2.2) * (1 - k * 0.6);
                p.Circle(x, y, rad, f[idx - 2]);
                p.Circle(x + rad * 0.25, y - rad * 0.1, rad * 0.65, f[idx]);
                p.Glow(x, y, rad * 2.4 + 2, f[idx], 0.2 * (1 - k));
            });

            // sparks shed behind, streaking back and dropping
            Beat(t, D, 30, s + 2, r => r.Range(0.18, 0.45), (age, life, r, _) =>
            {
                var sx = Nozzle - r.Range(2, J0 * 0.7);
                var sy = Bob(t - age) + r.Range(-1.5, 1.5);
                var vx0 = -r.Range(20, 190);
                var vy0 = r.Range(-80, 50);
                var (x, y, vx, vy) = Wake(sx, sy, vx0, vy0, age, v, 2.6, 160);
                if (x > left + 10)
                {
                    Spark(p, x, y, vx, vy, 1 - age / life, sp);
                }
            });
            Beat(t, D, 8, s + 6, r => r.Range(0.5, 0.8), (age, life, r, _) =>
            {
                var sx = Nozzle - r.Range(4, J0 * 0.8);
                var sy = Bob(t - age) + r.Range(-1, 1);
                var vx0 = -r.Range(10, 80);
                var vy0 = r.Range(-60, 10);
                var (x, y, vx, vy) = Wake(sx, sy, vx0, vy0, age, v, 1.8, 170);
                if (x > left + 10)
                {
                    Spark(p, x, y, vx, vy, 1 - age / life, sp, 0.5);
                }
            });

            Jet(p, f, Nozzle, by + 0.5, j, wob, fr + s * 31);
            var pal = new Dictionary<char, uint>
            {
                ['o'] = OutlineColor, ['1'] = m[1], ['2'] = m[2], ['3'] = m[3], ['4'] = m[4], ['5'] = m[5], ['6'] = m[6],
                ['a'] = paint[0], ['b'] = paint[1], ['c'] = paint[2], ['d'] = paint[3], ['e'] = paint[4], ['w'] = White, ['h'] = f[5], ['x'] = f[8],
            };
            p.Sprite(Ship.Rows, pal, Ship.X0, Ship.Y0 + by);

            // light: a warm pool round the tail that breathes with the jet, the flame's own glow, a white-hot nozzle
            p.Glow(Nozzle - 10, by, 60, f[4], 0.28 * fl);
            p.GlowLine(Nozzle - 3, by + 0.5, Nozzle - j * 0.9, by + 0.5, 11, f[4], 0.2);
            p.Glow(Nozzle - 1, by + 0.5, 8, f[7], 0.45);
            // the flame lights the tail from below
            p.Glow(Nozzle + 3, by + 5, 12, f[4], 0.22 * fl);
        }
    }
}
