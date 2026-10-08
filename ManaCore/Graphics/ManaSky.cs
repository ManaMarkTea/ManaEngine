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
    /// Where the sun and moon are, how much daylight there is, and the current lunar phase. Worked out here so
    /// the sky the shader draws and the light the scene gets from it can never disagree.
    /// </summary>
    public static class ManaSky
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        public static float Seconds => (float)Clock.Elapsed.TotalSeconds;

        public readonly record struct State(
            ManaSkyMode Mode,
            Vector3 TowardSun,
            float Daylight,
            Vector3 SunTint,
            float CloudTime = 0.0f,
            float MoonPhase = 0.0f,
            Vector3 TowardMoon = default,
            Vector3 CelestialEast = default,
            Vector3 CelestialUp = default,
            Vector3 CelestialNorth = default,
            float ConstellationLines = 0.0f);

        // The day speed the clouds drift at their own pace for; faster days push them faster, like a timelapse.
        private const float CloudBaseSpeed = 300.0f;

        public const float SecondsPerDay = 86400.0f;
        public const double SiderealDaySeconds = 86164.0905;
        private static readonly DateTimeOffset SiderealEpoch = new(2000, 1, 1, 6, 0, 0, TimeSpan.Zero);
        private const double ObserverLatitudeDegrees = 47.6062;
        private const double ObserverLongitudeDegrees = -122.3321;

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
                ? (settings.TimeSpeed < 0.0f ? -1.0f : 1.0f) * MathF.Max(1.0f, MathF.Abs(settings.TimeSpeed / CloudBaseSpeed))
                : 1.0f;
            settings.CloudSeconds += elapsed * wind;
            if (settings.Sky == ManaSkyMode.DayNightCycle)
            {
                if (settings.RealTime)
                {
                    settings.SkyDateTime = DateTimeOffset.Now;
                    settings.TimeOfDay = settings.DaySystem == ManaSkyDaySystem.Sidereal
                        ? SiderealPhaseAt(settings.SkyDateTime)
                        : PhaseAt(settings.SkyDateTime.TimeOfDay.TotalHours);
                }
                else
                {
                    var simulatedSeconds = elapsed * settings.TimeSpeed;
                    settings.SkyDateTime = settings.SkyDateTime.AddSeconds(simulatedSeconds);
                    var daySeconds = settings.DaySystem == ManaSkyDaySystem.Sidereal ? SiderealDaySeconds : SecondsPerDay;
                    settings.TimeOfDay = Wrap(settings.TimeOfDay + (float)(simulatedSeconds / daySeconds));
                }
            }

            return Evaluate(settings);
        }

        /// <summary>The cycle's phase for an hour of the day: 6 am is sunrise, phase 0.</summary>
        public static float PhaseAt(double hours) => Wrap((float)((hours - 6.0) / 24.0));

        /// <summary>The hour of the day, 0..24, a phase stands for.</summary>
        public static float HoursAt(float phase) => Wrap(phase + 0.25f) * 24.0f;

        /// <summary>The cycle phase measured from the sidereal epoch, one turn every 23 h 56 m 4.09 s.</summary>
        public static float SiderealPhaseAt(DateTimeOffset time)
        {
            var cycles = (time.ToUniversalTime() - SiderealEpoch).TotalSeconds / SiderealDaySeconds;
            return (float)(cycles - Math.Floor(cycles));
        }

        private static float Wrap(float phase) => phase - MathF.Floor(phase);

        public static State Evaluate(ManaIrradianceSettings settings)
        {
            var keyLight = settings.LightDirection.LengthSquared() > 1e-6f ? Vector3.Normalize(settings.LightDirection) : Vector3.UnitY;
            var moonPhase = ManaLunarPhase.PhaseAt(settings.SkyDateTime);
            if (settings.Sky != ManaSkyMode.DayNightCycle)
            {
                return new State(settings.Sky, keyLight, 1.0f, Vector3.One, (float)settings.CloudSeconds, moonPhase);
            }

            var (towardSun, towardMoon) = CelestialDirectionsAt(settings.SkyDateTime);

            // Keep a soft twilight while following the Sun's actual seasonal elevation at the observer.
            var elevation = towardSun.Y;
            var daylight = 0.08f + 0.92f * SmoothStep(-0.08f, 0.25f, elevation);
            var tint = Vector3.Lerp(new Vector3(1.0f, 0.62f, 0.42f), Vector3.One, SmoothStep(0.0f, 0.4f, elevation));
            // Rows of the matrix taking an equatorial star direction (x toward RA 0h, z toward the pole) to this
            // sky's east, up and north, so the star catalogue turns with the date and hour.
            var (east, up, north) = CelestialFrame(settings.SkyDateTime);
            return new State(ManaSkyMode.DayNightCycle, towardSun, daylight, tint, (float)settings.CloudSeconds, moonPhase, towardMoon,
                east, up, north, settings.ConstellationLines ? (settings.ShowAllConstellations ? 2.0f : 1.0f) : 0.0f);
        }

        /// <summary>Local sidereal time in degrees at the observer for <paramref name="time"/>.</summary>
        private static double LocalSiderealDegrees(DateTimeOffset time)
        {
            var julianDate = 2440587.5 + (time.UtcDateTime - DateTime.UnixEpoch).TotalDays;
            var centuries = (julianDate - 2451545.0) / 36525.0;
            return 280.46061837 + 360.98564736629 * (julianDate - 2451545.0) +
                0.000387933 * centuries * centuries - centuries * centuries * centuries / 38710000.0 + ObserverLongitudeDegrees;
        }

        /// <summary>The X (west), Y (up) and Z (north) rows of the equatorial-to-world rotation at the observer; same frame as the sun's direction.</summary>
        public static (Vector3 East, Vector3 Up, Vector3 North) CelestialFrame(DateTimeOffset time)
        {
            var sidereal = ToRadians(LocalSiderealDegrees(time));
            var latitude = ToRadians(ObserverLatitudeDegrees);
            float sinL = (float)Math.Sin(sidereal), cosL = (float)Math.Cos(sidereal);
            float sinP = (float)Math.Sin(latitude), cosP = (float)Math.Cos(latitude);
            // The first row is world +X, which is west (see EquatorialToHorizon): east negated.
            return (new Vector3(sinL, -cosL, 0.0f),
                new Vector3(cosP * cosL, cosP * sinL, sinP),
                new Vector3(-sinP * cosL, -sinP * sinL, cosP));
        }

        private static (Vector3 Sun, Vector3 Moon) CelestialDirectionsAt(DateTimeOffset time)
        {
            var julianDate = 2440587.5 + (time.UtcDateTime - DateTime.UnixEpoch).TotalDays;
            var days = julianDate - 2451543.5;

            var sunMeanAnomaly = 356.0470 + 0.9856002585 * days;
            var sunPerihelion = 282.9404 + 4.70935e-5 * days;
            var sunEccentricity = 0.016709 - 1.151e-9 * days;
            var sunEccentricAnomaly = sunMeanAnomaly + sunEccentricity * 180.0 / Math.PI * SinDegrees(sunMeanAnomaly) *
                (1.0 + sunEccentricity * Math.Cos(ToRadians(sunMeanAnomaly)));
            var sunX = Math.Cos(ToRadians(sunEccentricAnomaly)) - sunEccentricity;
            var sunY = Math.Sqrt(1.0 - sunEccentricity * sunEccentricity) * Math.Sin(ToRadians(sunEccentricAnomaly));
            var sunTrueAnomaly = Math.Atan2(sunY, sunX) * 180.0 / Math.PI;
            var sunLongitude = NormalizeDegrees(sunTrueAnomaly + sunPerihelion);

            var ascendingNode = 125.1228 - 0.0529538083 * days;
            const double inclination = 5.1454;
            var argumentOfPerigee = 318.0634 + 0.1643573223 * days;
            const double semimajorAxis = 60.2666;
            const double eccentricity = 0.0549;
            var moonMeanAnomaly = 115.3654 + 13.0649929509 * days;
            var moonEccentricAnomaly = moonMeanAnomaly + eccentricity * 180.0 / Math.PI * SinDegrees(moonMeanAnomaly) *
                (1.0 + eccentricity * Math.Cos(ToRadians(moonMeanAnomaly)));
            var moonX = semimajorAxis * (Math.Cos(ToRadians(moonEccentricAnomaly)) - eccentricity);
            var moonY = semimajorAxis * Math.Sqrt(1.0 - eccentricity * eccentricity) * Math.Sin(ToRadians(moonEccentricAnomaly));
            var moonTrueAnomaly = Math.Atan2(moonY, moonX) * 180.0 / Math.PI;
            var moonRadius = Math.Sqrt(moonX * moonX + moonY * moonY);
            var orbitalLongitude = moonTrueAnomaly + argumentOfPerigee;
            var nodeRadians = ToRadians(ascendingNode);
            var orbitRadians = ToRadians(orbitalLongitude);
            var inclinationRadians = ToRadians(inclination);
            var eclipticX = moonRadius * (Math.Cos(nodeRadians) * Math.Cos(orbitRadians) - Math.Sin(nodeRadians) * Math.Sin(orbitRadians) * Math.Cos(inclinationRadians));
            var eclipticY = moonRadius * (Math.Sin(nodeRadians) * Math.Cos(orbitRadians) + Math.Cos(nodeRadians) * Math.Sin(orbitRadians) * Math.Cos(inclinationRadians));
            var eclipticZ = moonRadius * Math.Sin(orbitRadians) * Math.Sin(inclinationRadians);
            var moonLongitude = Math.Atan2(eclipticY, eclipticX) * 180.0 / Math.PI;
            var moonLatitude = Math.Atan2(eclipticZ, Math.Sqrt(eclipticX * eclipticX + eclipticY * eclipticY)) * 180.0 / Math.PI;

            var moonMeanLongitude = ascendingNode + argumentOfPerigee + moonMeanAnomaly;
            var elongation = moonMeanLongitude - sunLongitude;
            var argumentOfLatitude = moonMeanLongitude - ascendingNode;
            moonLongitude +=
                -1.274 * SinDegrees(moonMeanAnomaly - 2.0 * elongation) +
                0.658 * SinDegrees(2.0 * elongation) -
                0.186 * SinDegrees(sunMeanAnomaly) -
                0.059 * SinDegrees(2.0 * moonMeanAnomaly - 2.0 * elongation) -
                0.057 * SinDegrees(moonMeanAnomaly - 2.0 * elongation + sunMeanAnomaly) +
                0.053 * SinDegrees(moonMeanAnomaly + 2.0 * elongation) +
                0.046 * SinDegrees(2.0 * elongation - sunMeanAnomaly) +
                0.041 * SinDegrees(moonMeanAnomaly - sunMeanAnomaly) -
                0.035 * SinDegrees(elongation) -
                0.031 * SinDegrees(moonMeanAnomaly + sunMeanAnomaly) -
                0.015 * SinDegrees(2.0 * argumentOfLatitude - 2.0 * elongation) +
                0.011 * SinDegrees(moonMeanAnomaly - 4.0 * elongation);
            moonLatitude +=
                -0.173 * SinDegrees(argumentOfLatitude - 2.0 * elongation) -
                0.055 * SinDegrees(moonMeanAnomaly - argumentOfLatitude - 2.0 * elongation) -
                0.046 * SinDegrees(moonMeanAnomaly + argumentOfLatitude - 2.0 * elongation) +
                0.033 * SinDegrees(argumentOfLatitude + 2.0 * elongation) +
                0.017 * SinDegrees(2.0 * moonMeanAnomaly + argumentOfLatitude) +
                0.009 * SinDegrees(moonMeanAnomaly - argumentOfLatitude);

            var centuries = (julianDate - 2451545.0) / 36525.0;
            var obliquity = 23.4393 - 3.563e-7 * (julianDate - 2451545.0);
            var siderealDegrees = 280.46061837 + 360.98564736629 * (julianDate - 2451545.0) +
                0.000387933 * centuries * centuries - centuries * centuries * centuries / 38710000.0 + ObserverLongitudeDegrees;
            var sunDirection = EquatorialToHorizon(sunLongitude, 0.0, obliquity, siderealDegrees);
            var moonDirection = EquatorialToHorizon(moonLongitude, moonLatitude, obliquity, siderealDegrees);
            return (sunDirection, moonDirection);
        }

        private static Vector3 EquatorialToHorizon(double longitude, double latitude, double obliquity, double siderealDegrees)
        {
            var longitudeRadians = ToRadians(longitude);
            var latitudeRadians = ToRadians(latitude);
            var obliquityRadians = ToRadians(obliquity);
            var equatorialY = Math.Cos(latitudeRadians) * Math.Sin(longitudeRadians) * Math.Cos(obliquityRadians) -
                Math.Sin(latitudeRadians) * Math.Sin(obliquityRadians);
            var equatorialZ = Math.Cos(latitudeRadians) * Math.Sin(longitudeRadians) * Math.Sin(obliquityRadians) +
                Math.Sin(latitudeRadians) * Math.Cos(obliquityRadians);
            var rightAscension = Math.Atan2(equatorialY, Math.Cos(latitudeRadians) * Math.Cos(longitudeRadians)) * 180.0 / Math.PI;
            var declination = Math.Asin(Math.Clamp(equatorialZ, -1.0, 1.0));
            var hourAngle = ToRadians(NormalizeSignedDegrees(siderealDegrees - rightAscension));
            var observerLatitude = ToRadians(ObserverLatitudeDegrees);
            var east = -Math.Cos(declination) * Math.Sin(hourAngle);
            var north = Math.Sin(declination) * Math.Cos(observerLatitude) -
                Math.Cos(declination) * Math.Cos(hourAngle) * Math.Sin(observerLatitude);
            var up = Math.Sin(declination) * Math.Sin(observerLatitude) +
                Math.Cos(declination) * Math.Cos(hourAngle) * Math.Cos(observerLatitude);
            // The scene looks down -Z by default, which is south here (north is +Z); east is then to the left, -X.
            // With +X as east the whole sky -- sun, moon and stars -- came out as a mirror image.
            return Vector3.Normalize(new Vector3((float)-east, (float)up, (float)north));
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

        private static double SinDegrees(double degrees) => Math.Sin(ToRadians(degrees));

        private static double NormalizeDegrees(double degrees) => degrees - 360.0 * Math.Floor(degrees / 360.0);

        private static double NormalizeSignedDegrees(double degrees)
        {
            var normalized = NormalizeDegrees(degrees);
            return normalized > 180.0 ? normalized - 360.0 : normalized;
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

            return (-sky.TowardMoon, new Vector4(0.62f, 0.7f, 1.0f,
                MoonIntensity * ManaLunarPhase.Illumination(sky.MoonPhase) * SmoothStep(0.0f, 0.08f, sky.TowardMoon.Y)));
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
            return t * t * (3.0f - 2.0f * t);
        }
    }
}
