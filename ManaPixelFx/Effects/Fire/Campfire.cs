using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Fire.FireLib;

namespace ManaPixelFx.Effects.Fire
{
    /// <summary>
    /// pixelfx's fire/campfire.js: crossed logs on a bed of coals and a lively fire, popping sparks, embers, smoke and a
    /// pool of light. The fire recipe is adapted from Sprite Fusion's open-source fire, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class Campfire : PixelEffect
    {
        private const double D = 3;
        private const double Cross = 0.56;
        private static readonly TimeNoise NL = NoiseFor(D);
        private static readonly uint Smoke1 = Hex("#5b5566");
        private static readonly uint[] Ash = [Hex("#2a2530"), Hex("#4a4350"), Hex("#6b6270")];

        // the logs, near end first: they cross in an X over the coals
        private static readonly (double X0, double Y0, double X1, double Y1, double R)[] Logs =
        [
            (-19, 2, 15, -12, 3.4),
            (19, 2, -15, -12, 3.4),
        ];

        // the fire: x, base, height (share of the height option), half-width, back row
        private static readonly Tongue[] Tongues =
        [
            new(-7.5, -7, 0.42, 1.8, true),
            new(7.5, -7, 0.46, 1.8, true),
            new(-3, -9, 0.78, 2.0, true),
            new(3.2, -9, 0.82, 2.0, true),
            new(-12, -3, 0.14, 1.3, false),
            new(12.5, -3, 0.16, 1.3, false),
            new(-9.5, -4.5, 0.3, 1.9, false),
            new(9.5, -4.5, 0.34, 1.9, false),
            new(-5, -6, 0.6, 2.6, false),
            new(5.2, -6, 0.66, 2.6, false),
            new(0, -7, 1, 3.4, false),
        ];

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "fire/campfire",
            Name = "Campfire",
            Description = "Crossed logs on a bed of coals and a lively fire: tongues that lick and leap, popping sparks, embers drifting up, smoke curling off, a warm pool of light.",
            Size = (128, 128),
            Origin = (64, 112),
            Duration = D,
            Loop = true,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#8a1f17", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["wood"] = new[] { "#0e080a", "#24130f", "#3b2216", "#5a3520", "#8a5a34", "#b9884f" },
                ["height"] = 22.0,
                ["seed"] = 3.0,
            },
        };

        private readonly record struct TongueState(double Dx, double Y, double H, double W, bool Back, double Env, double N);

        private readonly record struct Plume(double X, double Y, double Vx, double Vy, double Life, double S) : ILooped;

        private readonly record struct ShedPuff(int I, double Fire, bool Smokes, double Sl, double Ox, double Oy, double Vx, double Vy, double S, double F, double Life) : ILooped;

        private readonly record struct WoodFlame(double X, double Y, double Vx, double Vy, double Life, double S, double F) : ILooped;

        private readonly record struct TipLick(int I, double Vx, double Vy, double S, double Life) : ILooped;

        private readonly record struct Pop(double X, double Y, double N, double Seed, double Life) : ILooped;

        private readonly record struct RisingEmber(double X, double Y, double Vx, double Vy, double Life, double S, double A, double B) : ILooped;

        // a log from its near end, drawn between u0 and u1 along it: a dark rim, a lit top, grooves along the grain, a
        // shadowed underside; charred with glowing cracks in the fire
        private static void Log((double X0, double Y0, double X1, double Y1, double R) log, uint[] wood, uint[] hot, double u0, double u1, double t, double s, PixelPainter p)
        {
            var (x0, y0, x1, y1, r) = log;
            var dx = x1 - x0;
            var dy = y1 - y0;
            var len = Hypot(dx, dy);
            var ax = dx / len;
            var ay = dy / len;
            var nx = -ay;
            var ny = ax;
            if (ny < 0)
            {
                nx = -nx;
                ny = -ny;
            }
            var rim = 1 - 1.05 / r;
            p.Shade(Math.Min(x0, x1) - r - 1, Math.Min(y0, y1) - r - 1, Math.Max(x0, x1) + r + 1, Math.Max(y0, y1) + r + 1, (x, y, o) =>
            {
                var along = (x - x0) * ax + (y - y0) * ay;
                var u = along / len;
                var side = ((x - x0) * nx + (y - y0) * ny) / r;
                if (u < u0 || u > u1 || Math.Abs(side) > 1)
                {
                    return 0;
                }
                var burn = Clamp(1.2 - Math.Abs(x) / 8);
                var lane = Round(side * r * 0.85);
                var run = Rand(Math.Floor(along / 3), lane, s);
                if (Math.Abs(side) > rim || along > len - 1)
                {
                    if (side < 0 && burn > 0.4)
                    {
                        return hot[7];
                    }
                    return wood[0];
                }
                if (burn > 0.35 && lane < 2)
                {
                    // charred: black, with embers breathing in the cracks along the grain
                    if (run < 0.55 && lane > -2)
                    {
                        var g = 0.5 + 0.5 * Math.Sin((t / D) * Math.PI * 6 + Rand(Math.Floor(along / 2), lane, s + 1) * 6.3);
                        o.K = 0.3 * burn * g;
                        if (g > 0.72)
                        {
                            return hot[3];
                        }
                        if (g > 0.4)
                        {
                            return hot[4];
                        }
                        return hot[5];
                    }
                    if (lane < -1)
                    {
                        return wood[1];
                    }
                    return wood[0];
                }
                if (lane <= -2)
                {
                    if (run < 0.25)
                    {
                        return wood[2];
                    }
                    return Mix(wood[3], hot[4], 0.45 * burn);
                }
                if (lane == -1)
                {
                    if (run < 0.45)
                    {
                        return wood[1];
                    }
                    return wood[2];
                }
                if (lane == 0)
                {
                    if (run < 0.2)
                    {
                        return wood[1];
                    }
                    return wood[2];
                }
                if (run < 0.4)
                {
                    return wood[0];
                }
                return wood[1];
            });
            // the near end's cut face: a dark rim, pale sapwood, growth rings round a dark heart
            if (u0 <= 0)
            {
                p.Ellipse(x0, y0, r * 0.62, r + 0.2, (d, _) =>
                {
                    if (d > 0.78)
                    {
                        return wood[0];
                    }
                    if (d > 0.56)
                    {
                        return wood[5];
                    }
                    if (d > 0.32)
                    {
                        return wood[4];
                    }
                    return wood[3];
                });
            }
        }

        // the bed of coals under the logs: coals that breathe in the heat, grey ash round the rim
        private static void Coals(PixelPainter p, uint[] wood, uint[] hot, double t, double s)
        {
            p.Shade(-15, -3, 15, 2, (x, y, o) =>
            {
                var dx = x / 14;
                var dy = (y + 0.3) / 2.2;
                var d = dx * dx + dy * dy;
                if (d > 1)
                {
                    return 0;
                }
                var h = Rand(Math.Floor(x / 2), Math.Floor(y), s + 20);
                if (d > 0.55 && h < 0.55)
                {
                    if (h < 0.15)
                    {
                        return Ash[2];
                    }
                    if (h < 0.32)
                    {
                        return Ash[1];
                    }
                    return Ash[0];
                }
                if (h > 0.5)
                {
                    var g = (0.5 + 0.5 * Math.Sin((t / D) * Math.PI * 4 + h * 40)) * (1.1 - d);
                    o.K = 0.35 * g;
                    if (g > 0.7)
                    {
                        return hot[2];
                    }
                    if (g > 0.5)
                    {
                        return hot[3];
                    }
                    if (g > 0.3)
                    {
                        return hot[4];
                    }
                    return hot[5];
                }
                if (h > 0.3)
                {
                    return wood[1];
                }
                return wood[0];
            });
        }

        public override void Draw(PixelPainter p, double t, PixelFxOptions o)
        {
            var hot = HotFirst(Fit(o.Ramp("palette"), 9));
            var wood = Fit(o.Ramp("wood"), 6);
            var s = o.Number("seed");
            var rr = new PixelRng(s);
            var hc = o.Number("height");
            double N(double rate, double seed = 0, double off = 0) => NL(t, rate, seed, off);
            double Fla(double time) => 0.74 + 0.16 * NL(time, 7.3, s + 1) + 0.1 * NL(time, 19, s + 2);
            var fl = Fla(t);
            var glow = Mix(Mix(hot[3], hot[4], 0.5), White, 0.25);
            var smoke = Mix(Smoke1, hot[6], 0.25);
            uint[] sparks = [hot[1], hot[2], hot[4], hot[5]];
            uint[] emb = [hot[1], hot[2], hot[4], hot[6]];

            var tt = new TongueState[Tongues.Length];
            for (var i = 0; i < Tongues.Length; i++)
            {
                var (dx, y, h, w, back) = Tongues[i];
                var jx = dx + rr.Range(-0.4, 0.4);
                var jh = 3 + hc * h * rr.Range(0.92, 1.08);
                var jw = w * rr.Range(0.9, 1.1);
                tt[i] = new TongueState(jx, y, jh, jw, back, h, (s * 7 + i) * 13);
            }
            double Lean(double time) => NL(time, 0.67, s + 90) * 0.12;
            double Height(int i, double time)
            {
                var (h, n) = (tt[i].H, tt[i].N);
                var leap = Math.Max(0, NL(time, 2.6, n + 70) - 0.55) * 1.4;
                return (h + 0.25 * h * NL(time, 3.3, n) + h * leap) * (0.9 + 0.1 * Fla(time));
            }
            double Sweep(int i, double time, double h) =>
                NL(time, 2.2, tt[i].N + 40) * (1 + h * 0.08) + Lean(time) * h * 0.9 - Clamp(tt[i].Dx * 0.3, -2.5, 2.5);
            (double X, double Y) Tip(int i, double time)
            {
                var h = Height(i, time);
                return (tt[i].Dx + Sweep(i, time, h) * 0.85, tt[i].Y - h * 0.88 + 3);
            }
            void TongueAt(int i)
            {
                var q = tt[i];
                var h = Height(i, t);
                FlameTongue(p, q.Dx, q.Y, h, q.W, Sweep(i, t, h), N, q.N, hot, LayersFor(q.Back, q.W), 0.5, 1.1 + h * 0.04);
            }

            // smoke curling off the top, behind everything; the draught bends it over as it rises
            Looped(t, D, 48, s + 300, (r, _) =>
            {
                var x = r.Range(-5, 5);
                var y = -hc * 0.8 - r.Range(0, 8);
                var vx = r.Range(-2, 2);
                var vy = -r.Range(10, 17);
                var life = r.Range(2, 2.9);
                var size = r.Range(2.4, 3.8);
                return new Plume(x, y, vx, vy, life, size);
            }, (q, age, _) =>
            {
                var at = Drift(q.X, q.Y, q.Vx, q.Vy, age, 4, -20, 1.6, 1);
                SmokePuff(p, at.X + NL(t - age, 0.8, s + 60) * (2 + 3 * age), at.Y, 1 - age / q.Life, q.S, smoke);
            });
            // flame puffs shed from the tips: they burn out above the fire, some into smoke
            var puffs = new List<(double X, double Y, double K, double S, double F)>();
            Looped(t, D, tt.Length * 24, s + 200, (r, j) =>
            {
                var i = j % tt.Length;
                var fire = r.Range(0.14, 0.3);
                var smokes = r.Chance(0.45);
                var sl = r.Range(0.8, 1.4);
                var ox = r.Range(-1, 1);
                var oy = r.Range(0, 4);
                var vx = r.Range(-4, 4);
                var vy = -r.Range(15, 30);
                var size = r.Range(0.8, 1.6) * (0.8 + 0.6 * tt[i].Env);
                var f = r.Next();
                var life = fire;
                if (smokes)
                {
                    life += 1.4;
                }
                return new ShedPuff(i, fire, smokes, sl, ox, oy, vx, vy, size, f, life);
            }, (q, age, _) =>
            {
                var tip = Tip(q.I, t - age);
                var at = Drift(tip.X + q.Ox, tip.Y + q.Oy, q.Vx, q.Vy, Math.Min(age, q.Fire), 0, -95, 5, 3.2);
                if (age < q.Fire)
                {
                    puffs.Add((at.X, at.Y, 1 - age / q.Fire, q.S, q.F));
                    return;
                }
                if (age - q.Fire > q.Sl)
                {
                    return;
                }
                var sm = Drift(at.X, at.Y, at.Vx * 0.6, at.Vy * 0.6 - 6, age - q.Fire, 2, -16, 1.6, 1.2);
                SmokePuff(p, sm.X, sm.Y, 1 - (age - q.Fire) / q.Sl, q.S * 0.7, 0);
            });

            Coals(p, wood, hot, t, s);
            foreach (var log in Logs)
            {
                Log(log, wood, hot, Cross, 1, t, s + 11, p);
            }
            for (var i = 0; i < tt.Length; i++)
            {
                if (tt[i].Back)
                {
                    TongueAt(i);
                }
            }
            // flames licking up off the burning wood
            Looped(t, D, 90, s + 210, (r, _) =>
            {
                var x = r.Range(-10, 10);
                var y = -r.Range(3, 8);
                var vx = r.Range(-5, 5);
                var vy = -r.Range(20, 40);
                var life = r.Range(0.16, 0.3);
                var size = r.Range(1, 1.8);
                var f = r.Next();
                return new WoodFlame(x, y, vx, vy, life, size, f);
            }, (q, age, _) =>
            {
                var at = Drift(q.X, q.Y, q.Vx, q.Vy, age, 0, -95, 5, 3.2);
                FirePuff(p, at.X, at.Y, 1 - age / q.Life, q.S, q.F, hot);
            });
            for (var i = 0; i < tt.Length; i++)
            {
                if (!tt[i].Back)
                {
                    TongueAt(i);
                }
            }
            // licks torn off the tips, rising and shrinking
            Looped(t, D, tt.Length * 12, s + 220, (r, j) =>
            {
                var i = j % tt.Length;
                var vx = r.Range(-3, 3);
                var vy = -r.Range(12, 22);
                var size = r.Range(0.9, 1.6) * (0.8 + 0.5 * tt[i].Env);
                return new TipLick(i, vx, vy, size, 0.26);
            }, (q, age, _) =>
            {
                var tip = Tip(q.I, t - age);
                Lick(p, tip.X + q.Vx * age, tip.Y - 2 + q.Vy * age, 1 - age / q.Life, q.S, hot);
            });
            foreach (var (x, y, k, size, f) in puffs)
            {
                FirePuff(p, x, y, k, size, f, hot);
            }
            foreach (var log in Logs)
            {
                Log(log, wood, hot, 0, Cross, t, s + 11, p);
            }

            // pops: a crack, a flash, and a fistful of sparks thrown up out of the wood that bounce on the floor
            Looped(t, D, 4, s + 400, (r, _) =>
            {
                var x = r.Range(-9, 9);
                var y = -r.Range(4, 9);
                var n = r.Int(3, 5);
                var seed = r.Int(0, 1e6);
                return new Pop(x, y, n, seed, 0.7);
            }, (q, age, _) =>
            {
                if (age < 0.08)
                {
                    p.Px(q.X, q.Y, White);
                    p.Glow(q.X, q.Y, 12, hot[1], 0.7 * (1 - age / 0.08));
                }
                var r = new PixelRng(q.Seed);
                for (var j = 0; j < q.N; j++)
                {
                    var a = -Math.PI / 2 + r.Range(-0.8, 0.8);
                    var sp = r.Range(110, 220);
                    var life = r.Range(0.25, 0.6);
                    if (age >= life)
                    {
                        continue;
                    }
                    var at = SparkAt(q.X, q.Y, Math.Cos(a) * sp, Math.Sin(a) * sp, age, 0);
                    Spark(p, at.X, at.Y, at.Vx, at.Vy, 1 - age / life, sparks);
                }
            });
            // embers carried up by the heat, wandering and cooling
            Looped(t, D, 36, s + 500, (r, _) =>
            {
                var x = r.Range(-7, 7);
                var y = -r.Range(8, 18);
                var vx = r.Range(-15, 15);
                var vy = -r.Range(25, 60);
                var life = r.Range(1.1, 2.5);
                double size = 2;
                if (!r.Chance(0.15))
                {
                    size = r.Range(1, 1.6);
                }
                var a = r.Range(0, 6.3);
                var b = r.Range(0, 6.3);
                return new RisingEmber(x, y, vx, vy, life, size, a, b);
            }, (q, age, _) =>
            {
                var at = Drift(q.X, q.Y, q.Vx, q.Vy, age, 0, -30, 1.6, 1.6);
                Ember(p, at.X + 7 * (Math.Sin(age * 2.3 + q.A) - Math.Sin(q.A)), at.Y + 2 * (Math.Sin(age * 3 + q.B) - Math.Sin(q.B)), 1 - age / q.Life, q.S, emb, age * 40 + q.A);
            });

            // light: a big warm room light, a hot heart, a pool on the floor, all breathing with the flames
            var br = 0.92 + 0.08 * N(0.67, s + 3);
            var lx = Lean(t) * 12;
            p.Glow(lx, -8, 74 * br * (0.9 + 0.1 * fl), glow, 0.3 * fl);
            foreach (var (x, y, k) in new[] { (-6.0, -17.0, 0.85), (0.0, -21.0, 1.0), (6.0, -17.0, 0.85) })
            {
                p.Glow(x + lx, y, 46 * br, glow, 0.3 * k * fl);
                p.Glow(x * 0.6, y + 13, 15, hot[2], 0.16 * k * fl);
            }
            p.Glow(0, -3, 9, hot[0], 0.16 * fl);
            FloorLight(p, 0, 0.5, 48, 8, glow, 0.3 * fl);
        }
    }
}
