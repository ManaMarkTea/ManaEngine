using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Impacts.ImpactsLib;

namespace ManaPixelFx.Effects.Impacts
{
    /// <summary>
    /// pixelfx's impacts/ricochet.js: a tracer streaks down onto the floor, glances off with a white-hot star and skips
    /// away at an angle, leaving a glowing skid while sparks skitter along the ground. The glancing star, the tracer, the
    /// skid and the spark spray are adapted from Sprite Fusion's open-source impact, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class Ricochet : PixelEffect
    {
        private const double Duration = 1.1;

        // when it lands, how fast it flies, the angles in and out
        private const double Hit = 0.05;
        private const double Speed = 760;
        private const double In = 0.32;
        private const double Out = -0.21;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "impacts/ricochet",
            Name = "Ricochet",
            Description = "A tracer streaks down onto the floor, glances off with a white-hot star and skips away at an angle, leaving a glowing skid while sparks skitter along the ground.",
            Size = (232, 84),
            Origin = (64, 60),
            Duration = Duration,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#4a1712", "#c9281f", "#f2561d", "#ff8a2a", "#ffe26b", "#fff3a6", "#ffffff" },
                ["stone"] = "#5b5079",
                ["seed"] = 8.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o);

        // a tracer: a white head, a hot body cooling to a dim tail, from (hx, hy) back along angle a for l px
        private static void Tracer(PixelPainter p, double hx, double hy, double a, double l, uint[] c, double k = 1)
        {
            var cs = Math.Cos(a);
            var s = Math.Sin(a);
            for (double d = 0; d <= l; d += 0.5)
            {
                var u = d / l;
                uint col;
                if (u < 0.12)
                {
                    col = White;
                }
                else if (u < 0.45)
                {
                    col = c[5];
                }
                else if (u < 0.75)
                {
                    col = c[4];
                }
                else
                {
                    col = c[2];
                }
                var alpha = k * (1.25 - u);
                if (u < 0.45)
                {
                    alpha = 1;
                }
                p.Dot(hx - cs * d, hy - s * d, col, alpha);
            }
            p.Glow(hx, hy, 9, c[4], 0.6 * k);
        }

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly Stone _st;
            private readonly PixelRng _r;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly List<Spike> _sp;
            private double _t;
            private bool _hit;

            public Run(PixelFxOptions o)
            {
                _c = Fit(o.Ramp("palette"), 7);
                var c = _c;
                _st = Stone.Of(o.Color("stone"));
                _r = new PixelRng(o.Number("seed"));
                _ps = new Particles(rng: _r, ground: _ => 0, sparks: [c[6], c[5], c[4], c[2]]);
                // the star leans hard along the way out, and barely digs into the floor
                _sp = Spikes(_r, 9, Out, len: (6, 9), w: (1.4, 1.9), fwd: 2.6, back: 0.6);
                foreach (var s in _sp)
                {
                    if (Math.Sin(s.A) > 0.2)
                    {
                        s.Len *= 0.3;
                    }
                    if (Math.Abs(Math.Sin(s.A - Out)) > 0.6)
                    {
                        s.Len *= 0.65;
                    }
                }
            }

            public override bool HasBack => true;

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                var c = _c;
                var st = _st;
                _t += dt;
                if (!_hit && _t >= Hit)
                {
                    _hit = true;
                    _lights.Add(0, -2, 44, c[4], 0.65, 0.25);
                    _lights.Add(0, -1, 14, White, 0.6, 0.06);
                    // sparks skitter on along the floor, skipping, and a few spray higher
                    for (var i = 0; i < 18; i++)
                    {
                        var low = i < 12;
                        double a;
                        if (low)
                        {
                            a = Out + r.Range(-0.22, 0.12);
                        }
                        else
                        {
                            a = Out + r.Range(-0.7, 0.25);
                        }
                        var v = r.Range(260, 520);
                        if (!low)
                        {
                            v *= 0.7;
                        }
                        var x = r.Range(-1, 2);
                        var life = r.Range(0.15, 0.42);
                        if (low)
                        {
                            life *= 1.3;
                        }
                        double delay = 0;
                        if (low)
                        {
                            delay = r.Range(0, 0.05);
                        }
                        ps.Spawn(P.Spark, x, -1, Math.Cos(a) * v, Math.Sin(a) * v, life, White, 1, delay);
                    }
                    // grit kicked up and a scuff of dust
                    for (var i = 0; i < 7; i++)
                    {
                        var a = -Math.PI / 2 + r.Range(-0.2, 0.9);
                        var v = r.Range(60, 170);
                        var x = r.Range(0, 4);
                        var life = r.Range(0.6, 0.95);
                        var col = r.Pick([st.Light, st.Body, st.Lip]);
                        double size = 1;
                        if (i < 2)
                        {
                            size = 2;
                        }
                        ps.Spawn(P.Grit, x, -1, Math.Cos(a) * v, Math.Sin(a) * v, life, col, size);
                    }
                    for (var i = 0; i < 4; i++)
                    {
                        ps.Spawn(P.Dust, r.Range(0, 8), -1, r.Range(20, 70), -r.Range(4, 16), r.Range(0.25, 0.45), Mix(st.Dust, White, r.Range(0.1, 0.3)), r.Range(1.2, 2));
                    }
                }
                ps.Update(dt);
                _lights.Update(dt);
            }

            public override void DrawBack(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                // the skid: a gouge along the floor that glows where the round dragged, cooling from its far end
                var u = t - Hit;
                if (u >= 0)
                {
                    var heat = Math.Pow(Clamp(1 - u / 0.85), 1.2);
                    var fade = 1 - Smooth(Duration - 0.4, Duration - 0.05, t);
                    var l = 12 * Math.Min(1, u / 0.03);
                    for (var x = -3; x <= l; x++)
                    {
                        var q = (x + 3) / 15.0;
                        var h = heat * (1.15 - q * 0.6);
                        uint col;
                        if (h > 0.8)
                        {
                            col = White;
                        }
                        else if (h > 0.55)
                        {
                            col = c[5];
                        }
                        else if (h > 0.35)
                        {
                            col = c[4];
                        }
                        else if (h > 0.18)
                        {
                            col = c[3];
                        }
                        else if (h > 0.08)
                        {
                            col = c[2];
                        }
                        else
                        {
                            col = Soot;
                        }
                        p.Dot(x, 0, col, fade);
                        if (x > -2 && x < l - 2 && (x & 1) == 0)
                        {
                            var under = Soot;
                            if (h > 0.4)
                            {
                                under = c[3];
                            }
                            p.Dot(x, 1, under, fade * 0.7);
                        }
                    }
                    if (heat > 0)
                    {
                        p.GlowLine(-3, 0, l, 0, 5, c[3], 0.45 * heat);
                    }
                }
                _ps.Draw(p, "back");
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                _ps.Draw(p, "front");
                if (t < Hit)
                {
                    // incoming: the tracer races down to the floor
                    var d = (Hit - t) * Speed;
                    Tracer(p, -Math.Cos(In) * d, -Math.Sin(In) * d, In, 22, c);
                }
                else
                {
                    var u = t - Hit;
                    // glancing off: a stretched star, then the round skips away, its tail stretching out behind it
                    if (u < 0.03)
                    {
                        Star(p, 0, -1, _sp, 1, 1, [c[2], c[5], c[4], White], 1, 2);
                    }
                    else if (u < 0.06)
                    {
                        Star(p, 0, -1, _sp, 1.25, 0.55, [c[1], c[4], c[3], c[5]], 1, 2);
                    }
                    if (u < 0.2)
                    {
                        var head = 4 + u * Speed;
                        var k = 1 - u / 0.2;
                        Tracer(p, Math.Cos(Out) * head, -1 + Math.Sin(Out) * head, Out, Math.Min(head - 2, 18 + u * 200), c, k);
                    }
                    // a thin half ring hugging the floor, swelling and cooling
                    var g = (u - 0.02) / 0.2;
                    if (g >= 0 && g < 1)
                    {
                        var r = 5 + 12 * (1 - Math.Pow(1 - g, 3));
                        var col = c[3];
                        if (g < 0.3)
                        {
                            col = c[5];
                        }
                        else if (g < 0.6)
                        {
                            col = c[4];
                        }
                        for (int j = 0, n = (int)Math.Ceiling(r * Math.PI * 1.3); j <= n; j++)
                        {
                            var a = -Math.PI + (j / (double)n) * Math.PI;
                            p.Dot(Math.Cos(a) * r, -1 + Math.Sin(a) * r, col, 1.15 - g);
                        }
                    }
                    var pop = Math.Exp(-u / 0.045);
                    if (pop > 0.02)
                    {
                        p.Glow(0, -2, 22, c[3], 0.75 * pop);
                        p.Glow(0, -1, 8, White, 0.5 * pop);
                    }
                }
                _lights.Draw(p);
            }
        }
    }
}
