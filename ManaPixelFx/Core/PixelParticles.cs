using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Core
{
    /// <summary>
    /// pixelfx's particles.js: particles without a simulation. Each one's place is a closed-form function of its
    /// age, so an effect can be drawn at any time t, scrubbed and looped.
    /// </summary>
    public static class PixelParticles
    {
        /// <summary>Where a particle is t seconds after launch from (x, y) at (vx, vy) px/s, under gravity g and linear drag.</summary>
        public static (double X, double Y) Fly(double x, double y, double vx, double vy, double t, double g = 0, double drag = 0)
        {
            if (drag <= 0)
            {
                return (x + vx * t, y + vy * t + 0.5 * g * t * t);
            }
            var f = (1 - Math.Exp(-drag * t)) / drag;
            var vt = g / drag;
            return (x + vx * f, y + vt * t + (vy - vt) * f);
        }

        /// <summary>Its velocity at t, for streaks that point along the motion.</summary>
        public static (double Vx, double Vy) FlyV(double vx, double vy, double t, double g = 0, double drag = 0)
        {
            if (drag <= 0)
            {
                return (vx, vy + g * t);
            }
            var e = Math.Exp(-drag * t);
            var vt = g / drag;
            return (vx * e, vt + (vy - vt) * e);
        }

        /// <summary>n one-shot particles, each with a repeatable rng of its own.</summary>
        public static void Burst(int n, double seed, Action<PixelRng, int> fn)
        {
            for (var i = 0; i < n; i++)
            {
                fn(Rng(seed, i), i);
            }
        }

        /// <summary>
        /// Particles that repeat every period, for loops with no seam: particle i is born at its own phase and lives
        /// <paramref name="life"/>; fn(age, life, rng, i) runs for every live copy.
        /// </summary>
        public static void Cycle(double t, double period, int count, double seed, Func<PixelRng, double> life, Action<double, double, PixelRng, int> fn)
        {
            for (var i = 0; i < count; i++)
            {
                var g = Rng(seed, i, 911);
                var born = g.Next() * period;
                var l = life(g);
                var age = (((t - born) % period) + period) % period;
                for (; age < l; age += period)
                {
                    fn(age, l, Rng(seed, i), i);
                }
            }
        }

        public static void Cycle(double t, double period, int count, double seed, double life, Action<double, double, PixelRng, int> fn) =>
            Cycle(t, period, count, seed, _ => life, fn);

        /// <summary>Particles born at a steady rate between t0 and t1: fn(age, life, rng, i) for every one alive at t.</summary>
        public static void Stream(double t, double t0, double t1, double rate, double seed, Func<PixelRng, double> life, Action<double, double, PixelRng, int> fn)
        {
            var n = Math.Floor((Math.Min(t, t1) - t0) * rate);
            for (var i = 0; i <= n; i++)
            {
                var born = t0 + i / rate;
                var g = Rng(seed, i, 911);
                var l = life(g);
                var age = t - born;
                if (age >= 0 && age < l)
                {
                    fn(age, l, Rng(seed, i), i);
                }
            }
        }

        public static void Stream(double t, double t0, double t1, double rate, double seed, double life, Action<double, double, PixelRng, int> fn) =>
            Stream(t, t0, t1, rate, seed, _ => life, fn);
    }
}
