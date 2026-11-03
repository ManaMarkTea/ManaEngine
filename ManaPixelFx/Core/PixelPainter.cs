using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Core
{
    /// <summary>A shape's colour at a point of its own: (d, angle) for discs, (u, v) for beams and arcs, (x, y) for polygons.</summary>
    public delegate uint PixelInk(double a, double b);

    /// <summary>A shape's alpha at a point of its own, as <see cref="PixelInk"/>.</summary>
    public delegate double PixelAlpha(double a, double b);

    /// <summary>A pixel shader over a box: the colour at a local point, or 0 to leave it (or to use what <c>o</c> says).</summary>
    public delegate uint PixelShader(double x, double y, PixelShade o);

    /// <summary>What a <see cref="PixelShader"/> may set besides its colour: alpha, light colour and light amount.</summary>
    public sealed class PixelShade
    {
        public uint C;
        public double A = 1;
        public uint L;
        public double K;
    }

    /// <summary>
    /// pixelfx's painter.js: draws into a frame's two buffers -- <c>color</c> (opaque pixels faded with ordered
    /// dither, or truly translucent ones) and <c>light</c> (additive glow). Effects draw around their own origin; the
    /// painter places, mirrors and rotates them.
    /// </summary>
    public sealed class PixelPainter
    {
        // 4x4 ordered (Bayer) thresholds: a pixel at alpha a is drawn when a is above its cell's threshold.
        private static readonly double[] Bayer = new[] { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 }.Select(v => (v + 0.5) / 16).ToArray();

        private readonly PixelShade _out = new();
        private uint[] _c = [];
        private uint[] _l = [];
        private int _w;
        private int _h;
        private int _ox;
        private int _oy;
        private double _tx;
        private double _ty;
        private double _fx = 1;
        private double _cs = 1;
        private double _sn;

        public PixelPainter(PixelFrame frame)
        {
            Frame = frame;
            Reset();
        }

        public PixelFrame Frame { get; }

        /// <summary>Fades everything drawn (an effect told to stop fades out).</summary>
        public double Fade { get; set; } = 1;

        /// <summary>Glow strength: the light option.</summary>
        public double LightK { get; set; } = 1;

        /// <summary>"back" while drawing what goes behind a character, else "front".</summary>
        public string Layer { get; set; } = "front";

        /// <summary>Re-reads the frame and clears placement, fade and light strength.</summary>
        public PixelPainter Reset()
        {
            var f = Frame;
            _w = f.W;
            _h = f.H;
            _ox = f.Ox;
            _oy = f.Oy;
            _c = f.Color;
            _l = f.Light;
            _tx = 0;
            _ty = 0;
            _fx = 1;
            _cs = 1;
            _sn = 0;
            Fade = 1;
            LightK = 1;
            Layer = "front";
            return this;
        }

        /// <summary>Puts the effect's origin at (x, y) in frame space, mirrored if flip, turned by angle (radians).</summary>
        public PixelPainter Place(double x, double y, bool flip = false, double angle = 0)
        {
            _tx = x;
            _ty = y;
            _fx = 1;
            if (flip)
            {
                _fx = -1;
            }
            _cs = Math.Cos(angle);
            _sn = Math.Sin(angle);
            return this;
        }

        // ---- placement, for shapes that walk screen pixels themselves (pixelfx reads these straight off the painter)
        public double Tx => _tx;
        public double Ty => _ty;
        public double Fx => _fx;
        public double Cs => _cs;
        public double Sn => _sn;
        public int Ox => _ox;
        public int Oy => _oy;
        public int W => _w;
        public int H => _h;

        // ---- local (effect) space <-> screen (frame) space
        public double ToX(double x, double y) => _tx + x * _fx * _cs - y * _sn;
        public double ToY(double x, double y) => _ty + x * _fx * _sn + y * _cs;
        public double FromX(double sx, double sy) => ((sx - _tx) * _cs + (sy - _ty) * _sn) * _fx;
        public double FromY(double sx, double sy) => -(sx - _tx) * _sn + (sy - _ty) * _cs;

        // The screen pixel of the local cell holding (x, y): mirrors and rotations land on whole cells.
        public int CellX(double x, double y) => (int)Math.Floor(ToX(Math.Floor(x) + 0.5, Math.Floor(y) + 0.5));
        public int CellY(double x, double y) => (int)Math.Floor(ToY(Math.Floor(x) + 0.5, Math.Floor(y) + 0.5));

        // ---- raw writes, screen space. Colour 0 is "nothing": it never erases what is there.
        public void Put(int sx, int sy, uint c, double a = 1)
        {
            if (c == 0)
            {
                return;
            }
            a *= Fade;
            if (a < 1 && a <= Bayer[((sy & 3) << 2) | (sx & 3)])
            {
                return;
            }
            var x = sx - _ox;
            var y = sy - _oy;
            if (x < 0 || y < 0 || x >= _w || y >= _h)
            {
                return;
            }
            _c[y * _w + x] = c;
        }

        public void Add(int sx, int sy, uint c, double k = 1)
        {
            k *= Fade * LightK;
            if (k <= 0.004)
            {
                return;
            }
            var x = sx - _ox;
            var y = sy - _oy;
            if (x < 0 || y < 0 || x >= _w || y >= _h)
            {
                return;
            }
            var i = y * _w + x;
            var d = _l[i];
            var r = (uint)I32(Math.Min(255, (d & 255) + (c & 255) * k));
            var g = (uint)I32(Math.Min(255, ((d >> 8) & 255) + ((c >> 8) & 255) * k));
            var b = (uint)I32(Math.Min(255, ((d >> 16) & 255) + ((c >> 16) & 255) * k));
            _l[i] = 0xff000000u | (b << 16) | (g << 8) | r;
        }

        /// <summary>Blends a colour over what is there at alpha a, keeping the result's alpha: smoke, vapour, mist.</summary>
        public void BlendPx(int sx, int sy, uint c, double a)
        {
            a *= Fade;
            if (c == 0 || a <= 0.004)
            {
                return;
            }
            var x = sx - _ox;
            var y = sy - _oy;
            if (x < 0 || y < 0 || x >= _w || y >= _h)
            {
                return;
            }
            var i = y * _w + x;
            var d = _c[i];
            var da = (d >> 24) / 255.0;
            var oa = a + da * (1 - a);
            var k = a / oa;
            uint Channel(int s) => (uint)(I32(((c >> s) & 255) * k + ((d >> s) & 255) * (1 - k)) & 255);
            _c[i] = ((uint)(I32(oa * 255) & 255) << 24) | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
        }

        /// <summary>A translucent pixel at a local point.</summary>
        public void Blend(double x, double y, uint c, double a) => BlendPx(CellX(x, y), CellY(x, y), c, a);

        /// <summary>A translucent disc: puffs of smoke, vapour, dust.</summary>
        public void Disc(double cx, double cy, double r, uint c, double a) => Disc(cx, cy, r, null, c, null, a);
        public void Disc(double cx, double cy, double r, PixelInk c, double a) => Disc(cx, cy, r, c, 0, null, a);
        public void Disc(double cx, double cy, double r, uint c, PixelAlpha a) => Disc(cx, cy, r, null, c, a, 1);
        public void Disc(double cx, double cy, double r, PixelInk c, PixelAlpha a) => Disc(cx, cy, r, c, 0, a, 1);

        private void Disc(double cx, double cy, double r, PixelInk? cf, uint c, PixelAlpha? af, double a)
        {
            if (r < 0.5)
            {
                Blend(cx, cy, Ink(cf, c, 0, 0), Alpha(af, a, 0, 0));
                return;
            }
            Each(cx - r, cy - r, cx + r, cy + r, (x, y, sx, sy) =>
            {
                var dx = x - cx;
                var dy = y - cy;
                var d2 = dx * dx + dy * dy;
                if (d2 > r * r)
                {
                    return;
                }
                var d = Math.Sqrt(d2) / r;
                var ang = Math.Atan2(dy, dx);
                BlendPx(sx, sy, Ink(cf, c, d, ang), Alpha(af, a, d, ang));
            });
        }

        /// <summary>The colour under a local point (0 if empty).</summary>
        public uint Get(double x, double y)
        {
            var sx = CellX(x, y) - _ox;
            var sy = CellY(x, y) - _oy;
            if (sx < 0 || sy < 0 || sx >= _w || sy >= _h)
            {
                return 0;
            }
            return _c[sy * _w + sx];
        }

        // ---- pixels
        public void Px(double x, double y, uint c) => Put(CellX(x, y), CellY(x, y), c, 1);

        /// <summary>One pixel at alpha a, faded by ordered dither.</summary>
        public void Dot(double x, double y, uint c, double a = 1)
        {
            if (a > 0)
            {
                Put(CellX(x, y), CellY(x, y), c, a);
            }
        }

        public void Light(double x, double y, uint c, double k = 1) => Add(CellX(x, y), CellY(x, y), c, k);

        /// <summary>Visits every screen pixel whose centre falls in the local box, with that centre in local space.</summary>
        public void Each(double x0, double y0, double x1, double y1, Action<double, double, int, int> fn)
        {
            var ax = double.PositiveInfinity;
            var bx = double.NegativeInfinity;
            var ay = double.PositiveInfinity;
            var by = double.NegativeInfinity;
            for (var k = 0; k < 4; k++)
            {
                var x = x0;
                if ((k & 1) != 0)
                {
                    x = x1;
                }
                var y = y0;
                if ((k & 2) != 0)
                {
                    y = y1;
                }
                var sx = ToX(x, y);
                var sy = ToY(x, y);
                ax = Math.Min(ax, sx);
                bx = Math.Max(bx, sx);
                ay = Math.Min(ay, sy);
                by = Math.Max(by, sy);
            }
            var sx0 = (int)Math.Max(Math.Floor(ax), _ox);
            var sx1 = (int)Math.Min(Math.Ceiling(bx), _ox + _w - 1);
            var sy0 = (int)Math.Max(Math.Floor(ay), _oy);
            var sy1 = (int)Math.Min(Math.Ceiling(by), _oy + _h - 1);
            for (var sy = sy0; sy <= sy1; sy++)
            {
                for (var sx = sx0; sx <= sx1; sx++)
                {
                    var cx = sx + 0.5;
                    var cy = sy + 0.5;
                    fn(FromX(cx, cy), FromY(cx, cy), sx, sy);
                }
            }
        }

        /// <summary>A pixel shader over a local box.</summary>
        public void Shade(double x0, double y0, double x1, double y1, PixelShader fn)
        {
            var o = _out;
            Each(x0, y0, x1, y1, (lx, ly, sx, sy) =>
            {
                o.C = 0;
                o.A = 1;
                o.L = 0;
                o.K = 0;
                var c = fn(lx, ly, o);
                if (c == 0)
                {
                    c = o.C;
                }
                if (c != 0)
                {
                    Put(sx, sy, c, o.A);
                }
                if (o.K > 0)
                {
                    var l = o.L;
                    if (l == 0)
                    {
                        l = c;
                    }
                    Add(sx, sy, l, o.K);
                }
            });
        }

        // ---- shapes
        /// <summary>A 1 px line; c and a may follow u, 0 at the start to 1 at the end.</summary>
        public void Line(double x0, double y0, double x1, double y1, uint c, double a = 1) => Line(x0, y0, x1, y1, null, c, null, a);
        public void Line(double x0, double y0, double x1, double y1, Func<double, uint> c, double a = 1) => Line(x0, y0, x1, y1, c, 0, null, a);
        public void Line(double x0, double y0, double x1, double y1, uint c, Func<double, double> a) => Line(x0, y0, x1, y1, null, c, a, 1);
        public void Line(double x0, double y0, double x1, double y1, Func<double, uint> c, Func<double, double> a) => Line(x0, y0, x1, y1, c, 0, a, 1);

        private void Line(double x0, double y0, double x1, double y1, Func<double, uint>? cf, uint c, Func<double, double>? af, double a)
        {
            var sx = CellX(x0, y0);
            var sy = CellY(x0, y0);
            var ex = CellX(x1, y1);
            var ey = CellY(x1, y1);
            var dx = Math.Abs(ex - sx);
            var dy = -Math.Abs(ey - sy);
            var stepx = -1;
            if (sx < ex)
            {
                stepx = 1;
            }
            var stepy = -1;
            if (sy < ey)
            {
                stepy = 1;
            }
            var n = Math.Max(dx, -dy);
            if (n == 0)
            {
                n = 1;
            }
            var err = dx + dy;
            var i = 0;
            for (;;)
            {
                var u = i++ / (double)n;
                var color = c;
                if (cf != null)
                {
                    color = cf(u);
                }
                var alpha = a;
                if (af != null)
                {
                    alpha = af(u);
                }
                Put(sx, sy, color, alpha);
                if (sx == ex && sy == ey)
                {
                    break;
                }
                var e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    sx += stepx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    sy += stepy;
                }
            }
        }

        public void Polyline(IReadOnlyList<(double X, double Y)> pts, uint c, double a = 1)
        {
            for (var i = 1; i < pts.Count; i++)
            {
                Line(pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y, c, a);
            }
        }

        /// <summary>A thick segment with round ends; c and a may follow (u along, v across: 0 centre, 1 edge).</summary>
        public void Beam(double x0, double y0, double x1, double y1, double width, uint c, double a = 1) => Beam(x0, y0, x1, y1, width, null, c, null, a);
        public void Beam(double x0, double y0, double x1, double y1, double width, PixelInk c, double a = 1) => Beam(x0, y0, x1, y1, width, c, 0, null, a);
        public void Beam(double x0, double y0, double x1, double y1, double width, uint c, PixelAlpha a) => Beam(x0, y0, x1, y1, width, null, c, a, 1);
        public void Beam(double x0, double y0, double x1, double y1, double width, PixelInk c, PixelAlpha a) => Beam(x0, y0, x1, y1, width, c, 0, a, 1);

        private void Beam(double x0, double y0, double x1, double y1, double width, PixelInk? cf, uint c, PixelAlpha? af, double a)
        {
            var r = width / 2;
            var dx = x1 - x0;
            var dy = y1 - y0;
            var l2 = dx * dx + dy * dy;
            if (l2 == 0)
            {
                l2 = 1e-9;
            }
            Shade(Math.Min(x0, x1) - r, Math.Min(y0, y1) - r, Math.Max(x0, x1) + r, Math.Max(y0, y1) + r, (x, y, o) =>
            {
                var u = Clamp(((x - x0) * dx + (y - y0) * dy) / l2);
                var px = x0 + dx * u - x;
                var py = y0 + dy * u - y;
                var d2 = px * px + py * py;
                if (d2 > r * r)
                {
                    return 0;
                }
                var v = Math.Sqrt(d2) / r;
                o.C = Ink(cf, c, u, v);
                o.A = Alpha(af, a, u, v);
                return 0;
            });
        }

        /// <summary>A filled disc; c and a may follow (d: 0 centre to 1 rim, angle).</summary>
        public void Circle(double cx, double cy, double r, uint c, double a = 1) => Circle(cx, cy, r, null, c, null, a);
        public void Circle(double cx, double cy, double r, PixelInk c, double a = 1) => Circle(cx, cy, r, c, 0, null, a);
        public void Circle(double cx, double cy, double r, uint c, PixelAlpha a) => Circle(cx, cy, r, null, c, a, 1);
        public void Circle(double cx, double cy, double r, PixelInk c, PixelAlpha a) => Circle(cx, cy, r, c, 0, a, 1);

        private void Circle(double cx, double cy, double r, PixelInk? cf, uint c, PixelAlpha? af, double a)
        {
            if (r < 0.6)
            {
                Dot(cx, cy, Ink(cf, c, 0, 0), Alpha(af, a, 0, 0));
                return;
            }
            Shade(cx - r, cy - r, cx + r, cy + r, (x, y, o) =>
            {
                var dx = x - cx;
                var dy = y - cy;
                var d2 = dx * dx + dy * dy;
                if (d2 > r * r)
                {
                    return 0;
                }
                var d = Math.Sqrt(d2) / r;
                var ang = Math.Atan2(dy, dx);
                o.C = Ink(cf, c, d, ang);
                o.A = Alpha(af, a, d, ang);
                return 0;
            });
        }

        /// <summary>An ellipse; c and a may follow (d: 0 centre to 1 rim, angle).</summary>
        public void Ellipse(double cx, double cy, double rx, double ry, uint c, double a = 1) => Ellipse(cx, cy, rx, ry, null, c, null, a);
        public void Ellipse(double cx, double cy, double rx, double ry, PixelInk c, double a = 1) => Ellipse(cx, cy, rx, ry, c, 0, null, a);
        public void Ellipse(double cx, double cy, double rx, double ry, uint c, PixelAlpha a) => Ellipse(cx, cy, rx, ry, null, c, a, 1);
        public void Ellipse(double cx, double cy, double rx, double ry, PixelInk c, PixelAlpha a) => Ellipse(cx, cy, rx, ry, c, 0, a, 1);

        private void Ellipse(double cx, double cy, double rx, double ry, PixelInk? cf, uint c, PixelAlpha? af, double a)
        {
            if (rx <= 0 || ry <= 0)
            {
                return;
            }
            Shade(cx - rx, cy - ry, cx + rx, cy + ry, (x, y, o) =>
            {
                var dx = (x - cx) / rx;
                var dy = (y - cy) / ry;
                var d2 = dx * dx + dy * dy;
                if (d2 > 1)
                {
                    return 0;
                }
                var d = Math.Sqrt(d2);
                var ang = Math.Atan2(dy, dx);
                o.C = Ink(cf, c, d, ang);
                o.A = Alpha(af, a, d, ang);
                return 0;
            });
        }

        /// <summary>A ring from radius r - w to r; c and a may follow (v: 0 inner to 1 outer, angle).</summary>
        public void Ring(double cx, double cy, double r, double w, uint c, double a = 1) => Ring(cx, cy, r, w, null, c, null, a);
        public void Ring(double cx, double cy, double r, double w, PixelInk c, double a = 1) => Ring(cx, cy, r, w, c, 0, null, a);
        public void Ring(double cx, double cy, double r, double w, uint c, PixelAlpha a) => Ring(cx, cy, r, w, null, c, a, 1);
        public void Ring(double cx, double cy, double r, double w, PixelInk c, PixelAlpha a) => Ring(cx, cy, r, w, c, 0, a, 1);

        private void Ring(double cx, double cy, double r, double w, PixelInk? cf, uint c, PixelAlpha? af, double a)
        {
            var r0 = Math.Max(0, r - w);
            Shade(cx - r, cy - r, cx + r, cy + r, (x, y, o) =>
            {
                var dx = x - cx;
                var dy = y - cy;
                var d2 = dx * dx + dy * dy;
                if (d2 > r * r || d2 < r0 * r0)
                {
                    return 0;
                }
                double v = 1;
                if (w > 0)
                {
                    var span = r - r0;
                    if (span == 0)
                    {
                        span = 1;
                    }
                    v = (Math.Sqrt(d2) - r0) / span;
                }
                var ang = Math.Atan2(dy, dx);
                o.C = Ink(cf, c, v, ang);
                o.A = Alpha(af, a, v, ang);
                return 0;
            });
        }

        /// <summary>
        /// A band of a ring from angle a0 to a1, radius r - w to r: the slash and smear shape; c and a follow
        /// (u: 0 at a0 to 1 at a1, v: 0 inner to 1 outer), and w may follow u for a crescent.
        /// </summary>
        public void Arc(double cx, double cy, double r, double w, double a0, double a1, uint c, double a = 1) => Arc(cx, cy, r, null, w, a0, a1, null, c, null, a);
        public void Arc(double cx, double cy, double r, double w, double a0, double a1, PixelInk c, double a = 1) => Arc(cx, cy, r, null, w, a0, a1, c, 0, null, a);
        public void Arc(double cx, double cy, double r, double w, double a0, double a1, PixelInk c, PixelAlpha a) => Arc(cx, cy, r, null, w, a0, a1, c, 0, a, 1);
        public void Arc(double cx, double cy, double r, Func<double, double> w, double a0, double a1, PixelInk c, double a = 1) => Arc(cx, cy, r, w, 0, a0, a1, c, 0, null, a);
        public void Arc(double cx, double cy, double r, Func<double, double> w, double a0, double a1, PixelInk c, PixelAlpha a) => Arc(cx, cy, r, w, 0, a0, a1, c, 0, a, 1);

        private void Arc(double cx, double cy, double r, Func<double, double>? wf, double w, double a0, double a1, PixelInk? cf, uint c, PixelAlpha? af, double a)
        {
            var span = a1 - a0;
            Shade(cx - r, cy - r, cx + r, cy + r, (x, y, o) =>
            {
                var dx = x - cx;
                var dy = y - cy;
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d > r)
                {
                    return 0;
                }
                var ang = Math.Atan2(dy, dx) - a0;
                ang -= Math.Floor(ang / (Math.PI * 2)) * Math.PI * 2;
                if (ang > span)
                {
                    return 0;
                }
                var u = ang / span;
                var ww = w;
                if (wf != null)
                {
                    ww = wf(u);
                }
                if (ww <= 0 || d < r - ww)
                {
                    return 0;
                }
                var v = (d - (r - ww)) / ww;
                o.C = Ink(cf, c, u, v);
                o.A = Alpha(af, a, u, v);
                return 0;
            });
        }

        /// <summary>A filled rectangle.</summary>
        public void Rect(double x, double y, double w, double h, uint c, double a = 1)
        {
            if (_sn == 0)
            {
                var x0 = Round(x);
                var y0 = Round(y);
                var x1 = Round(x + w);
                var y1 = Round(y + h);
                for (var yy = y0; yy < y1; yy++)
                {
                    for (var xx = x0; xx < x1; xx++)
                    {
                        Put(CellX(xx, yy), CellY(xx, yy), c, a);
                    }
                }
                return;
            }
            Shade(x, y, x + w, y + h, (lx, ly, o) =>
            {
                if (lx < x || ly < y || lx >= x + w || ly >= y + h)
                {
                    return 0;
                }
                o.C = c;
                o.A = a;
                return 0;
            });
        }

        /// <summary>A filled polygon (even-odd); c and a may follow the local (x, y).</summary>
        public void Poly(IReadOnlyList<(double X, double Y)> pts, uint c, double a = 1) => Poly(pts, null, c, null, a);
        public void Poly(IReadOnlyList<(double X, double Y)> pts, PixelInk c, double a = 1) => Poly(pts, c, 0, null, a);

        private void Poly(IReadOnlyList<(double X, double Y)> pts, PixelInk? cf, uint c, PixelAlpha? af, double a)
        {
            var x0 = double.PositiveInfinity;
            var y0 = double.PositiveInfinity;
            var x1 = double.NegativeInfinity;
            var y1 = double.NegativeInfinity;
            foreach (var (px, py) in pts)
            {
                x0 = Math.Min(x0, px);
                x1 = Math.Max(x1, px);
                y0 = Math.Min(y0, py);
                y1 = Math.Max(y1, py);
            }
            var n = pts.Count;
            Shade(x0, y0, x1, y1, (x, y, o) =>
            {
                var inside = false;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    var (xi, yi) = pts[i];
                    var (xj, yj) = pts[j];
                    if ((yi > y) != (yj > y) && x < ((xj - xi) * (y - yi)) / (yj - yi) + xi)
                    {
                        inside = !inside;
                    }
                }
                if (!inside)
                {
                    return 0;
                }
                o.C = Ink(cf, c, x, y);
                o.A = Alpha(af, a, x, y);
                return 0;
            });
        }

        public void Tri(double x0, double y0, double x1, double y1, double x2, double y2, uint c, double a = 1) =>
            Poly([(x0, y0), (x1, y1), (x2, y2)], c, a);

        // ---- light
        /// <summary>Radial glow into the light buffer: k at the centre, falling to 0 at radius r.</summary>
        public void Glow(double x, double y, double r, uint c, double k = 0.5)
        {
            if (r < 1 || k <= 0.004)
            {
                return;
            }
            var cx = ToX(x, y);
            var cy = ToY(x, y);
            var x0 = (int)Math.Floor(cx - r);
            var x1 = (int)Math.Ceiling(cx + r);
            var y0 = (int)Math.Floor(cy - r);
            var y1 = (int)Math.Ceiling(cy + r);
            for (var sy = y0; sy <= y1; sy++)
            {
                var dy = (sy + 0.5 - cy) / r;
                for (var sx = x0; sx <= x1; sx++)
                {
                    var dx = (sx + 0.5 - cx) / r;
                    var d = dx * dx + dy * dy;
                    if (d >= 1)
                    {
                        continue;
                    }
                    var f = 1 - Math.Sqrt(d);
                    Add(sx, sy, c, f * f * k);
                }
            }
        }

        /// <summary>Glow round a segment: a soft beam of light.</summary>
        public void GlowLine(double x0, double y0, double x1, double y1, double r, uint c, double k = 0.5)
        {
            if (r < 1 || k <= 0.004)
            {
                return;
            }
            var ax = ToX(x0, y0);
            var ay = ToY(x0, y0);
            var bx = ToX(x1, y1);
            var by = ToY(x1, y1);
            var dx = bx - ax;
            var dy = by - ay;
            var l2 = dx * dx + dy * dy;
            if (l2 == 0)
            {
                l2 = 1e-9;
            }
            var sx0 = (int)Math.Floor(Math.Min(ax, bx) - r);
            var sx1 = (int)Math.Ceiling(Math.Max(ax, bx) + r);
            var sy0 = (int)Math.Floor(Math.Min(ay, by) - r);
            var sy1 = (int)Math.Ceiling(Math.Max(ay, by) + r);
            for (var sy = sy0; sy <= sy1; sy++)
            {
                for (var sx = sx0; sx <= sx1; sx++)
                {
                    var px = sx + 0.5;
                    var py = sy + 0.5;
                    var u = Clamp(((px - ax) * dx + (py - ay) * dy) / l2);
                    var ex = ax + dx * u - px;
                    var ey = ay + dy * u - py;
                    var d = Math.Sqrt(ex * ex + ey * ey) / r;
                    if (d >= 1)
                    {
                        continue;
                    }
                    var f = 1 - d;
                    Add(sx, sy, c, f * f * k);
                }
            }
        }

        /// <summary>Glow hugging a circle of radius big R (a shockwave's light), falling off over r.</summary>
        public void GlowRing(double x, double y, double bigR, double r, uint c, double k = 0.5)
        {
            if (r < 1 || k <= 0.004)
            {
                return;
            }
            var cx = ToX(x, y);
            var cy = ToY(x, y);
            var e = bigR + r;
            for (var sy = (int)Math.Floor(cy - e); sy <= Math.Ceiling(cy + e); sy++)
            {
                for (var sx = (int)Math.Floor(cx - e); sx <= Math.Ceiling(cx + e); sx++)
                {
                    var d = Math.Abs(Hypot(sx + 0.5 - cx, sy + 0.5 - cy) - bigR) / r;
                    if (d >= 1)
                    {
                        continue;
                    }
                    var f = 1 - d;
                    Add(sx, sy, c, f * f * k);
                }
            }
        }

        // ---- sprites
        /// <summary>
        /// A little sprite from strings: rows like "..##..", pal mapping characters to colours; '.' and ' ' are empty.
        /// Top left at (x, y); flip mirrors it, flipY turns it upside down, tint draws every pixel in one colour.
        /// </summary>
        public void Sprite(IReadOnlyList<string> rows, IReadOnlyDictionary<char, uint> pal, double x, double y, double scale = 1, double a = 1, bool flip = false, bool flipY = false, uint? tint = null)
        {
            var w = 0;
            if (rows.Count > 0)
            {
                w = rows[0].Length;
            }
            for (var r = 0; r < rows.Count; r++)
            {
                for (var q = 0; q < rows[r].Length; q++)
                {
                    var ch = rows[r][q];
                    if (ch == '.' || ch == ' ')
                    {
                        continue;
                    }
                    uint c = 0;
                    if (tint is { } single)
                    {
                        c = single;
                    }
                    else if (pal.TryGetValue(ch, out var mapped))
                    {
                        c = mapped;
                    }
                    if (c == 0)
                    {
                        continue;
                    }
                    var col = q;
                    if (flip)
                    {
                        col = w - 1 - q;
                    }
                    var row = r;
                    if (flipY)
                    {
                        row = rows.Count - 1 - r;
                    }
                    if (scale == 1)
                    {
                        Dot(x + col, y + row, c, a);
                    }
                    else
                    {
                        Rect(x + col * scale, y + row * scale, scale, scale, c, a);
                    }
                }
            }
        }

        // ---- masks
        /// <summary>Fills a mask with its top left at (x, y).</summary>
        public void Mask(PixelMask m, double x, double y, uint c, double a = 1)
        {
            for (var my = 0; my < m.H; my++)
            {
                for (var mx = 0; mx < m.W; mx++)
                {
                    if (m.Data[my * m.W + mx] != 0)
                    {
                        Dot(x + mx, y + my, c, a);
                    }
                }
            }
        }

        /// <summary>The 1 px outline just outside a mask.</summary>
        public void Outline(PixelMask m, double x, double y, uint c, double a = 1)
        {
            bool At(int mx, int my) => mx >= 0 && my >= 0 && mx < m.W && my < m.H && m.Data[my * m.W + mx] != 0;
            for (var my = -1; my <= m.H; my++)
            {
                for (var mx = -1; mx <= m.W; mx++)
                {
                    if (!At(mx, my) && (At(mx - 1, my) || At(mx + 1, my) || At(mx, my - 1) || At(mx, my + 1)))
                    {
                        Dot(x + mx, y + my, c, a);
                    }
                }
            }
        }

        private static uint Ink(PixelInk? f, uint c, double a, double b)
        {
            if (f != null)
            {
                return f(a, b);
            }
            return c;
        }

        private static double Alpha(PixelAlpha? f, double a, double u, double v)
        {
            if (f != null)
            {
                return f(u, v);
            }
            return a;
        }
    }

    /// <summary>A w x h mask, nonzero where it is solid.</summary>
    public sealed record PixelMask(int W, int H, byte[] Data);
}
