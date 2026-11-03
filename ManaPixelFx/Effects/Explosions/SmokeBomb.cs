using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Explosions.ExplosionsLib;

namespace ManaPixelFx.Effects.Explosions
{
    /// <summary>
    /// pixelfx's explosions/smoke-bomb.js: a round canister clinks down, fuse sparking, and pops -- thick grey-violet smoke
    /// gushes out, rolls along the floor, billows into a cloud that hides all, then thins to wisps.
    /// </summary>
    public sealed class SmokeBomb : PixelEffect
    {
        private const double Duration = 5;
        private const double Pop = 0.95;
        private const double R0 = 4;
        private static readonly uint[] BandColors = [Hex("#3a1418"), Hex("#7a2a24"), Hex("#b84a34")];
        private static readonly uint[] Grit = [Hex("#5a4f6b"), Hex("#3e3650"), Hex("#776c88")];

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "explosions/smoke-bomb",
            Name = "Smoke bomb",
            Description = "A round canister clinks down, fuse sparking, and pops: thick grey-violet smoke gushes out, rolls along the floor, billows into a cloud that hides all, then thins to wisps.",
            Size = (260, 176),
            Origin = (130, 156),
            Duration = Duration,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#0e0b14", "#1e1828", "#30283e", "#463c58", "#62567a", "#867a9e", "#aea4c4" },
                ["canister"] = new[] { "#16121e", "#2a2434", "#443c52", "#686078", "#9c94ac" },
                ["sparks"] = new[] { "#fff3a6", "#ffe26b", "#ff8a2a", "#f2561d" },
                ["seed"] = 3.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o, Meta);

        private sealed class Can
        {
            public double X = -70, Y = -60, Vx = 125, Vy = -80, Va = 7, A;
            public bool Rest;

            public Can Copy() => (Can)MemberwiseClone();
        }

        private sealed class Lobe
        {
            public int Kind;
            public double T0, Vx, Vh, D, R0, R1, F, Wob, X, H, Age;
            public bool On;
        }

        // the canister's flight: it arcs down, bounces with a clink, rolls and stops; hit(x) on each bounce
        private static void Step(Can c, double dt, Action<double>? hit = null)
        {
            if (c.Rest)
            {
                return;
            }
            c.Vy += 900 * dt;
            c.X += c.Vx * dt;
            c.Y += c.Vy * dt;
            if (c.Y >= -R0 && c.Vy > 0)
            {
                c.Y = -R0;
                if (c.Vy > 80)
                {
                    hit?.Invoke(c.X);
                    c.Vy = -c.Vy * 0.38;
                    c.Vx *= 0.78;
                }
                else
                {
                    c.Vy = 0;
                }
            }
            if (c.Vy == 0 && c.Y >= -R0 - 0.01)
            {
                c.Vx -= c.Vx * 3 * dt;
                if (Math.Abs(c.Vx) < 2)
                {
                    c.Vx = 0;
                    c.Rest = true;
                }
            }
            var spin = c.Va;
            if (c.Vy == 0)
            {
                spin = c.Vx / R0;
            }
            c.A += spin * dt;
        }

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _c;
            private readonly uint[] _k;
            private readonly uint[] _sparks;
            private readonly uint[] _sm;
            private readonly PixelRng _r;
            private readonly int _oy;
            private readonly double _x0, _x1, _y0, _y1;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly Can _can;
            private readonly List<Lobe> _lobes = [];
            private double _t;
            private bool _popped;

            public Run(PixelFxOptions o, PixelEffectMeta meta)
            {
                _c = Fit(o.Ramp("palette"), 7);
                _k = Fit(o.Ramp("canister"), 5);
                _sparks = Fit(o.Ramp("sparks"), 4);
                var c = _c;
                _sm = [c[5], c[4], c[3], c[2], c[1]];
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                var (w, h) = meta.Size;
                var (ox, oy) = meta.Origin;
                _oy = oy;
                _x0 = -ox + 3;
                _x1 = w - ox - 3;
                _y0 = -oy + 3;
                _y1 = h - oy - 3;
                _ps = new Particles(rng: r, ground: _ => 0, sparks: _sparks);
                // thrown in from the left so that it comes to rest on the anchor: run its path once to see where it stops
                var start = new Can();
                var sim = start.Copy();
                for (var i = 0; i < 2400 && !sim.Rest; i++)
                {
                    Step(sim, 1 / 240.0);
                }
                _can = start.Copy();
                _can.X = start.X - sim.X;
                // the smoke, gushing out front-loaded: rollers sliding out low along the floor, a fat core over the canister,
                // a plume driven up, a low trickle keeping the base filled; they rise as they billow
                const int n = 52;
                int[] kinds = [0, 0, 1, 1, 2, 3];
                for (var i = 0; i < n; i++)
                {
                    var kind = kinds[i % 6];
                    var side = r.Sign();
                    double ang;
                    if (kind == 0 || kind == 3)
                    {
                        if (side > 0)
                        {
                            ang = -r.Range(0, 0.25);
                        }
                        else
                        {
                            ang = Math.PI + r.Range(0, 0.25);
                        }
                    }
                    else
                    {
                        ang = -Math.PI / 2 + r.Range(-0.6, 0.6);
                    }
                    // every choice is drawn, then one picked, as pixelfx's array literal does
                    double[] speeds = [r.Range(70, 170), r.Range(0, 25), r.Range(45, 95), r.Range(20, 60)];
                    double[] sizes = [r.Range(12, 17), r.Range(16, 22), r.Range(13, 18), r.Range(14, 19)];
                    var sp = speeds[kind];
                    var r1 = sizes[kind];
                    double t0;
                    if (kind == 3)
                    {
                        t0 = Pop + r.Range(0.45, 1.5);
                    }
                    else
                    {
                        t0 = Pop + 0.6 * Math.Pow(i / (double)(n - 1), 1.6) + r.Range(0, 0.03);
                    }
                    var lobe = new Lobe { Kind = kind, T0 = t0, Vx = Math.Cos(ang) * sp, Vh = -Math.Sin(ang) * sp };
                    lobe.D = r.Range(-1, 1);
                    lobe.R0 = r.Range(5, 8);
                    lobe.R1 = r1;
                    lobe.F = r.Range(-0.45, 0.4);
                    lobe.Wob = r.Range(0, Tau);
                    _lobes.Add(lobe);
                }
            }

            public override bool HasBack => true;

            private (double X, double Y) FuseTip() => (_can.X + Math.Sin(_can.A) * (R0 + 2.5), _can.Y - Math.Cos(_can.A) * (R0 + 2.5));

            private double Thin(Lobe b) => Smooth(2.5 + b.F, 3.9 + b.F * 0.6, _t);

            private double Radius(Lobe b) => (b.R0 + (b.R1 - b.R0) * (1 - Math.Exp(-b.Age / 0.28))) * (1 + 0.05 * Math.Sin(_t * 3 + b.Wob)) * (1 + 0.35 * Thin(b));

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                var c = _c;
                _t += dt;
                var t = _t;
                Step(_can, dt, x =>
                {
                    // a clink: a few bright specks and a puff of dust
                    for (var i = 0; i < 3; i++)
                    {
                        ps.Spawn(P.Spark, x, -1, r.Range(-70, 70), -r.Range(40, 110), r.Range(0.06, 0.14));
                    }
                    for (var i = 0; i < 3; i++)
                    {
                        ps.Spawn(P.Dust, x + r.Range(-2, 2), -0.5, r.Sign() * r.Range(10, 40), -r.Range(4, 14), r.Range(0.3, 0.55), Grit[2], r.Range(1.4, 2.4));
                    }
                });
                if (t < Pop)
                {
                    // the fuse: sparks spitting off its tip, quicker as it burns down, and a thread of smoke
                    var (fx, fy) = FuseTip();
                    var hurry = Smooth(Pop - 0.4, Pop, t);
                    if (r.Chance((26 + 50 * hurry) * dt))
                    {
                        ps.Spawn(P.Spark, fx, fy, r.Range(-50, 50), -r.Range(30, 100), r.Range(0.08, 0.18));
                    }
                    if (r.Chance(8 * dt))
                    {
                        ps.Spawn(P.Vapour, fx, fy - 1, r.Range(-4, 4), -r.Range(10, 18), r.Range(0.5, 0.8), c[5], r.Range(1, 1.8));
                    }
                }
                else if (!_popped)
                {
                    _popped = true;
                    // the pop: a flash, a soft light that dies fast, sparks and grit, the canister jolted
                    _lights.Add(_can.X, -9, 64, c[6], 0.75, 0.22);
                    _lights.Add(_can.X, -5, 22, White, 0.7, 0.06);
                    for (var i = 0; i < 30; i++)
                    {
                        var a = r.Range(-Math.PI - 0.35, 0.35);
                        var sp = r.Range(120, 300);
                        ps.Spawn(P.Spark, _can.X, -5, Math.Cos(a) * sp, Math.Sin(a) * sp - 40, r.Range(0.15, 0.4));
                    }
                    for (var i = 0; i < 26; i++)
                    {
                        var u = r.Range(-1, 1);
                        var a = -Math.PI / 2 + u * 0.9;
                        var sp = r.Range(80, 220) * (1 - 0.35 * Math.Abs(u));
                        var life = r.Range(0.9, 1.6);
                        var col = r.Pick(Grit);
                        double size = 1;
                        if (r.Chance(0.35))
                        {
                            size = 2;
                        }
                        ps.Spawn(P.Grit, _can.X + u * 4, -1, Math.Cos(a) * sp, Math.Sin(a) * sp, life, col, size);
                    }
                    _can.Vy = -95;
                    _can.Vx = r.Range(-25, 25);
                    _can.Va = r.Sign() * 9;
                    _can.Rest = false;
                }
                // the cloud: lobes slide out low, slow, then rise as they billow; a breeze takes them late on
                var wind = 6 * Smooth(1.8, 3.2, t);
                foreach (var b in _lobes)
                {
                    if (!b.On)
                    {
                        if (t < b.T0)
                        {
                            continue;
                        }
                        b.On = true;
                        b.X = _can.X + r.Range(-2, 2);
                        b.H = 1;
                    }
                    b.Age += dt;
                    b.Vx -= b.Vx * 2.4 * dt;
                    // rollers hug the floor for a while; everything rises as it billows, gently
                    double[] lifts = [14 * Smooth(0.7, 1.6, b.Age), 3 + 9 * Smooth(0.8, 1.8, b.Age), 6 + 12 * Smooth(0.3, 1.4, b.Age), 10 * Smooth(0.8, 1.6, b.Age)];
                    b.Vh += lifts[b.Kind] * dt;
                    b.Vh -= b.Vh * 1.6 * dt;
                    b.X += (b.Vx + wind) * dt;
                    b.H = Math.Max(0, b.H + b.Vh * dt);
                    // thinning, it sheds wisps that drift off
                    var k = Thin(b);
                    if (k > 0.12 && k < 0.85 && r.Chance(1.8 * dt))
                    {
                        var rr = Radius(b);
                        var life = Math.Min(r.Range(1, 1.6), Duration - 0.15 - t);
                        if (life > 0.3)
                        {
                            var x = b.X + r.Range(-rr, rr) * 0.5;
                            var vx = wind + r.Range(-6, 10);
                            var vy = -r.Range(8, 20);
                            ps.Spawn(P.Vapour, x, -(b.H + rr * 1.4), vx, vy, life, c[4], rr * r.Range(0.3, 0.45));
                        }
                    }
                }
                // the spent canister keeps smoking a little
                if (t > Pop + 0.5 && t < 4.1 && r.Chance(4 * dt))
                {
                    ps.Spawn(P.Vapour, _can.X, _can.Y - 3, r.Range(-3, 3), -r.Range(8, 16), r.Range(0.6, 0.9), c[4], r.Range(1.5, 2.5));
                }
                ps.Update(dt);
                Cull(ps, _x0, _y0, _x1, _y1);
                FadeEdges(ps, _x0, _x1, 20, _y1);
                _lights.Update(dt);
            }

            public override void DrawBack(PixelPainter p, double time, PixelFxOptions o)
            {
                _ps.Draw(p, "back");
                Clouds(p, true);
            }

            public override void Draw(PixelPainter p, double time, PixelFxOptions o)
            {
                var t = _t;
                var c = _c;
                // the canister: live with its fuse burning, then spent; it fades as the last smoke clears
                var fade = 1 - Smooth(4.3, 4.8, t);
                if (fade > 0)
                {
                    DrawCanister(p, _k, _can, t < Pop, fade);
                }
                if (t < Pop)
                {
                    var (fx, fy) = FuseTip();
                    var col = _sparks[1];
                    if (Math.Sin(t * 50) > 0)
                    {
                        col = White;
                    }
                    p.Px(fx, fy, col);
                    p.Glow(fx, fy, 6, _sparks[2], 0.45 + 0.2 * Math.Sin(t * 37));
                }
                Clouds(p, false);
                _ps.Draw(p, "front");
                // the flash of the pop
                var u = t - Pop;
                if (u >= 0 && u < 0.067)
                {
                    p.Circle(_can.X, -5, 6.5, c[5]);
                    p.Circle(_can.X, -5, 4.5, White);
                }
                if (u >= 0 && u < 0.16)
                {
                    Sim.Flare(p, _can.X, -5, 22 * (1 - u / 0.16), 1 - u / 0.16, [c[3], c[4], c[5], c[6]]);
                }
                if (u >= 0 && u < 0.35)
                {
                    Pool(p, _can.X, 1, 50, 8, c[6], 0.5 * (1 - u / 0.35));
                }
                _lights.Draw(p);
            }

            // far lobes behind, near ones in front; each a cel-shaded ball lit from above, so lobes low in the cloud sit in
            // its shadow a step darker; anything nearing the top of the canvas thins out
            private void Clouds(PixelPainter p, bool back)
            {
                var on = _lobes.Where(b => b.On).ToList();
                if (on.Count == 0)
                {
                    return;
                }
                double top = 0;
                foreach (var b in on)
                {
                    top = Math.Min(top, -(b.H + Radius(b) * 1.92));
                }
                foreach (var b in on.Where(b => (b.D < -0.35) == back).OrderBy(b => b.D))
                {
                    var r = Radius(b);
                    var y = -(b.H + r * 0.92) + b.D * 2.5;
                    var k = Thin(b);
                    var a = Math.Min(1, b.Age / 0.05) * Math.Pow(1 - k, 1.4) * 0.97 * (1 - Smooth(-_oy + 34, -_oy + 6, y - r));
                    if (a <= 0.01)
                    {
                        continue;
                    }
                    var shade = 0;
                    if ((y - top) / Math.Max(20, -top) > 0.55)
                    {
                        shade = 1;
                    }
                    if (k > 0.6)
                    {
                        shade++;
                    }
                    ExplosionsLib.Lobe(p, b.X, y, r, _sm, _sm, 0, 0, a, b.D, Math.Min(2, shade), 1);
                }
            }
        }

        // a round canister turned by its roll: dark metal lit from the top left, a red band, an outline, and a fuse on
        // top while it is live (spent, its cap is blown off)
        private static void DrawCanister(PixelPainter p, uint[] k, Can c, bool live, double a)
        {
            var ca = Math.Cos(c.A);
            var sa = Math.Sin(c.A);
            p.Shade(c.X - R0 - 2, c.Y - R0 - 2, c.X + R0 + 2, c.Y + R0 + 2, (px, py, o) =>
            {
                var dx = px - c.X;
                var dy = py - c.Y;
                var d = Hypot(dx, dy);
                if (d > R0 + 1)
                {
                    return 0;
                }
                o.A = a;
                if (d > R0)
                {
                    return Outline;
                }
                var v = -dx * sa + dy * ca;
                var lit = -(dx * 0.6 + dy * 0.8) / R0;
                if (Math.Abs(v) < 0.9)
                {
                    if (lit > 0.3)
                    {
                        return BandColors[2];
                    }
                    if (lit > -0.4)
                    {
                        return BandColors[1];
                    }
                    return BandColors[0];
                }
                if (d < R0 * 0.5 && lit > 0.45)
                {
                    return k[4];
                }
                if (lit > 0.5)
                {
                    return k[3];
                }
                if (lit > 0)
                {
                    return k[2];
                }
                if (lit > -0.5)
                {
                    return k[1];
                }
                return k[0];
            });
            var cx = c.X + sa * R0;
            var cy = c.Y - ca * R0;
            if (live)
            {
                p.Line(cx, cy, cx + sa * 2.5, cy - ca * 2.5, Hex("#8a6a48"), a);
            }
            else
            {
                p.Dot(cx, cy, k[0], a);
            }
        }
    }
}
