using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Effects.Impacts
{
    /// <summary>Stone colours from one body colour: light, body, shade, dust (lighter and greyer), lip (a lit broken edge).</summary>
    public readonly record struct Stone(uint Light, uint Body, uint Shade, uint Dust, uint Lip)
    {
        public static Stone Of(uint c) =>
            new(Mix(Scale(c, 1.35), White, 0.08), c, Scale(c, 0.64), Mix(Scale(c, 1.2), Rgb(150, 140, 150), 0.3), Mix(Scale(c, 1.55), White, 0.12));
    }

    /// <summary>One spike of a hit star; effects lengthen or cut some after making them.</summary>
    public sealed class Spike(double a, double len, double w)
    {
        public double A { get; } = a;
        public double Len { get; set; } = len;
        public double W { get; } = w;
    }

    /// <summary>One arc of a ring that breaks: centre angle, gap (0..1), how fast it flies out, thickness scale.</summary>
    public readonly record struct RingSegment(double A, double Gap, double Dr, double W = 1);

    /// <summary>A crack in the floor: points (x, y, distance along), its length, when it starts, its forks.</summary>
    public sealed class Crack
    {
        public required List<(double X, double Y, double D)> Pts { get; init; }
        public required double Len { get; init; }
        public required double T0 { get; init; }
        public List<List<(double X, double Y, double D)>> Kids { get; } = [];
    }

    /// <summary>
    /// pixelfx's impacts/_lib.js: the hit star, a ring that breaks into flying arcs, floor cracks that glow and cool,
    /// standing shock fronts, soft lumpy dust rings, faceted tumbling rocks. The star and flying-arc recipes are adapted
    /// from Sprite Fusion's open-source impact, and the stone colours from its explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public static class ImpactsLib
    {
        public static readonly uint Soot = Hex("#0d0a12");
        public static readonly uint Outline = Hex("#1a1422");

        // light comes from the top left
        private static readonly double LightAngle = Math.Atan2(-1, -1);

        /// <summary>
        /// Spikes for a hit star: n round the circle, longer towards aim (fwd) and a little behind it (back); jag scales
        /// every other spike (short teeth between).
        /// </summary>
        public static List<Spike> Spikes(PixelRng r, int n, double aim, (double Min, double Max)? len = null, (double Min, double Max)? w = null, double fwd = 1.8, double back = 0.6, double jag = 1, double jit = 0.18)
        {
            var lens = len ?? (7, 10);
            var ws = w ?? (1.5, 2.1);
            var list = new List<Spike>();
            for (var i = 0; i < n; i++)
            {
                var a = aim + (i / (double)n) * Tau + r.Range(-jit, jit);
                var cb = Math.Cos(a - aim);
                double teeth = 1;
                double thin = 1;
                if (i % 2 != 0)
                {
                    teeth = jag;
                    thin = Math.Sqrt(jag);
                }
                var l = r.Range(lens.Min, lens.Max) * (1 + fwd * Math.Pow(Math.Max(0, cb), 3) + back * Math.Pow(Math.Max(0, -cb), 3)) * teeth;
                list.Add(new Spike(a, l, r.Range(ws.Min, ws.Max) * thin));
            }
            return list;
        }

        /// <summary>
        /// A hit star: tapered spikes in three passes (a rim, a body paler near the root, a hot core) round a hot heart.
        /// sc stretches the spikes, th thins them, cols [rim, body, inner, core], a dithers it out.
        /// </summary>
        public static void Star(PixelPainter p, double x, double y, IReadOnlyList<Spike> list, double sc, double th, uint[] cols, double a = 1, double heart = 2.4)
        {
            var (rim, body, inner, core) = (cols[0], cols[1], cols[2], cols[3]);
            for (var pass = 0; pass < 3; pass++)
            {
                foreach (var s in list)
                {
                    var c = Math.Cos(s.A);
                    var sn = Math.Sin(s.A);
                    var l = s.Len * sc;
                    for (double d = 0; d <= l; d += 0.5)
                    {
                        if (pass == 2 && d > l * 0.55)
                        {
                            break;
                        }
                        var hw0 = s.W * th * (1 - d / l) * Math.Min(1, 0.5 + d / 3);
                        double hw;
                        uint col;
                        if (pass == 0)
                        {
                            hw = hw0 + 0.8;
                            col = rim;
                        }
                        else if (pass == 1)
                        {
                            hw = hw0;
                            col = body;
                            if (d < l * 0.35)
                            {
                                col = inner;
                            }
                        }
                        else
                        {
                            hw = hw0 * 0.45;
                            col = core;
                        }
                        for (var o = -hw; o <= hw; o += 0.5)
                        {
                            p.Dot(x + c * d - sn * o, y + sn * d + c * o, col, a);
                        }
                    }
                }
            }
            var r = heart * sc * Math.Sqrt(th);
            p.Circle(x, y, r + 0.8, inner, a);
            p.Circle(x, y, r, core, a);
        }

        /// <summary>
        /// A ring that breaks into arcs: brk 0 draws a whole ring th thick; as brk grows each arc shrinks, tapers at both
        /// ends into a crescent and drifts out at its own speed. c is the outer edge's colour, c2 the inner's; sy squashes
        /// it onto a plane; nothing is drawn below maxY.
        /// </summary>
        public static void BrokenRing(PixelPainter p, double x, double y, double r, double th, IReadOnlyList<RingSegment> segs, double brk, uint c, uint? c2 = null, double k = 1, double sy = 1, double maxY = double.PositiveInfinity)
        {
            if (r <= 0 || k <= 0)
            {
                return;
            }
            var inner = c2 ?? c;
            var full = Tau / segs.Count;
            foreach (var s in segs)
            {
                var span = full * (1 - s.Gap * brk) + 0.02;
                var rr = r * (1 + s.Dr * brk);
                var m = Math.Max(4, Math.Ceiling(rr * span * 1.8));
                for (var j = 0; j <= m; j++)
                {
                    var u = j / m;
                    var ang = s.A + (u - 0.5) * span;
                    var w = Math.Max(0.5, th * s.W * (1 - brk * Math.Pow(Math.Abs(2 * u - 1), 1.6)));
                    var ca = Math.Cos(ang);
                    var sa = Math.Sin(ang);
                    for (var o = -w / 2; o <= w / 2 + 0.01; o += 0.5)
                    {
                        if (y + sa * (rr + o) * sy < maxY)
                        {
                            var col = inner;
                            if (o > -0.26)
                            {
                                col = c;
                            }
                            p.Dot(x + ca * (rr + o), y + sa * (rr + o) * sy, col, k);
                        }
                    }
                }
            }
        }

        // a jagged walk from (x, y) heading a for l px; each point is (x, y, distance along)
        private static List<(double X, double Y, double D)> Walk(PixelRng r, double x, double y, double a, double l, double jit = 0.55, double steer = 0.45, (double Min, double Max)? step = null, double maxY = double.PositiveInfinity, double d0 = 0)
        {
            var steps = step ?? (2, 4);
            var pts = new List<(double X, double Y, double D)> { (x, y, d0) };
            double d = 0;
            var cur = a;
            while (d < l - 0.01)
            {
                var s = Math.Min(l - d, r.Range(steps.Min, steps.Max));
                cur += r.Range(-jit, jit) + (a - cur) * steer;
                x += Math.Cos(cur) * s;
                y = Clamp(y + Math.Sin(cur) * s, 0.5, maxY);
                d += s;
                pts.Add((x, y, d0 + d));
            }
            return pts;
        }

        /// <summary>
        /// Cracks in the floor face below (0, 0): n jagged lines, alternately left and right, mostly shallow, a few diving
        /// steeply, some forked.
        /// </summary>
        public static List<Crack> MakeCracks(PixelRng r, int n, (double Min, double Max)? len = null, double dive = 0.85, double maxY = 22, double forks = 0.5, (double Min, double Max)? x0 = null)
        {
            var lens = len ?? (14, 40);
            var starts = x0 ?? (1, 5);
            var output = new List<Crack>();
            for (var i = 0; i < n; i++)
            {
                double side = -1;
                if (i % 2 != 0)
                {
                    side = 1;
                }
                var dip = 0.06 + Math.Pow(r.Next(), 1.7) * dive;
                var l = r.Range(lens.Min, lens.Max) * (1 - (0.5 * dip) / (dive + 0.06));
                var a = Math.PI - dip;
                if (side > 0)
                {
                    a = dip;
                }
                var sx = side * r.Range(starts.Min, starts.Max);
                var sy = r.Range(0.5, 1.5);
                var pts = Walk(r, sx, sy, a, l, maxY: maxY);
                var crack = new Crack { Pts = pts, Len = l, T0 = r.Range(0, 0.035) };
                for (var j = 2; j < pts.Count - 1; j++)
                {
                    if (r.Chance(forks * 0.4))
                    {
                        var (fx, fy, fd) = pts[j];
                        var fa = a + r.Sign() * r.Range(0.45, 1);
                        var fl = r.Range(3, 9) * (1 - (0.5 * j) / pts.Count);
                        crack.Kids.Add(Walk(r, fx, fy, fa, fl, jit: 0.7, step: (1.5, 3), maxY: maxY, d0: fd));
                    }
                }
                output.Add(crack);
            }
            return output;
        }

        /// <summary>
        /// Cracks grown to g(c) px of their length. heat(d) 0..1 at distance d along a crack lights it from the hot ramp
        /// (bright to dark) before it cools to dark; lip is the lit lower edge; k dithers them out.
        /// </summary>
        public static void DrawCracks(PixelPainter p, IReadOnlyList<Crack> cracks, Func<Crack, double> g, Func<double, double> heat, uint[] hot, uint dark, uint lip, double k = 1)
        {
            if (k <= 0)
            {
                return;
            }
            uint Col(double d)
            {
                var h = heat(d);
                if (h > 0.75)
                {
                    return hot[0];
                }
                if (h > 0.5)
                {
                    return hot[1];
                }
                if (h > 0.3)
                {
                    return hot[2];
                }
                if (h > 0.12)
                {
                    return hot[3];
                }
                return dark;
            }
            foreach (var c in cracks)
            {
                var grown = g(c);
                if (grown <= 0)
                {
                    continue;
                }
                foreach (var path in new[] { c.Pts }.Concat(c.Kids))
                {
                    for (var i = 1; i < path.Count; i++)
                    {
                        var (x0, y0, d0) = path[i - 1];
                        var (x1, y1, d1) = path[i];
                        if (d0 >= grown)
                        {
                            break;
                        }
                        double f = 1;
                        if (d1 > grown)
                        {
                            f = (grown - d0) / (d1 - d0);
                        }
                        var ex = x0 + (x1 - x0) * f;
                        var ey = y0 + (y1 - y0) * f;
                        p.Line(x0, y0 + 1, ex, ey + 1, lip, u =>
                        {
                            if (Rand(Round(x0 + (ex - x0) * u), Round(y0 + (ey - y0) * u), 5) < 0.3)
                            {
                                return k * 0.8;
                            }
                            return 0;
                        });
                        p.Line(x0, y0, ex, ey, u => Col(d0 + (d1 - d0) * f * u), k);
                        var h = heat(d0);
                        if (h > 0.12)
                        {
                            p.Glow((x0 + ex) / 2, (y0 + ey) / 2, 4 + h * 4, hot[2], 0.22 * h * k);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// A standing crescent shock front on the floor: it leans back from a leading edge low at x, dir 1 racing right,
        /// -1 left; h tall, w thick at its widest; cols [edge, body, back] bright to dark; k dithers it out.
        /// </summary>
        public static void FrontCrescent(PixelPainter p, double x, double dir, double h, double w, uint[] cols, double k = 1)
        {
            if (h < 1 || w < 0.3 || k <= 0)
            {
                return;
            }
            var ry = h * 0.8;
            var cy = -h * 0.2;
            var rx = Math.Max(2, h * 0.55);
            var x0 = x - 1;
            var x1 = x + rx + w + 1;
            if (dir > 0)
            {
                x0 = x - rx - w - 1;
                x1 = x + 1;
            }
            p.Shade(x0, -h - 1, x1, 0, (px, py, o) =>
            {
                if (py > 0)
                {
                    return 0;
                }
                var ey = (py - cy) / ry;
                if (ey <= -1 || ey >= 1)
                {
                    return 0;
                }
                var e2 = 1 - ey * ey;
                var edge = rx * Math.Sqrt(e2) - rx;
                var wt = w * Math.Pow(e2, 0.45);
                var q = (edge - (px - x) * dir) / Math.Max(0.5, wt);
                if (q < 0 || q > 1)
                {
                    return 0;
                }
                if (q < 0.28)
                {
                    o.C = cols[0];
                }
                else if (q < 0.62)
                {
                    o.C = cols[1];
                }
                else
                {
                    o.C = cols[2];
                }
                var top = 1.0;
                if (ey < -0.8)
                {
                    top = 0.7;
                }
                var back = 1.0;
                if (q > 0.7)
                {
                    back = 1.25 - q * 0.8;
                }
                o.A = k * top * back;
                return 0;
            });
        }

        /// <summary>
        /// A soft, lumpy ring of dust round (cx, cy): radius r, w thick, alpha k, translucent and lit on its upper side.
        /// sy squashes it onto the floor; brk 0..1 tears it into clumps; ph varies it; nothing below maxY; half -1 draws
        /// only the far half, 1 only the near half.
        /// </summary>
        public static void DustRing(PixelPainter p, double cx, double cy, double r, double w, double k, uint c, double sy = 1, double ph = 0, double brk = 0, double lit = 0.35, double maxY = double.PositiveInfinity, int half = 0)
        {
            if (k <= 0.01 || r <= 0)
            {
                return;
            }
            var bigW = w * 1.6;
            var lite = Mix(c, White, lit);
            for (var y = Math.Floor(cy - (r + bigW) * sy) - 1; y <= Math.Min(maxY - 1, Math.Ceiling(cy + (r + bigW) * sy) + 1); y++)
            {
                for (var x = Math.Floor(cx - r - bigW) - 1; x <= Math.Ceiling(cx + r + bigW) + 1; x++)
                {
                    var dx = x + 0.5 - cx;
                    var dy = (y + 0.5 - cy) / sy;
                    var d = Hypot(dx, dy);
                    var g = Math.Atan2(dy, dx);
                    if ((half < 0 && dy >= 0) || (half > 0 && dy < 0))
                    {
                        continue;
                    }
                    var lumpy = 0.6 + 0.3 * Math.Sin(5 * g + ph) + 0.18 * Math.Sin(11 * g + ph * 2.1) + 0.12 * Math.Sin(17 * g + ph * 0.7);
                    var wt = w * lumpy;
                    var off = Math.Abs(d - r - w * 0.25 * Math.Sin(7 * g + ph));
                    if (off >= wt)
                    {
                        continue;
                    }
                    double tear = 1;
                    if (brk > 0)
                    {
                        tear = Clamp((0.55 + 0.45 * Math.Sin(6 * g + ph * 1.7) - brk) / 0.25);
                    }
                    var a = k * (1 - Math.Pow(off / wt, 2)) * tear;
                    if (a > 0.01)
                    {
                        var col = c;
                        if (dy < 0 && d > r - wt * 0.3)
                        {
                            col = lite;
                        }
                        p.Blend(x, y, col, a);
                    }
                }
            }
        }

        /// <summary>A rock's outline: n vertex radii round its centre.</summary>
        public static double[] RockShape(PixelRng r, int n = 6) => Enumerable.Range(0, n).Select(_ => r.Range(0.7, 1.12)).ToArray();

        /// <summary>
        /// A faceted rock tumbling at angle b.A: every facet shaded by how it faces the light, a lit top face, a dark
        /// outline, dithering out over its last half second.
        /// </summary>
        public static void Rock(PixelPainter p, Bit b, Stone st)
        {
            var shape = b.Shape ?? [1.0, 1, 1, 1, 1, 1];
            var r = 0.75 + b.S * 0.8;
            var n = shape.Length;
            var fade = Math.Min(1, b.Life / 0.5);
            var ca = Math.Cos(b.A);
            var sa = Math.Sin(b.A);
            p.Shade(b.X - r - 2, b.Y - r - 2, b.X + r + 2, b.Y + r + 2, (x, y, o) =>
            {
                var dx = x - b.X;
                var dy = y - b.Y;
                var u = dx * ca + dy * sa;
                var v = -dx * sa + dy * ca;
                var d = Hypot(u, v);
                var ang = Math.Atan2(v, u);
                if (ang < 0)
                {
                    ang += Tau;
                }
                var f = (ang / Tau) * n;
                var i = (int)(Math.Floor(f) % n);
                var w = f - Math.Floor(f);
                var edge = r * (shape[i] * (1 - w) + shape[(i + 1) % n] * w);
                if (d > edge + 0.85)
                {
                    return 0;
                }
                o.A = fade;
                if (d > edge)
                {
                    return Outline;
                }
                var lit = Math.Cos(((i + 0.5) / n) * Tau + b.A - LightAngle);
                if (d < edge * 0.42)
                {
                    if (lit > 0.55)
                    {
                        return st.Light;
                    }
                    return st.Body;
                }
                if (lit > 0.35)
                {
                    return st.Light;
                }
                if (lit < -0.4)
                {
                    return st.Shade;
                }
                return st.Body;
            });
        }
    }
}
