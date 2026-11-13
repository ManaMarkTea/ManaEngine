using ManaGeoTools.Code.Planet;

namespace ManaGeoTools.Code.Flights
{
    /// <summary>
    /// Carries a reported aircraft forward along its great circle, so it keeps flying between replies that come
    /// minutes apart. Works on the planet's unit sphere (<see cref="LatLong.ToUnitSphere"/>'s frame) throughout.
    /// </summary>
    public static class FlightDeadReckoning
    {
        // Climbs and descents end; past this they are held level rather than run into space or the ground.
        public const float VerticalRateSeconds = 90.0f;

        /// <summary>The unit direction to the aircraft and its heading (a unit tangent) <paramref name="seconds"/> after its report.</summary>
        public static (Vector3 Direction, Vector3 Heading) Advance(in FlightState state, float seconds)
        {
            var (direction, heading) = Start(state);
            var angle = state.SpeedMetersPerSecond * seconds / PlanetLoader.EarthRadiusMeters;
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            return (direction * cos + heading * sin, heading * cos - direction * sin);
        }

        /// <summary>Altitude <paramref name="seconds"/> after the report, the climb or descent run for at most <see cref="VerticalRateSeconds"/>.</summary>
        public static float Altitude(in FlightState state, float seconds)
        {
            var climbing = Math.Clamp(seconds, 0.0f, VerticalRateSeconds);
            return MathF.Max(0.0f, state.AltitudeMeters + state.VerticalRateMetersPerSecond * climbing);
        }

        /// <summary>Where it was reported and which way it was going: the track turned from north towards east.</summary>
        public static (Vector3 Direction, Vector3 Heading) Start(in FlightState state)
        {
            var direction = new LatLong(state.Longitude, state.Latitude).ToUnitSphere();
            var longitude = state.Longitude * (MathF.PI / 180.0f);
            var latitude = state.Latitude * (MathF.PI / 180.0f);
            // The derivatives of ToUnitSphere by latitude and longitude, normalised.
            var north = new Vector3(MathF.Sin(longitude) * MathF.Sin(latitude), MathF.Cos(latitude), MathF.Cos(longitude) * MathF.Sin(latitude));
            var east = new Vector3(-MathF.Cos(longitude), 0.0f, MathF.Sin(longitude));
            var track = state.TrackDegrees * (MathF.PI / 180.0f);
            var heading = north * MathF.Cos(track) + east * MathF.Sin(track);
            return (direction, heading);
        }
    }
}
