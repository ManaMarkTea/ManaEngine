using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>
    /// pixelfx's projectiles/magic-missile.js: motes gather into an arcane orb at a hand height px above the floor; it
    /// weaves to its target trailing a twisting ribbon and bursts into a star of light. The gathering motes, star
    /// particles and burst flare follow Sprite Fusion's open-source energy bolt, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class MagicMissile : PixelEffect
    {
        private const double Charge = 0.2;
        private const double FlyTime = 0.62;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "projectiles/magic-missile",
            Name = "Magic missile",
            Description = "Motes gather into an arcane orb at a hand `height` px above the floor (the origin); it weaves to its target trailing a twisting ribbon and bursts into a star of light.",
            Size = (208, 112),
            Origin = (24, 104),
            Duration = 1.9,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#140a26", "#2b1252", "#481c88", "#6b2cc0", "#9a48ec", "#c684ff", "#ecc8ff", "#ffffff" },
                ["height"] = 26.0,
                ["reach"] = 112.0,
                ["seed"] = 6.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly double _h;
            private readonly double _x1;
            private readonly PixelRng _r;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly List<TrailPoint> _trail = [];
            private double _t;
            private bool _burst;

            public Run(PixelFxOptions o)
            {
                _c = Fit(o.Ramp("palette"), 8);
                var c = _c;
                _h = o.Number("height");
                _x1 = o.Number("reach");
                _r = new PixelRng(o.Number("seed"));
                _ps = new Particles(rng: _r, ground: _ => 0, sparks: [White, c[6], c[5], c[4]]);
            }

            // the path: out of the hand, weaving up and down twice, homing in on the target at the same height
            private (double X, double Y) Pos(double u) => (_x1 * Ease.InOutSine(u) + Math.Sin(u * Math.PI) * 6, -_h + Math.Sin(u * Tau * 1.5) * 9 * Math.Sin(u * Math.PI));

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                var c = _c;
                var h = _h;
                var x1 = _x1;
                _t += dt;
                var t = _t;
                if (t < Charge)
                {
                    // motes spiral in to the hand
                    if (r.Chance(0.8))
                    {
                        var a = r.Range(0, Tau);
                        var d = r.Range(12, 26);
                        var sp = d / 0.14;
                        ps.Spawn(P.Mote, Math.Cos(a) * d, -h + Math.Sin(a) * d, -Math.Cos(a) * sp - Math.Sin(a) * sp * 0.5, -Math.Sin(a) * sp + Math.Cos(a) * sp * 0.5, 0.14, r.Pick([c[5], c[6], White]), 0.8);
                    }
                }
                else if (t < Charge + FlyTime)
                {
                    var u = (t - Charge) / FlyTime;
                    var (x, y) = Pos(u);
                    _trail.Add(new TrailPoint(x, y, t));
                    if (r.Chance(0.6))
                    {
                        ps.Spawn(P.Mote, x, y, r.Range(-50, 50), r.Range(-50, 50), r.Range(0.2, 0.4), r.Pick([c[4], c[5], c[6]]), 0.7);
                    }
                    if (r.Chance(0.25))
                    {
                        ps.Spawn(P.Star, x + r.Range(-3, 3), y + r.Range(-3, 3), r.Range(-10, 10), r.Range(-10, 10), r.Range(0.25, 0.5), c[6], r.Range(0.6, 1.6));
                    }
                }
                else if (!_burst)
                {
                    _burst = true;
                    var y = -h;
                    _lights.Add(x1, y, 52, c[4], 0.95, 0.55);
                    _lights.Add(x1, y, 22, White, 0.8, 0.12);
                    for (var i = 0; i < 26; i++)
                    {
                        var a = r.Range(0, Tau);
                        var sp = r.Range(60, 150) * (1 - 0.65 * Math.Max(0, Math.Sin(a)));
                        var kind = P.Star;
                        if (i % 3 != 0)
                        {
                            kind = P.Mote;
                        }
                        ps.Spawn(kind, x1, y, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.25, 0.45), r.Pick([c[5], c[6], White]), r.Range(0.8, 1.8));
                    }
                    for (var i = 0; i < 18; i++)
                    {
                        var a = r.Range(0, Tau);
                        var sp = r.Range(110, 240) * (1 - 0.5 * Math.Max(0, Math.Sin(a)));
                        ps.Spawn(P.Spark, x1, y, Math.Cos(a) * sp, Math.Sin(a) * sp - 30, r.Range(0.12, 0.3));
                    }
                    for (var i = 0; i < 14; i++)
                    {
                        var a = r.Range(0, Tau);
                        var d = r.Range(4, 22);
                        ps.Spawn(P.Star, x1 + Math.Cos(a) * d, y + Math.Sin(a) * d * 0.8, 0, -r.Range(2, 8), r.Range(0.5, 1.1), r.Pick([c[6], White, c[5]]), r.Range(0.8, 2.6), r.Range(0.05, 0.45));
                    }
                }
                ps.Update(dt);
                _lights.Update(dt);
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                var h = _h;
                var x1 = _x1;
                // the ribbon: a strip that twists as it trails the orb, its bright face and its dark back taking turns,
                // fading from the tail
                var now = t;
                var keep = _trail.Where(q => now - q.T < 0.32).ToList();
                for (var i = 1; i < keep.Count; i++)
                {
                    var a = keep[i - 1];
                    var b = keep[i];
                    var dx = b.X - a.X;
                    var dy = b.Y - a.Y;
                    var l = Hypot(dx, dy);
                    if (l == 0)
                    {
                        l = 1;
                    }
                    var nx = -dy / l;
                    var ny = dx / l;
                    for (double f = 0; f < 1; f += 0.5 / l)
                    {
                        var tt = a.T + (b.T - a.T) * f;
                        var k = 1 - (now - tt) / 0.32;
                        var tw = Math.Sin(tt * 26);
                        var w = 3.2 * k * Math.Abs(tw) + 0.4;
                        var face = tw > 0;
                        var x = a.X + dx * f;
                        var y = a.Y + dy * f;
                        for (var s = -w; s <= w; s += 0.5)
                        {
                            var edge = Math.Abs(s) > w - 0.8;
                            uint col;
                            if (face)
                            {
                                if (edge)
                                {
                                    col = c[6];
                                }
                                else if (k > 0.6)
                                {
                                    col = c[5];
                                }
                                else
                                {
                                    col = c[4];
                                }
                            }
                            else if (edge)
                            {
                                col = c[4];
                            }
                            else
                            {
                                col = c[2];
                            }
                            var alpha = k / 0.35;
                            if (k > 0.35)
                            {
                                alpha = 1;
                            }
                            p.Dot(x + nx * s, y + ny * s, col, alpha);
                        }
                    }
                    if (i % 3 == 0)
                    {
                        p.Glow(b.X, b.Y, 6, c[4], 0.25 * (1 - (now - b.T) / 0.32));
                    }
                }
                _ps.Draw(p);
                // the orb: gathering at the hand, then flying, its heart white, a ring of light turning round it
                if (t < Charge + FlyTime)
                {
                    double k = 1;
                    var (x, y) = (0.0, -h);
                    if (t < Charge)
                    {
                        k = t / Charge;
                    }
                    else
                    {
                        (x, y) = Pos((t - Charge) / FlyTime);
                    }
                    var r = 1 + 3 * Ease.OutBack(k);
                    p.Circle(x, y, r + 1.4, c[3]);
                    p.Circle(x, y, r + 0.4, c[5]);
                    p.Circle(x - 0.4, y - 0.4, r * 0.55, White);
                    for (var i = 0; i < 3; i++)
                    {
                        var a = t * 14 + (i * Tau) / 3;
                        var col = White;
                        if (i != 0)
                        {
                            col = c[6];
                        }
                        p.Px(x + Math.Cos(a) * (r + 3), y + Math.Sin(a) * (r + 3) * 0.6, col);
                    }
                    p.Glow(x, y, 20, c[4], 0.65 * k);
                    p.Glow(x, y, 7, c[6], 0.5 * k);
                }
                // the burst: a star flare and a ring that open out and fade
                var q = t - Charge - FlyTime;
                if (q >= 0 && q < 0.5)
                {
                    var e = q / 0.5;
                    if (e < 0.5)
                    {
                        Sim.Flare(p, x1, -h, 26 * Math.Pow(1 - e * 2, 1.2) + 2, 1 - e * 2, c);
                    }
                    if (e < 0.12)
                    {
                        p.Circle(x1, -h, 6 * (1 - e / 0.12) + 1, White);
                    }
                    var col = c[5];
                    if (e < 0.3)
                    {
                        col = c[6];
                    }
                    p.Ring(x1, -h, 4 + 20 * Ease.OutCubic(e), Math.Max(1, 2.5 * (1 - e)), col, Math.Min(1, (1 - e) * 1.5));
                    if (e < 0.4)
                    {
                        Sim.Sparkle(p, x1, -h, 7 * (1 - e / 0.4), c[6], c[4], 0.6 * (1 - e / 0.4));
                    }
                }
                _lights.Draw(p);
            }
        }
    }
}
