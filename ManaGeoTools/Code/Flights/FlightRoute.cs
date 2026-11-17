using ManaGeoTools.Code.Planet;

namespace ManaGeoTools.Code.Flights
{
    /// <summary>
    /// The trip shown for a selected flight. <see cref="Scheduled"/>: adsbdb's route for the callsign, which the aircraft
    /// is flying. Otherwise what the aircraft shows: <see cref="From"/> where its track took off, <see cref="To"/> the
    /// airport ahead it is descending toward (<see cref="ToGuessed"/>); <see cref="Usual"/> names the route on record
    /// ("SFO to CLT") when it was turned down. <see cref="Live"/>: the trip came from FlightAware, and says how it is going
    /// ("FlightAware · En Route / On Time").
    /// </summary>
    public sealed record FlightRoute(RouteAirport? From, RouteAirport? To, bool Scheduled, bool ToGuessed, string? Usual, string? Live = null)
    {
        public bool HasEnds => From != null || To != null;
    }

    /// <summary>
    /// Whether a callsign's route on record is this aircraft's trip. adsbdb keeps one route a callsign, and airlines
    /// reuse numbers -- for the next leg of a through flight, the other way, or another trip entirely -- so a route is
    /// believed only when the aircraft lies between its ends, heads for its destination and took off from its origin.
    /// </summary>
    public static class FlightRouteJudge
    {
        // How far off the line between the two ends (summed detour) the aircraft may be: airways wander, weather too.
        private const float DetourKilometres = 300.0f;
        // A live trip only has to be roughly where the aircraft is: diversions and holding stray far further.
        private const float LiveDetourKilometres = 1500.0f;
        // Its course against the great circle on to the destination; nearer than NearDestination it may be turning in.
        private const float HeadingSlackDegrees = 70.0f;
        private const float NearDestinationKilometres = 150.0f;
        // A track starting this low began at an airport (OpenSky drops ground points far from receivers).
        private const float DepartureAltitudeMeters = 2000.0f;
        private const float DepartureRadiusKilometres = 40.0f;
        private const float SameAirportKilometres = 60.0f;
        // Descending this fast, below this, the airport ahead within a 3-degree glide (with room for vectoring) is the guess.
        private const float ArrivalDescentMetersPerSecond = -2.5f;
        private const float ArrivalCeilingMeters = 9000.0f;
        private const float ArrivalConeDegrees = 30.0f;

        public static FlightRoute? Judge(FlightDetails? details, FlightState state, IReadOnlyList<Airport> airports)
        {
            if (details == null)
            {
                return null;
            }

            var here = Direction(state.Latitude, state.Longitude);
            if (details.Live is { } live && Believable(live, here))
            {
                var note = "FlightAware";
                if (live.Diverted)
                {
                    note += " · diverted";
                }
                else if (!string.IsNullOrEmpty(live.Status))
                {
                    note += $" · {live.Status}";
                }
                return new FlightRoute(live.Origin, live.Destination, true, false, null, note);
            }

            var departed = DepartedFrom(details, airports);
            string? usual = null;
            if (details is { Origin: { } origin, Destination: { } destination })
            {
                if (Fits(origin, destination, departed, state, here))
                {
                    return new FlightRoute(origin, destination, true, false, null);
                }
                usual = $"{origin.Code} to {destination.Code}";
            }

            var arriving = ArrivingAt(state, here, airports);
            return new FlightRoute(departed, arriving, false, arriving != null, usual);
        }

        // FlightAware knows the trip, but the airframe it was asked about came from adsbdb's registration: a leg nowhere
        // near the aircraft means that lookup named another airframe.
        private static bool Believable(LiveRoute live, Vector3 here)
        {
            if (live.Origin == null || live.Destination == null)
            {
                return true;
            }
            var from = Direction(live.Origin.Latitude, live.Origin.Longitude);
            var to = Direction(live.Destination.Latitude, live.Destination.Longitude);
            return Kilometres(from, here) + Kilometres(here, to) <= Kilometres(from, to) + LiveDetourKilometres;
        }

        private static bool Fits(RouteAirport origin, RouteAirport destination, RouteAirport? departed, FlightState state, Vector3 here)
        {
            var from = Direction(origin.Latitude, origin.Longitude);
            var to = Direction(destination.Latitude, destination.Longitude);
            if (Kilometres(from, here) + Kilometres(here, to) > Kilometres(from, to) + DetourKilometres)
            {
                return false;
            }
            if (departed != null && Kilometres(from, Direction(departed.Latitude, departed.Longitude)) > SameAirportKilometres)
            {
                return false;
            }
            if (!state.OnGround && state.SpeedMetersPerSecond > 30.0f && Kilometres(here, to) > NearDestinationKilometres
                && AngleBetween(Bearing(here, to), state.TrackDegrees) > HeadingSlackDegrees)
            {
                return false;
            }
            return true;
        }

        /// <summary>The airport the track took off from, when it starts low enough to have begun at one.</summary>
        public static RouteAirport? DepartedFrom(FlightDetails details, IReadOnlyList<Airport> airports)
        {
            if (details.Track.Length == 0 || details.Track[0].AltitudeMeters > DepartureAltitudeMeters)
            {
                return null;
            }
            var start = Direction(details.Track[0].Latitude, details.Track[0].Longitude);
            // The route's own ends first: adsbdb knows smaller fields than the list of large airports.
            foreach (var end in new[] { details.Origin, details.Destination })
            {
                if (end != null && Kilometres(start, Direction(end.Latitude, end.Longitude)) < DepartureRadiusKilometres)
                {
                    return end;
                }
            }
            Airport? nearest = null;
            var nearestKilometres = DepartureRadiusKilometres;
            foreach (var airport in airports)
            {
                var distance = Kilometres(start, airport.Direction);
                if (distance < nearestKilometres)
                {
                    nearest = airport;
                    nearestKilometres = distance;
                }
            }
            return Of(nearest);
        }

        /// <summary>The nearest large airport ahead of an aircraft on its way down, or null when it is not descending.</summary>
        public static RouteAirport? ArrivingAt(FlightState state, Vector3 here, IReadOnlyList<Airport> airports)
        {
            if (state.OnGround || state.VerticalRateMetersPerSecond > ArrivalDescentMetersPerSecond || state.AltitudeMeters > ArrivalCeilingMeters)
            {
                return null;
            }
            var reach = 30.0f + state.AltitudeMeters * 0.025f;
            Airport? best = null;
            var bestKilometres = reach;
            foreach (var airport in airports)
            {
                var distance = Kilometres(here, airport.Direction);
                if (distance >= bestKilometres)
                {
                    continue;
                }
                // Close in, approach control turns aircraft onto the runway from any side: the cone widens.
                var cone = ArrivalConeDegrees;
                if (distance < 100.0f)
                {
                    cone = 70.0f;
                }
                if (distance > 25.0f && AngleBetween(Bearing(here, airport.Direction), state.TrackDegrees) > cone)
                {
                    continue;
                }
                best = airport;
                bestKilometres = distance;
            }
            return Of(best);
        }

        /// <summary>Compass degrees (0 north, 90 east) of the great circle from one point to another.</summary>
        public static float Bearing(Vector3 from, Vector3 to)
        {
            var north = Vector3.UnitY - from * from.Y;
            if (north.LengthSquared() < 1e-10f)
            {
                return 0.0f;
            }
            north = Vector3.Normalize(north);
            var east = Vector3.Cross(north, from);
            var degrees = MathF.Atan2(Vector3.Dot(to, east), Vector3.Dot(to, north)) * 180.0f / MathF.PI;
            if (degrees < 0.0f)
            {
                degrees += 360.0f;
            }
            return degrees;
        }

        private static float AngleBetween(float a, float b)
        {
            var difference = MathF.Abs(a - b) % 360.0f;
            if (difference > 180.0f)
            {
                difference = 360.0f - difference;
            }
            return difference;
        }

        private static RouteAirport? Of(Airport? airport)
        {
            if (airport == null)
            {
                return null;
            }
            return new RouteAirport(airport.Code, airport.City, "", airport.Latitude, airport.Longitude);
        }

        private static Vector3 Direction(float latitude, float longitude) => new LatLong(longitude, latitude).ToUnitSphere();

        private static float Kilometres(Vector3 a, Vector3 b) =>
            MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1.0f, 1.0f)) * PlanetLoader.EarthRadiusMeters / 1000.0f;
    }
}
