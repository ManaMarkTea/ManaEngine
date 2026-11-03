using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Core
{
    /// <summary>Particle kinds (sim.js's P).</summary>
    public static class P
    {
        public const int Spark = 0, Mote = 1, Streak = 2, Flake = 3, Vapour = 4, Dust = 5, Star = 6, Ember = 7, Smoke = 8, Fire = 9, Brand = 10, Grit = 11;

        /// <summary>Vapour, dust and smoke: drawn on the back layer, dragged harder.</summary>
        public static bool IsSoft(int k) => k == Vapour || k == Dust || k == Smoke;
    }

    /// <summary>Particle flags (PF): a FIRE puff that leaves smoke, bigger slower smoke, drawn behind.</summary>
    public static class PF
    {
        public const int Smokes = 1, Big = 2, Back = 4;
    }

    /// <summary>One simulated particle; effects read and nudge these directly, as pixelfx's do.</summary>
    public sealed class SimParticle
    {
        public int K;
        public double X, Y, Vx, Vy, Life, Max, Size, Seed, Delay;
        public uint Col;
        public int Flag;
    }

    /// <summary>
    /// pixelfx's sim.js Particles: sparks that streak and bounce off the floor, vapour and smoke that swell and thin,
    /// fire puffs, embers, brands that set little fires where they land. Adapted from Sprite Fusion's open-source pixel
    /// art VFX (https://www.spritefusion.com/pixel-art-vfx), MIT License, Copyright (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class Particles
    {
        public const double Gravity = 950;

        internal static readonly uint[] Hot = Ramp(["#fff3a6", "#ffe26b", "#ff8a2a", "#f2561d"]);
        private static readonly (int X, int Y)[] Cross = [(1, 0), (-1, 0), (0, 1), (0, -1)];

        public Particles(PixelRng? rng = null, double seed = 1, Func<double, double>? ground = null, uint[]? sparks = null, uint haze = 0, Action<double, double>? onLand = null)
        {
            R = rng ?? new PixelRng(seed);
            Ground = ground ?? (_ => double.PositiveInfinity);
            Sparks = sparks ?? Hot;
            Haze = haze;
            OnLand = onLand;
        }

        public PixelRng R { get; }
        public List<SimParticle> List { get; set; } = [];
        public double T { get; private set; }
        public Func<double, double> Ground { get; set; }
        public uint[] Sparks { get; set; }

        /// <summary>What vapour glows with; 0 for none.</summary>
        public uint Haze { get; set; }

        /// <summary>Where a BRAND hit the floor.</summary>
        public Action<double, double>? OnLand { get; set; }

        public int Count => List.Count;

        /// <summary>delay: seconds before it appears; flag: PF bits. col 0 lets FIRE and SMOKE use their own ramps.</summary>
        public SimParticle Spawn(int k, double x, double y, double vx, double vy, double life, uint col = 0xffffffffu, double size = 1, double delay = 0, int flag = 0)
        {
            var q = new SimParticle { K = k, X = x, Y = y, Vx = vx, Vy = vy, Life = life, Max = life, Col = col, Size = size, Seed = R.Next() * 100, Delay = delay, Flag = flag };
            List.Add(q);
            return q;
        }

        public void Update(double dt)
        {
            T += dt;
            var r = R;
            var t = T;
            for (var i = List.Count - 1; i >= 0; i--)
            {
                var p = List[i];
                if (p.Delay > 0)
                {
                    p.Delay -= dt;
                    continue;
                }
                p.Life -= dt;
                if (p.Life <= 0)
                {
                    if (p.K == P.Fire && (p.Flag & PF.Smokes) != 0)
                    {
                        var big = (p.Flag & PF.Big) != 0;
                        double life;
                        if (big)
                        {
                            life = r.Range(1.3, 2.3);
                        }
                        else
                        {
                            life = r.Range(0.8, 1.4);
                        }
                        var size = p.Size * 0.7;
                        var flag = 0;
                        if (big)
                        {
                            size = p.Size * 0.85;
                            flag = PF.Big;
                        }
                        Spawn(P.Smoke, p.X, p.Y, p.Vx * 0.6, p.Vy * 0.6 - 6, life, 0, size, 0, flag);
                    }
                    List.RemoveAt(i);
                    continue;
                }
                switch (p.K)
                {
                    case P.Fire:
                        p.Vy -= 95 * dt;
                        p.Vx += Math.Sin(t * 17 + p.Seed) * 30 * dt;
                        p.Vx -= p.Vx * 5 * dt;
                        p.Vy -= p.Vy * 3.2 * dt;
                        break;
                    case P.Grit:
                    {
                        p.Vy += Gravity * dt;
                        p.Vx -= p.Vx * 0.8 * dt;
                        var g = Ground(p.X);
                        var ny = p.Y + p.Vy * dt;
                        p.X += p.Vx * dt;
                        if (p.Vy > 0 && p.Y <= g && ny >= g)
                        {
                            p.Y = g - 0.25;
                            if (p.Vy > 130 && r.Chance(0.4))
                            {
                                p.Vy = -p.Vy * 0.25;
                                p.Vx = p.Vx * 0.5 + r.Range(-10, 10);
                            }
                            else
                            {
                                p.Vx = 0;
                                p.Vy = 0;
                                p.Life = Math.Min(p.Life, 0.5);
                            }
                        }
                        else
                        {
                            p.Y = ny;
                        }
                        continue;
                    }
                    case P.Brand:
                    {
                        // a burning chunk: arcs, trails flame, and starts a little fire where it lands
                        p.Vy += Gravity * 0.8 * dt;
                        p.Vx -= p.Vx * 0.9 * dt;
                        if (r.Chance(0.9 * Math.Min(1, dt * 60)))
                        {
                            var vx = p.Vx * 0.1 + r.Range(-7, 7);
                            var vy = p.Vy * 0.1 - 10;
                            var life = r.Range(0.16, 0.32);
                            var size = r.Range(0.9, 1.7) * (0.5 + (p.Life / p.Max) * 0.6);
                            var flag = 0;
                            if (r.Chance(0.4))
                            {
                                flag = PF.Smokes;
                            }
                            Spawn(P.Fire, p.X, p.Y, vx, vy, life, 0, size, 0, flag);
                        }
                        var g = Ground(p.X);
                        var ny = p.Y + p.Vy * dt;
                        p.X += p.Vx * dt;
                        if (p.Vy > 0 && p.Y <= g && ny >= g)
                        {
                            OnLand?.Invoke(p.X, g);
                            List.RemoveAt(i);
                        }
                        else
                        {
                            p.Y = ny;
                        }
                        continue;
                    }
                    case P.Spark:
                    {
                        p.Vy += Gravity * 0.45 * dt;
                        p.Vx -= p.Vx * 2.2 * dt;
                        p.Vy -= p.Vy * 2.2 * dt;
                        var g = Ground(p.X);
                        var ny = p.Y + p.Vy * dt;
                        p.X += p.Vx * dt;
                        if (p.Vy > 0 && p.Y <= g && ny >= g)
                        {
                            p.Vy = -p.Vy * 0.45;
                            p.Vx *= 0.7;
                            p.Y = g - 0.25;
                        }
                        else
                        {
                            p.Y = ny;
                        }
                        continue;
                    }
                    case P.Streak:
                        p.Vx += p.Vx * 3 * dt;
                        p.Vy += p.Vy * 3 * dt;
                        break;
                    case P.Flake:
                        p.Vx -= p.Vx * 2.2 * dt;
                        p.Vy -= p.Vy * 2.2 * dt - 35 * dt;
                        p.X += Math.Sin(T * 2.5 + p.Seed) * 7 * dt;
                        break;
                    case P.Ember:
                        p.Vy += (-30 + Math.Sin(t * 3 + p.Seed) * 20) * dt;
                        p.Vx += Math.Cos(t * 2.3 + p.Seed) * 45 * dt;
                        p.Vx -= p.Vx * 1.6 * dt;
                        p.Vy -= p.Vy * 1.6 * dt;
                        break;
                    case P.Smoke:
                    {
                        double lift = 16;
                        if ((p.Flag & PF.Big) != 0)
                        {
                            lift = 23;
                        }
                        p.Vy -= lift * dt;
                        p.Vx += Math.Sin(t * 1.3 + p.Seed) * 8 * dt;
                        p.Vx -= p.Vx * 1.6 * dt;
                        p.Vy -= p.Vy * 1.2 * dt;
                        break;
                    }
                    case P.Mote:
                        p.Vx -= p.Vx * 0.6 * dt;
                        p.Vy -= p.Vy * 0.6 * dt;
                        break;
                    default:
                    {
                        var drag = 1.6;
                        if (P.IsSoft(p.K))
                        {
                            drag = 4;
                        }
                        p.Vx -= p.Vx * drag * dt;
                        p.Vy -= p.Vy * drag * dt + 5 * dt;
                        break;
                    }
                }
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
            }
        }

        /// <summary>"back" draws the soft kinds and those flagged back, "front" the rest, null both.</summary>
        public void Draw(PixelPainter p, string? layer = null)
        {
            foreach (var q in List)
            {
                if (q.Delay > 0)
                {
                    continue;
                }
                if (layer != null)
                {
                    var own = "front";
                    if (P.IsSoft(q.K) || (q.Flag & PF.Back) != 0)
                    {
                        own = "back";
                    }
                    if (own != layer)
                    {
                        continue;
                    }
                }
                var k = q.Life / q.Max;
                var x = q.X;
                var y = q.Y;
                var sp = Hypot(q.Vx, q.Vy);
                if (sp == 0)
                {
                    sp = 1;
                }
                var ux = q.Vx / sp;
                var uy = q.Vy / sp;
                switch (q.K)
                {
                    case P.Spark:
                    {
                        var s = Sparks;
                        var head = s[2];
                        if (k > 0.6)
                        {
                            head = s[0];
                        }
                        else if (k > 0.3)
                        {
                            head = s[1];
                        }
                        var len = Math.Min(9, 1 + sp * 0.022);
                        var tail = s[^1];
                        if (k > 0.5)
                        {
                            tail = head;
                        }
                        p.Line(x - ux * len, y - uy * len, x - ux * len * 0.4, y - uy * len * 0.4, tail);
                        p.Line(x - ux * len * 0.45, y - uy * len * 0.45, x, y, head);
                        p.Glow(x, y, 3, head, 0.45 * k);
                        break;
                    }
                    case P.Mote:
                    {
                        var len = Math.Min(16, 0.5 + sp * 0.026) * q.Size;
                        double a = 1;
                        if (k < 0.35)
                        {
                            a = k / 0.35;
                        }
                        p.Line(x - ux * len, y - uy * len, x - ux * len * 0.35, y - uy * len * 0.35, Scale(q.Col, 0.55), a);
                        p.Line(x - ux * len * 0.4, y - uy * len * 0.4, x, y, q.Col, a);
                        p.Glow(x, y, 2.5 + q.Size, q.Col, 0.4 * k);
                        break;
                    }
                    case P.Streak:
                    {
                        var l = Clamp(sp / 30, 1, 7);
                        for (var d = l; d >= 0; d -= 0.5)
                        {
                            var c = Scale(q.Col, 0.6);
                            if (d < 1)
                            {
                                c = White;
                            }
                            else if (d < l * 0.5)
                            {
                                c = q.Col;
                            }
                            var a = 1.2 - d / l;
                            if (d < l * 0.5)
                            {
                                a = 1;
                            }
                            p.Dot(x - ux * d, y - uy * d, c, a);
                        }
                        p.Glow(x, y, 4, q.Col, 0.4);
                        break;
                    }
                    case P.Flake:
                    {
                        var a = Math.Min(1, k * 3);
                        p.Dot(x, y, q.Col, a);
                        if (q.Size > 1.3)
                        {
                            foreach (var (dx, dy) in Cross)
                            {
                                p.Dot(x + dx, y + dy, Scale(q.Col, 0.8), a * 0.8);
                            }
                        }
                        break;
                    }
                    case P.Vapour:
                    {
                        var r = q.Size * (0.6 + (1 - k) * 0.9);
                        p.Disc(x, y, r, q.Col, Math.Pow(k, 1.3) * 0.45);
                        if (Haze != 0)
                        {
                            p.Glow(x, y, r * 1.8, Haze, 0.16 * k);
                        }
                        break;
                    }
                    case P.Dust:
                        p.Disc(x, y, q.Size * (0.6 + (1 - k) * 0.9), q.Col, Math.Pow(k, 1.3) * 0.7);
                        break;
                    case P.Smoke:
                    {
                        // three soft discs: a body, a denser core sitting low, a lit cap while it is young
                        var age = 1 - k;
                        var big = (q.Flag & PF.Big) != 0;
                        var grow = 0.4;
                        var thick = 0.58;
                        if (big)
                        {
                            grow = 0.5;
                            thick = 0.7;
                        }
                        var r = q.Size * (0.65 + Math.Min(1, age * 2.5) * 0.45 + age * grow);
                        var a = Math.Min(1, age * 12) * Math.Min(1, k * 2.4) * thick;
                        var c = RampAt(SmokeRamp, age * 1.05);
                        if (q.Col != 0)
                        {
                            c = Mix(q.Col, RampAt(SmokeRamp, age), age);
                        }
                        p.Disc(x, y, r, c, a * 0.45);
                        p.Disc(x + r * 0.05, y + r * 0.06, r * 0.74, c, a * 0.62);
                        if (age < 0.5)
                        {
                            p.Disc(x - r * 0.22, y - r * 0.3, r * 0.5, Mix(c, White, 0.12), a * 0.4 * (1 - age * 2));
                        }
                        break;
                    }
                    case P.Fire:
                    {
                        // a ball of flame that cools down the ramp as it rises: a darker rim, a lit core, a tongue on top
                        var age = 1 - k;
                        var heat = Math.Min(0.999, Math.Pow(age, 0.62) + (q.Seed % 1) * 0.1);
                        var f = FireRamp;
                        var n = f.Length;
                        var idx = (int)Math.Min(n - 2, Math.Floor(heat * (n - 1)));
                        var r = q.Size * (0.55 + 0.45 * Math.Min(1, age * 7)) * (1 - age * 0.55);
                        var c = f[idx];
                        if (idx >= n - 2)
                        {
                            p.Disc(x, y, r, c, Math.Min(1, k * 3) * 0.85);
                        }
                        else
                        {
                            p.Circle(x, y, r, f[Math.Min(n - 1, idx + 2)]);
                            p.Circle(x - r * 0.16, y - r * 0.22, r * 0.78, c);
                            if (r > 1.6)
                            {
                                p.Circle(x, y - r * 0.9, r * 0.42, c);
                            }
                            if (idx <= 3 && r > 1.8)
                            {
                                p.Circle(x - r * 0.28, y - r * 0.34, r * 0.36, f[Math.Max(0, idx - 1)]);
                            }
                            if (idx < 6)
                            {
                                p.Glow(x, y, r * 2.4 + 2, c, 0.18 * k);
                            }
                        }
                        break;
                    }
                    case P.Brand:
                    {
                        p.Circle(x, y, 1.6, Hot[3]);
                        var c = Hot[1];
                        if (Math.Sin(T * 40 + q.Seed) > 0)
                        {
                            c = Hot[0];
                        }
                        p.Px(x, y, c);
                        p.Px(x + 1, y, Hot[1]);
                        p.Glow(x, y, 7, Hot[2], 0.45);
                        break;
                    }
                    case P.Grit:
                        if (k > 0.25 || k * 4 > 0.5)
                        {
                            p.Px(x, y, q.Col);
                            if (q.Size >= 2)
                            {
                                p.Px(x + 1, y, q.Col);
                                p.Px(x, y + 1, Scale(q.Col, 0.8));
                                p.Px(x + 1, y + 1, Scale(q.Col, 0.7));
                            }
                        }
                        break;
                    case P.Ember:
                    {
                        var c = Sparks[2];
                        if (k > 0.6)
                        {
                            c = Sparks[0];
                        }
                        else if (k > 0.3)
                        {
                            c = Sparks[1];
                        }
                        if (k < 0.25 && Math.Sin(T * 40 + q.Seed) < 0)
                        {
                            break;
                        }
                        p.Px(x, y, c);
                        if (q.Size > 1.5)
                        {
                            p.Px(x, y + 1, Sparks[^1]);
                        }
                        p.Glow(x, y, 3 + q.Size, c, 0.35 * k);
                        break;
                    }
                    default:
                    {
                        // STAR: a twinkling cross, arms set by size
                        var tw = 0.6 + 0.4 * Math.Sin(T * (8 + (q.Seed % 5)) + q.Seed);
                        if (q.Size < 1)
                        {
                            if (tw * k > 0.25)
                            {
                                var c = q.Col;
                                if (tw > 0.8)
                                {
                                    c = White;
                                }
                                p.Px(x, y, c);
                            }
                            p.Glow(x, y, 3, q.Col, 0.4 * k * tw);
                            break;
                        }
                        Sim.Sparkle(p, x, y, q.Size * tw * Math.Min(1, k * 3), q.Col, Scale(q.Col, 0.75), 0.5 * k * tw);
                        break;
                    }
                }
            }
        }

        internal static uint RampAt(uint[] r, double t) => r[(int)Math.Min(r.Length - 1, Math.Max(0, Math.Floor(t * r.Length)))];
    }

    /// <summary>Little fires burning where something landed, feeding FIRE puffs (and the odd ember) into a particle system.</summary>
    public sealed class Blazes
    {
        private sealed class Blaze
        {
            public double X, Y, Life, Max, Acc, Power;
        }

        private readonly Particles _ps;
        private readonly List<Blaze> _list = [];

        public Blazes(Particles ps)
        {
            _ps = ps;
        }

        public int Count => _list.Count;

        public void Add(double x, double y, double life, double power) =>
            _list.Add(new Blaze { X = x, Y = y, Life = life, Max = life, Acc = _ps.R.Next(), Power = power });

        public void Update(double dt)
        {
            var r = _ps.R;
            for (var i = _list.Count - 1; i >= 0; i--)
            {
                var b = _list[i];
                b.Life -= dt;
                if (b.Life <= 0)
                {
                    _list.RemoveAt(i);
                    continue;
                }
                var k = b.Life / b.Max;
                var intensity = b.Power;
                if (k <= 0.7)
                {
                    intensity = b.Power * (0.35 + (k / 0.7) * 0.65);
                }
                for (b.Acc += dt * (6 + 16 * intensity); b.Acc > 1; b.Acc--)
                {
                    var s = r.Range(1.2, 2.8) * intensity;
                    var x = b.X + r.Range(-1.5, 1.5);
                    var vx = r.Range(-6, 6);
                    var vy = r.Range(-30, -12);
                    var life = r.Range(0.28, 0.6);
                    var flag = PF.Back;
                    if (r.Chance(0.3))
                    {
                        flag |= PF.Smokes;
                    }
                    _ps.Spawn(P.Fire, x, b.Y - s * 0.6, vx, vy, life, 0, s, 0, flag);
                    if (r.Chance(0.06))
                    {
                        _ps.Spawn(P.Ember, b.X, b.Y - 1.5, r.Range(-15, 15), r.Range(-45, -15), r.Range(0.6, 1.4), Particles.Hot[2], 1, 0, PF.Back);
                    }
                }
            }
        }

        /// <summary>A hot pixel and a glow at each fire's foot.</summary>
        public void DrawBack(PixelPainter p)
        {
            foreach (var b in _list)
            {
                var k = b.Life / b.Max;
                var c = Particles.Hot[3];
                if (k > 0.4)
                {
                    c = Particles.Hot[2];
                    if (Math.Sin(b.Life * 31 + b.X) > 0)
                    {
                        c = Particles.Hot[1];
                    }
                }
                p.Px(b.X, b.Y, c);
                p.Glow(b.X, b.Y, 6 + 5 * k * b.Power, Particles.Hot[2], 0.3 * k * b.Power);
            }
        }
    }

    /// <summary>A point of a moving thing's trail, oldest first.</summary>
    public readonly record struct TrailPoint(double X, double Y, double T);

    /// <summary>Debris kinds (B).</summary>
    public static class B
    {
        public const int Shard = 0, Chunk = 1, Drip = 2;
    }

    /// <summary>One piece of debris; <see cref="Heat"/> and <see cref="Tag"/> are what effects hang on theirs.</summary>
    public sealed class Bit
    {
        public int Kind = B.Shard;
        public double X, Y, Vx, Vy, A, Va;
        public double S = 1;
        public uint C = 0xffffffffu;
        public uint[]? Ramp;
        public double G = 1;
        public double Drag;
        public double Bounce;
        public bool Rest;
        public double Life = 1;
        public double Max;

        /// <summary>0..1 how molten a rock still is (explosions).</summary>
        public double Heat;

        /// <summary>Its fall speed before this step, to tell when it hit (explosions' pvy).</summary>
        public double Pvy;

        /// <summary>Its landing already answered with dust (explosions' dusted, thud).</summary>
        public bool Landed;

        /// <summary>Where it has been, for a smoke trail.</summary>
        public List<TrailPoint>? Pts;

        /// <summary>Anything else an effect keeps on a piece.</summary>
        public object? Tag;
    }

    /// <summary>Rigid debris that tumbles, bounces and settles (sim.js Bits).</summary>
    public sealed class Bits
    {
        private static readonly uint[] DefaultRamp = PixelColor.Ramp(["#1c2030", "#3a4a66", "#6e86a8", "#a8c0dc", "#e8f2ff"]);

        public Bits(Func<double, double>? ground = null)
        {
            Ground = ground ?? (_ => double.PositiveInfinity);
        }

        public List<Bit> List { get; } = [];
        public Func<double, double> Ground { get; set; }
        public int Count => List.Count;

        public Bit Add(Bit b)
        {
            b.Max = b.Life;
            List.Add(b);
            return b;
        }

        public void Update(double dt)
        {
            for (var i = List.Count - 1; i >= 0; i--)
            {
                var b = List[i];
                b.Life -= dt;
                if (b.Life <= 0)
                {
                    List.RemoveAt(i);
                    continue;
                }
                if (b.Rest)
                {
                    continue;
                }
                b.Vy += Particles.Gravity * b.G * dt;
                b.Vx -= b.Vx * b.Drag * dt;
                b.Vy -= b.Vy * b.Drag * dt;
                b.X += b.Vx * dt;
                b.Y += b.Vy * dt;
                b.A += b.Va * dt;
                var floor = Ground(b.X);
                var sp = b.Vy;
                if (b.G <= 0 || b.Y < floor || sp <= 0)
                {
                    continue;
                }
                b.Y = floor - 0.01;
                if (b.Kind == B.Drip)
                {
                    b.Life = 0;
                }
                else if (b.Bounce > 0 && sp > 70)
                {
                    b.Vy = -sp * b.Bounce;
                    b.Vx *= 0.7;
                    b.Va *= 0.6;
                }
                else
                {
                    b.Rest = true;
                    b.Vx = 0;
                    b.Vy = 0;
                }
            }
        }

        public void Draw(PixelPainter p)
        {
            foreach (var b in List)
            {
                var a = Math.Min(1, b.Life / 0.4);
                var ca = Math.Cos(b.A);
                var sa = Math.Sin(b.A);
                if (b.Kind == B.Shard)
                {
                    for (var d = -b.S / 2; d <= b.S / 2; d += 0.5)
                    {
                        var c = b.C;
                        if (d > b.S / 2 - 0.6)
                        {
                            c = White;
                        }
                        p.Dot(b.X + ca * d, b.Y + sa * d, c, a);
                    }
                }
                else if (b.Kind == B.Drip)
                {
                    p.Dot(b.X, b.Y, b.C, a);
                    p.Dot(b.X, b.Y - 1, b.C, a * 0.5);
                }
                else
                {
                    // a shaded chunk: lit top left, dark bottom right, a dark rim
                    var r = b.Ramp ?? DefaultRamp;
                    var n = r.Length;
                    var x = Round(b.X);
                    var y = Round(b.Y);
                    var rr = Math.Ceiling(b.S) + 1;
                    for (var py = -rr; py <= rr; py++)
                    {
                        for (var px = -rr; px <= rr; px++)
                        {
                            var u = (px + 0.5) * ca + (py + 0.5) * sa;
                            var v = -(px + 0.5) * sa + (py + 0.5) * ca;
                            var d = Math.Abs(u) / b.S + Math.Abs(v) / (b.S * 0.62);
                            if (d > 1.3)
                            {
                                continue;
                            }
                            uint c;
                            if (d > 1)
                            {
                                c = r[0];
                            }
                            else if (v < 0)
                            {
                                c = r[n - 2];
                                if (u < 0)
                                {
                                    c = r[n - 1];
                                }
                            }
                            else
                            {
                                c = r[Math.Max(1, n - 4)];
                                if (u < 0)
                                {
                                    c = r[n - 3];
                                }
                            }
                            p.Dot(x + px, y + py, c, a);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Lights that flare and die (sim.js Lights).</summary>
    public sealed class Lights
    {
        private sealed class Light
        {
            public double X, Y, R, K, Life, Max;
            public uint Color;
        }

        private readonly List<Light> _list = [];

        public int Count => _list.Count;

        public void Add(double x, double y, double r, uint color, double k, double life) =>
            _list.Add(new Light { X = x, Y = y, R = r, Color = color, K = k, Life = life, Max = life });

        public void Update(double dt)
        {
            for (var i = _list.Count - 1; i >= 0; i--)
            {
                _list[i].Life -= dt;
                if (_list[i].Life <= 0)
                {
                    _list.RemoveAt(i);
                }
            }
        }

        public void Draw(PixelPainter p)
        {
            foreach (var l in _list)
            {
                var k = l.Life / l.Max;
                p.Glow(l.X, l.Y, l.R * (0.55 + 0.45 * k), l.Color, l.K * k * k);
            }
        }
    }

    /// <summary>sim.js's shapes: a star glint, an 8-ray flare, a ring on a tilted plane, a faceted crystal.</summary>
    public static class Sim
    {
        private static readonly (int X, int Y)[] Cross = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        private static readonly (int X, int Y)[] Diag = [(1, 1), (1, -1), (-1, 1), (-1, -1)];

        /// <summary>A star glint: arms of length arm in c with tips in tip, a white heart, light.</summary>
        public static void Sparkle(PixelPainter p, double x, double y, double arm, uint c, uint tip, double k = 0.6)
        {
            var n = (int)Round(arm);
            if (n < 1)
            {
                return;
            }
            var bx = Math.Floor(x);
            var by = Math.Floor(y);
            for (var d = 1; d <= n; d++)
            {
                var cc = c;
                if (d > n * 0.6)
                {
                    cc = tip;
                }
                foreach (var (dx, dy) in Cross)
                {
                    p.Px(bx + dx * d, by + dy * d, cc);
                }
            }
            if (n >= 3)
            {
                foreach (var (dx, dy) in Diag)
                {
                    p.Px(bx + dx, by + dy, c);
                }
            }
            if (n >= 6)
            {
                foreach (var (dx, dy) in Diag)
                {
                    p.Px(bx + dx * 2, by + dy * 2, tip);
                }
            }
            p.Px(bx, by, White);
            p.Glow(x, y, 4 + n * 2, c, k);
        }

        public static void Sparkle(PixelPainter p, double x, double y, double arm, uint c) => Sparkle(p, x, y, arm, c, c);

        /// <summary>An 8-ray flare: long rays on the axes, shorter between, fading along their length; ramp dark to bright.</summary>
        public static void Flare(PixelPainter p, double x, double y, double l, double k, uint[] ramp)
        {
            if (l < 1)
            {
                return;
            }
            var n = ramp.Length;
            for (var i = 0; i < 8; i++)
            {
                var an = (i * Math.PI) / 4;
                var len = l * 0.3;
                if (i % 4 == 0)
                {
                    len = l;
                }
                else if (i % 2 == 0)
                {
                    len = l * 0.6;
                }
                var ca = Math.Cos(an);
                var sa = Math.Sin(an);
                for (double d = 0; d <= len; d += 0.5)
                {
                    var u = d / len;
                    var c = ramp[n - 3];
                    if (u < 0.35)
                    {
                        c = White;
                    }
                    else if (u < 0.7)
                    {
                        c = ramp[n - 2];
                    }
                    var a = k * 1.5 * (1 - u) + 0.2;
                    if (u < 0.6)
                    {
                        a = 1;
                    }
                    p.Dot(x + ca * d, y + sa * d, c, a);
                }
            }
        }

        /// <summary>
        /// A ring on a tilted plane (sy squashes it: 0.16 lies on the floor), th thick inward, alpha k, coloured by depth
        /// through the band with ramp (dark to bright). keep(angle) picks the part drawn: sin(g) &lt; 0 is the far half.
        /// </summary>
        public static void Band(PixelPainter p, double cx, double cy, double r, double sy, double th, double k, uint[] ramp, Func<double, bool>? keep = null, bool hot = false)
        {
            if (r <= 0 || k <= 0)
            {
                return;
            }
            var n = ramp.Length;
            var m = Math.Ceiling(r * Tau * 1.2);
            for (var j = 0; j < m; j++)
            {
                var ang = (j / m) * Tau;
                var ca = Math.Cos(ang);
                var sa = Math.Sin(ang);
                if (keep != null && !keep(ang))
                {
                    continue;
                }
                for (var o = th; o >= 0; o -= 0.5)
                {
                    var u = o / th;
                    uint c;
                    if (u < 0.15)
                    {
                        c = ramp[n - 1];
                        if (hot)
                        {
                            c = White;
                        }
                    }
                    else if (u < 0.4)
                    {
                        c = ramp[n - 2];
                    }
                    else if (u < 0.7)
                    {
                        c = ramp[n - 3];
                    }
                    else
                    {
                        c = ramp[n - 4];
                    }
                    var a = k * (1.15 - u);
                    if (u < 0.4)
                    {
                        a = k * 1.2;
                    }
                    p.Dot(cx + ca * (r - o), cy + sa * (r - o) * sy, c, a);
                }
            }
        }

        /// <summary>
        /// A faceted crystal spike on (x, base), h tall, w wide at its base, leaning lean at the tip, shaded in ramp (dark
        /// to bright, 6+ steps). a: alpha; white flashes it white; glint (0..1, or -1) the height of a sweeping highlight.
        /// </summary>
        public static void Crystal(PixelPainter p, double x, double baseY, double h, double w, double lean, uint[] ramp, double a = 1, bool white = false, double glint = -1)
        {
            if (h < 1.5)
            {
                return;
            }
            var n = ramp.Length;
            uint C(int i) => ramp[Math.Max(0, n - 1 - i)];
            var top = baseY - h;
            var ridge0 = x - w * 0.14;
            bool Inside(double px, double py)
            {
                var u = (baseY - (py + 0.5)) / h;
                return u >= 0 && u <= 1 && Math.Abs(px + 0.5 - (x + lean * u)) <= (w / 2) * (1 - u) + 0.35;
            }
            for (var py = Math.Floor(top) - 1; py < Math.Ceiling(baseY); py++)
            {
                for (var px = Math.Floor(x - w / 2 - Math.Abs(lean) - 2); px <= Math.Ceiling(x + w / 2 + Math.Abs(lean) + 2); px++)
                {
                    if (!Inside(px, py))
                    {
                        if (Inside(px - 1, py) || Inside(px + 1, py) || Inside(px, py + 1) || (Inside(px, py - 1) && py < baseY - 1))
                        {
                            p.Dot(px, py, ramp[0], a);
                        }
                        continue;
                    }
                    if (white)
                    {
                        p.Dot(px, py, White, a);
                        continue;
                    }
                    var u = (baseY - (py + 0.5)) / h;
                    var rx = ridge0 + (x + lean - ridge0) * u;
                    var c = x + lean * u;
                    var hw = (w / 2) * (1 - u) + 0.35;
                    var fx = px + 0.5;
                    uint col;
                    if (u > 0.9)
                    {
                        col = White;
                    }
                    else if (Math.Abs(fx - rx) < 0.55)
                    {
                        col = C(1);
                    }
                    else if (fx < rx)
                    {
                        if (fx - (c - hw) < 1)
                        {
                            col = C(1);
                        }
                        else if ((fx - (c - hw)) / Math.Max(1, rx - (c - hw)) > 0.55)
                        {
                            col = C(2);
                        }
                        else
                        {
                            col = C(3);
                        }
                    }
                    else if (c + hw - fx < 1.2)
                    {
                        col = C(Math.Min(n - 2, 6));
                    }
                    else if ((fx - rx) / Math.Max(1, c + hw - rx) > 0.5)
                    {
                        col = C(5);
                    }
                    else
                    {
                        col = C(4);
                    }
                    if (glint >= 0 && fx <= rx + 0.6)
                    {
                        var g = Math.Abs(u - glint);
                        if (g < 0.05)
                        {
                            col = White;
                        }
                        else if (g < 0.1)
                        {
                            col = C(1);
                        }
                    }
                    p.Dot(px, py, col, a);
                }
            }
        }
    }
}
