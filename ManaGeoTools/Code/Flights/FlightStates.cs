using System.Text.Json;

namespace ManaGeoTools.Code.Flights
{
    /// <summary>One aircraft as last reported: where it was at <see cref="PositionTime"/> and how it was moving.</summary>
    public readonly record struct FlightState(
        string Icao24,
        string Callsign,
        float Longitude,
        float Latitude,
        float AltitudeMeters,
        float SpeedMetersPerSecond,
        float TrackDegrees,
        float VerticalRateMetersPerSecond,
        double PositionTime,
        bool OnGround);

    /// <summary>Every aircraft in one reply, and the reply's own time (Unix seconds).</summary>
    public sealed record FlightSnapshot(double Time, FlightState[] States);

    /// <summary>
    /// Reads OpenSky's <c>/api/states/all</c> reply: <c>{"time", "states": [[icao24, callsign, country, time_position,
    /// last_contact, longitude, latitude, baro_altitude, on_ground, velocity, true_track, vertical_rate, sensors,
    /// geo_altitude, ...]]}</c>, any field possibly null. Rows with no position are skipped.
    /// </summary>
    public static class OpenSkyStates
    {
        public static FlightSnapshot Parse(ReadOnlySpan<byte> json)
        {
            var reader = new Utf8JsonReader(json);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            double time = 0;
            if (root.TryGetProperty("time", out var timeElement) && timeElement.ValueKind == JsonValueKind.Number)
            {
                time = timeElement.GetDouble();
            }

            if (!root.TryGetProperty("states", out var states) || states.ValueKind != JsonValueKind.Array)
            {
                return new FlightSnapshot(time, []);
            }

            var result = new List<FlightState>(states.GetArrayLength());
            foreach (var row in states.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 12)
                {
                    continue;
                }

                var longitude = Number(row[5]);
                var latitude = Number(row[6]);
                if (longitude == null || latitude == null)
                {
                    continue;
                }

                // GPS height where there is one: barometric drifts with the weather.
                var altitude = 0.0;
                if (row.GetArrayLength() > 13 && Number(row[13]) is { } geometric)
                {
                    altitude = geometric;
                }
                else if (Number(row[7]) is { } barometric)
                {
                    altitude = barometric;
                }

                var positionTime = Number(row[3]) ?? Number(row[4]) ?? time;
                result.Add(new FlightState(
                    Text(row[0]),
                    Text(row[1]).Trim(),
                    (float)longitude.Value,
                    (float)latitude.Value,
                    (float)Math.Max(0.0, altitude),
                    (float)(Number(row[9]) ?? 0.0),
                    (float)(Number(row[10]) ?? 0.0),
                    (float)(Number(row[11]) ?? 0.0),
                    positionTime,
                    row[8].ValueKind == JsonValueKind.True));
            }

            return new FlightSnapshot(time, result.ToArray());
        }

        private static double? Number(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetDouble();
            }

            return null;
        }

        private static string Text(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? "";
            }

            return "";
        }
    }
}
