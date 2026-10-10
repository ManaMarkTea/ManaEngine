using System.Numerics;

namespace ManaCore.Graphics
{
    /// <summary>
    /// The world's wind: a steady breeze from <see cref="ManaIrradianceSettings"/> that gusts and lulls. Ticked once a
    /// frame beside <see cref="ManaSky"/>; the renderer hands the result to every shader (the water's swell, ripples and
    /// the gust patches running across it), and gameplay can read the same state.
    /// </summary>
    public static class ManaWind
    {
        /// <param name="Direction">Unit vector in world XZ the wind blows toward.</param>
        /// <param name="Speed">Metres a second right now, gusts included.</param>
        /// <param name="Gustiness">0 steady .. 1 squally, as set.</param>
        /// <param name="Drift">How far the air has carried something since the start, in world XZ metres -- what
        /// shaders scroll gust patches and ripples by, so a change of speed never makes them jump.</param>
        /// <param name="Distance">The length of that path, for wave phases.</param>
        /// <param name="Gust">How much of a gust is blowing now, 0..1.</param>
        public readonly record struct State(Vector2 Direction, float Speed, float Gustiness, Vector2 Drift, float Distance, float Gust)
        {
            public static State Still => new(new Vector2(0.0f, -1.0f), 0.0f, 0.0f, Vector2.Zero, 0.0f, 0.0f);
        }

        /// <summary>The breeze water's waveStrength is tuned for: at this speed waves are as authored.</summary>
        public const float ReferenceSpeed = 4.0f;

        public const float MaximumSpeed = 20.0f;

        /// <summary>World XZ direction for a heading in degrees clockwise from -Z (north, seen from above).</summary>
        public static Vector2 DirectionFor(float headingDegrees)
        {
            var radians = headingDegrees * MathF.PI / 180.0f;
            return new Vector2(MathF.Sin(radians), -MathF.Cos(radians));
        }

        /// <summary>
        /// How much of a gust is blowing at a moment, 0..1: three slow swells at unrelated rates, so their sum peaks
        /// now and then but never on a beat you can hear.
        /// </summary>
        public static float GustAt(double seconds)
        {
            var raw = 0.5 + 0.25 * Math.Sin(seconds * 0.37) + 0.15 * Math.Sin(seconds * 0.83 + 1.7) + 0.1 * Math.Sin(seconds * 2.1 + 4.1);
            // Starts well above the swells' average, so most of the time is breeze and a gust is an event.
            var x = Math.Clamp((raw - 0.65) / 0.3, 0.0, 1.0);
            return (float)(x * x * (3.0 - 2.0 * x));
        }

        /// <summary>The speed at a moment: lulls a little under the breeze, gusts up to about twice it when squally.</summary>
        public static float SpeedAt(float baseSpeed, float gustiness, float gust)
        {
            return MathF.Max(0.0f, baseSpeed * (1.0f + gustiness * (1.5f * gust - 0.35f)));
        }

        /// <summary>Carries the wind on by the time since the last tick and returns where it stands.</summary>
        public static State Tick(ManaIrradianceSettings settings)
        {
            var now = ManaSky.Seconds;
            var elapsed = settings.LastWindTickSeconds < 0.0 ? 0.0f : Math.Clamp(now - (float)settings.LastWindTickSeconds, 0.0f, 0.25f);
            settings.LastWindTickSeconds = now;
            return Advance(settings, now, elapsed);
        }

        /// <summary><see cref="Tick"/> with the clock given -- tests drive it directly.</summary>
        public static State Advance(ManaIrradianceSettings settings, double seconds, float elapsed)
        {
            var baseSpeed = Math.Clamp(settings.WindSpeed, 0.0f, MaximumSpeed);
            var gustiness = Math.Clamp(settings.Gustiness, 0.0f, 1.0f);
            var direction = DirectionFor(settings.WindDirection);
            var gust = gustiness > 0.0f ? GustAt(seconds) : 0.0f;
            var speed = SpeedAt(baseSpeed, gustiness, gust);

            settings.WindDrift += direction * (speed * elapsed);
            settings.WindDistance += speed * elapsed;
            return new State(direction, speed, gustiness, settings.WindDrift, settings.WindDistance, gust);
        }
    }
}
