using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Effects.Fire
{
    /// <summary>A loop's noise: wobbles about <c>rate</c> times a second and is the same at tt and tt + D.</summary>
    public delegate double TimeNoise(double tt, double rate, double seed = 0, double off = 0);

    /// <summary>The same noise at this frame's time.</summary>
    public delegate double FrameNoise(double rate, double seed = 0, double off = 0);

    /// <summary>A looped particle: anything with a life, which may be longer than the loop.</summary>
    public interface ILooped
    {
        double Life { get; }
    }

    /// <summary>Tongue layers: width share, height share, step of the hot-first ramp.</summary>
    public readonly record struct FlameLayer(double W, double H, int Ri);

    /// <summary>One tongue of a <see cref="FireLib.Burn"/> fire: x and y from the base, height share, half-width, back row.</summary>
    public readonly record struct Tongue(double Dx, double Dy, double H, double W, bool Back);

    /// <summary>A <see cref="FireLib.Burn"/> fire's settings; unset ones take pixelfx's defaults.</summary>
    public sealed class BurnOptions
    {
        public required uint[] H { get; init; }
        public required double D { get; init; }
        public required TimeNoise NL { get; init; }
        public required double S { get; init; }
        public required double Height { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Lean { get; init; } = 0.1;
        public double Spread { get; init; } = 6;
        public double Puffs { get; init; } = 8;
        public double Licks { get; init; } = 4;
        public double Smoke { get; init; } = 12;
        public double SmokeSize { get; init; } = 3;
        public (double Min, double Max) SmokeLife { get; init; } = (1.6, 2.6);
        public double Embers { get; init; } = 8;
        public double GlowR { get; init; } = 46;
        public double Glow { get; init; } = 1;
        public double Wind { get; init; } = 3;

        /// <summary>Called after the tongues, before what flies off them: what the fire burns out of.</summary>
        public Action<double>? Mid { get; init; }
    }

    /// <summary>
    /// pixelfx's fire/_lib.js: flame tongues, licks, flame puffs that cool into smoke, embers and sparks, with their
    /// motion in closed form so loops draw from t alone. The tongue, lick, puff, smoke, ember and spark recipes are
    /// adapted from Sprite Fusion's open-source fire (https://www.spritefusion.com/pixel-art-vfx/fire), MIT License,
    /// Copyright (c) 2026 Hugo Duprez.
    /// </summary>
    public static class FireLib
    {
        /// <summary>Red rim, orange, yellow, white heart.</summary>
        public static readonly FlameLayer[] Layers = [new(1, 1, 6), new(0.76, 0.86, 4), new(0.54, 0.68, 2), new(0.3, 0.42, 0)];

        /// <summary>For big tongues: more orange and a smaller white heart, so the light does not wash them out.</summary>
        public static readonly FlameLayer[] BigLayers = [new(1, 1, 6), new(0.8, 0.9, 5), new(0.62, 0.78, 4), new(0.44, 0.6, 3), new(0.26, 0.4, 1), new(0.12, 0.22, 0)];

        /// <summary>A step cooler, for flames further back.</summary>
        public static readonly FlameLayer[] BackLayers = [new(1, 1, 7), new(0.76, 0.86, 5), new(0.54, 0.68, 3), new(0.3, 0.42, 2)];

        private static readonly uint Smoke1 = Hex("#5b5566");

        /// <summary>Smooth value noise in [-1, 1] that repeats every <paramref name="period"/> cells.</summary>
        public static double PNoise(double x, double period, double seed = 0)
        {
            var i = Math.Floor(x);
            var f = x - i;
            var u = f * f * (3 - 2 * f);
            var a = Rand(Wrap(i, period), seed);
            var b = Rand(Wrap(i + 1, period), seed);
            return (a + (b - a) * u) * 2 - 1;
        }

        /// <summary>Noise for a loop D seconds long; D = 0 for one-shots (plain noise).</summary>
        public static TimeNoise NoiseFor(double d)
        {
            if (d == 0)
            {
                return (tt, rate, seed, off) => Noise(tt * rate + off, seed);
            }
            return (tt, rate, seed, off) =>
            {
                var period = Math.Max(1, Round(rate * d));
                return PNoise((tt / d) * period + off, period, seed);
            };
        }

        /// <summary>Seamless particles for a loop D long: count of them, each born at its own phase and reborn every D.</summary>
        public static void Looped<T>(double t, double d, double count, double seed, Func<PixelRng, int, T> make, Action<T, double, int> draw) where T : ILooped
        {
            for (var i = 0; i < count; i++)
            {
                var r = Rng(seed, i);
                var born = r.Next() * d;
                var q = make(r, i);
                for (var age = Wrap(t - born, d); age < q.Life; age += d)
                {
                    draw(q, age, i);
                }
            }
        }

        /// <summary>A fire ramp hot first (white .. smouldering) from a palette dark to bright.</summary>
        public static uint[] HotFirst(uint[] f) => f.Reverse().ToArray();

        public static FlameLayer[] LayersFor(bool back, double w)
        {
            if (back)
            {
                return BackLayers;
            }
            if (w > 2.2)
            {
                return BigLayers;
            }
            return Layers;
        }

        /// <summary>
        /// A flame tongue standing at (x, y), h tall, w its half-width at the base, its tip swept sx sideways: a red rim
        /// round orange round yellow round a white heart, every row's width and centre jittered so the edges crackle.
        /// </summary>
        public static void FlameTongue(PixelPainter p, double x, double y, double h, double w, double sx, FrameNoise n, double seed, uint[] hot, FlameLayer[]? layers = null, double jit = 0.5, double wob = 1.1)
        {
            if (h < 1)
            {
                return;
            }
            foreach (var (lw, lh, ri) in layers ?? Layers)
            {
                var l = h * lh;
                var c = hot[ri];
                for (double r = 0; r <= l; r++)
                {
                    var u = r / Math.Max(1, l);
                    var half = w * lw * Math.Pow(1 - u, 0.85) * Math.Min(1, (r + 1.5) / 3.5) + n(9, seed, r * 0.45) * jit;
                    if (half < 0.3)
                    {
                        continue;
                    }
                    var cx = x + sx * u * u * lh + n(4, seed + 7, r * 0.2) * u * wob;
                    var rowY = Round(y - r);
                    for (double px = Round(cx - half), x1 = Round(cx + half); px <= x1; px++)
                    {
                        p.Px(px, rowY, c);
                    }
                }
            }
        }

        /// <summary>The same tongue lying along any direction (ux, uy) from its root: jets, breath, comet tails.</summary>
        public static void FlameBlade(PixelPainter p, double x, double y, double ux, double uy, double len, double w, double bend, FrameNoise n, double seed, uint[] hot, FlameLayer[]? layers = null, double jit = 0.5, double wob = 1.1)
        {
            if (len < 1)
            {
                return;
            }
            var nx = -uy;
            var ny = ux;
            foreach (var (lw, lh, ri) in layers ?? Layers)
            {
                var l = len * lh;
                var c = hot[ri];
                for (double r = 0; r <= l; r += 0.5)
                {
                    var u = r / Math.Max(1, l);
                    var half = w * lw * Math.Pow(1 - u, 0.85) * Math.Min(1, (r + 1.5) / 3.5) + n(9, seed, r * 0.45) * jit;
                    if (half < 0.3)
                    {
                        continue;
                    }
                    var off = bend * u * u * lh + n(4, seed + 7, r * 0.2) * u * wob;
                    var cx = x + ux * r + nx * off;
                    var cy = y + uy * r + ny * off;
                    for (var q = -half; q <= half; q += 0.5)
                    {
                        p.Px(cx + nx * q, cy + ny * q, c);
                    }
                }
            }
        }

        /// <summary>A solid disc drawn straight in screen space (fast; a disc looks the same mirrored or turned).</summary>
        public static void Solid(PixelPainter p, double x, double y, double r, uint c)
        {
            if (r < 0.6)
            {
                p.Px(x, y, c);
                return;
            }
            var cx = p.ToX(x, y);
            var cy = p.ToY(x, y);
            var r2 = r * r;
            for (var sy = Math.Floor(cy - r); sy <= Math.Ceiling(cy + r); sy++)
            {
                var dy = sy + 0.5 - cy;
                var s2 = r2 - dy * dy;
                if (s2 < 0)
                {
                    continue;
                }
                var s = Math.Sqrt(s2);
                for (var sx = Math.Ceiling(cx - s - 0.5); sx <= Math.Floor(cx + s - 0.5); sx++)
                {
                    p.Put((int)sx, (int)sy, c, 1);
                }
            }
        }

        /// <summary>A translucent disc in screen space: smoke and haze.</summary>
        public static void Soft(PixelPainter p, double x, double y, double r, uint c, double a)
        {
            if (a <= 0.004)
            {
                return;
            }
            if (r < 0.5)
            {
                p.Blend(x, y, c, a);
                return;
            }
            var cx = p.ToX(x, y);
            var cy = p.ToY(x, y);
            var r2 = r * r;
            for (var sy = Math.Floor(cy - r); sy <= Math.Ceiling(cy + r); sy++)
            {
                var dy = sy + 0.5 - cy;
                var s2 = r2 - dy * dy;
                if (s2 < 0)
                {
                    continue;
                }
                var s = Math.Sqrt(s2);
                for (var sx = Math.Ceiling(cx - s - 0.5); sx <= Math.Floor(cx + s - 0.5); sx++)
                {
                    p.BlendPx((int)sx, (int)sy, c, a);
                }
            }
        }

        /// <summary>
        /// A ball of flame with life left k cooling down the hot-first ramp as it ages: a darker rim, a lit core up and
        /// to the left, a little tongue on top, a hot heart while young, and its light.
        /// </summary>
        public static void FirePuff(PixelPainter p, double x, double y, double k, double size, double frac, uint[] hot, double glowK = 0.18, double tx = 0, double ty = -1)
        {
            var age = 1 - k;
            var n = hot.Length;
            var heat = Math.Min(0.999, Math.Pow(age, 0.62) + frac * 0.1);
            var idx = (int)Math.Min(n - 2, Math.Floor(heat * (n - 1)));
            var r = size * (0.55 + 0.45 * Math.Min(1, age * 7)) * (1 - age * 0.55);
            var c = hot[idx];
            if (idx >= n - 2)
            {
                Soft(p, x, y, r, c, Math.Min(1, k * 3) * 0.85);
                return;
            }
            Solid(p, x, y, r, hot[Math.Min(n - 1, idx + 2)]);
            if (r > 1.6 && ty > -0.99)
            {
                Solid(p, x + tx * r * 1.5, y + ty * r * 1.5, r * 0.3, hot[Math.Min(n - 1, idx + 1)]);
            }
            Solid(p, x - r * 0.16, y - r * 0.22, r * 0.78, c);
            if (r > 1.6)
            {
                Solid(p, x + tx * r * 0.9, y + ty * r * 0.9, r * 0.42, c);
            }
            if (idx <= 3 && r > 1.8)
            {
                Solid(p, x - r * 0.28, y - r * 0.34, r * 0.36, hot[Math.Max(0, idx - 1)]);
            }
            if (idx < 6)
            {
                p.Glow(x, y, r * 2.4 + 2, c, glowK * k);
            }
        }

        /// <summary>A lick: a blob of flame torn off a tongue's tip, shrinking as it rises.</summary>
        public static void Lick(PixelPainter p, double x, double y, double k, double s, uint[] hot)
        {
            var r = s * (0.4 + 0.6 * k);
            var rim = hot[6];
            if (k > 0.5)
            {
                rim = hot[5];
            }
            Solid(p, x, y, r + 0.6, rim);
            if (k > 0.35)
            {
                var heart = hot[4];
                if (k > 0.7)
                {
                    heart = hot[2];
                }
                Solid(p, x, y - 0.3, r * 0.6, heart);
            }
        }

        private static uint RampAt(uint[] r, double t) => r[(int)Math.Min(r.Length - 1, Math.Max(0, Math.Floor(t * r.Length)))];

        /// <summary>A puff of smoke with life left k: a body, a denser core sitting low, a lit cap while young.</summary>
        public static void SmokePuff(PixelPainter p, double x, double y, double k, double size, uint col = 0, bool big = false, double aMul = 1)
        {
            var age = 1 - k;
            var grow = 0.4;
            var thick = 0.58;
            if (big)
            {
                grow = 0.5;
                thick = 0.7;
            }
            var r = size * (0.65 + Math.Min(1, age * 2.5) * 0.45 + age * grow);
            var a = Math.Min(1, age * 12) * Math.Min(1, k * 2.4) * thick * aMul;
            var c = RampAt(SmokeRamp, age * 1.05);
            if (col != 0)
            {
                c = Mix(col, RampAt(SmokeRamp, age), age);
            }
            Soft(p, x, y, r, c, a * 0.45);
            Soft(p, x + r * 0.05, y + r * 0.06, r * 0.74, c, a * 0.62);
            if (age < 0.5)
            {
                Soft(p, x - r * 0.22, y - r * 0.3, r * 0.5, Mix(c, White, 0.12), a * 0.4 * (1 - age * 2));
            }
        }

        /// <summary>An ember with life left k: a hot pixel cooling through cols [hot, warm, cool, rim], flickering out.</summary>
        public static void Ember(PixelPainter p, double x, double y, double k, double size, uint[] cols, double ph)
        {
            if (k < 0.25 && Math.Sin(ph) < 0)
            {
                return;
            }
            var c = cols[2];
            if (k > 0.6)
            {
                c = cols[0];
            }
            else if (k > 0.3)
            {
                c = cols[1];
            }
            if (size >= 2)
            {
                var rim = cols[3];
                if (k > 0.45)
                {
                    rim = cols[2];
                }
                p.Px(x - 1, y, rim);
                p.Px(x + 1, y, rim);
                p.Px(x, y - 1, rim);
                p.Px(x, y + 1, rim);
                p.Px(x, y, c);
                p.Glow(x, y, 8, c, 0.4 * k);
                return;
            }
            p.Px(x, y, c);
            if (size > 1.4)
            {
                p.Px(x, y + 1, cols[3]);
            }
            p.Glow(x, y, 3 + size, c, 0.35 * k);
        }

        /// <summary>A spark: a streak along its velocity, a hot head and a cooler tail, and a point of light.</summary>
        public static void Spark(PixelPainter p, double x, double y, double vx, double vy, double k, uint[] cols)
        {
            var sp = Hypot(vx, vy);
            if (sp == 0)
            {
                sp = 1;
            }
            var ux = vx / sp;
            var uy = vy / sp;
            var len = Math.Min(9, 1 + sp * 0.022);
            var head = cols[2];
            if (k > 0.6)
            {
                head = cols[0];
            }
            else if (k > 0.3)
            {
                head = cols[1];
            }
            var tail = cols[^1];
            if (k > 0.5)
            {
                tail = head;
            }
            p.Line(x - ux * len, y - uy * len, x - ux * len * 0.4, y - uy * len * 0.4, tail);
            p.Line(x - ux * len * 0.45, y - uy * len * 0.45, x, y, head);
            p.Glow(x, y, 3, head, 0.45 * k);
        }

        private static (double X, double V) Along(double x, double v, double a, double k, double t)
        {
            if (k <= 0)
            {
                return (x + v * t + 0.5 * a * t * t, v + a * t);
            }
            var e = Math.Exp(-k * t);
            var vt = a / k;
            return (x + vt * t + ((v - vt) * (1 - e)) / k, vt + (v - vt) * e);
        }

        /// <summary>Where a body is t seconds after launch, under acceleration (ax, ay) and linear drag (kx, ky).</summary>
        public static (double X, double Y, double Vx, double Vy) Drift(double x, double y, double vx, double vy, double t, double ax, double ay, double kx, double ky)
        {
            var (px, pvx) = Along(x, vx, ax, kx, t);
            var (py, pvy) = Along(y, vy, ay, ky, t);
            return (px, py, pvx, pvy);
        }

        /// <summary>A spark's flight: falls, drags, and bounces off the floor, losing speed each time.</summary>
        public static (double X, double Y, double Vx, double Vy) SparkAt(double x, double y, double vx, double vy, double t, double floor = 0, double g = 427, double k = 2.2)
        {
            var s = (X: x, Y: y, Vx: vx, Vy: vy);
            var left = t;
            for (var b = 0; b < 3; b++)
            {
                var e = Drift(s.X, s.Y, s.Vx, s.Vy, left, 0, g, k, k);
                if (e.Y < floor || s.Y >= floor)
                {
                    return e;
                }
                double lo = 0;
                var hi = left;
                for (var i = 0; i < 16; i++)
                {
                    var m = (lo + hi) / 2;
                    if (Drift(s.X, s.Y, s.Vx, s.Vy, m, 0, g, k, k).Y < floor)
                    {
                        lo = m;
                    }
                    else
                    {
                        hi = m;
                    }
                }
                var c = Drift(s.X, s.Y, s.Vx, s.Vy, hi, 0, g, k, k);
                s = (c.X, floor - 0.25, c.Vx * 0.7, -Math.Abs(c.Vy) * 0.45);
                left -= hi;
            }
            var end = Drift(s.X, s.Y, s.Vx, s.Vy, left, 0, g, k, k);
            if (end.Y < floor)
            {
                return end;
            }
            return (end.X, floor - 0.25, end.Vx, 0);
        }

        /// <summary>A pool of light on the floor: an ellipse of glow, k at its middle falling to 0 at its rim.</summary>
        public static void FloorLight(PixelPainter p, double x, double y, double rx, double ry, uint c, double k)
        {
            if (k <= 0.004 || rx < 1 || ry < 0.5)
            {
                return;
            }
            p.Shade(x - rx, y - ry, x + rx, y + ry, (px, py, o) =>
            {
                var dx = (px - x) / rx;
                var dy = (py - y) / ry;
                var d = dx * dx + dy * dy;
                if (d >= 1)
                {
                    return 0;
                }
                var f = 1 - Math.Sqrt(d);
                o.L = c;
                o.K = f * f * k;
                return 0;
            });
        }

        private readonly record struct TongueState(double Dx, double Y, double H, double W, bool Back, double Env, double N);

        private readonly record struct PlumePuff(double X, double Y, double Vx, double Vy, double Life, double S) : ILooped;

        private readonly record struct ShedPuff(int I, double Fire, bool Smokes, double Sl, double Ox, double Oy, double Vx, double Vy, double S, double F, double Life) : ILooped;

        private readonly record struct TipLick(int I, double Vx, double Vy, double S, double Life) : ILooped;

        private readonly record struct RisingEmber(double X, double Y, double Vx, double Vy, double Life, double S, double A, double B) : ILooped;

        /// <summary>
        /// A fire for loops, drawn from t alone: tongues from the base (back row first), licks torn off their tips, flame
        /// puffs that burn out into smoke, a plume curling off with the draught, embers, and the fire's light.
        /// </summary>
        public static void Burn(PixelPainter p, double t, IReadOnlyList<Tongue> tongues, BurnOptions o)
        {
            var hot = o.H;
            var d = o.D;
            var nl = o.NL;
            var s = o.S;
            var hc = o.Height;
            var bx = o.X;
            var by = o.Y;
            var spread = o.Spread;
            var g = o.Glow;
            double N(double rate, double seed = 0, double off = 0) => nl(t, rate, seed, off);
            double Fla(double tt) => 0.74 + 0.16 * nl(tt, 7.3, s + 1) + 0.1 * nl(tt, 19, s + 2);
            var fl = Fla(t);
            var smoke = Mix(Smoke1, hot[6], 0.25);
            uint[] emb = [hot[1], hot[2], hot[4], hot[6]];
            var glow = Mix(Mix(hot[3], hot[4], 0.5), White, 0.25);
            var rr = Rng(s, 77);
            double Count(double rate) => Round(rate * d);
            var tt = new TongueState[tongues.Count];
            for (var i = 0; i < tongues.Count; i++)
            {
                var (dx, dy, h, w, back) = tongues[i];
                var jx = dx + rr.Range(-0.3, 0.3);
                var jh = 2 + hc * h * rr.Range(0.92, 1.08);
                var jw = w * rr.Range(0.92, 1.08);
                tt[i] = new TongueState(jx, dy, jh, jw, back, h, (s * 7 + i) * 13);
            }
            double Lean(double time) => nl(time, 0.67, s + 90) * o.Lean;
            double Height(int i, double time)
            {
                var (h, n) = (tt[i].H, tt[i].N);
                var leap = Math.Max(0, nl(time, 2.6, n + 70) - 0.55) * 1.4;
                return (h + 0.25 * h * nl(time, 3.3, n) + h * leap) * (0.9 + 0.1 * Fla(time));
            }
            double Sweep(int i, double time, double h) =>
                nl(time, 2.2, tt[i].N + 40) * (1 + h * 0.08) + Lean(time) * h * 0.9 - Clamp(tt[i].Dx * 0.3, -2.5, 2.5);
            (double X, double Y) Tip(int i, double time)
            {
                var h = Height(i, time);
                return (bx + tt[i].Dx + Sweep(i, time, h) * 0.85, by + tt[i].Y - h * 0.88 + 3);
            }
            void TongueAt(int i)
            {
                var q = tt[i];
                var h = Height(i, t);
                FlameTongue(p, bx + q.Dx, by + q.Y, h, q.W, Sweep(i, t, h), N, q.N, hot, LayersFor(q.Back, q.W), 0.5, 1.1 + h * 0.04);
            }

            // the plume: each puff keeps the sway the plume had when it left, so the wiggle climbs it
            Looped(t, d, Count(o.Smoke), s + 300, (r, _) =>
            {
                var x = r.Range(-spread, spread) * 0.7;
                var y = -hc * 0.8 - r.Range(0, 6);
                var vx = r.Range(-2, 2);
                var vy = -r.Range(10, 17);
                var life = r.Range(o.SmokeLife.Min, o.SmokeLife.Max);
                var size = r.Range(0.7, 1.1) * o.SmokeSize;
                return new PlumePuff(x, y, vx, vy, life, size);
            }, (q, age, _) =>
            {
                var at = Drift(q.X, q.Y, q.Vx, q.Vy, age, o.Wind, -20, 1.6, 1);
                SmokePuff(p, bx + at.X + nl(t - age, 0.8, s + 60) * (1.5 + 2.5 * age), by + at.Y, 1 - age / q.Life, q.S, smoke);
            });
            // flame puffs shed from the tips
            var puffs = new List<(double X, double Y, double K, double S, double F)>();
            Looped(t, d, tongues.Count * Count(o.Puffs), s + 200, (r, j) =>
            {
                var i = j % tongues.Count;
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
                var sm = Drift(at.X, at.Y, at.Vx * 0.6, at.Vy * 0.6 - 6, age - q.Fire, o.Wind, -16, 1.6, 1.2);
                SmokePuff(p, sm.X, sm.Y, 1 - (age - q.Fire) / q.Sl, q.S * 0.7, 0);
            });
            for (var i = 0; i < tt.Length; i++)
            {
                if (tt[i].Back)
                {
                    TongueAt(i);
                }
            }
            for (var i = 0; i < tt.Length; i++)
            {
                if (!tt[i].Back)
                {
                    TongueAt(i);
                }
            }
            o.Mid?.Invoke(fl);
            // licks torn off the tips, rising and shrinking
            Looped(t, d, tongues.Count * Count(o.Licks), s + 220, (r, j) =>
            {
                var i = j % tongues.Count;
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
            // embers carried up by the heat, wandering and cooling
            Looped(t, d, Count(o.Embers), s + 500, (r, _) =>
            {
                var x = r.Range(-spread, spread);
                var y = -r.Range(hc * 0.3, hc * 0.8);
                var vx = r.Range(-15, 15);
                var vy = -r.Range(25, 60);
                var life = r.Range(1.1, 2.4);
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
                Ember(p, bx + at.X + 7 * (Math.Sin(age * 2.3 + q.A) - Math.Sin(q.A)), by + at.Y + 2 * (Math.Sin(age * 3 + q.B) - Math.Sin(q.B)), 1 - age / q.Life, q.S, emb, age * 40 + q.A);
            });
            // light: a broad warm wash, lights on the flames, a hot heart at the base
            if (g > 0)
            {
                var r1 = o.GlowR;
                var lx = bx + Lean(t) * 12;
                p.Glow(lx, by - hc * 0.35, r1 * 1.5, glow, 0.22 * fl * g);
                p.Glow(lx, by - hc * 0.75, r1, glow, 0.34 * fl * g);
                p.Glow(bx, by - hc * 0.2, r1 * 0.32, hot[2], 0.2 * fl * g);
                p.Glow(bx, by - 2, 8, hot[0], 0.14 * fl * g);
            }
        }
    }
}
