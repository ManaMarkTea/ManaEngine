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

        /// <summary>The nearest aircraft drawn as models (an airliner, or an An-2 for light ones) instead of markers.</summary>
        public bool Models { get; set; } = true;

        /// <summary>How many of the nearest aircraft are drawn as models.</summary>
        public int ModelCount { get; set; } = 24;

        /// <summary>Kilometres from the camera within which an aircraft may be a model; the selected one always is.</summary>
        public float ModelRange { get; set; } = 150.0f;

        /// <summary>A model's length against its marker's: at a marker's dozen pixels a model reads no better than one.</summary>
        public float ModelScale { get; set; } = 3.0f;

        public bool ShowGrounded { get; set; }

        /// <summary>Every aircraft in <see cref="Color"/>, or false to shade each by its height (green low to magenta at cruise).</summary>
        public bool OneColor { get; set; } = true;

        public Vector4 Color { get; set; } = new(0.3f, 0.85f, 1.0f, 1.0f);

        /// <summary>Rings round the busiest airports, sized by the aircraft coming and going (drawn by the editor's viewport).</summary>
        public bool ShowAirports { get; set; } = true;

        /// <summary>How far from an airport, in kilometres, an aircraft counts as one of its own.</summary>
        public float AirportRadius { get; set; } = 100.0f;

        /// <summary>Aircraft above this many metres are passing over, not coming or going.</summary>
        public float AirportAltitude { get; set; } = 4500.0f;

        /// <summary>How many of the busiest airports are ringed.</summary>
        public int AirportCount { get; set; } = 40;

        /// <summary>Aircraft not heard from in this long are dropped rather than flown on blind.</summary>
        public float MaxAgeMinutes { get; set; } = 30.0f;

        /// <summary>What the feed is doing, for the Inspector and the log.</summary>
        [JsonIgnore]
        public string Status { get; private set; } = "Starting";

        [JsonIgnore]
        public int AircraftShown { get; private set; }

        /// <summary>The relay to read (ManaServers' on hera); empty goes to OpenSky directly.</summary>
        public string RelayUrl { get => _client.RelayUrl ?? ""; set => _client.RelayUrl = value; }

        /// <summary>Set while traffic comes from a fallback because the usual source failed -- shown under the flight search.</summary>
        [JsonIgnore]
        public string? FeedNote { get; private set; }

        /// <summary>Where the traffic shown came from; None until a fetch has answered.</summary>
        [JsonIgnore]
        public FlightFeedSource Source { get; private set; }

        /// <summary>The last fetch brought traffic (false: what is shown is the saved reply, or nothing).</summary>
        [JsonIgnore]
        public bool Live { get; private set; }

        /// <summary>Airports with traffic round them, busiest first; recounted every <see cref="AirportRecountSeconds"/>.</summary>
        [JsonIgnore]
        public IReadOnlyList<AirportCount> BusyAirports { get; private set; } = [];

        private const double AirportRecountSeconds = 20.0;
        private static readonly Lazy<Airport[]> Airports = new(() => AirportTraffic.Load(ManaTexture.ResolveTexturePath(Path.Combine("Assets", "GeoData", "airports.csv"))));
        private Task<AirportCount[]>? _counting;
        private DateTime _nextCountUtc;

        private const long RedrawMilliseconds = 100;
        private Vector3 _drawnFrom;
        private FlightSnapshot? _drawnSnapshot;
        private long _drawnAt;

        // Where each aircraft drawn last went, for picking one with the pointer: its index in the snapshot and its local centre.
        private int[] _drawnIndexes = [];
        private Vector3[] _drawnCenters = [];
        private int _drawnCount;
        private CancellationTokenSource? _detailsCancel;
        private Task<FlightDetails>? _detailsWork;

        private static readonly Vector4 SelectedColor = new(1.0f, 1.0f, 1.0f, 1.0f);

        /// <summary>The aircraft picked by a click or a search (its ICAO 24-bit address), or null.</summary>
        [JsonIgnore]
        public string? SelectedIcao24 { get; private set; }

        /// <summary>The selected aircraft's latest report, while it is still in the feed.</summary>
        [JsonIgnore]
        public FlightState? Selected
        {
            get
            {
                if (SelectedIcao24 == null || _snapshot == null)
                {
                    return null;
                }
                foreach (ref readonly var state in _snapshot.States.AsSpan())
                {
                    if (state.Icao24 == SelectedIcao24)
                    {
                        return state;
                    }
                }
                return null;
            }
        }

        /// <summary>Route, airframe and path flown for the selection; null while they are fetched.</summary>
        [JsonIgnore]
        public FlightDetails? SelectedDetails { get; private set; }

        [JsonIgnore]
        public bool SelectedDetailsLoading => _detailsWork != null;

        /// <summary>The selection's trip: the callsign's route on record when the aircraft is flying it, else what its track shows.</summary>
        [JsonIgnore]
        public FlightRoute? SelectedRoute
        {
            get
            {
                if (Selected is not { } state)
                {
                    return null;
                }
                // Judged again only when a new report or the details come in.
                if (!ReferenceEquals(_routeFor, SelectedDetails) || _routeAt != state.PositionTime)
                {
                    _routeFor = SelectedDetails;
                    _routeAt = state.PositionTime;
                    _route = FlightRouteJudge.Judge(SelectedDetails, state, Airports.Value);
                }
                return _route;
            }
        }

        private FlightDetails? _routeFor;
        private double _routeAt = double.NaN;
        private FlightRoute? _route;

        /// <summary>Picks an aircraft (null clears the pick) and starts fetching its details.</summary>
        public void Select(string? icao24)
        {
            if (icao24 == SelectedIcao24)
            {
                return;
            }

            _detailsCancel?.Cancel();
            _detailsCancel = null;
            _detailsWork = null;
            SelectedDetails = null;
            SelectedIcao24 = icao24;
            _drawnAt = 0;
            if (Selected is { } state)
            {
                _detailsCancel = new CancellationTokenSource();
                var token = _detailsCancel.Token;
                var relay = RelayUrl;
                _detailsWork = Task.Run(() => FlightDetailsLookup.FetchAsync(state, relay, token));
            }
        }

        /// <summary>
        /// The aircraft a search names: a callsign (UAL2095, spaces ignored), its start, or an ICAO address. An exact
        /// callsign wins over one it starts.
        /// </summary>
        public FlightState? Find(string text)
        {
            var wanted = text.Replace(" ", "").Trim();
            if (_snapshot == null || wanted.Length == 0)
            {
                return null;
            }

            FlightState? starts = null;
            foreach (ref readonly var state in _snapshot.States.AsSpan())
            {
                if (string.Equals(state.Callsign, wanted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(state.Icao24, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return state;
                }
                if (starts == null && state.Callsign.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    starts = state;
                }
            }
            return starts;
        }

        /// <summary>Where <paramref name="state"/> is now in the planet's local space, flown on from its report, and its heading.</summary>
        public (Vector3 Center, Vector3 Heading) Locate(PlanetNode planet, in FlightState state)
        {
            var seconds = (float)Math.Max(0.0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - state.PositionTime);
            if (state.OnGround)
            {
                seconds = 0.0f;
            }
            var (direction, heading) = FlightDeadReckoning.Advance(state, seconds);
            var altitude = FlightDeadReckoning.Altitude(state, seconds);
            return (direction * (planet.Radius + altitude * HeightScale(planet)), heading);
        }

        /// <summary>A track point in the planet's local space, at the height aircraft are drawn.</summary>
        public Vector3 Place(PlanetNode planet, float latitude, float longitude, float altitudeMeters)
        {
            return new LatLong(longitude, latitude).ToUnitSphere() * (planet.Radius + altitudeMeters * HeightScale(planet));
        }

        private float HeightScale(PlanetNode planet) => planet.Radius * AltitudeScale / PlanetLoader.EarthRadiusMeters;

        /// <summary>The ICAO address of the drawn aircraft nearest <paramref name="toScreen"/>'s point within <paramref name="reach"/> pixels.</summary>
        public string? Pick(Func<Vector3, Vector2?> toScreen, Vector2 point, float reach)
        {
            if (_snapshot == null)
            {
                return null;
            }

            string? best = null;
            var bestDistance = reach;
            for (var i = 0; i < _drawnCount; i++)
            {
                if (toScreen(_drawnCenters[i]) is not { } screen)
                {
                    continue;
                }
                var distance = Vector2.Distance(screen, point);
                if (distance <= bestDistance && _drawnIndexes[i] < _snapshot.States.Length)
                {
                    bestDistance = distance;
                    best = _snapshot.States[_drawnIndexes[i]].Icao24;
                }
            }
            return best;
        }

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
        private FlightTrafficModels? _models;
        private readonly List<Nearest> _nearest = [];

        private readonly record struct Nearest(int Index, float DistanceSquared, Vector3 Center, Vector3 Heading, Vector3 Up, float Size, Vector4 Color);

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

                // The relay refetches on its own timer; asking it every minute costs OpenSky nothing.
                if (Source is FlightFeedSource.Relay or FlightFeedSource.None)
                {
                    return TimeSpan.FromSeconds(60);
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
            if (_detailsWork is { IsCompleted: true } details)
            {
                if (details.IsCompletedSuccessfully)
                {
                    SelectedDetails = details.Result;
                }
                _detailsWork = null;
            }
            StartWorkIfDue();
            if (_snapshot != null)
            {
                Draw(planet, _snapshot);
                CountAirports(_snapshot);
            }
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            _cancel.Cancel();
            _detailsCancel?.Cancel();
            if (_markers != null)
            {
                lock (Owner.Context.World.SceneLock)
                {
                    Owner.RemoveChild(_markers);
                }
                _markers = null;
                _models = null;
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
                // A new reply may hold the selection under a new index; the details stay.
                _drawnAt = 0;
                _nextCountUtc = DateTime.MinValue;
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
            Source = reply.Source;
            Live = reply.Json != null;
            FeedNote = reply.FallbackReason;
            if (reply.Json == null && OperatingSystem.IsBrowser())
            {
                FeedNote = $"Flight relay unreachable ({reply.Error}); the browser has no other way to OpenSky";
            }
            if (reply.Json == null)
            {
                var wait = reply.RetryAfter ?? TimeSpan.FromSeconds(120);
                _nextFetchUtc = DateTime.UtcNow + wait;
                Status = $"Fetch failed: {reply.Error}; trying again in {FormatAge(wait)}";
                if (!OperatingSystem.IsBrowser())
                {
                    FeedNote = $"No live traffic ({reply.Error}); showing the last saved reply";
                }
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

            var account = reply.Source switch
            {
                FlightFeedSource.Relay => "relay",
                FlightFeedSource.OpenSkyAccount => "account",
                _ => "anonymous"
            };

            var credits = "";
            if (_creditsLeft is { } left)
            {
                credits = $", {left} credits left ({account})";
            }

            var from = reply.Source switch
            {
                FlightFeedSource.Relay => "the relay",
                FlightFeedSource.OpenSkyAccount => "OpenSky (this machine's account)",
                _ => "OpenSky (anonymous)"
            };
            Status = $"{snapshot.States.Length:N0} aircraft from {from}; next fetch in {FormatAge(Interval)}{credits}";
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
            if (!DueForRedraw(planet, snapshot, camera))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            var maxAge = MaxAgeMinutes * 60.0f;
            var heightScale = planet.Radius * AltitudeScale / PlanetLoader.EarthRadiusMeters;
            var shown = 0;
            if (_drawnIndexes.Length < states.Length)
            {
                _drawnIndexes = new int[states.Length];
                _drawnCenters = new Vector3[states.Length];
            }
            _nearest.Clear();
            var index = -1;
            foreach (ref readonly var state in states.AsSpan())
            {
                index++;
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
                // Over the horizon: hidden by the planet anyway, and half the traffic not rewritten every frame.
                if (Vector3.Dot(direction, camera) < planet.Radius * 0.99f)
                {
                    continue;
                }

                var center = direction * (planet.Radius + altitude * heightScale);
                var size = Vector3.Distance(camera, center) * MarkerSize;
                var color = Color;
                if (state.Icao24 == SelectedIcao24)
                {
                    size *= 1.8f;
                    color = SelectedColor;
                }
                if (!OneColor)
                {
                    color = AltitudeColor(altitude, state.OnGround);
                }
                var distanceSquared = Vector3.DistanceSquared(camera, center);
                var range = ModelRange * 1000.0f * planet.Radius / PlanetLoader.EarthRadiusMeters;
                if (Models && ModelCount > 0 && (distanceSquared < range * range || state.Icao24 == SelectedIcao24))
                {
                    // The selected aircraft first, then nearest first.
                    if (state.Icao24 == SelectedIcao24)
                    {
                        distanceSquared = -1.0f;
                    }
                    _nearest.Add(new Nearest(index, distanceSquared, center, heading, direction, size, color));
                    continue;
                }
                mesh.SetMarker(shown, center, heading, direction, size, color);
                _drawnIndexes[shown] = index;
                _drawnCenters[shown] = center;
                shown++;
            }

            // The nearest few as models, the rest as markers; a model that cannot load leaves its marker.
            _models ??= new FlightTrafficModels(Owner.Context, _markers!);
            _models.Begin();
            if (_nearest.Count > 0)
            {
                _nearest.Sort(static (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));
                for (var i = 0; i < _nearest.Count; i++)
                {
                    var near = _nearest[i];
                    var size = near.Size;
                    if (i < ModelCount && _models.Place(FlightTrafficModels.KindOf(states[near.Index]), near.Center, near.Heading, near.Up, near.Size * ModelScale))
                    {
                        // Its marker folds to nothing; the slot stays, so picking still finds the aircraft.
                        size = 0.0f;
                    }
                    mesh.SetMarker(shown, near.Center, near.Heading, near.Up, size, near.Color);
                    _drawnIndexes[shown] = near.Index;
                    _drawnCenters[shown] = near.Center;
                    shown++;
                }
            }
            _models.End();

            mesh.Publish(shown);
            _drawnCount = shown;
            AircraftShown = shown;
        }

        // Aircraft creep across the screen, so a still camera needs them rewritten ten times a second, not every frame;
        // a moving one (their size follows its distance) every frame.
        private bool DueForRedraw(PlanetNode planet, FlightSnapshot snapshot, Vector3 camera)
        {
            var now = Environment.TickCount64;
            var height = MathF.Max(camera.Length() - planet.Radius, planet.Radius * 1e-6f);
            var moved = Vector3.Distance(camera, _drawnFrom) > height * 0.001f;
            if (!moved && ReferenceEquals(snapshot, _drawnSnapshot) && now - _drawnAt < RedrawMilliseconds)
            {
                return false;
            }

            _drawnFrom = camera;
            _drawnSnapshot = snapshot;
            _drawnAt = now;
            return true;
        }

        // Off the main thread: every low aircraft against a thousand airports is a few milliseconds.
        private void CountAirports(FlightSnapshot snapshot)
        {
            if (_counting is { IsCompleted: true } done)
            {
                if (done.IsCompletedSuccessfully)
                {
                    BusyAirports = done.Result;
                }
                _counting = null;
            }

            if (!ShowAirports || _counting != null || DateTime.UtcNow < _nextCountUtc)
            {
                return;
            }

            _nextCountUtc = DateTime.UtcNow.AddSeconds(AirportRecountSeconds);
            var radius = AirportRadius;
            var altitude = AirportAltitude;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            _counting = Task.Run(() => AirportTraffic.Count(Airports.Value, snapshot, now, radius, altitude));
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
