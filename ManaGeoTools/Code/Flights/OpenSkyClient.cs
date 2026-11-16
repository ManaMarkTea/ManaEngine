using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ManaGeoTools.Code.Flights
{
    /// <summary>The outcome of one fetch: the reply's bytes, or how long OpenSky asked us to wait.</summary>
    public sealed record OpenSkyReply(byte[]? Json, TimeSpan? RetryAfter, string? Error, int? CreditsLeft)
    {
        /// <summary>Where the reply came from.</summary>
        public FlightFeedSource Source { get; init; }

        /// <summary>The usual source failed and this came from the next one; why, for the UI.</summary>
        public string? FallbackReason { get; init; }
    }

    public enum FlightFeedSource
    {
        None,
        /// <summary>The ManaServers relay on hera: one shared copy for every viewer, and the only way a browser gets traffic.</summary>
        Relay,
        /// <summary>OpenSky itself with this machine's API client.</summary>
        OpenSkyAccount,
        /// <summary>OpenSky itself, anonymously: 400 credits a day for this address, a fetch every 15 minutes.</summary>
        OpenSkyAnonymous
    }

    /// <summary>
    /// Fetches every aircraft from OpenSky (non-commercial use; credit "The OpenSky Network" where shown).
    /// Anonymous use gets 400 credits a day and a global query costs 4; an account's API client (Account >
    /// API Client, saved as credentials.json) gets 4,000. Its id and secret are read from OPENSKY_CLIENT_ID /
    /// OPENSKY_CLIENT_SECRET, or that credentials.json copied to %LOCALAPPDATA%/ManaRenderer/opensky-credentials.json.
    /// </summary>
    public sealed class OpenSkyClient
    {
        // Extended: each row also says what kind of aircraft it is (OpenSky's emitter category), for the close-up models.
        private const string StatesUrl = "https://opensky-network.org/api/states/all?extended=1";

        /// <summary>The relay ManaServers runs on hera (role flight_relay): /states, /tracks?icao24=, /health.</summary>
        public const string DefaultRelayUrl = "https://hera.manamarktea.com/flights";
        private const string TokenUrl = "https://auth.opensky-network.org/auth/realms/opensky-network/protocol/openid-connect/token";

        private static readonly HttpClient Http = CreateHttp();

        private readonly (string Id, string Secret)? _credentials = ReadCredentials();
        private string? _token;
        private DateTime _tokenExpires;

        public bool HasAccount => _credentials != null;

        /// <summary>The relay to read first; null or empty goes to OpenSky directly.</summary>
        public string? RelayUrl { get; set; } = DefaultRelayUrl;

        /// <summary>
        /// Every aircraft: from the relay (one shared copy, no credits spent here), and when the relay fails, from OpenSky
        /// itself -- with this machine's API client if it has one, anonymously if not. The relay uses the same account,
        /// so asking OpenSky every time as well would spend its credits twice. The browser has only the relay.
        /// </summary>
        public async Task<OpenSkyReply> FetchAsync(CancellationToken cancellation)
        {
            var relay = RelayUrl;
            if (string.IsNullOrEmpty(relay))
            {
                return await FetchDirectAsync(cancellation).ConfigureAwait(false);
            }

            var reply = await FetchRelayAsync(relay, cancellation).ConfigureAwait(false);
            if (reply.Json != null || OperatingSystem.IsBrowser())
            {
                return reply;
            }
            var direct = await FetchDirectAsync(cancellation).ConfigureAwait(false);
            return direct with { FallbackReason = $"Relay unreachable ({reply.Error}); straight from OpenSky" };
        }

        private static async Task<OpenSkyReply> FetchRelayAsync(string relay, CancellationToken cancellation)
        {
            try
            {
                using var response = await Http.GetAsync($"{relay.TrimEnd('/')}/states", cancellation).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return new OpenSkyReply(null, null, $"relay said {(int)response.StatusCode}", null) { Source = FlightFeedSource.Relay };
                }
                var json = await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
                return new OpenSkyReply(json, null, null, null) { Source = FlightFeedSource.Relay };
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellation.IsCancellationRequested)
            {
                return new OpenSkyReply(null, null, ex.Message, null) { Source = FlightFeedSource.Relay };
            }
        }

        private async Task<OpenSkyReply> FetchDirectAsync(CancellationToken cancellation)
        {
            var source = FlightFeedSource.OpenSkyAnonymous;
            if (HasAccount)
            {
                source = FlightFeedSource.OpenSkyAccount;
            }
            var reply = await FetchOpenSkyAsync(cancellation).ConfigureAwait(false);
            return reply with { Source = source };
        }

        private async Task<OpenSkyReply> FetchOpenSkyAsync(CancellationToken cancellation)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, StatesUrl);
                if (_credentials != null)
                {
                    var token = await TokenAsync(cancellation).ConfigureAwait(false);
                    if (token != null)
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                }

                using var response = await Http.SendAsync(request, cancellation).ConfigureAwait(false);
                var credits = HeaderNumber(response, "X-Rate-Limit-Remaining");
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var wait = HeaderNumber(response, "X-Rate-Limit-Retry-After-Seconds") ?? 3600;
                    return new OpenSkyReply(null, TimeSpan.FromSeconds(Math.Max(60, wait)), "out of OpenSky credits", 0);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _token = null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new OpenSkyReply(null, null, $"OpenSky said {(int)response.StatusCode} {response.ReasonPhrase}", credits);
                }

                var json = await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
                return new OpenSkyReply(json, null, null, credits);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new OpenSkyReply(null, null, ex.Message, null);
            }
        }

        private async Task<string?> TokenAsync(CancellationToken cancellation)
        {
            if (_token != null && DateTime.UtcNow < _tokenExpires)
            {
                return _token;
            }

            var (id, secret) = _credentials!.Value;
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = id,
                ["client_secret"] = secret
            });
            using var response = await Http.PostAsync(TokenUrl, form, cancellation).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Falls back to anonymous rather than failing the fetch outright.
                Console.WriteLine($"[Flights] OpenSky token request failed: {(int)response.StatusCode}; fetching anonymously.");
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false));
            _token = document.RootElement.GetProperty("access_token").GetString();
            var lifetime = 1800;
            if (document.RootElement.TryGetProperty("expires_in", out var expires) && expires.ValueKind == JsonValueKind.Number)
            {
                lifetime = expires.GetInt32();
            }

            _tokenExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, lifetime - 60));
            return _token;
        }

        private static (string, string)? ReadCredentials()
        {
            var id = Environment.GetEnvironmentVariable("OPENSKY_CLIENT_ID");
            var secret = Environment.GetEnvironmentVariable("OPENSKY_CLIENT_SECRET");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(secret))
            {
                return (id, secret);
            }

            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ManaRenderer", "opensky-credentials.json");
                if (!File.Exists(path))
                {
                    return null;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                if (root.TryGetProperty("clientId", out var idElement) && root.TryGetProperty("clientSecret", out var secretElement))
                {
                    return (idElement.GetString() ?? "", secretElement.GetString() ?? "");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Flights] Could not read OpenSky credentials: {ex.Message}");
            }

            return null;
        }

        private static int? HeaderNumber(HttpResponseMessage response, string name)
        {
            if (response.Headers.TryGetValues(name, out var values) && int.TryParse(values.FirstOrDefault(), out var number))
            {
                return number;
            }

            return null;
        }

        private static HttpClient CreateHttp()
        {
            HttpClient http;
            // The relay gzips the 1.5 MB reply; the browser's fetch unpacks it by itself.
            if (OperatingSystem.IsBrowser())
            {
                http = new HttpClient();
            }
            else
            {
                http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
            }
            http.Timeout = TimeSpan.FromSeconds(60);
            // Browsers refuse a page setting its own User-Agent.
            if (!OperatingSystem.IsBrowser())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ManaEngine-FlightTraffic/1.0");
            }
            return http;
        }
    }
}
