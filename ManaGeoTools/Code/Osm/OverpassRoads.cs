using System.Globalization;
using System.Text.Json;

namespace ManaGeoTools.Code.Osm
{
    public enum RoadClass
    {
        Motorway,
        Trunk,
        Primary
    }

    /// <summary>One stretch of road: its class and its points as (longitude, latitude) degrees.</summary>
    public sealed record RoadLine(RoadClass Class, (double Longitude, double Latitude)[] Points);

    /// <summary>
    /// The larger roads of a lat/long box from OpenStreetMap through the Overpass API: motorways, trunk and
    /// primary roads, their geometry only -- no tags, no nodes -- so a box of tens of kilometres is a small reply.
    /// OSM data is (c) OpenStreetMap contributors, ODbL.
    /// </summary>
    public static class OverpassRoads
    {
        /// <summary>The public Overpass servers, tried in turn: each is busy or down now and then.</summary>
        public static readonly string[] Endpoints =
        [
            "https://overpass-api.de/api/interpreter",
            "https://overpass.private.coffee/api/interpreter",
            "https://overpass.kumi.systems/api/interpreter"
        ];

        // The server that answered last is asked first next time.
        private static int _preferred;

        private static readonly RoadClass[] ClassOrder = [RoadClass.Motorway, RoadClass.Trunk, RoadClass.Primary];

        /// <summary>
        /// One set per class, untagged and without node lists (<c>ids geom</c>), clipped to the box; the <c>out count</c> after each set marks
        /// where the next class starts in the reply.
        /// </summary>
        public static string Query(double south, double west, double north, double east)
        {
            var box = string.Create(CultureInfo.InvariantCulture, $"{south:0.####},{west:0.####},{north:0.####},{east:0.####}");
            return $"[out:json][timeout:60][bbox:{box}];" +
                $"way[highway=motorway];out ids geom({box});out count;" +
                $"way[highway=trunk];out ids geom({box});out count;" +
                $"way[highway=primary];out ids geom({box});";
        }

        /// <summary>
        /// The reply's JSON text from the first server that gives one; throws <see cref="OverpassBusyException"/> when
        /// every one is busy or down, so the caller comes back later.
        /// </summary>
        public static async Task<string> FetchAsync(HttpClient http, double south, double west, double north, double east, CancellationToken cancellation)
        {
            var query = Uri.EscapeDataString(Query(south, west, north, east));
            var reasons = new List<string>();
            var first = Volatile.Read(ref _preferred);
            for (var attempt = 0; attempt < Endpoints.Length; attempt++)
            {
                var index = (first + attempt) % Endpoints.Length;
                try
                {
                    var text = await FetchFromAsync(http, $"{Endpoints[index]}?data={query}", cancellation).ConfigureAwait(false);
                    Volatile.Write(ref _preferred, index);
                    return text;
                }
                catch (Exception ex) when (ex is OverpassBusyException or HttpRequestException or JsonException
                    || (ex is TaskCanceledException && !cancellation.IsCancellationRequested))
                {
                    reasons.Add($"{new Uri(Endpoints[index]).Host}: {ex.Message}");
                }
            }
            throw new OverpassBusyException(string.Join("; ", reasons));
        }

        private static async Task<string> FetchFromAsync(HttpClient http, string url, CancellationToken cancellation)
        {
            using var response = await http.GetAsync(url, cancellation).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status == 429 || status >= 500)
            {
                throw new OverpassBusyException($"busy ({status})");
            }
            response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            // A query that ran out of time or memory still comes back 200, with what it had and a remark.
            using (var document = JsonDocument.Parse(text))
            {
                if (document.RootElement.TryGetProperty("remark", out var remark) && remark.GetString() is { } message
                    && message.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    throw new OverpassBusyException(message);
                }
            }
            return text;
        }

        public static List<RoadLine> Parse(string json)
        {
            var lines = new List<RoadLine>();
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
            {
                return lines;
            }

            var classIndex = 0;
            var run = new List<(double Longitude, double Latitude)>();
            foreach (var element in elements.EnumerateArray())
            {
                var type = "";
                if (element.TryGetProperty("type", out var typeValue))
                {
                    type = typeValue.GetString() ?? "";
                }
                if (type == "count")
                {
                    classIndex++;
                    continue;
                }
                if (type != "way" || classIndex >= ClassOrder.Length || !element.TryGetProperty("geometry", out var geometry))
                {
                    continue;
                }

                // Clipped geometry leaves a null for each point outside the box: the road breaks there.
                foreach (var point in geometry.EnumerateArray())
                {
                    if (point.ValueKind != JsonValueKind.Object)
                    {
                        Flush(lines, run, ClassOrder[classIndex]);
                        continue;
                    }
                    run.Add((point.GetProperty("lon").GetDouble(), point.GetProperty("lat").GetDouble()));
                }
                Flush(lines, run, ClassOrder[classIndex]);
            }
            return lines;
        }

        private static void Flush(List<RoadLine> lines, List<(double Longitude, double Latitude)> run, RoadClass roadClass)
        {
            if (run.Count >= 2)
            {
                lines.Add(new RoadLine(roadClass, run.ToArray()));
            }
            run.Clear();
        }
    }

    public sealed class OverpassBusyException(string message) : Exception(message);
}
