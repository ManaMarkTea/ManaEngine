using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Effects.Explosions
{
    /// <summary>Stone colours from one body colour: a lit face, the body, a shaded face, and the dust it makes.</summary>
    public readonly record struct Stone(uint Light, uint Body, uint Shade, uint Dust)
    {
        public static Stone Of(uint c) =>
            new(Mix(Scale(c, 1.35), White, 0.08), c, Scale(c, 0.64), Mix(Scale(c, 1.2), Rgb(150, 140, 150), 0.3));
    }

    /// <summary>
    /// pixelfx's explosions/_lib.js: fire balls, rocks, scorch, dirt, craters, domes, billowing lobes, a rolling
    /// fireball and a rising column. The fire balls, rocks and scorch are adapted from Sprite Fusion's open-source
    /// explosion (https://www.spritefusion.com/pixel-art-vfx/explosion), MIT License, Copyright (c) 2026 Hugo Duprez.
    /// </summary>
    public static class ExplosionsLib
    {
        public static readonly uint Outline = Hex("#1a1422");
        public static readonly uint Soot = Hex("#0d0a12");
        public static readonly uint Soot2 = Hex("#17121d");

        /// <summary>A 9-step fire ramp, white-hot to smouldering, from an 8-step palette (dark to bright).</summary>
        public static uint[] FireRamp(uint[] f) => [f[7], f[6], f[5], f[4], f[3], f[2], f[1], Mix(f[1], f[0], 0.5), f[0]];

        /// <summary>One FIRE particle drawn in the fire ramp: a darker rim, a lit core, a tongue on top, a hot heart.</summary>
        public static void FireBall(PixelPainter p, SimParticle q, uint[] fr)
        {
            var k = q.Life / q.Max;
            var age = 1 - k;
            var n = fr.Length;
            var x = q.X;
            var y = q.Y;
            var heat = Math.Min(0.999, Math.Pow(age, 0.62) + (q.Seed % 1) * 0.1);
            var idx = (int)Math.Min(n - 2, Math.Floor(heat * (n - 1)));
            var r = q.Size * (0.55 + 0.45 * Math.Min(1, age * 7)) * (1 - age * 0.55);
            var c = fr[idx];
            if (idx >= n - 2)
            {
                p.Disc(x, y, r, c, Math.Min(1, k * 3) * 0.85);
                return;
            }
            // the hottest puffs get a rim a step darker still, so a mass of them reads as separate balls
            var darker = 2;
            if (idx <= 2)
            {
                darker = 3;
            }
            p.Circle(x, y, r, fr[Math.Min(n - 1, idx + darker)]);
            p.Circle(x - r * 0.16, y - r * 0.22, r * 0.78, c);
            if (r > 1.6)
            {
                p.Circle(x, y - r * 0.9, r * 0.42, c);
            }
            if (idx <= 3 && r > 1.8)
            {
                p.Circle(x - r * 0.28, y - r * 0.34, r * 0.36, fr[Math.Max(0, idx - 1)]);
            }
            if (idx < 6)
            {
                p.Glow(x, y, r * 2.4 + 2, c, 0.18 * k);
            }
        }

        /// <summary>A particle system's layer with its FIRE puffs in the fire ramp, so a palette recolours them.</summary>
        public static void DrawPs(PixelPainter p, Particles ps, string layer, uint[] fr)
        {
            var all = ps.List;
            var fires = new List<SimParticle>();
            var rest = new List<SimParticle>();
            foreach (var q in all)
            {
                if (q.K == P.Fire)
                {
                    fires.Add(q);
                }
                else
                {
                    rest.Add(q);
                }
            }
            var back = layer == "back";
            if (!back)
            {
                foreach (var q in fires)
                {
                    if (q.Delay <= 0 && (q.Flag & PF.Back) == 0)
                    {
                        FireBall(p, q, fr);
                    }
                }
            }
            ps.List = rest;
            ps.Draw(p, layer);
            ps.List = all;
            if (back)
            {
                foreach (var q in fires)
                {
                    if (q.Delay <= 0 && (q.Flag & PF.Back) != 0)
                    {
                        FireBall(p, q, fr);
                    }
                }
            }
        }

        private static bool Lifts(int k) => k == P.Smoke || k == P.Dust || k == P.Fire || k == P.Ember || k == P.Vapour;

        /// <summary>
        /// An updraft column at x: smoke, dust, fire and embers below top and within w of the axis are carried up and
        /// squeezed toward it; near the floor (within low) they are drawn in from as far as reach. k scales it all.
        /// </summary>
        public static void Draft(Particles ps, double dt, double x = 0, double top = double.NegativeInfinity, double w = 12, double up = 150, double squeeze = 4, double pull = 0, double low = 10, double reach = 100, double k = 1)
        {
            if (k <= 0)
            {
                return;
            }
            foreach (var q in ps.List)
            {
                if (q.Delay > 0 || !Lifts(q.K) || q.Y < top)
                {
                    continue;
                }
                var dx = q.X - x;
                var ax = Math.Abs(dx);
                var u = ax / w;
                if (u < 1)
                {
                    var f = (1 - u * u) * k;
                    q.Vy -= up * f * dt;
                    q.Vx -= dx * squeeze * f * dt;
                }
                if (pull != 0 && q.Y > -low && ax > w * 0.4 && ax < reach)
                {
                    q.Vx -= Math.Sign(dx) * pull * k * (1 - ax / reach) * dt;
                }
            }
        }

        /// <summary>Drops particles that leave the box, so nothing is drawn past the canvas.</summary>
        public static void Cull(Particles ps, double x0, double y0, double x1, double y1)
        {
            var l = ps.List;
            for (var i = l.Count - 1; i >= 0; i--)
            {
                var q = l[i];
                if (q.X < x0 || q.X > x1 || q.Y < y0 || q.Y > y1)
                {
                    l.RemoveAt(i);
                }
            }
        }

        /// <summary>Soft particles fade out over w as they near the sides x0, x1 (and a bottom edge y1), and go before reaching them.</summary>
        public static void FadeEdges(Particles ps, double x0, double x1, double w = 30, double y1 = double.PositiveInfinity)
        {
            var l = ps.List;
            for (var i = l.Count - 1; i >= 0; i--)
            {
                var q = l[i];
                if (!P.IsSoft(q.K))
                {
                    continue;
                }
                var r = q.Size * 1.65;
                var e = Math.Min((Math.Min(q.X - x0, x1 - q.X) - r) / w, (y1 - q.Y - r) / 6);
                if (e <= 0)
                {
                    l.RemoveAt(i);
                }
                else if (e < 1)
                {
                    q.Life = Math.Min(q.Life, q.Max * 0.42 * e);
                }
            }
        }

        /// <summary>A soft pool of light on the floor, an ellipse rx by ry.</summary>
        public static void Pool(PixelPainter p, double x, double y, double rx, double ry, uint c, double k)
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

        /// <summary>A translucent ellipse, blended, never dithered; colour and alpha may follow d (0 centre .. 1 rim).</summary>
        public static void Haze(PixelPainter p, double x, double y, double rx, double ry, Func<double, uint> c, Func<double, double> a)
        {
            if (rx < 0.5 || ry < 0.5)
            {
                return;
            }
            p.Each(x - rx, y - ry, x + rx, y + ry, (lx, ly, sx, sy) =>
            {
                var dx = (lx - x) / rx;
                var dy = (ly - y) / ry;
                var d2 = dx * dx + dy * dy;
                if (d2 > 1)
                {
                    return;
                }
                var d = Math.Sqrt(d2);
                p.BlendPx(sx, sy, c(d), a(d));
            });
        }

        public static void Haze(PixelPainter p, double x, double y, double rx, double ry, uint c, double a) => Haze(p, x, y, rx, ry, _ => c, _ => a);
        public static void Haze(PixelPainter p, double x, double y, double rx, double ry, uint c, Func<double, double> a) => Haze(p, x, y, rx, ry, _ => c, a);

        /// <summary>Heat for a scorch, from a fire ramp (bright to dark) down to cinders.</summary>
        public static uint[] HeatRamp(uint[] fr) => [.. fr.Take(8), Mix(fr[8], Soot, 0.3), Mix(fr[8], Soot, 0.65)];

        /// <summary>
        /// The floor under a blast: a burn that spreads in a blink, glows white-hot and cools through the heat ramp to
        /// soot over cool seconds; a few cracks hold their glow twice as long. rows deep, fade 0..1.
        /// </summary>
        public static void Scorch(PixelPainter p, uint[] hr, double t, double rx0, double cool, double fade, int rows = 3)
        {
            if (fade <= 0)
            {
                return;
            }
            var rx = rx0 * Math.Min(1, t / 0.08);
            var ry = rows + 1.5;
            var n = hr.Length;
            for (var y = 0; y < rows; y++)
            {
                for (var x = Math.Floor(-rx); x <= Math.Ceiling(rx); x++)
                {
                    var dx = (x + 0.5) / rx;
                    var dy = (y + 0.5) / ry;
                    var d = Math.Sqrt(dx * dx + dy * dy);
                    if (d > 1)
                    {
                        continue;
                    }
                    var k = Rand(x * 31 + y, 3);
                    var heat = Math.Pow(Clamp(1 - t / cool), 1.4) * (1.25 - d) - k * 0.35;
                    double crack = 0;
                    if (k > 0.88 && d < 0.85)
                    {
                        crack = Math.Pow(Clamp(1 - t / (cool * 2)), 1.2) * (1.15 - d) * 0.8;
                    }
                    var h = Math.Max(heat, crack);
                    if (h > 0.06)
                    {
                        var c = hr[(int)Math.Min(n - 1, Math.Floor((1 - Math.Min(1, h)) * (n - 1)))];
                        p.Dot(x, y, c, fade);
                        if ((I32(x) & 3) == 0 || crack > heat)
                        {
                            p.Glow(x + 0.5, y, 6, c, 0.18 * h * fade);
                        }
                    }
                    else
                    {
                        var c = Soot2;
                        if (d < 0.5)
                        {
                            c = Soot;
                        }
                        var a = 1 - d;
                        if (d < 0.95 - k * 0.3)
                        {
                            a = 1;
                        }
                        p.Dot(x, y, c, a * fade);
                    }
                }
            }
        }

        /// <summary>
        /// A rock (a Bits chunk): a lit side, a shaded side and an outline, turning as it spins. Its Heat makes it molten:
        /// heart and shaded side glowing through the fire ramp, giving off light, cooling to stone.
        /// </summary>
        public static void Rock(PixelPainter p, Bit b, Stone st, uint[] fr)
        {
            var r = 0.6 + b.S * 0.75;
            var fade = Math.Min(1, b.Life / 0.5);
            var lx = Math.Cos(b.A) * r * 0.4;
            var ly = Math.Sin(b.A) * r * 0.4;
            var heat = b.Heat;
            p.Shade(b.X - r - 1, b.Y - r - 1, b.X + r + 1, b.Y + r + 1, (x, y, o) =>
            {
                var dx = x - b.X;
                var dy = y - b.Y;
                var d = Hypot(dx * 1.15, dy);
                if (d > r + 0.9)
                {
                    return 0;
                }
                o.A = fade;
                if (d > r)
                {
                    return Outline;
                }
                var lit = dx * lx + dy * ly;
                if (heat > 0.08)
                {
                    var g = heat * (1.1 - d / (r + 0.5));
                    if (lit > 0.3)
                    {
                        g += heat * 0.45;
                    }
                    if (g > 0.5)
                    {
                        if (g > 1)
                        {
                            return fr[1];
                        }
                        if (g > 0.8)
                        {
                            return fr[2];
                        }
                        if (g > 0.62)
                        {
                            return fr[3];
                        }
                        return fr[4];
                    }
                    if (g > 0.32)
                    {
                        return fr[5];
                    }
                }
                if (lit < -0.4)
                {
                    return st.Light;
                }
                if (lit > 0.6)
                {
                    return st.Shade;
                }
                return st.Body;
            });
            if (heat > 0.05)
            {
                p.Glow(b.X, b.Y, r * 2 + 3, fr[3], 0.32 * heat * fade);
            }
        }

        /// <summary>Light hugging the upper half of a circle (a dome's shock), falling off over w; box keeps it in a canvas.</summary>
        public static void GlowDome(PixelPainter p, double x, double y, double bigR, double w, uint c, double k,
            double x0 = double.NegativeInfinity, double y0 = double.NegativeInfinity, double x1 = double.PositiveInfinity, double y1 = double.PositiveInfinity)
        {
            if (k <= 0.004 || w < 1)
            {
                return;
            }
            p.Shade(Math.Max(x0, x - bigR - w), Math.Max(y0, y - bigR - w), Math.Min(x1, x + bigR + w), Math.Min(y1, y + 0.5), (px, py, o) =>
            {
                var d = Math.Abs(Hypot(px - x, py - y) - bigR) / w;
                if (d >= 1)
                {
                    return 0;
                }
                o.L = c;
                o.K = (1 - d) * (1 - d) * k;
                return 0;
            });
        }

        /// <summary>A burning thing's trail: fire near the head, swelling into thin smoke behind over life seconds.</summary>
        public static void Trail(PixelPainter p, IReadOnlyList<TrailPoint> pts, double t, uint[] fr, uint[] sm, double life = 0.5, double w = 1, double heat = 1)
        {
            var lx = double.NaN;
            var ly = double.NaN;
            for (var i = pts.Count - 1; i >= 0; i--)
            {
                var q = pts[i];
                var age = (t - q.T) / life;
                if (age >= 1)
                {
                    break;
                }
                if (Hypot(q.X - lx, q.Y - ly) < 1.6)
                {
                    continue;
                }
                lx = q.X;
                ly = q.Y;
                var r = w * (0.7 + age * 2.6);
                if (age < 0.12 * heat)
                {
                    var c = fr[5];
                    if (age < 0.05)
                    {
                        c = fr[3];
                    }
                    p.Circle(q.X, q.Y - age * 6, r * 0.8, c);
                }
                else
                {
                    p.Disc(q.X, q.Y - age * 6, r, sm[Math.Min(sm.Length - 1, 1 + (int)Math.Floor(age * 3))], 0.55 * (1 - age));
                }
            }
        }

        /// <summary>A particle system's GRIT as dirt in flight: fast bits streak, slow ones are 1 or 2 px chips; the rest as usual.</summary>
        public static void DrawDirt(PixelPainter p, Particles ps, string layer)
        {
            var all = ps.List;
            var grit = new List<SimParticle>();
            var rest = new List<SimParticle>();
            foreach (var q in all)
            {
                if (q.K == P.Grit)
                {
                    grit.Add(q);
                }
                else
                {
                    rest.Add(q);
                }
            }
            ps.List = rest;
            ps.Draw(p, layer);
            ps.List = all;
            if (layer == "back")
            {
                return;
            }
            foreach (var q in grit)
            {
                if (q.Delay > 0)
                {
                    continue;
                }
                var k = q.Life / q.Max;
                var sp = Hypot(q.Vx, q.Vy);
                if (k < 0.25 && k * 4 < 0.5)
                {
                    continue;
                }
                if (sp > 140)
                {
                    var l = Math.Min(5, sp * 0.012);
                    var ux = q.Vx / sp;
                    var uy = q.Vy / sp;
                    var col = q.Col;
                    p.Line(q.X - ux * l, q.Y - uy * l, q.X, q.Y, u =>
                    {
                        if (u < 0.5)
                        {
                            return Scale(col, 0.7);
                        }
                        return col;
                    });
                }
                else
                {
                    p.Px(q.X, q.Y, q.Col);
                    if (q.Size >= 2)
                    {
                        p.Px(q.X + 1, q.Y, q.Col);
                        p.Px(q.X, q.Y + 1, Scale(q.Col, 0.8));
                        p.Px(q.X + 1, q.Y + 1, Scale(q.Col, 0.7));
                    }
                }
            }
        }

        /// <summary>A crater at x: a dark pit with its far wall lit, a heaped lip either side, loose earth round it.</summary>
        public static void Crater(PixelPainter p, uint[] e, double x, double r, double k, double a = 1)
        {
            if (k <= 0 || a <= 0)
            {
                return;
            }
            var n = e.Length;
            var rx = r * k;
            var ry = Math.Max(1.5, r * 0.17 * k);
            for (var py = -1; py <= Math.Ceiling(ry) + 1; py++)
            {
                for (var px = Math.Floor(-rx - 1); px <= Math.Ceiling(rx + 1); px++)
                {
                    var dx = (px + 0.5) / rx;
                    var dy = (py + 0.5 - ry * 0.35) / ry;
                    var d = Math.Sqrt(dx * dx + dy * dy);
                    if (d > 1.08)
                    {
                        continue;
                    }
                    // the pit: darkest at its far side under the lit rim, the near wall a step lighter
                    uint c;
                    if (d > 0.92)
                    {
                        c = e[2];
                        if (dy < 0)
                        {
                            c = e[n - 2];
                        }
                    }
                    else if (dy < -0.2)
                    {
                        c = e[0];
                    }
                    else if (dy < 0.35)
                    {
                        c = e[1];
                    }
                    else
                    {
                        c = e[2];
                    }
                    p.Dot(x + px, py, c, a);
                }
            }
            // the lip: dirt heaped up either side, lit on top, and a scatter of loose earth
            foreach (var s in new[] { -1, 1 })
            {
                for (var i = 0; i < 9; i++)
                {
                    var u = i / 8.0;
                    var seed = 4;
                    if (s > 0)
                    {
                        seed = 3;
                    }
                    double extra = 0;
                    if (Rand(i, seed) < 0.4)
                    {
                        extra = 1;
                    }
                    var h = Round(Math.Sin(Math.PI * u) * 2.2 * k + extra * k);
                    var hx = x + s * (rx * 0.82 + u * r * 0.55);
                    for (var j = 0; j <= h; j++)
                    {
                        var c = e[2];
                        if (j == h)
                        {
                            c = e[n - 2];
                        }
                        else if (j == h - 1)
                        {
                            c = e[3];
                        }
                        p.Dot(hx, -j, c, a);
                    }
                }
            }
            for (var i = 0; i < 26; i++)
            {
                var s = 1;
                if (Rand(i, 7) < 0.5)
                {
                    s = -1;
                }
                var d = rx * (1.05 + Rand(i, 8) * 1.4);
                var y = Round(Rand(i, 9) * 3) - 1;
                if (Rand(i, 10) < k)
                {
                    var c = e[2];
                    if (Rand(i, 11) < 0.5)
                    {
                        c = e[3];
                    }
                    p.Dot(x + s * d, y, c, a);
                }
            }
        }

        /// <summary>A hemisphere of white-hot air on the floor at x: an orange rim, a hot body, a white core; k 1 to 0 as it dies.</summary>
        public static void Dome(PixelPainter p, uint[] f, double x, double r, double k)
        {
            var e = 1.4 / r;
            p.Shade(x - r - 1.5, -r - 1.5, x + r + 1.5, 0.5, (px, py, _) =>
            {
                var d = Hypot(px - x, py) / r;
                if (d > 1 + e)
                {
                    return 0;
                }
                if (d > 1)
                {
                    return f[2];
                }
                if (d > 0.8)
                {
                    if (k > 0.45)
                    {
                        return f[6];
                    }
                    return f[5];
                }
                if (d > 0.5)
                {
                    if (k > 0.45)
                    {
                        return White;
                    }
                    return f[6];
                }
                if (k > 0.2)
                {
                    return White;
                }
                return f[6];
            });
        }

        /// <summary>
        /// A lobe of a billowing cloud, shaded like a ball: lit by the sky from the top left through the smoke ramp
        /// (light to dark); fire (0..1) burns in its heart in the fire ramp; glow lights its underside from below. a:
        /// opacity; z: -1 far .. 1 near; dark shifts the smoke down the ramp; style 1 cel-shades the smoke.
        /// </summary>
        public static void Lobe(PixelPainter p, double x, double y, double r, uint[] fr, uint[] sm, double fire, double glow, double a, double z = 0, int dark = 0, int style = 0)
        {
            if (r < 0.7 || a <= 0.01)
            {
                return;
            }
            var n = fr.Length;
            var m = sm.Length;
            var back = 0;
            if (z < -0.35)
            {
                back = 1;
            }
            var rim = 1 - Math.Min(0.35, 1.6 / r);
            var seam = 1 - Math.Min(0.3, 1.3 / r);
            // the soft edge: a step more see-through than the body, less of a step as the whole lobe thins
            uint D(int i) => sm[Math.Min(m - 1, i + back + dark)];
            var soft = 0.55 + 0.4 * (1 - Math.Min(1, a));
            void Px(double lx, double ly, int sx, int sy)
            {
                var dx = (lx - x) / r;
                var dy = (ly - y) / r;
                var d2 = dx * dx + dy * dy;
                if (d2 > 1)
                {
                    return;
                }
                var d = Math.Sqrt(d2);
                var nz = Math.Sqrt(1 - d2);
                var sky = -0.5 * dx - 0.66 * dy + 0.56 * nz;
                var v = fire * (1.32 - 0.78 * d) + 0.14 * sky * fire;
                uint c;
                double k = 0;
                var solid = false;
                if (v > 0.44)
                {
                    var i = (int)Math.Min(n - 3, Math.Max(0, Math.Floor((1.3 - v) * (n - 1) * 0.78)));
                    if (d > seam)
                    {
                        i = Math.Min(6, Math.Max(4, i + 2));
                    }
                    else if (d > seam - 0.22)
                    {
                        i = Math.Min(n - 3, i + 1);
                    }
                    c = fr[i];
                    k = 0.05 * fire;
                    solid = true;
                }
                else if (style != 0)
                {
                    var lam = -0.42 * dx - 0.78 * dy + 0.46 * nz;
                    if (lam > 0.66 && d > 0.4)
                    {
                        c = D(0);
                    }
                    else if (lam > 0.16)
                    {
                        c = D(1);
                    }
                    else if (lam > -0.3)
                    {
                        c = D(2);
                    }
                    else
                    {
                        var step = 3;
                        if (lam < -0.64)
                        {
                            step = 4;
                        }
                        c = D(step);
                        var g = glow * (0.6 + 0.4 * dy);
                        if (g > 0.55)
                        {
                            c = fr[6];
                            if (g > 0.8)
                            {
                                c = fr[5];
                            }
                            k = 0.08 * g;
                        }
                        else if (g > 0.34)
                        {
                            c = Mix(c, fr[6], 0.6);
                        }
                        else if (g > 0.16)
                        {
                            c = Mix(c, fr[7], 0.45);
                        }
                    }
                }
                else
                {
                    var g = glow * (0.82 * dy + 0.3 * nz);
                    if (g > 0.45)
                    {
                        c = fr[5];
                        if (g > 0.66)
                        {
                            c = fr[4];
                        }
                        k = 0.1 * glow;
                    }
                    else if (g > 0.3)
                    {
                        c = fr[6];
                    }
                    else if (g > 0.2)
                    {
                        c = fr[7];
                    }
                    else
                    {
                        var s = sky * 0.5 + 0.5;
                        int step;
                        if (s > 0.8)
                        {
                            step = 0;
                        }
                        else if (s > 0.62)
                        {
                            step = 1;
                        }
                        else if (s > 0.42)
                        {
                            step = 2;
                        }
                        else if (s > 0.22)
                        {
                            step = 3;
                        }
                        else
                        {
                            step = 4;
                        }
                        c = D(step);
                    }
                }
                var alpha = a * soft;
                if (solid || d <= rim)
                {
                    alpha = a;
                }
                p.BlendPx(sx, sy, c, alpha);
                if (k > 0)
                {
                    p.Add(sx, sy, c, k);
                }
            }
            if (p.Sn != 0 || p.Cs != 1)
            {
                p.Each(x - r, y - r, x + r, y + r, Px);
                return;
            }
            // unrotated (the usual case): walk the screen pixels directly, only each row's span inside the circle
            var ax = p.Tx + (x - r) * p.Fx;
            var bx = p.Tx + (x + r) * p.Fx;
            var sx0 = (int)Math.Max(p.Ox, Math.Floor(Math.Min(ax, bx)));
            var sx1 = (int)Math.Min(p.Ox + p.W - 1, Math.Ceiling(Math.Max(ax, bx)));
            var sy0 = (int)Math.Max(p.Oy, Math.Floor(p.Ty + y - r));
            var sy1 = (int)Math.Min(p.Oy + p.H - 1, Math.Ceiling(p.Ty + y + r));
            for (var sy = sy0; sy <= sy1; sy++)
            {
                var ly = sy + 0.5 - p.Ty;
                var q = (ly - y) / r;
                if (q * q >= 1)
                {
                    continue;
                }
                var hw = r * Math.Sqrt(1 - q * q);
                var cx = p.Tx + x * p.Fx;
                for (int sx = (int)Math.Max(sx0, Math.Floor(cx - hw - 0.5)), e = (int)Math.Min(sx1, Math.Ceiling(cx + hw)); sx <= e; sx++)
                {
                    Px((sx + 0.5 - p.Tx) * p.Fx, ly, sx, sy);
                }
            }
        }
    }

    /// <summary>A <see cref="Plume"/>'s settings; unset ones take pixelfx's defaults.</summary>
    public sealed class PlumeOptions
    {
        public double X;
        public double Y = -20;
        public double Rise = 100;
        public double Tau = 1.2;
        public double Drift = 4;
        public (double From, double To) Tube = (8, 20);
        public (double From, double To) Ring = (1, 18);
        public double Grow = 0.9;
        public int N = 40;
        public (double Min, double Max) Size = (5, 10);
        public double Swell = 0.5;
        public double Spin = 2.4;
        public double SpinTau = 1.6;
        public double Heat = 1.15;
        public double Cool = 0.45;
        public double Under = 0.5;
        public double Tilt = 0.2;
        public double Squash = 0.7;
        public (double Start, double End) Fade = (4, 6);
        public double Glow = 1;
        public double Spread = 10;
        public int Style;
        public bool Inner;
        public bool Hollow;

        /// <summary>When the rim frays: from t0, at rate a second, until t1 (infinity for never).</summary>
        public (double T0, double Rate, double T1)? Fray;
    }

    /// <summary>
    /// A rolling fireball: lobes ride a vortex ring that climbs, swells and rolls, cooling from white-hot fire into
    /// smoke that glows from inside, then thinning away (explosions/_lib.js Plume).
    /// </summary>
    public sealed class Plume
    {
        private sealed class Lobe
        {
            public double Th, Ph, J, S, H, Dx, Dy, F, Gv;
        }

        private readonly record struct Placed(double X, double Y, double Z, double Up, double Out, double Heat, Lobe Q, double Fade, double R, double D, double C);

        private readonly PixelRng _r;
        private readonly List<Lobe> _lobes = [];
        private double _roll;

        public Plume(PixelRng r, PlumeOptions o)
        {
            O = o;
            _r = r;
            // spread over the ring evenly: the roll angle on a golden-ratio walk, the turn round the axis in order
            for (var i = 0; i < o.N; i++)
            {
                var lobe = new Lobe();
                lobe.Th = Tau * ((i * 0.618034 + r.Range(-0.08, 0.08)) % 1);
                lobe.Ph = (Tau * (i + r.Range(-0.35, 0.35))) / o.N;
                lobe.J = r.Range(-0.55, 0.12);
                lobe.S = r.Range(o.Size.Min, o.Size.Max);
                lobe.H = r.Range(-0.22, 0.08);
                lobe.Dx = r.Range(-1, 1);
                lobe.Dy = r.Range(-1, 0.4);
                lobe.F = r.Range(-0.9, 0.5);
                lobe.Gv = r.Range(0.55, 1.15);
                _lobes.Add(lobe);
            }
        }

        public PlumeOptions O { get; }
        public double T { get; private set; }

        /// <summary>Called as the rim frays: (x, y, r, ux, uy) of a small puff shed outward.</summary>
        public Action<double, double, double, double, double>? OnFray { get; set; }

        /// <summary>Its centre and radii at time t.</summary>
        public (double X, double Y, double Tube, double Ring, double G) Shape(double t)
        {
            var o = O;
            var u = Math.Max(0, t);
            var g = 1 - Math.Exp(-u / o.Grow);
            return (o.X, o.Y - o.Rise * (1 - Math.Exp(-u / o.Tau)) - o.Drift * u, o.Tube.From + (o.Tube.To - o.Tube.From) * g, o.Ring.From + (o.Ring.To - o.Ring.From) * g, g);
        }

        public (double X, double Y, double Tube, double Ring, double G) Shape() => Shape(T);

        /// <summary>How hot its heart still is.</summary>
        public double Hot(double t) => Clamp(O.Heat - O.Cool * t + 0.3);
        public double Hot() => Hot(T);

        /// <summary>How strongly the inside still glows (0..1), lingering well after the fire has gone.</summary>
        public double Ember(double t) => Clamp((O.Heat - O.Cool * t + 1) / 1.1);

        // where every lobe is at time t, far to near
        private List<Placed> LobesAt(double t)
        {
            var o = O;
            var s = Shape(t);
            var baseHeat = o.Heat - o.Cool * t;
            var span = s.Ring + s.Tube;
            if (span == 0)
            {
                span = 1;
            }
            var list = new List<Placed>();
            foreach (var q in _lobes)
            {
                var fade = 1 - Smooth(o.Fade.Start + q.F, o.Fade.End + q.F * 0.5, t);
                var loose = Math.Pow(Smooth(o.Fade.Start - 1.8 + q.F, o.Fade.End, t), 1.5) * o.Spread;
                if (fade <= 0)
                {
                    continue;
                }
                var th = q.Th - _roll;
                var rt = s.Tube * (1 + q.J);
                var rho = s.Ring + rt * Math.Cos(th);
                var up = Math.Sin(th);
                var bigX = rho * Math.Cos(q.Ph);
                var bigZ = rho * Math.Sin(q.Ph);
                // the outside cools first; the underside keeps its heat, fed from below
                var heat = baseHeat + q.H - 0.18 * Math.Max(0, Math.Cos(th)) * Clamp(t * 2) + o.Under * Math.Max(0, -up) * Clamp(baseHeat + 0.6);
                var r = q.S * (0.5 + 0.5 * s.G) * (1 + o.Swell * Clamp(1 - heat)) * (1 + 0.35 * (1 - fade));
                list.Add(new Placed(s.X + bigX + q.Dx * loose, s.Y - rt * up * o.Squash + bigZ * o.Tilt + q.Dy * loose, bigZ / span, up, Math.Cos(th), heat, q, fade, r,
                    Hypot(bigX, bigZ) / span, 1 - Math.Min(1, Math.Abs(bigX) / span)));
            }
            // stable, as JavaScript's sort is
            return list.OrderBy(e => e.Z).ToList();
        }

        public void Update(double dt)
        {
            var o = O;
            T += dt;
            _roll += o.Spin * (0.12 + 0.88 * Math.Exp(-T / o.SpinTau)) * dt;
            // late on, the rim frays: lobes on the outside shed small puffs that drift off and thin
            if (o.Fray is { } fray && OnFray != null && T > fray.T0 && T < fray.T1)
            {
                var s = Shape();
                var k = fray.Rate * dt * Smooth(fray.T0, fray.T0 + 1, T);
                foreach (var e in LobesAt(T))
                {
                    if (e.Out < 0.2 || !_r.Chance(k * e.Fade))
                    {
                        continue;
                    }
                    var ux = e.X - s.X;
                    var uy = e.Y - s.Y;
                    var l = Hypot(ux, uy);
                    if (l == 0)
                    {
                        l = 1;
                    }
                    OnFray(e.X + (ux / l) * e.R * 0.6, e.Y + (uy / l) * e.R * 0.6, e.R, ux / l, uy / l);
                }
            }
        }

        /// <summary>Draws it in the fire ramp (bright to dark) and smoke ramp (light to dark); mid() is drawn between the far and near lobes.</summary>
        public void Draw(PixelPainter p, uint[] fr, uint[] sm, Action? mid = null)
        {
            var o = O;
            var t = T;
            if (t >= o.Fade.End + 0.5)
            {
                return;
            }
            var s = Shape(t);
            var baseHeat = o.Heat - o.Cool * t;
            var span = s.Ring + s.Tube;
            if (span == 0)
            {
                span = 1;
            }
            var list = LobesAt(t);
            var age = Clamp(t / o.Fade.End);
            var ember = Ember(t);
            var reach = 0.3 + 1.15 * ember;
            var drawn = false;
            foreach (var e in list)
            {
                if (!drawn && e.Z >= 0 && mid != null)
                {
                    drawn = true;
                    mid();
                }
                var h = e.Heat;
                var fade = e.Fade;
                var fire = Clamp((h - 0.2) / 0.75);
                // the glow from inside: strongest under the middle, uneven from lobe to lobe; with inner, it draws back to
                // the middle as the cap cools
                double glow;
                if (o.Inner)
                {
                    glow = ember * Clamp((reach - e.D) / 0.45) * (0.45 + 0.55 * Math.Max(0, -e.Up)) * e.Q.Gv * o.Glow;
                }
                else
                {
                    glow = Clamp((baseHeat + 0.45) / 0.85) * o.Glow * (0.4 + 0.35 * Math.Max(0, -e.Up) + 0.4 * e.C) * e.Q.Gv;
                }
                var dark = 0;
                if (age > 0.72)
                {
                    dark++;
                }
                if (o.Hollow && e.Up < -0.35)
                {
                    dark++;
                }
                ExplosionsLib.Lobe(p, e.X, e.Y, e.R, fr, sm, fire, glow, fade * (0.82 + 0.18 * fire), e.Z, dark, o.Style);
                if (fire > 0.25)
                {
                    p.Glow(e.X, e.Y, e.R * 1.25 + 2, fr[2], 0.07 * fire);
                }
            }
            if (!drawn && mid != null)
            {
                mid();
            }
            var hot = Hot(t);
            if (hot > 0)
            {
                p.Glow(s.X, s.Y, span * 1.8 + 14, fr[3], 0.32 * hot * o.Glow);
            }
            if (o.Inner && ember > 0.05)
            {
                p.Glow(s.X, s.Y + s.Tube * 0.3, span * (0.5 + 0.7 * ember) + 8, fr[4], 0.22 * ember * ember);
            }
        }
    }

    /// <summary>A <see cref="Column"/>'s settings; unset ones take pixelfx's defaults.</summary>
    public sealed class ColumnOptions
    {
        public double W = 7;
        public double Foot = 16;
        public double Speed = 70;
        public (double Min, double Max) Size = (5, 8);
        public double N = 18;
        public double Sway = 2.5;
        public double Fade = 0.5;
        public int Style;
    }

    /// <summary>
    /// A rising column: lobes boil up a stem from the floor into whatever sits on top, wide at the foot, swaying,
    /// cooling from fire to smoke and dust (explosions/_lib.js Column).
    /// </summary>
    public sealed class Column
    {
        private sealed class Lobe
        {
            public double Y, X, S, Ph, A, V;
        }

        private readonly PixelRng _r;
        private readonly List<Lobe> _lobes = [];
        private double _acc;
        private double _t;

        public Column(PixelRng r, ColumnOptions o)
        {
            _r = r;
            O = o;
        }

        public ColumnOptions O { get; }

        private void Spawn(double y)
        {
            var lobe = new Lobe { Y = y };
            lobe.X = _r.Range(-1, 1);
            lobe.S = _r.Range(O.Size.Min, O.Size.Max);
            lobe.Ph = _r.Range(0, Tau);
            lobe.A = 0;
            lobe.V = _r.Range(0.8, 1.2);
            _lobes.Add(lobe);
        }

        /// <summary>top: the y the stem rises to; feed 0..1 how strongly it is still fed (0 stops new lobes, and it breaks up from the bottom).</summary>
        public void Update(double dt, double top, double feed)
        {
            var o = O;
            var l = Math.Max(10, -top);
            _t += dt;
            if (feed > 0)
            {
                for (_acc += dt * feed * o.N * (o.Speed / l); _acc > 1; _acc--)
                {
                    Spawn(-1);
                }
            }
            for (var i = _lobes.Count - 1; i >= 0; i--)
            {
                var b = _lobes[i];
                var u = Clamp(-b.Y / l);
                b.Y -= o.Speed * b.V * (0.45 + 0.55 * Math.Min(1, -b.Y / 24)) * dt;
                b.A = Math.Min(1, b.A + dt * 4);
                if (b.Y < top)
                {
                    _lobes.RemoveAt(i);
                    continue;
                }
                if (feed <= 0)
                {
                    b.A -= dt * (2 - u);
                    if (b.A <= 0)
                    {
                        _lobes.RemoveAt(i);
                    }
                }
            }
        }

        public void Draw(PixelPainter p, double top, uint[] fr, uint[] sm, double fire, double glow)
        {
            var o = O;
            var l = Math.Max(10, -top);
            // from the top down so the foot sits in front
            foreach (var b in _lobes.OrderBy(b => b.Y).ToList())
            {
                var u = Clamp(-b.Y / l);
                var foot = Math.Pow(1 - u, 4);
                var w = o.W + o.Foot * foot;
                var x = b.X * w * 0.45 + Math.Sin(_t * 1.7 + b.Ph + u * 3) * o.Sway * u;
                var r = b.S * (0.75 + 0.5 * foot + 0.35 * u) * Math.Min(1, b.A * 1.5);
                var end = 1 - Smooth(0.85, 1, u);
                var dark = 1;
                if (u > 0.5)
                {
                    dark = 0;
                }
                ExplosionsLib.Lobe(p, x, b.Y - r * 0.3 * foot, r, fr, sm, Clamp(fire * (1.1 - u * 0.9)), glow * Smooth(0.55, 1, u), Math.Min(1, b.A) * end * 0.9, 0, dark, o.Style);
            }
        }
    }
}
