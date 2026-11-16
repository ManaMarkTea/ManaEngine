using System.Globalization;
using ManaGeoTools.Code.Planet;

namespace ManaGeoTools.Code.Flights
{
    /// <summary>A large airport (Assets/GeoData/airports.csv, from OurAirports via Tools/Planet/make_airports.py).</summary>
    public sealed record Airport(string Code, string City, float Latitude, float Longitude)
    {
        /// <summary>Where it is on the planet's unit sphere.</summary>
        public Vector3 Direction { get; } = new LatLong(Longitude, Latitude).ToUnitSphere();
    }

    /// <summary>An airport and the aircraft round it, coming in or going out.</summary>
    public readonly record struct AirportCount(Airport Airport, int Count);

    /// <summary>
    /// How busy each airport is: the airborne aircraft within a radius of it and below an altitude -- the ones
    /// climbing out of it or on their way down into it.
    /// </summary>
    public static class AirportTraffic
    {
        public static Airport[] Load(string path)
        {
            if (!File.Exists(path))
            {
                return [];
            }

            var airports = new List<Airport>();
            foreach (var line in File.ReadLines(path).Skip(1))
            {
                var fields = line.Split(',');
                if (fields.Length < 4
                    || !float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                    || !float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
                {
                    continue;
                }

                airports.Add(new Airport(fields[0], fields[1], latitude, longitude));
            }

            return airports.ToArray();
        }

        /// <summary>
        /// The airports with any traffic, busiest first. Each aircraft counts once, for its nearest airport, so two
        /// fields a few miles apart do not both claim the same arrivals. Positions are flown on to <paramref name="now"/>.
        /// </summary>
        public static AirportCount[] Count(Airport[] airports, FlightSnapshot snapshot, double now, float radiusKilometres, float maximumAltitudeMeters)
        {
            if (airports.Length == 0)
            {
                return [];
            }

            var counts = new int[airports.Length];
            var reach = MathF.Cos(radiusKilometres * 1000.0f / PlanetLoader.EarthRadiusMeters);
            foreach (ref readonly var state in snapshot.States.AsSpan())
            {
                if (state.OnGround)
                {
                    continue;
                }

                var seconds = (float)Math.Max(0.0, now - state.PositionTime);
                if (FlightDeadReckoning.Altitude(state, seconds) > maximumAltitudeMeters)
                {
                    continue;
                }

                var direction = FlightDeadReckoning.Advance(state, seconds).Direction;
                var nearest = -1;
                var nearestDot = reach;
                for (var i = 0; i < airports.Length; i++)
                {
                    var dot = Vector3.Dot(direction, airports[i].Direction);
                    if (dot > nearestDot)
                    {
                        nearestDot = dot;
                        nearest = i;
                    }
                }

                if (nearest >= 0)
                {
                    counts[nearest]++;
                }
            }

            var busy = new List<AirportCount>();
            for (var i = 0; i < airports.Length; i++)
            {
                if (counts[i] > 0)
                {
                    busy.Add(new AirportCount(airports[i], counts[i]));
                }
            }

            busy.Sort((a, b) => b.Count.CompareTo(a.Count));
            return busy.ToArray();
        }
    }
}
