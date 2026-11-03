namespace ManaPixelFx.Core
{
    /// <summary>What an effect is: its id and name, canvas size and origin, length, loop, and its own options' defaults.</summary>
    public sealed class PixelEffectMeta
    {
        public required string Id { get; init; }
        public required string Name { get; init; }

        /// <summary>What it looks like and how it moves, in pixelfx's words.</summary>
        public string Description { get; init; } = string.Empty;
        public required (int W, int H) Size { get; init; }

        /// <summary>The effect's origin on its canvas, from the top left: usually where it stands on the floor.</summary>
        public required (int X, int Y) Origin { get; init; }

        public double Duration { get; init; } = 1;
        public bool Loop { get; init; }
        public int Fps { get; init; } = 30;

        /// <summary>The defaults of its options (palette ramps as hex strings, numbers, flags), by name.</summary>
        public IReadOnlyDictionary<string, object> Options { get; init; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// One effect, ported from a pixelfx file: a pure one draws the frame at t from t alone (<see cref="Draw"/>);
    /// a simulated one keeps state in what <see cref="Create"/> returns.
    /// </summary>
    public abstract class PixelEffect
    {
        public abstract PixelEffectMeta Meta { get; }

        /// <summary>The frame at t seconds, drawn around the origin; t runs 0..duration (wrapped for loops).</summary>
        public virtual void Draw(PixelPainter p, double t, PixelFxOptions o)
        {
        }

        /// <summary>What goes behind a character, drawn first; most effects have none.</summary>
        public virtual bool HasBack => false;

        public virtual void DrawBack(PixelPainter p, double t, PixelFxOptions o)
        {
        }

        /// <summary>A simulated effect's state; null for a pure effect, which is drawn from t alone.</summary>
        public virtual IPixelSimulation? Create(PixelFxOptions o) => null;
    }

    /// <summary>A simulation's state with nothing behind a character unless it says so.</summary>
    public abstract class PixelSimulation : IPixelSimulation
    {
        public abstract void Update(double dt, PixelFxOptions o);
        public virtual bool HasBack => false;

        public virtual void DrawBack(PixelPainter p, double t, PixelFxOptions o)
        {
        }

        public abstract void Draw(PixelPainter p, double t, PixelFxOptions o);
    }

    /// <summary>A simulated effect's live state: stepped at 120 Hz or finer, drawn from what it holds.</summary>
    public interface IPixelSimulation
    {
        void Update(double dt, PixelFxOptions o);
        bool HasBack { get; }
        void DrawBack(PixelPainter p, double t, PixelFxOptions o);
        void Draw(PixelPainter p, double t, PixelFxOptions o);
    }

    /// <summary>
    /// An effect's options: its defaults with the caller's on top (resolveOptions). A ramp of a different length than
    /// its default is resampled to the default's, so an effect can index steps by number whatever palette it gets.
    /// </summary>
    public sealed class PixelFxOptions
    {
        private readonly Dictionary<string, object> _values;

        public PixelFxOptions(PixelEffectMeta meta, IReadOnlyDictionary<string, object>? overrides = null)
        {
            _values = new Dictionary<string, object>(meta.Options, StringComparer.Ordinal);
            if (overrides == null)
            {
                return;
            }
            foreach (var (key, value) in overrides)
            {
                if (meta.Options.TryGetValue(key, out var fallback) && fallback is string[] ramp && value is string[] given && given.Length != ramp.Length && given.Length > 0)
                {
                    _values[key] = PixelColor.Fit(given, ramp.Length).Select(PixelColor.ToHex).ToArray();
                    continue;
                }
                _values[key] = value;
            }
        }

        public double X => Number("x");
        public double Y => Number("y");
        public bool Flip => Flag("flip");
        public double Angle => Number("angle");

        /// <summary>Glow strength (the light option); 1 unless set.</summary>
        public double LightStrength => Number("light", 1);

        public bool Has(string name) => _values.ContainsKey(name);

        public double Number(string name, double fallback = 0)
        {
            if (_values.TryGetValue(name, out var value))
            {
                return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            return fallback;
        }

        public bool Flag(string name)
        {
            if (_values.TryGetValue(name, out var value) && value is bool flag)
            {
                return flag;
            }
            return false;
        }

        public string Text(string name, string fallback = "")
        {
            if (_values.TryGetValue(name, out var value) && value is string text)
            {
                return text;
            }
            return fallback;
        }

        /// <summary>A single colour option ("#4a3f69").</summary>
        public uint Color(string name) => PixelColor.Hex(Text(name, "#ffffff"));

        /// <summary>A colour ramp option as hex strings, dark to bright.</summary>
        public string[] Ramp(string name)
        {
            if (_values.TryGetValue(name, out var value) && value is string[] ramp)
            {
                return ramp;
            }
            return [];
        }
    }

    /// <summary>
    /// A live effect (createFx): <see cref="Update"/> by the frame's seconds, <see cref="Draw"/> into a frame,
    /// <see cref="Stop"/> to let a loop fade out. Simulations step at 120 Hz or finer, so they look the same at any rate.
    /// </summary>
    public sealed class PixelFxInstance
    {
        private const double FadeSeconds = 0.3;
        private readonly IPixelSimulation? _sim;
        private double _stopAt = double.PositiveInfinity;

        public PixelFxInstance(PixelEffect effect, IReadOnlyDictionary<string, object>? options = null)
        {
            Effect = effect;
            Options = new PixelFxOptions(effect.Meta, options);
            _sim = effect.Create(Options);
        }

        public PixelEffect Effect { get; }
        public PixelEffectMeta Meta => Effect.Meta;
        public PixelFxOptions Options { get; }
        public double T { get; private set; }

        public void Update(double dt)
        {
            T += dt;
            if (_sim == null)
            {
                return;
            }
            var n = Math.Max(1, (int)Math.Ceiling(dt * 120 - 1e-9));
            for (var i = 0; i < n; i++)
            {
                _sim.Update(dt / n, Options);
            }
        }

        /// <summary>Draws into a frame: layer "back" is what goes behind a character, "front" in front, null both.</summary>
        public void Draw(PixelFrame frame, string? layer = null)
        {
            var p = frame.Painter.Reset();
            p.Place(Options.X, Options.Y, Options.Flip, Options.Angle);
            p.Fade = 1;
            if (!double.IsPositiveInfinity(_stopAt))
            {
                p.Fade = PixelMath.Clamp(1 - (T - _stopAt) / FadeSeconds);
            }
            p.LightK = Options.LightStrength;
            var d = Meta.Duration;
            var tt = Math.Min(T, d);
            if (Meta.Loop)
            {
                tt = T % d;
            }
            var hasBack = Effect.HasBack;
            if (_sim != null)
            {
                hasBack = _sim.HasBack;
            }
            if (layer != "front" && hasBack)
            {
                p.Layer = "back";
                if (_sim != null)
                {
                    _sim.DrawBack(p, tt, Options);
                }
                else
                {
                    Effect.DrawBack(p, tt, Options);
                }
            }
            if (layer != "back")
            {
                p.Layer = "front";
                if (_sim != null)
                {
                    _sim.Draw(p, tt, Options);
                }
                else
                {
                    Effect.Draw(p, tt, Options);
                }
            }
        }

        /// <summary>Lets it end: loops fade out, one-shots are cut short.</summary>
        public void Stop()
        {
            if (double.IsPositiveInfinity(_stopAt))
            {
                _stopAt = T;
            }
        }

        public bool Done => T - _stopAt >= FadeSeconds || (!Meta.Loop && T >= Meta.Duration);
    }
}
