namespace ManaPixelFx.Core
{
    /// <summary>
    /// pixelfx's math.js: numbers, easing, seeded randomness and noise, all pure, so an effect drawn at time t looks
    /// the same every time. JavaScript's integer rules are spelled out (<see cref="I32"/>, <see cref="Round"/>) so
    /// a port draws the same pixels as the original.
    /// </summary>
    public static class PixelMath
    {
        public const double Tau = Math.PI * 2;

        /// <summary>JavaScript's ToInt32 -- what <c>x | 0</c> and every bitwise operator do to a number.</summary>
        public static int I32(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x))
            {
                return 0;
            }
            return unchecked((int)(long)x);
        }

        /// <summary>JavaScript's Math.round: halves go up, not to even.</summary>
        public static double Round(double x) => Math.Floor(x + 0.5);

        public static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

        public static double Clamp(double x, double a = 0, double b = 1)
        {
            if (x < a)
            {
                return a;
            }
            if (x > b)
            {
                return b;
            }
            return x;
        }

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static double InvLerp(double a, double b, double x)
        {
            if (b == a)
            {
                return 0;
            }
            return (x - a) / (b - a);
        }

        public static double Remap(double x, double a0, double a1, double b0, double b1) => Lerp(b0, b1, InvLerp(a0, a1, x));
        public static double Fract(double x) => x - Math.Floor(x);
        public static double Wrap(double x, double m) => ((x % m) + m) % m;

        /// <summary>Progress 0..1 of t through the window [t0, t1].</summary>
        public static double Seg(double t, double t0, double t1) => Clamp((t - t0) / (t1 - t0));

        public static double Smooth(double e0, double e1, double x)
        {
            var t = Seg(x, e0, e1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>0 to 1 to 0 over the window [t0, t1] (a sine bump).</summary>
        public static double Bump(double t, double t0, double t1)
        {
            if (t <= t0 || t >= t1)
            {
                return 0;
            }
            return Math.Sin(Math.PI * Seg(t, t0, t1));
        }

        public static double PingPong(double x) => 1 - Math.Abs(Wrap(x, 2) - 1);
        public static double Dist(double x0, double y0, double x1, double y1) => Hypot(x1 - x0, y1 - y0);
        public static double AngleTo(double x0, double y0, double x1, double y1) => Math.Atan2(y1 - y0, x1 - x0);

        // ---- hashing: murmur-style mixing of whole numbers, as Math.imul does it
        private static uint Step(uint h, double n)
        {
            h = unchecked((h ^ (uint)I32(n)) * 0x85ebca6bu);
            h ^= h >> 13;
            h = unchecked(h * 0xc2b2ae35u);
            h ^= h >> 16;
            return h;
        }

        public static uint Hash(double a) => Step(0x9e3779b9u, a);
        public static uint Hash(double a, double b) => Step(Step(0x9e3779b9u, a), b);
        public static uint Hash(double a, double b, double c) => Step(Step(Step(0x9e3779b9u, a), b), c);
        public static uint Hash(double a, double b, double c, double d) => Step(Step(Step(Step(0x9e3779b9u, a), b), c), d);

        /// <summary>A repeatable random number in [0, 1) from whole numbers (seed, particle, which value).</summary>
        public static double Rand(double a) => Hash(a) / 4294967296.0;
        public static double Rand(double a, double b) => Hash(a, b) / 4294967296.0;
        public static double Rand(double a, double b, double c) => Hash(a, b, c) / 4294967296.0;
        public static double Rand(double a, double b, double c, double d) => Hash(a, b, c, d) / 4294967296.0;

        /// <summary>A generator for one particle: the same numbers every frame.</summary>
        public static PixelRng Rng(double a) => new(Hash(a));
        public static PixelRng Rng(double a, double b) => new(Hash(a, b));
        public static PixelRng Rng(double a, double b, double c) => new(Hash(a, b, c));

        // ---- noise
        /// <summary>Smooth 1D value noise in [-1, 1].</summary>
        public static double Noise(double x, double seed = 0)
        {
            var i = Math.Floor(x);
            var f = x - i;
            var u = f * f * (3 - 2 * f);
            return Lerp(Rand(i, seed), Rand(i + 1, seed), u) * 2 - 1;
        }

        /// <summary>Smooth 2D value noise in [-1, 1].</summary>
        public static double Noise2(double x, double y, double seed = 0)
        {
            var ix = Math.Floor(x);
            var iy = Math.Floor(y);
            var fx = x - ix;
            var fy = y - iy;
            var ux = fx * fx * (3 - 2 * fx);
            var uy = fy * fy * (3 - 2 * fy);
            var a = Rand(ix, iy, seed);
            var b = Rand(ix + 1, iy, seed);
            var c = Rand(ix, iy + 1, seed);
            var d = Rand(ix + 1, iy + 1, seed);
            return Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uy) * 2 - 1;
        }

        /// <summary>Fractal 2D noise in about [-1, 1].</summary>
        public static double Fbm(double x, double y, double seed = 0, int octaves = 3)
        {
            double s = 0;
            var a = 0.5;
            double n = 0;
            for (var o = 0; o < octaves; o++)
            {
                s += Noise2(x, y, seed + o * 101) * a;
                n += a;
                x *= 2.03;
                y *= 2.03;
                a *= 0.5;
            }
            return s / n;
        }

        /// <summary>2D value noise whose lattice repeats every px cells across and py down (0: no repeat).</summary>
        public static double TileNoise(double x, double y, double px = 0, double py = 0, double seed = 0)
        {
            var ix = Math.Floor(x);
            var iy = Math.Floor(y);
            var fx = x - ix;
            var fy = y - iy;
            var ux = fx * fx * (3 - 2 * fx);
            var uy = fy * fy * (3 - 2 * fy);
            double x0 = ix;
            double x1 = ix + 1;
            double y0 = iy;
            double y1 = iy + 1;
            if (px != 0)
            {
                x0 = Wrap(ix, px);
                x1 = Wrap(ix + 1, px);
            }
            if (py != 0)
            {
                y0 = Wrap(iy, py);
                y1 = Wrap(iy + 1, py);
            }
            var a = Rand(x0, y0, seed);
            var b = Rand(x1, y0, seed);
            var c = Rand(x0, y1, seed);
            var d = Rand(x1, y1, seed);
            return Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uy) * 2 - 1;
        }

        /// <summary>Fractal <see cref="TileNoise"/>: every octave keeps the repeat, so scrolled fields still loop.</summary>
        public static double TileFbm(double x, double y, double px = 0, double py = 0, double seed = 0, int octaves = 3)
        {
            double s = 0;
            var a = 0.5;
            double n = 0;
            double k = 1;
            for (var o = 0; o < octaves; o++)
            {
                s += TileNoise(x * k, y * k, px * k, py * k, seed + o * 101) * a;
                n += a;
                a *= 0.5;
                k *= 2;
            }
            return s / n;
        }

        /// <summary>Noise that loops: the same at phase 0 and phase 1.</summary>
        public static double LoopNoise(double phase, double radius = 1, double seed = 0)
        {
            var a = phase * Tau;
            return Noise2(Math.Cos(a) * radius + 50, Math.Sin(a) * radius + 50, seed);
        }
    }

    /// <summary>Easing curves: 0..1 in, 0..1 out (back and elastic overshoot).</summary>
    public static class Ease
    {
        public static double Linear(double t) => t;
        public static double InQuad(double t) => t * t;
        public static double OutQuad(double t) => 1 - (1 - t) * (1 - t);

        public static double InOutQuad(double t)
        {
            if (t < 0.5)
            {
                return 2 * t * t;
            }
            return 1 - Math.Pow(-2 * t + 2, 2) / 2;
        }

        public static double InCubic(double t) => t * t * t;
        public static double OutCubic(double t) => 1 - Math.Pow(1 - t, 3);

        public static double InOutCubic(double t)
        {
            if (t < 0.5)
            {
                return 4 * t * t * t;
            }
            return 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }

        public static double InQuart(double t) => t * t * t * t;
        public static double OutQuart(double t) => 1 - Math.Pow(1 - t, 4);

        public static double InExpo(double t)
        {
            if (t <= 0)
            {
                return 0;
            }
            return Math.Pow(2, 10 * t - 10);
        }

        public static double OutExpo(double t)
        {
            if (t >= 1)
            {
                return 1;
            }
            return 1 - Math.Pow(2, -10 * t);
        }

        public static double InSine(double t) => 1 - Math.Cos((t * Math.PI) / 2);
        public static double OutSine(double t) => Math.Sin((t * Math.PI) / 2);
        public static double InOutSine(double t) => -(Math.Cos(Math.PI * t) - 1) / 2;

        public static double InBack(double t, double s = 1.70158)
        {
            if (t <= 0)
            {
                return 0;
            }
            if (t >= 1)
            {
                return 1;
            }
            return (s + 1) * t * t * t - s * t * t;
        }

        public static double OutBack(double t, double s = 1.70158)
        {
            if (t <= 0)
            {
                return 0;
            }
            if (t >= 1)
            {
                return 1;
            }
            return 1 + (s + 1) * Math.Pow(t - 1, 3) + s * Math.Pow(t - 1, 2);
        }

        public static double OutElastic(double t)
        {
            if (t <= 0)
            {
                return 0;
            }
            if (t >= 1)
            {
                return 1;
            }
            return Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * ((2 * Math.PI) / 3)) + 1;
        }

        public static double OutBounce(double t)
        {
            const double n = 7.5625;
            const double d = 2.75;
            if (t < 1 / d)
            {
                return n * t * t;
            }
            if (t < 2 / d)
            {
                t -= 1.5 / d;
                return n * t * t + 0.75;
            }
            if (t < 2.5 / d)
            {
                t -= 2.25 / d;
                return n * t * t + 0.9375;
            }
            t -= 2.625 / d;
            return n * t * t + 0.984375;
        }
    }

    /// <summary>pixelfx's seeded generator (mulberry32), bit for bit.</summary>
    public sealed class PixelRng
    {
        private uint _s;

        public PixelRng(uint seed)
        {
            _s = seed;
            if (_s == 0)
            {
                _s = 1;
            }
        }

        public PixelRng(double seed = 1) : this(unchecked((uint)PixelMath.I32(seed)))
        {
        }

        public double Next()
        {
            unchecked
            {
                _s += 0x6d2b79f5u;
                var t = _s;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public double Range(double a, double b) => a + (b - a) * Next();
        public double Int(double a, double b) => Math.Floor(Range(a, b + 1));
        public bool Chance(double p) => Next() < p;

        public double Sign()
        {
            if (Next() < 0.5)
            {
                return -1;
            }
            return 1;
        }

        public T Pick<T>(IReadOnlyList<T> list) => list[(int)Math.Floor(Next() * list.Count)];

        /// <summary>Roughly normal, mean 0, deviation 1.</summary>
        public double Gauss() => (Next() + Next() + Next() + Next() - 2) * 1.732;
    }
}
