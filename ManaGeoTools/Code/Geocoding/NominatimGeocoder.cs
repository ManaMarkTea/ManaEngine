using System.Globalization;
using System.Text.Json;

namespace ManaGeoTools.Code.Geocoding
{
    public readonly record struct GeocodeResult(string DisplayName, float Longitude, float Latitude);

    /// <summary>
    /// Wraps OpenStreetMap's Nominatim search API (https://nominatim.org/release-docs/latest/api/Search/)
    /// -- the free, public geocoding service behind OSM's own search box. Its usage policy
    /// (https://operations.osmfoundation.org/policies/nominatim/) requires a descriptive User-Agent
    /// identifying the calling application (a generic browser-style one gets blocked) and caps casual use
    /// at roughly one request/second; this is a single interactive user typing into a search box, well
    /// under that, so no extra client-side throttling is added on top. Mirrors
    /// TerrariumTileFetcher's own "one shared HttpClient, one attempt, no retry/backoff" shape -- the
    /// second network call in this codebase.
    /// </summary>
    public static class NominatimGeocoder
    {
        private const string SearchUrlTemplate = "https://nominatim.openstreetmap.org/search?q={0}&format=json&limit={1}";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "ManaRenderer/1.0 (+https://github.com/ManaMarkTea/ManaRenderer)");
            return client;
        }

        public static async Task<IReadOnlyList<GeocodeResult>> SearchAsync(string query, int maxResults = 5)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Array.Empty<GeocodeResult>();
            }

            var url = string.Format(SearchUrlTemplate, Uri.EscapeDataString(query), maxResults);
            try
            {
                using var stream = await Http.GetStreamAsync(url);
                using var document = await JsonDocument.ParseAsync(stream);

                var results = new List<GeocodeResult>();
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("lat", out var latProperty) ||
                        !element.TryGetProperty("lon", out var lonProperty))
                    {
                        continue;
                    }

                    if (!float.TryParse(latProperty.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                        !float.TryParse(lonProperty.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
                    {
                        continue;
                    }

                    var displayName = element.TryGetProperty("display_name", out var nameProperty)
                        ? nameProperty.GetString() ?? query
                        : query;
                    results.Add(new GeocodeResult(displayName, longitude, latitude));
                }

                return results;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                Console.WriteLine($"[Nominatim] Search for '{query}' failed: {ex.Message}");
                return Array.Empty<GeocodeResult>();
            }
        }
    }
}
