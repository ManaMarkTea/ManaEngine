using System.Diagnostics;
using System.Numerics;

namespace ManaCore.Graphics
{
    public enum ManaSkyMode
    {
        /// <summary>The environment image, as the reflections use it.</summary>
        Photo,

        /// <summary>A clear day with clouds, the sun where the key light comes from.</summary>
        Day,

        /// <summary>The sun crosses the sky and sets; at night the scene's fill light dims and lamps carry it.</summary>
        DayNightCycle,

        /// <summary>Stars, a nebula and the galaxy's band.</summary>
        Space
    }

    /// <summary>
    /// Where the sun is and how much daylight there is right now. Worked out once, here, so the sky the shader
    /// draws and the light the scene gets from it can never disagree.
    /// </summary>
    public static class ManaSky
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        public static float Seconds => (float)Clock.Elapsed.TotalSeconds;

        public readonly record struct State(ManaSkyMode Mode, Vector3 TowardSun, float Daylight, Vector3 SunTint, float CloudTime = 0.0f);

        // The day speed the clouds drift at their own pace for; faster days push them faster, like a timelapse.
        private const float CloudBaseSpeed = 300.0f;

        public const float SecondsPerDay = 86400.0f;

        /// <summary>
        /// Moves a day-night cycle on by the time since the last tick, at its speed -- or to the wall clock's
        /// time of day -- then evaluates it. Called once a frame, by the world whose settings these are.
        /// </summary>
        public static State Tick(ManaIrradianceSettings settings)
        {
            var now = Clock.Elapsed.TotalSeconds;
            var elapsed = settings.LastTickSeconds < 0.0 ? 0.0 : Math.Clamp(now - settings.LastTickSeconds, 0.0, 0.25);
            settings.LastTickSeconds = now;
            var wind = settings.Sky == ManaSkyMode.DayNightCycle && !settings.RealTime
                ? MathF.Max(1.0f, settings.TimeSpeed / CloudBaseSpeed)
                : 1.0f;
            settings.CloudSeconds += elapsed * wind;
            if (settings.Sky == ManaSkyMode.DayNightCycle)
            {
                settings.TimeOfDay = settings.RealTime
                    ? PhaseAt(DateTime.Now.TimeOfDay.TotalHours)
                    : Wrap(settings.TimeOfDay + (float)elapsed * settings.TimeSpeed / SecondsPerDay);
            }

            return Evaluate(settings);
        }

        /// <summary>The cycle's phase for an hour of the day: 6 am is sunrise, phase 0.</summary>
        public static float PhaseAt(double hours) => Wrap((float)((hours - 6.0) / 24.0));

        /// <summary>The hour of the day, 0..24, a phase stands for.</summary>
        public static float HoursAt(float phase) => Wrap(phase + 0.25f) * 24.0f;

        private static float Wrap(float phase) => phase - MathF.Floor(phase);

        public static State Evaluate(ManaIrradianceSettings settings)
        {
            var keyLight = settings.LightDirection.LengthSquared() > 1e-6f ? Vector3.Normalize(settings.LightDirection) : Vector3.UnitY;
            if (settings.Sky != ManaSkyMode.DayNightCycle)
            {
                return new State(settings.Sky, keyLight, 1.0f, Vector3.One, (float)settings.CloudSeconds);
            }

            // Sunrise in the east (+X), noon overhead, sunset in the west, then the night half of the turn.
            var angle = Wrap(settings.TimeOfDay) * MathF.Tau;
            var towardSun = Vector3.Normalize(new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0.3f));

            // Full light once the sun is well up, a moonlit floor at night, and warm while it is low.
            var elevation = towardSun.Y;
            var daylight = 0.08f + 0.92f * SmoothStep(-0.08f, 0.25f, elevation);
            var tint = Vector3.Lerp(new Vector3(1.0f, 0.62f, 0.42f), Vector3.One, SmoothStep(0.0f, 0.4f, elevation));
            return new State(ManaSkyMode.DayNightCycle, towardSun, daylight, tint, (float)settings.CloudSeconds);
        }

        /// <summary>How bright the sky's sun is as a scene light, at full height; the moon is a sliver of it.</summary>
        public const float SunIntensity = 1.6f;
        public const float MoonIntensity = 0.12f;

        /// <summary>
        /// The directional light the sky gives a scene with none of its own: the sun while it is up, then the
        /// moon opposite it, each fading out at the horizon so the hand-over is dark rather than a jump. Direction
        /// is the way the light travels, as a scene sun's is. Photo has no sun to give, so returns nothing.
        /// </summary>
        public static (Vector3 Direction, Vector4 Colour)? Light(State sky)
        {
            if (sky.Mode == ManaSkyMode.Photo)
            {
                return null;
            }

            var sunUp = sky.TowardSun.Y;
            if (sky.Mode != ManaSkyMode.DayNightCycle || sunUp > 0.0f)
            {
                return (-sky.TowardSun, new Vector4(sky.SunTint, SunIntensity * SmoothStep(0.0f, 0.08f, sunUp)));
            }

            return (sky.TowardSun, new Vector4(0.62f, 0.7f, 1.0f, MoonIntensity * SmoothStep(0.0f, 0.08f, -sunUp)));
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
            return t * t * (3.0f - 2.0f * t);
        }
    }
}
