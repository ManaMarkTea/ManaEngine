using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Projectiles.ProjectilesLib;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/flare.js: a signal flare streaks up off the ground trailing sparks and smoke, bursts, and
    /// hangs as a pulsing red star that drips sparks under its own red-lit smoke, then gutters out. The spark, star and
    /// light recipes are adapted from Sprite Fusion's open-source explosion and sparkle, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class SignalFlare : PixelEffect
    {
        private const double RiseTime = 0.85;
        private const double Burn = 3.1;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/flare",
            Name = "Signal flare",
            Description = "A signal flare streaks up off the ground trailing sparks and smoke, bursts, and hangs as a pulsing red star that drips sparks under its own red-lit smoke, then gutters out.",
            Size = (152, 232),
            Origin = (76, 226),
            Duration = 5,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#2a0610", "#5c0d1c", "#9a1428", "#d8243a", "#ff4d5e", "#ff8a94", "#ffd0d4", "#ffffff" },
                ["smoke"] = new[] { "#1e1a26", "#2e2938", "#433d4d", "#5b5466", "#77707f", "#958e9c", "#b3acb9" },
                ["height"] = 150.0,
                ["seed"] = 2.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly uint[] _sm;
            private readonly uint[] _wr;
            private readonly double _h;
            private readonly PixelRng _r;
            private readonly Particles _ps;
            private readonly Smoke _smoke;
            private readonly Lights _lights = new();
            private double _t;
            private bool _burst;
            private double _emit;
            private double _drip;

            public Run(PixelFxOptions o)
            {
                _c = Fit(o.Ramp("palette"), 8);
                _sm = Fit(o.Ramp("smoke"), 7);
                var c = _c;
                var sm = _sm;
                _h = o.Number("height");
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [White, c[6], c[4], c[2]]);
                _smoke = new Smoke(r);
                _wr = [c[0], c[1], Mix(c[2], sm[2], 0.45), Mix(c[3], sm[3], 0.35), c[4], c[5], c[6]];
                _lights.Add(0, -3, 26, c[4], 0.8, 0.25);
                for (var i = 0; i < 5; i++)
                {
                    _smoke.Add(new SmokePuff { X = r.Range(-3, 3), Y = -2, Vx = r.Range(-25, 25), Vy = -r.Range(5, 20), R0 = 2, R1 = r.Range(4, 6), Life = r.Range(1, 1.6), A = 0.5, Warm = 0.8, Rise = 6 });
                }
            }

            // the rise: fast off the ground, slowing to the top; then the star hangs, sways and sinks slowly
            private (double X, double Y) Rise(double t) => (Math.Sin(t * 3) * 2 * (t / RiseTime), -_h * Ease.OutCubic(Clamp(t / RiseTime)));

            private (double X, double Y) Star(double t)
            {
                var u = t - RiseTime;
                return (Math.Sin(u * 1.6) * 3 + Rise(RiseTime).X, -_h + u * 9 + Math.Sin(u * 2.3) * 1.5);
            }

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                var c = _c;
                _t += dt;
                var t = _t;
                if (t < RiseTime)
                {
                    var (x, y) = Rise(t);
                    var (px, py) = Rise(Math.Max(0, t - dt));
                    for (_emit += Hypot(x - px, y - py) / 1.5; _emit > 1; _emit--)
                    {
                        _smoke.Add(new SmokePuff { X = x + r.Range(-0.6, 0.6), Y = y + 4, Vx = r.Range(-6, 6), Vy = r.Range(0, 8), R0 = 1.3, R1 = r.Range(3, 4.8), Life = r.Range(1.4, 2.1), A = 0.5, B = 0.95, Warm = 0.9, Rise = 3, Drag = 2, Grow = 1.4 });
                    }
                    if (r.Chance(0.5))
                    {
                        ps.Spawn(P.Spark, x, y + 3, r.Range(-40, 40), r.Range(20, 90), r.Range(0.2, 0.45));
                    }
                }
                else if (!_burst)
                {
                    _burst = true;
                    var (x, y) = Star(t);
                    _lights.Add(x, y, 64, c[4], 0.9, 0.5);
                    _lights.Add(x, y, 24, White, 0.8, 0.12);
                    for (var i = 0; i < 30; i++)
                    {
                        var a = r.Range(0, Tau);
                        var sp = r.Range(70, 190);
                        ps.Spawn(P.Spark, x, y, Math.Cos(a) * sp, Math.Sin(a) * sp - 30, r.Range(0.25, 0.7));
                    }
                    for (var i = 0; i < 10; i++)
                    {
                        _smoke.Add(new SmokePuff { X = x + r.Range(-3, 3), Y = y + r.Range(-3, 3), Vx = r.Range(-40, 40), Vy = r.Range(-40, 10), R0 = 2.5, R1 = r.Range(6, 9), Life = r.Range(1.6, 2.4), A = 0.55, Warm = 1, Rise = 10, Drag = 2.5 });
                    }
                }
                else if (t < RiseTime + Burn)
                {
                    // the star burns: sparks drip off it, smoke boils up off it into a plume it lights from below
                    var (x, y) = Star(t);
                    var k = (t - RiseTime) / Burn;
                    var power = 1 - Smooth(0.75, 1, k);
                    for (_drip += dt * 26 * power; _drip > 1; _drip--)
                    {
                        ps.Spawn(P.Spark, x + r.Range(-1.5, 1.5), y + 2, r.Range(-30, 30), r.Range(10, 60), r.Range(0.35, 0.8));
                    }
                    if (r.Chance(dt * 18 * power))
                    {
                        _smoke.Add(new SmokePuff { X = x + r.Range(-1.5, 1.5), Y = y - 2, Vx = r.Range(-8, 8), Vy = -r.Range(15, 30), R0 = 2, R1 = r.Range(6, 10), Life = r.Range(1.6, 2.4) * (1 - 0.4 * k), A = 0.5, B = 0.85, Warm = 1, Rise = 14, Drag = 1.6, Grow = 1.2 });
                    }
                }
                ps.Update(dt);
                _smoke.Update(dt);
                _lights.Update(dt);
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                // the smoke lit red by the star, more the closer and the brighter it burns
                var list = _smoke.Puffs();
                double lit = 0;
                if (t > RiseTime && t < RiseTime + Burn)
                {
                    lit = 1 - Smooth(0.75, 1, (t - RiseTime) / Burn);
                }
                if (lit > 0)
                {
                    var (sx, sy) = Star(t);
                    foreach (var q in list)
                    {
                        q.W = Math.Max(q.W, lit * Math.Exp(-Hypot(q.X - sx, q.Y - sy) / 26));
                    }
                }
                Cloud(p, list, _sm, _wr, 1.4, 0.8, null, 1.3);
                _ps.Draw(p);
                if (t < RiseTime)
                {
                    // the rising flare: a white-hot head, a short red flame under it
                    var (x, y) = Rise(t);
                    p.Line(x, y + 1, x, y + 5, u =>
                    {
                        if (u < 0.3)
                        {
                            return c[6];
                        }
                        if (u < 0.7)
                        {
                            return c[4];
                        }
                        return c[3];
                    });
                    p.Px(x, y, White);
                    p.Px(x, y - 1, c[6]);
                    p.Glow(x, y, 14, c[4], 0.6);
                    p.Glow(x, y, 4, White, 0.4);
                }
                else if (t < RiseTime + Burn)
                {
                    var (x, y) = Star(t);
                    var u = t - RiseTime;
                    var k = u / Burn;
                    var fr = Math.Floor(t * 30);
                    var fl = 0.8 + 0.2 * Rand(fr, 3) + 0.08 * Math.Sin(u * 19);
                    var die = 1 - Smooth(0.78, 1, k);
                    // the burst: a flash of white and rays for a moment
                    if (u < 0.25)
                    {
                        var e = u / 0.25;
                        Sim.Flare(p, x, y, 22 * (1 - e) + 3, 1 - e, c);
                        if (e < 0.25)
                        {
                            p.Circle(x, y, 5 * (1 - e * 3) + 1, White);
                        }
                    }
                    // the star: a hot ball with a long cross of light that pulses, guttering at the end
                    var r = (2 + 0.6 * fl) * (0.4 + 0.6 * die);
                    p.Circle(x, y, r + 1.2, c[3]);
                    var body = c[4];
                    if (die > 0.4)
                    {
                        body = c[5];
                    }
                    p.Circle(x, y, r, body);
                    if (die > 0.25)
                    {
                        var heart = c[6];
                        if (die > 0.6)
                        {
                            heart = White;
                        }
                        p.Circle(x - 0.3, y - 0.3, r * 0.55, heart);
                    }
                    if (die > 0.15 && Rand(fr, 9) > 0.15)
                    {
                        Sim.Sparkle(p, x, y, Round((5 + 3 * fl) * die), c[6], c[4], 0.5 * die);
                    }
                    p.Glow(x, y, 70 * (0.6 + 0.4 * die), c[3], 0.55 * fl * die);
                    p.Glow(x, y, 22, c[4], 0.5 * fl * die);
                    p.Glow(x, y, 6, White, 0.35 * die);
                }
                _lights.Draw(p);
            }
        }
    }
}
