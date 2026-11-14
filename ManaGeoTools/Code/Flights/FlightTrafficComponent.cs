using System.Text.Json.Serialization;
using Mana;
using Mana.Code.Nodes;
using ManaGeoTools.Code.Planet;

namespace ManaGeoTools.Code.Flights
{
    /// <summary>
    /// Live air traffic over a planet: every aircraft OpenSky tracks, drawn as a dart at its height and heading and
    /// flown on by dead reckoning between replies, which come minutes apart (see <see cref="OpenSkyClient"/> for the
    /// credit limits). The last reply is kept in Library/FlightTraffic, so reopening the level shows traffic at once
    /// and spends no credits until it is stale. Lives on a Planet node; draws nothing elsewhere.
    /// </summary>
    public sealed class FlightTrafficComponent : ManaComponent
    {
        public const string ScriptType = "FlightTraffic";

        /// <summary>Where the last reply is kept between runs.</summary>
        public static string CacheFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "Library", "FlightTraffic");

        public bool Enabled { get; set; } = true;

        /// <summary>Seconds between fetches; 0 picks by account: 90 with an OpenSky API client, 900 without (400 credits a day, 4 a fetch).</summary>
        public float UpdateSeconds { get; set; }

        /// <summary>How many times their true height aircraft fly: 1 on a true-size planet, more to lift them off a small one.</summary>
        public float AltitudeScale { get; set; } = 1.0f;

        /// <summary>Marker length as a share of its distance from the camera, so it holds its size on screen at any zoom.</summary>
        public float MarkerSize { get; set; } = 0.006f;

        public bool ShowGrounded { get; set; }

        /// <summary>Aircraft not heard from in this long are dropped rather than flown on blind.</summary>
        public float MaxAgeMinutes { get; set; } = 30.0f;

        /// <summary>What the feed is doing, for the Inspector and the log.</summary>
        [JsonIgnore]
        public string Status { get; private set; } = "Starting";

        [JsonIgnore]
        public int AircraftShown { get; private set; }

        private readonly OpenSkyClient _client = new();
        private readonly CancellationTokenSource _cancel = new();
        private Task<FlightSnapshot?>? _work;
        private FlightSnapshot? _snapshot;
        private DateTime _nextFetchUtc = DateTime.MinValue;
        private bool _cacheTried;
        private int? _creditsLeft;
        private FlightTrafficMesh? _mesh;
        private MeshRendererComponent? _renderer;
        // The markers' own node, kept out of shadows and reflections: thousands of darts would speckle the globe.
        private ManaSceneNode? _markers;

        public FlightTrafficComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Flight Traffic";
        }

        private TimeSpan Interval
        {
            get
            {
                if (UpdateSeconds > 0.0f)
                {
                    return TimeSpan.FromSeconds(Math.Max(10.0f, UpdateSeconds));
                }

                if (_client.HasAccount)
                {
                    return TimeSpan.FromSeconds(90);
                }

                return TimeSpan.FromSeconds(900);
            }
        }

        private static string CachePath => Path.Combine(CacheFolder, "states.json");

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            if (Owner is not PlanetNode planet)
            {
                return;
            }

            if (_renderer != null)
            {
                _renderer.IsVisible = Enabled;
            }

            if (!Enabled)
            {
                return;
            }

            AdoptFinishedWork();
            StartWorkIfDue();
            if (_snapshot != null)
            {
                Draw(planet, _snapshot);
            }
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            _cancel.Cancel();
            if (_markers != null)
            {
                lock (Owner.Context.World.SceneLock)
                {
                    Owner.RemoveChild(_markers);
                }
                _markers = null;
                _renderer = null;
            }
        }

        private void AdoptFinishedWork()
        {
            if (_work == null || !_work.IsCompleted)
            {
                return;
            }

            if (_work.IsCompletedSuccessfully && _work.Result is { } snapshot)
            {
                _snapshot = snapshot;
            }

            _work = null;
            Console.WriteLine($"[Flights] {Status}");
        }

        private void StartWorkIfDue()
        {
            if (_work != null)
            {
                return;
            }

            if (!_cacheTried)
            {
                _cacheTried = true;
                _work = Task.Run(LoadCache);
                return;
            }

            if (DateTime.UtcNow < _nextFetchUtc)
            {
                return;
            }

            // Held off a full interval until the fetch answers, so a slow reply is never asked for twice.
            _nextFetchUtc = DateTime.UtcNow + Interval;
            _work = Task.Run(FetchAsync);
        }

        private FlightSnapshot? LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath))
                {
                    Status = "No saved traffic; fetching";
                    return null;
                }

                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(CachePath);
                var snapshot = OpenSkyStates.Parse(File.ReadAllBytes(CachePath));
                if (age < Interval)
                {
                    _nextFetchUtc = DateTime.UtcNow + (Interval - age);
                }

                Status = $"{snapshot.States.Length:N0} aircraft from the saved reply ({FormatAge(age)} old)";
                return snapshot;
            }
            catch (Exception ex)
            {
                Status = $"Saved traffic unreadable ({ex.Message}); fetching";
                return null;
            }
        }

        private async Task<FlightSnapshot?> FetchAsync()
        {
            var reply = await _client.FetchAsync(_cancel.Token).ConfigureAwait(false);
            _creditsLeft = reply.CreditsLeft ?? _creditsLeft;
            if (reply.Json == null)
            {
                var wait = reply.RetryAfter ?? TimeSpan.FromSeconds(120);
                _nextFetchUtc = DateTime.UtcNow + wait;
                Status = $"Fetch failed: {reply.Error}; trying again in {FormatAge(wait)}";
                return null;
            }

            var snapshot = OpenSkyStates.Parse(reply.Json);
            try
            {
                Directory.CreateDirectory(CacheFolder);
                await File.WriteAllBytesAsync(CachePath, reply.Json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The browser and read-only checkouts have nowhere to keep it; traffic still shows.
                Console.WriteLine($"[Flights] Could not save the reply: {ex.Message}");
            }

            var account = "anonymous";
            if (_client.HasAccount)
            {
                account = "account";
            }

            var credits = "";
            if (_creditsLeft is { } left)
            {
                credits = $", {left} credits left ({account})";
            }

            Status = $"{snapshot.States.Length:N0} aircraft from OpenSky; next fetch in {FormatAge(Interval)}{credits}";
            return snapshot;
        }

        private void Draw(PlanetNode planet, FlightSnapshot snapshot)
        {
            var states = snapshot.States;
            if (_mesh == null || _mesh.Capacity < states.Length)
            {
                Rebuild(planet, states.Length);
            }

            var mesh = _mesh!;
            Matrix4x4.Invert(Owner.WorldTransform, out var toLocal);
            var camera = Vector3.Transform(Owner.Context.World.CameraNode.WorldTransform.Translation, toLocal);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            var maxAge = MaxAgeMinutes * 60.0f;
            var heightScale = planet.Radius * AltitudeScale / PlanetLoader.EarthRadiusMeters;
            var shown = 0;
            foreach (ref readonly var state in states.AsSpan())
            {
                if (state.OnGround && !ShowGrounded)
                {
                    continue;
                }

                var seconds = (float)Math.Max(0.0, now - state.PositionTime);
                if (seconds > maxAge)
                {
                    continue;
                }

                if (state.OnGround)
                {
                    seconds = 0.0f;
                }

                var (direction, heading) = FlightDeadReckoning.Advance(state, seconds);
                var altitude = FlightDeadReckoning.Altitude(state, seconds);
                var center = direction * (planet.Radius + altitude * heightScale);
                var size = Vector3.Distance(camera, center) * MarkerSize;
                mesh.SetMarker(shown, center, heading, direction, size, AltitudeColor(altitude, state.OnGround));
                shown++;
            }

            mesh.ClearFrom(shown);
            mesh.Publish();
            AircraftShown = shown;
        }

        private void Rebuild(PlanetNode planet, int count)
        {
            // Rounded up so a few more aircraft in the next reply do not rebuild it again.
            var capacity = Math.Max(1024, (count + 2047) / 2048 * 2048);
            _mesh = new FlightTrafficMesh(Owner.Context, capacity, planet.Radius * 1.5f);
            lock (Owner.Context.World.SceneLock)
            {
                if (_markers == null)
                {
                    _markers = new ManaSceneNode(Owner.Context, Matrix4x4.Identity)
                    {
                        Name = "Flight Traffic",
                        IncludeInEnvironmentCapture = false
                    };
                    Owner.AddChild(_markers);
                    _markers.UpdateTransform();
                }

                if (_renderer != null)
                {
                    _markers.Components.Remove(_renderer);
                }

                _renderer = new MeshRendererComponent(_markers, _mesh);
                _markers.Components.Add(_renderer);
            }
        }

        // Green low, through yellow and orange, to magenta at cruise -- the usual flight-map ramp.
        private static readonly (float Meters, Vector4 Color)[] Ramp =
        [
            (0.0f, new Vector4(0.25f, 0.95f, 0.35f, 1.0f)),
            (3000.0f, new Vector4(1.0f, 0.9f, 0.2f, 1.0f)),
            (7000.0f, new Vector4(1.0f, 0.5f, 0.15f, 1.0f)),
            (10500.0f, new Vector4(1.0f, 0.2f, 0.35f, 1.0f)),
            (13000.0f, new Vector4(0.85f, 0.3f, 1.0f, 1.0f))
        ];

        public static Vector4 AltitudeColor(float meters, bool onGround)
        {
            if (onGround)
            {
                return new Vector4(0.7f, 0.7f, 0.7f, 1.0f);
            }

            if (meters <= Ramp[0].Meters)
            {
                return Ramp[0].Color;
            }

            for (var i = 1; i < Ramp.Length; i++)
            {
                if (meters <= Ramp[i].Meters)
                {
                    var t = (meters - Ramp[i - 1].Meters) / (Ramp[i].Meters - Ramp[i - 1].Meters);
                    return Vector4.Lerp(Ramp[i - 1].Color, Ramp[i].Color, t);
                }
            }

            return Ramp[^1].Color;
        }

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalMinutes >= 1.0)
            {
                return $"{age.TotalMinutes:0} min";
            }

            return $"{age.TotalSeconds:0} s";
        }
    }
}
