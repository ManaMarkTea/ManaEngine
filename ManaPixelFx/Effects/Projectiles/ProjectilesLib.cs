using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Effects.Projectiles
{
    /// <summary>One puff of a <see cref="ProjectilesLib.Cloud"/>: where, how big, how dense, how bright, how fire-lit.</summary>
    public sealed class CloudPuff
    {
        public double X, Y, R, A, B, W;
    }

    /// <summary>One lobe of a muzzle flash: offset along the barrel, angle, length, width, how far its hot core reaches.</summary>
    public sealed class FlashLobe
    {
        public double Off, A, Len, Wid, Core;
    }

    /// <summary>A casing's place at an age: where, its angle, lying still, seconds since it last hit (-1 never).</summary>
    public readonly record struct CasingAt(double X, double Y, double A, bool Rest, double Hit);

    /// <summary>A puff of <see cref="Smoke"/>; set what differs from the defaults, as pixelfx's add() takes them.</summary>
    public sealed class SmokePuff
    {
        public double X, Y, Vx, Vy, R0, R1, Life;
        public double Drag = 2.5, Rise = 12, Grow = 2, Curl;
        public double A = 0.6, B = 0.85, Warm, Delay, Age, Seed;
    }

    /// <summary>
    /// Simulated smoke for one-shots, drawn with <see cref="ProjectilesLib.Cloud"/>: puffs push out, slow, lift, sway,
    /// swell from r0 to r1, thin away and darken; warm (fire light) fades fast; curl keeps turning a puff's heading.
    /// </summary>
    public sealed class Smoke
    {
        private readonly PixelRng _r;
        private readonly List<SmokePuff> _list = [];
        private double _t;

        public Smoke(PixelRng r)
        {
            _r = r;
        }

        public int Count => _list.Count;

        public void Add(SmokePuff q)
        {
            q.Seed = _r.Next() * 100;
            _list.Add(q);
        }

        public void Update(double dt)
        {
            _t += dt;
            for (var i = _list.Count - 1; i >= 0; i--)
            {
                var q = _list[i];
                if (q.Delay > 0)
                {
                    q.Delay -= dt;
                    continue;
                }
                q.Age += dt;
                if (q.Age >= q.Life)
                {
                    _list.RemoveAt(i);
                    continue;
                }
                if (q.Curl != 0)
                {
                    var c = Math.Cos(q.Curl * dt);
                    var s = Math.Sin(q.Curl * dt);
                    var vx = q.Vx;
                    q.Vx = vx * c - q.Vy * s;
                    q.Vy = vx * s + q.Vy * c;
                }
                q.Vy -= q.Rise * dt;
                q.Vx += Math.Sin(_t * 1.3 + q.Seed) * 8 * dt;
                q.Vx -= q.Vx * q.Drag * dt;
                q.Vy -= q.Vy * q.Drag * dt;
                q.X += q.Vx * dt;
                q.Y += q.Vy * dt;
            }
        }

        /// <summary>The live puffs, for <see cref="ProjectilesLib.Cloud"/>.</summary>
        public List<CloudPuff> Puffs()
        {
            var output = new List<CloudPuff>();
            foreach (var q in _list)
            {
                if (q.Delay > 0)
                {
                    continue;
                }
                var k = q.Age / q.Life;
                output.Add(new CloudPuff
                {
                    X = q.X,
                    Y = q.Y,
                    R = q.R0 + (q.R1 - q.R0) * (1 - Math.Exp(-q.Grow * q.Age)),
                    A = q.A * Math.Min(1, q.Age * 30) * Math.Pow(1 - k, 1.3),
                    B = q.B * (1 - k * 0.5),
                    W = q.Warm * Math.Exp(-q.Age * 5),
                });
            }
            return output;
        }
    }

    /// <summary>
    /// pixelfx's projectiles/_lib.js: a steady emitter for loops, motion in a projectile's own frame, smoke puffs merged
    /// into one lit volume, speed-stretched sparks, muzzle-flash lobes, brass casings that bounce, turned sprites and
    /// outlined pixel shapes. The smoke, spark, flash lobe and casing recipes are adapted from Sprite Fusion's
    /// open-source muzzle-flash and explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public static class ProjectilesLib
    {
        /// <summary>
        /// Like Cycle, but particle i is born exactly at i * period / n: an emitter with a steady beat, so a trail has no
        /// clumps or gaps. fn(age, life, rng, i), the rng the same every period.
        /// </summary>
        public static void Beat(double t, double period, int n, double seed, Func<PixelRng, double> life, Action<double, double, PixelRng, int> fn)
        {
            for (var i = 0; i < n; i++)
            {
                var l = life(Rng(seed, i, 911));
                for (var age = Wrap(t - (i * period) / n + 1e-7, period); age < l; age += period)
                {
                    fn(age, l, Rng(seed, i), i);
                }
            }
        }

        /// <summary>
        /// Where something is after age seconds in the frame of a projectile flying right at v px/s: launched from (x, y)
        /// at (vx, vy) through still air, slowed toward it by drag k, pulled by g; the air streams left at v.
        /// </summary>
        public static (double X, double Y, double Vx, double Vy) Wake(double x, double y, double vx, double vy, double age, double v, double k, double g = 0)
        {
            var e = Math.Exp(-k * age);
            var f = (1 - e) / k;
            var vt = g / k;
            return (x + vx * f - v * age, y + vt * age + (vy - vt) * f, vx * e - v, vt + (vy - vt) * e);
        }

        /// <summary>A soft smoke puff: a body, a denser core sitting low, a lit cap, and light from a fire on one side.</summary>
        public static void Puff(PixelPainter p, double x, double y, double r, double a, uint c, double cap = 1, double warm = 0, uint wc = 0, double wa = 0)
        {
            if (a <= 0.01 || r < 0.5)
            {
                return;
            }
            p.Disc(x, y, r, c, a * 0.45);
            p.Disc(x + r * 0.05, y + r * 0.06, r * 0.74, c, a * 0.62);
            if (cap > 0.02)
            {
                p.Disc(x - r * 0.22, y - r * 0.3, r * 0.5, Mix(c, White, 0.14), a * 0.42 * cap);
            }
            if (warm > 0.02)
            {
                p.Disc(x + Math.Cos(wa) * r * 0.32, y + Math.Sin(wa) * r * 0.32, r * 0.55, wc, a * 0.7 * warm);
            }
        }

        /// <summary>
        /// Puffs merged into one translucent volume: each lit on top and shaded underneath, densities adding up, tone and
        /// density in clean steps, so a trail reads as one billowing cloud. r: smoke ramp, dark to bright; wr: the same lit
        /// by fire, which young puffs lean to. clip(x, y) keeps it off part of the canvas.
        /// </summary>
        public static void Cloud(PixelPainter p, IReadOnlyList<CloudPuff> list, uint[] r, uint[] wr, double dens = 1.5, double max = 0.86, Func<double, double, bool>? clip = null, double lit = 1)
        {
            if (list.Count == 0)
            {
                return;
            }
            var x0 = double.PositiveInfinity;
            var y0 = double.PositiveInfinity;
            var x1 = double.NegativeInfinity;
            var y1 = double.NegativeInfinity;
            foreach (var q in list)
            {
                x0 = Math.Min(x0, Math.Floor(q.X - q.R));
                x1 = Math.Max(x1, Math.Ceiling(q.X + q.R));
                y0 = Math.Min(y0, Math.Floor(q.Y - q.R));
                y1 = Math.Max(y1, Math.Ceiling(q.Y + q.R));
            }
            var w = (int)(x1 - x0 + 3);
            var h = (int)(y1 - y0 + 3);
            // Float32Arrays in pixelfx: every store rounds to single precision, and that rounding is kept
            var dField = new float[w * h];
            var lField = new float[w * h];
            var fField = new float[w * h];
            x0--;
            y0--;
            foreach (var q in list)
            {
                var rr = q.R;
                var r2 = rr * rr;
                for (var y = Math.Floor(q.Y - rr); y <= Math.Ceiling(q.Y + rr); y++)
                {
                    var dy = y + 0.5 - q.Y;
                    for (var x = Math.Floor(q.X - rr); x <= Math.Ceiling(q.X + rr); x++)
                    {
                        var dx = x + 0.5 - q.X;
                        var d2 = (dx * dx + dy * dy) / r2;
                        if (d2 >= 1)
                        {
                            continue;
                        }
                        // a ball's thickness: flat in the middle, steep at the rim, so every lump keeps its own crown
                        var k = Math.Sqrt(1 - d2) * q.A;
                        var i = (int)((y - y0) * w + (x - x0));
                        dField[i] = (float)(dField[i] + k);
                        lField[i] = (float)(lField[i] + k * q.B);
                        fField[i] = (float)(fField[i] + k * q.W * (0.5 + 0.5 * Clamp(0.5 + dx / rr)));
                    }
                }
            }
            // light the merged surface off a blurred copy, so only lump-sized features catch it, never the crossing rims
            var blurred = Blur(Blur(dField, w, h, 1, 2), w, h, w, 2);
            var n = r.Length;
            var bx0 = x0;
            var by0 = y0;
            p.Each(bx0 + 1, by0 + 1, x1 + 1, y1 + 1, (lx, ly, sx, sy) =>
            {
                var cx = (int)(Math.Floor(lx) - bx0);
                var cy = (int)(Math.Floor(ly) - by0);
                if (cx < 1 || cy < 1 || cx >= w - 1 || cy >= h - 1)
                {
                    return;
                }
                var i = cy * w + cx;
                double d = dField[i];
                if (d < 0.05 || (clip != null && !clip(lx, ly)))
                {
                    return;
                }
                var a = Math.Min(max, Math.Ceiling((1 - Math.Exp(-d * dens)) * 6) / 6);
                if (a < 0.15)
                {
                    return;
                }
                double below = blurred[i + w];
                double above = blurred[i - w];
                double right = blurred[i + 1];
                double left = blurred[i - 1];
                double here = blurred[i];
                var gy = below - above;
                var gx = right - left;
                var up = Clamp(0.5 + Math.Atan((gy * 0.9 + gx * 0.35) / (here * 0.16 + 0.03)) * 0.42 * lit);
                var fw = fField[i] / d;
                var j = (int)Clamp(Round((lField[i] / d) * (0.3 + 0.7 * up) * (n - 1) + 0.25 + fw * 1.5), 0, n - 1);
                var col = r[j];
                if (fw > 0.03)
                {
                    col = Mix(r[j], wr[j], Math.Min(0.9, Math.Ceiling(fw * 10) / 10));
                }
                p.BlendPx(sx, sy, col, a);
            });
        }

        // a box blur of radius r along one axis of a w x h field (step 1 for rows, w for columns)
        private static float[] Blur(float[] src, int w, int h, int step, int r)
        {
            var output = new float[src.Length];
            var n = h;
            var lines = w;
            if (step == 1)
            {
                n = w;
                lines = h;
            }
            var k = 1.0 / (2 * r + 1);
            for (var l = 0; l < lines; l++)
            {
                var start = l;
                if (step == 1)
                {
                    start = l * w;
                }
                double acc = 0;
                for (var j = -r; j <= r; j++)
                {
                    if (j >= 0 && j < n)
                    {
                        acc += src[start + j * step];
                    }
                }
                for (var j = 0; j < n; j++)
                {
                    output[start + j * step] = (float)(acc * k);
                    var a = j - r;
                    var b = j + r + 1;
                    if (a >= 0)
                    {
                        acc -= src[start + a * step];
                    }
                    if (b < n)
                    {
                        acc += src[start + b * step];
                    }
                }
            }
            return output;
        }

        /// <summary>A spark: a two-tone streak stretched along its screen velocity, its head cooling down s as k falls, with a point of light.</summary>
        public static void Spark(PixelPainter p, double x, double y, double vx, double vy, double k, uint[] s, double glow = 0.45, double stretch = 0.022, double max = 9)
        {
            var sp = Hypot(vx, vy);
            if (sp == 0)
            {
                sp = 1;
            }
            var ux = vx / sp;
            var uy = vy / sp;
            var len = Math.Min(max, 1 + sp * stretch);
            var head = s[2];
            if (k > 0.6)
            {
                head = s[0];
            }
            else if (k > 0.3)
            {
                head = s[1];
            }
            var tail = s[3];
            if (k > 0.5)
            {
                tail = head;
            }
            p.Line(x - ux * len, y - uy * len, x - ux * len * 0.4, y - uy * len * 0.4, tail);
            p.Line(x - ux * len * 0.45, y - uy * len * 0.45, x, y, head);
            p.Glow(x, y, 3, head, glow * k);
        }

        /// <summary>
        /// The lobes of one muzzle flash drawn from rng r: a fat fireball, a spike forward, a burst of rays round it.
        /// Frame f 2 is the same shape cooled, shorter and thinner; the same r sequence gives the same shape.
        /// </summary>
        public static List<FlashLobe> FlashShape(PixelRng r, int f, double size = 1, (double Min, double Max)? spike = null, (double Min, double Max)? diag = null, (double Min, double Max)? cross = null, (double Min, double Max)? back = null, double core = 0.7)
        {
            var spikes = spike ?? (12, 20);
            var diags = diag ?? (5, 10);
            var crosses = cross ?? (3, 6);
            var backs = back ?? (2, 4.5);
            var k = 0.6;
            var w = 0.72;
            if (f == 1)
            {
                k = 1;
                w = 1;
            }
            var s = size;
            var lobes = new List<FlashLobe>();
            {
                var a = r.Range(-0.08, 0.08);
                var len = r.Range(5.5, 8) * s * k;
                var wid = r.Range(2.1, 2.6) * s * w;
                lobes.Add(new FlashLobe { Off = 0.5, A = a, Len = len, Wid = wid, Core = 0.5 * core });
            }
            {
                var a = r.Range(-0.07, 0.07);
                var len = r.Range(spikes.Min, spikes.Max) * s * k;
                var wid = r.Range(1, 1.3) * s * w;
                lobes.Add(new FlashLobe { Off = 2, A = a, Len = len, Wid = wid, Core = 0.35 * core });
            }
            void Ray(double a, double len, double wid, bool on)
            {
                if (on)
                {
                    lobes.Add(new FlashLobe { Off = r.Range(0.5, 2), A = a, Len = len * s * k, Wid = wid * s * w, Core = 0.25 * core });
                }
            }
            foreach (var side in new[] { -1, 1 })
            {
                {
                    var a = side * r.Range(0.3, 0.8);
                    var len = r.Range(diags.Min, diags.Max);
                    var wid = r.Range(1.1, 1.5);
                    Ray(a, len, wid, r.Chance(0.85));
                }
                {
                    var a = side * r.Range(1.15, 1.7);
                    var len = r.Range(crosses.Min, crosses.Max);
                    var wid = r.Range(1, 1.3);
                    Ray(a, len, wid, r.Chance(0.6));
                }
                {
                    var a = side * r.Range(2, 2.7);
                    var len = r.Range(backs.Min, backs.Max);
                    Ray(a, len, 1, r.Chance(0.7) && f == 1);
                }
            }
            return lobes;
        }

        private static double Profile(FlashLobe l, double u)
        {
            var k = u / l.Len;
            return l.Wid * Math.Pow(Math.Max(0, 1 - k), 0.8) * Math.Min(1, 0.45 + k * 3);
        }

        /// <summary>
        /// Flash lobes at (x, y) pointing along angle a: a white (or hot) core, a hot inner flame, a body and a ragged rim,
        /// chosen by how deep a pixel sits inside its lobe; c [core, inner, body, rim]; seed varies the ragged edge.
        /// Returns the flash's reach.
        /// </summary>
        public static double Flash(PixelPainter p, double x, double y, double a, IReadOnlyList<FlashLobe> lobes, uint[] c, double seed)
        {
            var cs = Math.Cos(a);
            var s = Math.Sin(a);
            double ext = 2;
            foreach (var l in lobes)
            {
                ext = Math.Max(ext, l.Off + l.Len + 2);
            }
            p.Shade(x - ext, y - ext, x + ext, y + ext, (px, py, _) =>
            {
                var dx = px - x;
                var dy = py - y;
                var along = dx * cs + dy * s;
                var across = -dx * s + dy * cs;
                double m = -9;
                var hot = false;
                var warm = false;
                foreach (var l in lobes)
                {
                    var ca = Math.Cos(l.A);
                    var sa = Math.Sin(l.A);
                    var lu = (along - l.Off) * ca + across * sa;
                    var lv = -(along - l.Off) * sa + across * ca;
                    if (lu < -1.5 || lu > l.Len + 1)
                    {
                        continue;
                    }
                    var r = Profile(l, Math.Max(0, lu));
                    var mm = r - Math.Abs(lv);
                    if (mm > m)
                    {
                        m = mm;
                    }
                    if (lu < l.Len * l.Core && Math.Abs(lv) <= r * 0.5)
                    {
                        hot = true;
                    }
                    if (mm > r * 0.4)
                    {
                        warm = true;
                    }
                }
                var dm = 1.6 - Hypot(along - 0.5, across);
                if (dm > m)
                {
                    m = dm;
                }
                if (dm > 0.6)
                {
                    hot = true;
                }
                m += (Rand(Math.Floor(px) * 73 + Math.Floor(py) * 151, seed) - 0.5) * 0.5 * Math.Min(1, ext / 14);
                if (m < -0.85)
                {
                    return 0;
                }
                if (m < 0)
                {
                    return c[3];
                }
                if (hot)
                {
                    return c[0];
                }
                if (warm)
                {
                    return c[1];
                }
                return c[2];
            });
            return ext;
        }

        /// <summary>
        /// A brass casing thrown from (x, y) at (vx, vy) with spin va, falling under g onto a floor: it bounces until it is
        /// too slow, then slides to a stop. Closed form, so a loop can draw it at any age.
        /// </summary>
        public static CasingAt Casing(double x, double y, double vx, double vy, double va, double age, double g, double floor, double bounce = 0.42, double a0 = 0)
        {
            double t0 = 0;
            var a = a0;
            double hit = -1;
            for (var n = 0; n < 6; n++)
            {
                // time to reach the floor from (x, y) moving (vx, vy)
                var d = floor - y;
                var bigT = (-vy + Math.Sqrt(Math.Max(0, vy * vy + 2 * g * d))) / g;
                if (age - t0 < bigT)
                {
                    var u = age - t0;
                    return new CasingAt(x + vx * u, y + vy * u + 0.5 * g * u * u, a + va * u, false, hit);
                }
                x += vx * bigT;
                a += va * bigT;
                t0 += bigT;
                hit = age - t0;
                var vin = vy + g * bigT;
                y = floor;
                if (vin * bounce < 45)
                {
                    // too slow to hop again: slide to a stop, settling flat
                    var u = age - t0;
                    const double k = 7;
                    var s = (1 - Math.Exp(-k * u)) / k;
                    return new CasingAt(x + vx * 0.6 * s, floor, Round(a / Math.PI) * Math.PI, true, hit);
                }
                vy = -vin * bounce;
                vx *= 0.55;
                va *= 0.6;
            }
            return new CasingAt(x, floor, Round(a / Math.PI) * Math.PI, true, hit);
        }

        /// <summary>A casing: a 3 px brass slug turned by its angle, lit on one end; a glint when it has just hit.</summary>
        public static void DrawCasing(PixelPainter p, CasingAt k, uint[] b, double fade = 1)
        {
            var ca = Math.Cos(k.A);
            var sa = Math.Sin(k.A);
            var x = k.X;
            var y = k.Y - 0.5;
            p.Dot(x - ca, y - sa, b[1], fade);
            p.Dot(x, y, b[2], fade);
            p.Dot(x + ca, y + sa, b[3], fade);
            if (k.Hit >= 0 && k.Hit < 0.07)
            {
                p.Px(x + ca * 0.5, y - 1, White);
                p.Glow(x, y, 5, b[3], 0.55 * (1 - k.Hit / 0.07));
            }
        }

        /// <summary>A little sprite turned by ang about its pivot (px, py in sprite pixels) placed at (x, y), sampled nearest-pixel.</summary>
        public static void TurnSprite(PixelPainter p, IReadOnlyList<string> rows, IReadOnlyDictionary<char, uint> pal, double x, double y, double ang, double px, double py, double a = 1)
        {
            var h = rows.Count;
            var w = rows[0].Length;
            var cs = Math.Cos(ang);
            var sn = Math.Sin(ang);
            var reach = Hypot(Math.Max(px, w - px), Math.Max(py, h - py)) + 1;
            p.Shade(x - reach, y - reach, x + reach, y + reach, (lx, ly, o) =>
            {
                var dx = lx - x;
                var dy = ly - y;
                var i = (int)Math.Floor(dx * cs + dy * sn + px);
                var j = (int)Math.Floor(-dx * sn + dy * cs + py);
                if (i < 0 || j < 0 || i >= w || j >= h)
                {
                    return 0;
                }
                if (!pal.TryGetValue(rows[j][i], out var c) || c == 0)
                {
                    return 0;
                }
                o.C = c;
                o.A = a;
                return 0;
            });
        }

        /// <summary>
        /// A pixel shape from a builder: build(set) calls set(px, py, ch) for every interior pixel; an outline ('o') is
        /// added round it. Ready for <see cref="PixelPainter.Sprite"/> at (x0, y0).
        /// </summary>
        public static (string[] Rows, double X0, double Y0) PixelShape(int x0, int y0, int w, int h, Action<Action<int, int, char>> build)
        {
            var g = new char[h][];
            for (var j = 0; j < h; j++)
            {
                g[j] = Enumerable.Repeat('.', w).ToArray();
            }
            void Set(int px, int py, char ch)
            {
                var i = px - x0;
                var j = py - y0;
                if (i >= 0 && j >= 0 && i < w && j < h)
                {
                    g[j][i] = ch;
                }
            }
            build(Set);
            bool Solid(int i, int j) => i >= 0 && j >= 0 && i < w && j < h && g[j][i] != '.' && g[j][i] != 'o';
            for (var j = 0; j < h; j++)
            {
                for (var i = 0; i < w; i++)
                {
                    if (g[j][i] == '.' && (Solid(i - 1, j) || Solid(i + 1, j) || Solid(i, j - 1) || Solid(i, j + 1)))
                    {
                        g[j][i] = 'o';
                    }
                }
            }
            return (g.Select(row => new string(row)).ToArray(), x0, y0);
        }
    }
}
