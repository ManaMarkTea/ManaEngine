using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Effects.Impacts
{
    /// <summary>
    /// pixelfx's impacts/impact-frame.js: an anime impact -- two frames of white with black burst lines rushing at the
    /// hit, two frames inverted, then the lines pull out past the edges and vanish.
    /// </summary>
    public sealed class ImpactFrame : PixelEffect
    {
        // the beats are whole frames: 2 white, 2 inverted, 4 pulling out
        private const double Frame = 1 / 30.0;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "impacts/impact-frame",
            Name = "Impact frame",
            Description = "An anime impact: two frames of white with black burst lines rushing at the hit, two frames inverted, then the lines pull out past the edges and vanish.",
            Size = (320, 180),
            Origin = (160, 90),
            Duration = 0.3,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#000000", "#ffffff" },
                ["lines"] = 64.0,
                ["seed"] = 19.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o, Meta);

        private readonly record struct Line(double A, double Inner, double W);

        // burst lines: wedges aimed at the hit from past the edges, each its own width and reach, in angle order
        private static List<Line> Burst(PixelRng r, int n)
        {
            var lines = new List<Line>();
            for (var i = 0; i < n; i++)
            {
                var thick = r.Chance(0.18);
                var a = ((i + r.Range(0.1, 0.9)) / n) * Tau;
                double inner;
                double w;
                if (thick)
                {
                    inner = r.Range(30, 48);
                    w = r.Range(5, 8);
                }
                else
                {
                    inner = r.Range(18, 70);
                    w = r.Range(1.2, 3.6);
                }
                lines.Add(new Line(a, inner, w));
            }
            return lines;
        }

        // the jagged burst at the middle: a spiky star whose points shift every frame
        private static double Spikes(double a, double ph) => 10 + 6 * Math.Abs(Math.Sin(a * 5 + ph)) + 4 * Math.Abs(Math.Sin(a * 11 + ph * 2.7)) + 2 * Math.Sin(a * 23 + ph);

        private sealed class Run : PixelSimulation
        {
            private readonly uint _ink;
            private readonly uint _paper;
            private readonly int _w, _h, _x0, _y0;
            private readonly List<byte[]> _masks = [];
            private double _t;

            public Run(PixelFxOptions o, PixelEffectMeta meta)
            {
                var c = Fit(o.Ramp("palette"), 3);
                var r = new PixelRng(o.Number("seed"));
                _ink = c[0];
                _paper = c[2];
                (_w, _h) = meta.Size;
                var (ox, oy) = meta.Origin;
                _x0 = -ox;
                _y0 = -oy;
                var lines = (int)o.Number("lines");
                List<Line>[] sets = [Burst(r, lines), Burst(r, lines)];
                var jit = new (double X, double Y)[2];
                for (var i = 0; i < 2; i++)
                {
                    var jx = Round(r.Range(-2, 2));
                    var jy = Round(r.Range(-1, 1));
                    jit[i] = (jx, jy);
                }
                double[] ph = [r.Range(0, Tau), r.Range(0, Tau), r.Range(0, Tau), r.Range(0, Tau)];
                // every frame is fixed, so work each one out once: which pixels are ink (1) or paper (2), 0 for nothing
                for (var f = 0; f < 8; f++)
                {
                    var m = new byte[_w * _h];
                    var set = sets[f % 2];
                    var n = set.Count;
                    (double X, double Y) shake = (0, 0);
                    double reach = 0;
                    double thin = 1;
                    if (f < 4)
                    {
                        shake = jit[f % 2];
                    }
                    else
                    {
                        reach = Math.Pow((f - 3) / 4.5, 1.5);
                        thin = 1 - (f - 3) / 5.5;
                    }
                    for (var j = 0; j < _h; j++)
                    {
                        for (var i = 0; i < _w; i++)
                        {
                            var x = _x0 + i + 0.5 - shake.X;
                            var y = _y0 + j + 0.5 - shake.Y;
                            var rho = Hypot(x, y * 1.35);
                            var ang = Math.Atan2(y * 1.35, x);
                            var slot = (int)Math.Floor((((ang % Tau) + Tau) % Tau) / (Tau / n));
                            var line = false;
                            for (var k = -1; k <= 1 && !line; k++)
                            {
                                var l = set[(slot + k + n) % n];
                                var d = (((ang - l.A) % Tau) + Tau) % Tau;
                                if (d > Math.PI)
                                {
                                    d = Tau - d;
                                }
                                var inner = l.Inner + reach * 150;
                                var hw = l.W * Clamp((rho - inner) / 40) * thin;
                                line = rho > inner && d * rho < Math.Max(0.5, hw);
                            }
                            if (f < 4)
                            {
                                var r2 = Hypot(x, y * 1.1);
                                var grow = 1 + f * 0.08;
                                if (f == 0)
                                {
                                    grow = 0.85;
                                }
                                var core = Spikes(Math.Atan2(y * 1.1, x), ph[f]) * grow;
                                byte v;
                                if (r2 < core)
                                {
                                    v = 1;
                                    if (r2 < 4 + f)
                                    {
                                        v = 2;
                                    }
                                }
                                else if (line)
                                {
                                    v = 1;
                                }
                                else
                                {
                                    v = 2;
                                }
                                m[j * _w + i] = v;
                            }
                            else if (line)
                            {
                                m[j * _w + i] = 2;
                            }
                        }
                    }
                    _masks.Add(m);
                }
            }

            public override void Update(double dt, PixelFxOptions o) => _t += dt;

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var f = (int)Math.Floor(_t / Frame + 1e-6);
                if (f >= 8)
                {
                    return;
                }
                // two frames of ink on paper, two inverted, then paper lines pulling out over the scene
                var m = _masks[f];
                var inverted = f == 2 || f == 3;
                var ink = _ink;
                var paper = _paper;
                if (inverted)
                {
                    ink = _paper;
                    paper = _ink;
                }
                var w = _w;
                var h = _h;
                var x0 = _x0;
                var y0 = _y0;
                var plain = _paper;
                p.Shade(x0, y0, x0 + w, y0 + h, (x, y, _) =>
                {
                    var i = (int)Math.Floor(x - x0);
                    var j = (int)Math.Floor(y - y0);
                    if (i < 0 || j < 0 || i >= w || j >= h)
                    {
                        return 0;
                    }
                    var v = m[j * w + i];
                    if (v == 1)
                    {
                        return ink;
                    }
                    if (v == 2)
                    {
                        if (f < 4)
                        {
                            return paper;
                        }
                        return plain;
                    }
                    return 0;
                });
            }
        }
    }
}
