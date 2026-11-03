using ManaPixelFx.Core;
using static ManaPixelFx.Core.PixelColor;
using static ManaPixelFx.Core.PixelMath;
using static ManaPixelFx.Effects.Explosions.ExplosionsLib;

namespace ManaPixelFx.Effects.Explosions
{
    /// <summary>
    /// pixelfx's explosions/dirt-explosion.js: a buried charge -- a flash under the floor, a V of clods and grit thrown
    /// high, a brown dust plume that billows and settles, a crater left behind. The earth recipe is adapted from Sprite
    /// Fusion's open-source explosion, MIT, (c) 2026 Hugo Duprez.
    /// </summary>
    public sealed class DirtExplosion : PixelEffect
    {
        private const double Duration = 3.6;

        public override PixelEffectMeta Meta { get; } = new()
        {
            Id = "explosions/dirt-explosion",
            Name = "Dirt explosion",
            Description = "A buried charge goes off: a flash under the floor, a V of dirt clods and grit thrown high, a brown dust plume that billows and settles, and a crater left behind.",
            Size = (260, 240),
            Origin = (130, 206),
            Duration = Duration,
            Fps = 30,
            Options = new Dictionary<string, object>
            {
                ["palette"] = new[] { "#1c1610", "#33281c", "#4f3f2c", "#6e5a40", "#927a58", "#b89f78", "#dcc8a2" },
                ["fire"] = new[] { "#4a1712", "#c9281f", "#f2561d", "#ff8a2a", "#ffc23d", "#ffe26b", "#fff3a6", "#ffffff" },
                ["seed"] = 3.0,
            },
        };

        public override IPixelSimulation Create(PixelFxOptions o) => new Run(o, Meta);

        private sealed class Run : PixelSimulation
        {
            private readonly uint[] _e;
            private readonly uint[] _f;
            private readonly PixelRng _r;
            private readonly double _x0, _x1, _y0, _y1;
            private readonly Stone _clod;
            private readonly uint _dust;
            private readonly Particles _ps;
            private readonly Lights _lights = new();
            private readonly Bits _clods = new(_ => 0);
            private double _t;

            public Run(PixelFxOptions o, PixelEffectMeta meta)
            {
                _e = Fit(o.Ramp("palette"), 7);
                _f = Fit(o.Ramp("fire"), 8);
                var e = _e;
                var f = _f;
                _r = new PixelRng(o.Number("seed"));
                var r = _r;
                var (w, h) = meta.Size;
                var (ox, oy) = meta.Origin;
                _x0 = -ox + 3;
                _x1 = w - ox - 3;
                _y0 = -oy + 3;
                _y1 = h - oy - 3;
                _clod = new Stone(e[5], e[3], e[2], 0);
                _dust = Mix(e[4], Rgb(150, 140, 150), 0.2);
                var soil = Mix(e[3], Rgb(90, 80, 90), 0.2);
                var dark = Mix(e[2], Rgb(60, 52, 60), 0.3);
                _ps = new Particles(rng: r, ground: _ => 0, sparks: [f[6], f[5], f[3], f[2]]);
                var ps = _ps;

                _lights.Add(0, -10, 56, f[4], 0.7, 0.22);
                _lights.Add(0, -4, 26, White, 0.6, 0.06);
                // the charge: a few sparks out of the hole
                for (var i = 0; i < 14; i++)
                {
                    var a = -Math.PI / 2 + r.Range(-0.7, 0.7);
                    var sp = r.Range(160, 320);
                    ps.Spawn(P.Spark, 0, -2, Math.Cos(a) * sp, Math.Sin(a) * sp, r.Range(0.15, 0.35));
                }
                // the fountain: grit in a V, clods thrown high, a few pebbles
                for (var i = 0; i < 300; i++)
                {
                    var side = r.Sign();
                    var a = -Math.PI / 2 + side * r.Range(0.05, 0.62);
                    var sp = r.Range(150, 470) * (1 - 0.3 * Math.Abs(a + Math.PI / 2));
                    var x = r.Range(-4, 4);
                    var life = r.Range(1.2, 2.2);
                    var col = r.Pick([e[1], e[2], e[2], e[3], e[3], e[4], e[5]]);
                    double size = 1;
                    if (r.Chance(0.45))
                    {
                        size = 2;
                    }
                    ps.Spawn(P.Grit, x, -1, Math.Cos(a) * sp, Math.Sin(a) * sp, life, col, size, r.Range(0, 0.06));
                }
                for (var i = 0; i < 30; i++)
                {
                    var side = r.Sign();
                    var a = -Math.PI / 2 + side * r.Range(0.1, 0.6);
                    var sp = r.Range(200, 430);
                    var bit = new Bit
                    {
                        Kind = B.Chunk, X = r.Range(-5, 5), Y = -2, Vx = Math.Cos(a) * sp, Vy = Math.Sin(a) * sp, G = 1, Drag = 0.35, Bounce = 0.25,
                        Life = r.Range(2.2, 3), A = r.Range(0, Tau), Va = r.Range(-12, 12),
                    };
                    if (i < 12)
                    {
                        bit.S = r.Range(2.6, 3.8);
                    }
                    else
                    {
                        bit.S = r.Range(1.4, 2.4);
                    }
                    _clods.Add(bit);
                }
                // the dust: a plume punched straight up, two lobes riding the V, and a skirt rolling out along the floor
                for (var i = 0; i < 22; i++)
                {
                    var x = r.Range(-7, 7);
                    var y = -r.Range(2, 12);
                    var vx = r.Range(-25, 25);
                    var vy = -r.Range(70, 180);
                    var life = r.Range(1.8, 2.6);
                    var col = _dust;
                    if (r.Chance(0.6))
                    {
                        col = soil;
                    }
                    ps.Spawn(P.Smoke, x, y, vx, vy, life, col, r.Range(9, 14), r.Range(0, 0.14), PF.Big);
                }
                for (var i = 0; i < 14; i++)
                {
                    var side = r.Sign();
                    var a = -Math.PI / 2 + side * r.Range(0.35, 0.6);
                    var sp = r.Range(110, 210);
                    var x = side * r.Range(2, 8);
                    var y = -r.Range(2, 8);
                    var life = r.Range(1.6, 2.4);
                    var col = _dust;
                    if (r.Chance(0.5))
                    {
                        col = soil;
                    }
                    ps.Spawn(P.Smoke, x, y, Math.Cos(a) * sp, Math.Sin(a) * sp, life, col, r.Range(8, 12), r.Range(0.02, 0.1), PF.Big);
                }
                for (var i = 0; i < 16; i++)
                {
                    var side = r.Sign();
                    ps.Spawn(P.Smoke, side * r.Range(4, 16), -r.Range(1, 5), side * r.Range(70, 200), -r.Range(2, 12), r.Range(1.4, 2.2), _dust, r.Range(6, 9), r.Range(0.02, 0.1), PF.Big);
                }
                // dark soil thrown up with it, heavier and lower
                for (var i = 0; i < 8; i++)
                {
                    ps.Spawn(P.Smoke, r.Range(-8, 8), -r.Range(2, 8), r.Range(-40, 40), -r.Range(40, 110), r.Range(1.2, 1.8), dark, r.Range(6, 9), r.Range(0, 0.05));
                }
            }

            public override bool HasBack => true;

            public override void Update(double dt, PixelFxOptions o)
            {
                var r = _r;
                var ps = _ps;
                var e = _e;
                _t += dt;
                foreach (var b in _clods.List)
                {
                    b.Pvy = b.Vy;
                }
                _clods.Update(dt);
                // clods thud down: a puff of dust and a spray of crumbs where they land, the big ones break up
                for (var i = _clods.List.Count - 1; i >= 0; i--)
                {
                    var b = _clods.List[i];
                    if (b.X < _x0 + 4 || b.X > _x1 - 4)
                    {
                        _clods.List.RemoveAt(i);
                        continue;
                    }
                    var landed = (b.Pvy > 0 && b.Vy <= 0) || (b.Rest && !b.Landed);
                    if (!landed || b.Pvy < 80)
                    {
                        if (b.Rest)
                        {
                            b.Landed = true;
                        }
                        continue;
                    }
                    b.Landed = b.Rest;
                    for (var j = 0; j < 2 + Round(b.S); j++)
                    {
                        ps.Spawn(P.Dust, b.X + r.Range(-2, 2), -0.5, r.Sign() * r.Range(15, 55), -r.Range(4, 22), r.Range(0.35, 0.7), _dust, r.Range(1.6, 1.2 + b.S));
                    }
                    for (var j = 0; j < Round(b.S * 3); j++)
                    {
                        ps.Spawn(P.Grit, b.X, -1, r.Range(-70, 70), -r.Range(40, 140), r.Range(0.8, 1.4), r.Pick([e[2], e[3], e[4]]), 1);
                    }
                    if (b.S > 2.2 && b.Pvy > 200)
                    {
                        b.S *= 0.6;
                    }
                }
                ps.Update(dt);
                Cull(ps, _x0, _y0, _x1, _y1);
                FadeEdges(ps, _x0, _x1, 24, _y1);
                _lights.Update(dt);
            }

            private void Wave(PixelPainter p, bool far)
            {
                var k = _t / 0.45;
                if (k >= 1)
                {
                    return;
                }
                Func<double, bool> keep = g => Math.Sin(g) >= 0;
                if (far)
                {
                    keep = g => Math.Sin(g) < 0;
                }
                Sim.Band(p, 0, 0.5, 90 * Ease.OutCubic(k), 0.16, Math.Max(1, 3 * (1 - k)), 1.1 - k, [_e[2], _e[4], _e[5], _e[6]], keep);
            }

            public override void DrawBack(PixelPainter p, double t, PixelFxOptions o)
            {
                Crater(p, _e, 0, 28, Ease.OutCubic(Math.Min(1, _t / 0.12)), 1 - Smooth(Duration - 0.7, Duration - 0.1, _t));
                Wave(p, true);
                DrawDirt(p, _ps, "back");
            }

            public override void Draw(PixelPainter p, double t, PixelFxOptions o)
            {
                Wave(p, false);
                // the flash out of the ground: a squat dome of white-hot air for a frame or two
                if (_t < 0.09)
                {
                    Dome(p, _f, 0, 15 * (1 - _t * 3), 1 - _t / 0.09);
                }
                foreach (var b in _clods.List)
                {
                    Rock(p, b, _clod, _f);
                }
                DrawDirt(p, _ps, "front");
                Pool(p, 0, 1, 60, 10, _f[3], 0.5 * Math.Max(0, 1 - _t / 0.3));
                _lights.Draw(p);
            }
        }
    }
}
