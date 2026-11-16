using System.Text.Json;

namespace ManaGeoTools.Code.Flights
{
    /// <summary>One end of a route, as adsbdb names it.</summary>
    public sealed record RouteAirport(string Code, string City, string Name, float Latitude, float Longitude);

    /// <summary>A point the aircraft reported on its way: OpenSky's track, oldest first.</summary>
    public readonly record struct TrackPoint(double Time, float Latitude, float Longitude, float AltitudeMeters);

    /// <summary>What is known about one aircraft beyond its position: the route its callsign flies, the airframe, the path flown.</summary>
    public sealed record FlightDetails(
        string? Airline,
        RouteAirport? Origin,
        RouteAirport? Destination,
        string? AircraftType,
        string? Registration,
        TrackPoint[] Track,
        string? Error);

    /// <summary>
    /// Fetches a selected flight's details: the path flown from OpenSky's tracks (free, experimental), and the
    /// callsign's scheduled route and the airframe from adsbdb.com (free, no key). The route is the callsign's
    /// usual one -- a reused callsign can name another trip.
    /// </summary>
    public static class FlightDetailsLookup
    {
        private static readonly HttpClient Http = CreateHttp();

        public static async Task<FlightDetails> FetchAsync(FlightState state, string? relayUrl, CancellationToken cancellation)
        {
            var track = FetchTrackAsync(state.Icao24, relayUrl, cancellation);
            var route = FetchJsonAsync($"https://api.adsbdb.com/v0/callsign/{Uri.EscapeDataString(state.Callsign)}", cancellation);
            var aircraft = FetchJsonAsync($"https://api.adsbdb.com/v0/aircraft/{Uri.EscapeDataString(state.Icao24)}", cancellation);
            string? airline = null;
            RouteAirport? origin = null;
            RouteAirport? destination = null;
            string? type = null;
            string? registration = null;
            string? error = null;
            try
            {
                if (state.Callsign.Length > 0 && await route.ConfigureAwait(false) is { } routeJson
                    && routeJson.RootElement.TryGetProperty("response", out var routeResponse)
                    && routeResponse.ValueKind == JsonValueKind.Object
                    && routeResponse.TryGetProperty("flightroute", out var flightRoute))
                {
                    if (flightRoute.TryGetProperty("airline", out var airlineElement) && airlineElement.ValueKind == JsonValueKind.Object)
                    {
                        airline = Text(airlineElement, "name");
                    }
                    origin = ReadAirport(flightRoute, "origin");
                    destination = ReadAirport(flightRoute, "destination");
                }

                if (await aircraft.ConfigureAwait(false) is { } aircraftJson
                    && aircraftJson.RootElement.TryGetProperty("response", out var aircraftResponse)
                    && aircraftResponse.ValueKind == JsonValueKind.Object
                    && aircraftResponse.TryGetProperty("aircraft", out var airframe))
                {
                    var manufacturer = Text(airframe, "manufacturer");
                    type = Text(airframe, "type");
                    if (manufacturer != null && type != null && !type.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase))
                    {
                        type = $"{manufacturer} {type}";
                    }
                    registration = Text(airframe, "registration");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = ex.Message;
            }

            return new FlightDetails(airline, origin, destination, type, registration, await track.ConfigureAwait(false), error);
        }

        // OpenSky's track: [time, latitude, longitude, baro altitude, track, on ground] a waypoint. The relay first
        // (the browser's only way); OpenSky itself when the relay has nothing.
        private static async Task<TrackPoint[]> FetchTrackAsync(string icao24, string? relayUrl, CancellationToken cancellation)
        {
            var id = Uri.EscapeDataString(icao24);
            JsonDocument? json = null;
            if (!string.IsNullOrEmpty(relayUrl))
            {
                json = await FetchJsonAsync($"{relayUrl.TrimEnd('/')}/tracks?icao24={id}", cancellation).ConfigureAwait(false);
            }
            if (json == null && !OperatingSystem.IsBrowser())
            {
                json = await FetchJsonAsync($"https://opensky-network.org/api/tracks/all?icao24={id}&time=0", cancellation).ConfigureAwait(false);
            }
            using var owned = json;
            if (json == null || !json.RootElement.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var points = new List<TrackPoint>();
            foreach (var row in path.EnumerateArray())
            {
                if (row.GetArrayLength() < 4 || row[1].ValueKind != JsonValueKind.Number || row[2].ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var altitude = 0.0f;
                if (row[3].ValueKind == JsonValueKind.Number)
                {
                    altitude = MathF.Max(0.0f, row[3].GetSingle());
                }
                points.Add(new TrackPoint(row[0].GetDouble(), row[1].GetSingle(), row[2].GetSingle(), altitude));
            }

            return points.ToArray();
        }

        /// <summary>The reply as JSON, or null for a 404 or anything unreadable -- a lookup that finds nothing is not a failure.</summary>
        private static async Task<JsonDocument?> FetchJsonAsync(string url, CancellationToken cancellation)
        {
            try
            {
                using var response = await Http.GetAsync(url, cancellation).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
                return JsonDocument.Parse(bytes);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellation.IsCancellationRequested)
            {
                return null;
            }
        }

        private static RouteAirport? ReadAirport(JsonElement route, string name)
        {
            if (!route.TryGetProperty(name, out var airport) || airport.ValueKind != JsonValueKind.Object
                || !airport.TryGetProperty("latitude", out var latitude) || !airport.TryGetProperty("longitude", out var longitude))
            {
                return null;
            }

            return new RouteAirport(Text(airport, "iata_code") ?? Text(airport, "icao_code") ?? "?", Text(airport, "municipality") ?? "",
                Text(airport, "name") ?? "", latitude.GetSingle(), longitude.GetSingle());
        }

        private static string? Text(JsonElement element, string name)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return null;
        }

        private static HttpClient CreateHttp()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            if (!OperatingSystem.IsBrowser())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ManaEngine-FlightTraffic/1.0");
            }
            return http;
        }
    }
}
