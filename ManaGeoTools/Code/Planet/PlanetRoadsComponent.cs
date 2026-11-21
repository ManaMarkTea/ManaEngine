using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Mana;
using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaCore.Graphics;
using ManaGeoTools.Code.Geometry;
using ManaGeoTools.Code.Osm;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// OpenStreetMap's larger roads (motorway, trunk, primary) drawn on a planet near the camera. Once the camera is
    /// below <see cref="ShowBelow"/> km the zoom-9 vector tiles around it (~50-80 km across) are fetched from
    /// OpenFreeMap (kept in Library/PlanetRoads on desktop), drawn as lines on the terrain and re-laid on it as the
    /// patches below refine, and let go again when the camera leaves. Primary roads show only lower down. The same tiles'
    /// lakes, rivers and ponds are laid on the ground as water (<see cref="Water"/>): the satellite picture shows inland
    /// water only where it is kilometres wide, and Natural Earth's lakes leave out all but the largest.
    /// </summary>
    public sealed class PlanetRoadsComponent : ManaComponent
    {
        public const string ScriptType = "PlanetRoads";

        /// <summary>Where fetched tiles are kept between runs (desktop only; the browser fetches again).</summary>
        public static string CacheFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "Library", "PlanetRoads");

        // Zoom 9 carries every primary road, its points to ~15 m, in tiles of ~150 KB around a city.
        private const int TileZoom = 9;
        private const int DetailZoom = 14;
        private const float DetailBelowKm = 1.6f;
        private const float DetailRadiusKm = 4.0f;
        private const float DetailDrawMetres = 3000.0f;
        private const int RoadKinds = 3;
        private const int BridgeOffset = RoadKinds * 2;
        private const int RoadClassCount = BridgeOffset * 2;

        private const int MaxCells = 48;

        // Roads are straight between OSM's points, which may be kilometres apart on a motorway: cut them finer so the
        // line follows the ground between.
        private const float DensifyMetres = 150.0f;

        // Share of the camera distance a road is pulled toward the camera (draped_line.wgsl): enough to beat the
        // ground it lies on, too little to show through a hill in front.
        private const float TowardCamera = 0.004f;

        public bool Enabled { get; set; } = true;

        public bool Motorways { get; set; } = true;

        public bool Trunks { get; set; } = true;

        public bool Primaries { get; set; } = true;

        /// <summary>Height above the ground, in km, below which roads are fetched and motorways and trunk roads show.</summary>
        public float ShowBelow { get; set; } = 150.0f;

        /// <summary>Height in km below which primary roads show as well.</summary>
        public float PrimaryBelow { get; set; } = 40.0f;

        /// <summary>The farthest roads are fetched around the camera, in km (nearer when the camera is low).</summary>
        public float LoadRadius { get; set; } = 120.0f;

        /// <summary>Narrowest a motorway is drawn on screen, in pixels (far off); trunk and primary roads a little thinner.</summary>
        public float Width { get; set; } = 2.0f;

        /// <summary>Far roads in their map colours (below) rather than the grey of roads seen from the air.</summary>
        public bool MapStyle { get; set; }

        /// <summary>Guard rails and street lights from Kenney's City Kit along the roads near a low camera.</summary>
        public bool Props { get; set; } = true;

        public Vector4 MotorwayColor { get; set; } = new(1.0f, 0.55f, 0.15f, 1.0f);

        public Vector4 TrunkColor { get; set; } = new(0.95f, 0.3f, 0.25f, 1.0f);

        public Vector4 PrimaryColor { get; set; } = new(1.0f, 0.85f, 0.35f, 1.0f);

        /// <summary>Lakes, rivers and ponds from the same tiles, laid on the ground.</summary>
        public bool Water { get; set; } = true;

        /// <summary>Height in km below which the water shows.</summary>
        public float WaterBelow { get; set; } = 150.0f;


        [JsonIgnore]
        public string Status
        {
            get
            {
                if (_layer.Status.Length == 0)
                {
                    return $"Roads show below {ShowBelow:0} km";
                }
                return _layer.Status;
            }
        }

        /// <summary>Some roads are on screen -- the cue to show OpenStreetMap's credit.</summary>
        [JsonIgnore]
        public bool Drawing => _layer.Drawing || _detail.Drawing;

        private static readonly HttpClient Http = CreateHttp();

        // Downloads under way, so the road and water layers asking for one tile share a single fetch and file write.
        private static readonly ConcurrentDictionary<(int Zoom, int X, int Y), Task<byte[]>> Fetching = new();

        private readonly RoadLayer _layer;
        private readonly RoadLayer _detail;
        private readonly WaterLayer _water;
        private PlanetRoadProps? _props;
        private ManaSceneNode? _propsGroup;
        private readonly List<(RoadClass Class, Vector3[][] Lines, ManaPosition Origin)> _propRoads = [];
        private double _groundRadius;
        private Vector3 _groundSample;
        private long _nextGroundSample;

        public PlanetRoadsComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Planet Roads";
            _layer = new RoadLayer(this, "Roads", TileZoom);
            _detail = new RoadLayer(this, "Road Detail", DetailZoom);
            _water = new WaterLayer(this);
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            if (Owner is PlanetNode planet)
            {
                var camera = Context.World.MainCamera;
                _layer.Update(planet, camera, Enabled);
                _water.Update(planet, camera, Enabled && Water);
                UpdateCloseRoads(planet);
            }
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            _layer.Remove();
            _detail.Remove();
            _water.Remove();
            if (_propsGroup != null)
            {
                lock (Context.World.SceneLock)
                {
                    _propsGroup.Parent?.RemoveChild(_propsGroup);
                }
                _propsGroup = null;
                _props = null;
            }
        }

        // The kit's pieces along the roads shown, near a low camera.
        private void UpdateCloseRoads(PlanetNode planet)
        {
            if (Context.World.MainCamera is not { } camera || !Matrix4x4.Invert(planet.WorldTransform, out var toLocal))
            {
                return;
            }
            var cameraLocal = Vector3.Transform(camera.Owner.Position, toLocal);
            var metresPerUnit = PlanetLoader.EarthRadiusMeters / planet.Radius;
            var heightMetres = HeightAboveGround(planet, camera.Owner.Position, cameraLocal, metresPerUnit);
            _detail.Update(planet, camera, Enabled, heightMetres / 1000.0f);
            var (longitude, latitude) = Degrees(Vector3.Normalize(cameraLocal));
            var detailReady = Enabled && heightMetres <= Math.Min(DetailBelowKm, ShowBelow) * 1000.0f
                && _detail.IsBuilt(OsmRoadTiles.TileAt(longitude, latitude, DetailZoom));
            _layer.SetDetailCoverage(detailReady);
            _detail.SetDrawEnabled(detailReady);
            if (_props == null && detailReady && Props)
            {
                _propsGroup = new ManaSceneNode(Context, Matrix4x4.Identity) { Name = "Road Props", IncludeInEnvironmentCapture = false };
                var group = _propsGroup;
                var world = Context.World;
                world.PostUpdateFutures.Add(() =>
                {
                    lock (world.SceneLock)
                    {
                        planet.AddChild(group);
                        group.UpdateTransform();
                    }
                });
                _props = new PlanetRoadProps(Context, _propsGroup);
            }
            if (_props == null)
            {
                return;
            }
            _propRoads.Clear();
            if (detailReady && Props)
            {
                foreach (var set in _detail.Built)
                {
                    if (set.RoadOrigin is not { } origin)
                    {
                        continue;
                    }
                    // Ramps and bridge decks have their own widths and no kit props.
                    for (var c = 0; c < RoadKinds; c++)
                    {
                        if (set.Points[c].Length > 0 && _detail.ShowsClass(c, heightMetres / 1000.0f))
                        {
                            _propRoads.Add(((RoadClass)c, set.Points[c], origin));
                        }
                    }
                }
            }
            _props.Update(cameraLocal, heightMetres, metresPerUnit, _propRoads, _detail.Revision, detailReady && Props);
        }

        private float HeightAboveGround(PlanetNode planet, Vector3 eye, Vector3 cameraLocal, float metresPerUnit)
        {
            var distance = cameraLocal.Length();
            var now = Environment.TickCount64;
            if (now >= _nextGroundSample && (distance - planet.Radius) * metresPerUnit < 12_000.0f)
            {
                if (Vector3.DistanceSquared(cameraLocal, _groundSample) * metresPerUnit * metresPerUnit > 250.0f * 250.0f)
                {
                    _groundRadius = planet.Radius;
                }
                if (planet.SurveyBelow(eye)?.GroundRadius is { } ground)
                {
                    _groundRadius = Math.Max(planet.Radius, ground);
                    _groundSample = cameraLocal;
                    _nextGroundSample = now + 250;
                }
                else
                {
                    _nextGroundSample = now + 50;
                }
            }
            return (float)(distance - Math.Max(_groundRadius, planet.Radius)) * metresPerUnit;
        }

        private static HttpClient CreateHttp()
        {
            // The browser unzips replies itself and sends its own agent, refusing ours.
            if (OperatingSystem.IsBrowser())
            {
                return new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            }
            var handler = new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ManaEngine-PlanetRoads/1.0");
            return http;
        }

        // The tiles to fetch, nearest first, and the wider set to keep. None at all above the height roads show from.
        private static (double Longitude, double Latitude) Degrees(Vector3 direction)
        {
            return (Math.Atan2(-direction.X, -direction.Z) * 180.0 / Math.PI,
                Math.Asin(Math.Clamp(direction.Y, -1.0f, 1.0f)) * 180.0 / Math.PI);
        }

        private (List<((int X, int Y) Key, int Level)> Wanted, HashSet<(int X, int Y)> Keep) PlanTiles(
            Vector3 below, float altitudeKm, float showBelow, int zoom = TileZoom)
        {
            var wanted = new List<((int X, int Y) Key, float Distance)>();
            var keep = new HashSet<(int X, int Y)>();
            if (altitudeKm > showBelow * 1.25f)
            {
                return (new List<((int X, int Y) Key, int Level)>(), keep);
            }

            // Low down the horizon is still tens of kilometres off, so never less than 60 km; what is loaded stays
            // while it is within reach of the farthest load, so coming down does not drop the roads in view.
            var loadKm = Math.Clamp(altitudeKm * 1.5f, 60.0f, MathF.Max(60.0f, LoadRadius));
            if (altitudeKm > showBelow)
            {
                loadKm = 0.0f;
            }
            var keepKm = MathF.Max(60.0f, LoadRadius) * 1.2f;
            if (zoom == DetailZoom)
            {
                if (altitudeKm <= showBelow)
                {
                    loadKm = DetailRadiusKm;
                }
                keepKm = DetailRadiusKm * 1.5f;
            }

            var (longitude, latitude) = Degrees(below);
            var spanLatitude = keepKm / 111.0f;
            var spanLongitude = Math.Min(179.0, keepKm / (111.0 * Math.Max(0.05, Math.Cos(latitude * Math.PI / 180.0))));
            var count = 1 << zoom;
            var (westX, northY) = OsmRoadTiles.TileAt(longitude - spanLongitude, Math.Min(85.0, latitude + spanLatitude), zoom);
            var (eastX, southY) = OsmRoadTiles.TileAt(longitude + spanLongitude, Math.Max(-85.0, latitude - spanLatitude), zoom);
            if (eastX < westX)
            {
                eastX += count;
            }

            for (var y = northY; y <= southY; y++)
            {
                for (var rawX = westX; rawX <= eastX; rawX++)
                {
                    var key = (X: rawX % count, Y: y);
                    if (keep.Contains(key))
                    {
                        continue;
                    }
                    var (west, south, east, north) = OsmRoadTiles.Bounds(zoom, key.X, y);
                    var distanceKm = PlanetLineLayer.TileDistanceKm(below, west, south, east, north);
                    if (distanceKm <= keepKm)
                    {
                        keep.Add(key);
                    }
                    if (distanceKm <= loadKm)
                    {
                        wanted.Add((key, distanceKm));
                    }
                }
            }

            wanted.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
            if (wanted.Count > MaxCells)
            {
                wanted.RemoveRange(MaxCells, wanted.Count - MaxCells);
            }
            return (wanted.Select(entry => (entry.Key, 0)).ToList(), keep);
        }

        // A tile's bytes: kept from an earlier run, or fetched (once, however many layers ask) and kept.
        private static async Task<byte[]> ReadTileAsync((int X, int Y) key, CancellationToken cancellation, int zoom = TileZoom)
        {
            string? path = null;
            if (!OperatingSystem.IsBrowser())
            {
                path = Path.Combine(CacheFolder, "Tiles", $"z{zoom}", $"{key.X}_{key.Y}.pbf");
                if (File.Exists(path))
                {
                    return await File.ReadAllBytesAsync(path, cancellation).ConfigureAwait(false);
                }
            }
            var id = (zoom, key.X, key.Y);
            var fetch = Fetching.GetOrAdd(id, _ => FetchTileAsync(key, path, zoom));
            try
            {
                return await fetch.WaitAsync(cancellation).ConfigureAwait(false);
            }
            finally
            {
                if (fetch.IsCompleted)
                {
                    Fetching.TryRemove(new KeyValuePair<(int Zoom, int X, int Y), Task<byte[]>>(id, fetch));
                }
            }
        }

        private static async Task<byte[]> FetchTileAsync((int X, int Y) key, string? path, int zoom)
        {
            var data = await OsmRoadTiles.FetchAsync(Http, zoom, key.X, key.Y, CancellationToken.None).ConfigureAwait(false);
            if (path != null)
            {
                // Written aside and moved in, so a run stopped mid-write never leaves half a tile to be read next time.
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path + ".part", data).ConfigureAwait(false);
                File.Move(path + ".part", path, overwrite: true);
            }
            return data;
        }

        // A tile that does not parse is dropped from the cache, so the next try fetches it afresh.
        private static void DropCached((int X, int Y) key, int zoom = TileZoom)
        {
            if (OperatingSystem.IsBrowser())
            {
                return;
            }
            var path = Path.Combine(CacheFolder, "Tiles", $"z{zoom}", $"{key.X}_{key.Y}.pbf");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private sealed class RoadLayer(PlanetRoadsComponent roads, string name, int zoom) :
            PlanetLineLayer(roads, name, name, RoadClassCount)
        {
            private bool Detailed => zoom == DetailZoom;
            private static readonly bool[] BridgeClasses = Enumerable.Range(0, RoadClassCount).Select(c => c >= BridgeOffset).ToArray();
            private bool _hasDetail;

            public void SetDetailCoverage(bool enabled)
            {
                if (_hasDetail != enabled)
                {
                    _hasDetail = enabled;
                    RefreshMaterials();
                }
            }

            // Read (or fetched and kept) and cut finer.
            protected override (List<((int X, int Y) Key, int Level)> Wanted, HashSet<(int X, int Y)> Keep) Plan(Vector3 below, float altitudeKm)
            {
                var showBelow = roads.ShowBelow;
                if (Detailed)
                {
                    showBelow = Math.Min(DetailBelowKm, showBelow);
                }
                return roads.PlanTiles(below, altitudeKm, showBelow, zoom);
            }

            protected override async Task<PlanetDrapedLineSet?> LoadAsync((int X, int Y) key, int level, CancellationToken cancellation)
            {
                var data = await ReadTileAsync(key, cancellation, zoom).ConfigureAwait(false);
                List<RoadLine> lines;
                try
                {
                    lines = OsmRoadCleanup.Join(OsmRoadTiles.Parse(data, zoom, key.X, key.Y));
                }
                catch (Exception)
                {
                    DropCached(key, zoom);
                    throw;
                }
                var step = DensifyMetres;
                bool[]? bridges = null;
                if (Detailed)
                {
                    step = 12.0f;
                    bridges = BridgeClasses;
                }
                return PlanetDrapedLineSet.From(lines.Select(road => (Style(road), (IReadOnlyList<(double, double)>)road.Points)),
                    RoadClassCount, step / PlanetLoader.EarthRadiusMeters, bridges,
                    PlanetLoader.EarthRadiusMeters / MetresPerUnit);
            }

            private static int Style(RoadLine road)
            {
                var style = (int)road.Class;
                if (road.Ramp)
                {
                    style += RoadKinds;
                }
                if (road.Bridge)
                {
                    style += BridgeOffset;
                }
                return style;
            }

            protected override string ClassName(int lineClass) => $"{(RoadClass)(lineClass % RoadKinds)} roads";

            protected override Vector4 ColorOf(int lineClass)
            {
                switch ((RoadClass)(lineClass % RoadKinds))
                {
                    case RoadClass.Motorway:
                        return roads.MotorwayColor;
                    case RoadClass.Trunk:
                        return roads.TrunkColor;
                    default:
                        return roads.PrimaryColor;
                }
            }

            public bool ShowsClass(int lineClass, float altitudeKm) => Shows(lineClass, altitudeKm);

            protected override bool Shows(int lineClass, float altitudeKm)
            {
                switch ((RoadClass)(lineClass % RoadKinds))
                {
                    case RoadClass.Motorway:
                        return roads.Motorways && altitudeKm <= roads.ShowBelow;
                    case RoadClass.Trunk:
                        return roads.Trunks && altitudeKm <= roads.ShowBelow;
                    default:
                        return roads.Primaries && altitudeKm <= roads.PrimaryBelow;
                }
            }

            protected override ManaProceduralMesh CreateMesh(ManaContext context, PlanetDrapedLineSet geometry, int lineClass)
            {
                return new PlanetRoadMesh(context, geometry.Points[lineClass], MetresPerUnit, geometry.RoadOrigin);
            }

            // A constant width on screen from afar; up close the road's own width, with its lanes. OpenStreetMap draws a
            // motorway as one way per direction, so each is a three-lane carriageway; a primary road is two-way.
            protected override void SetLook(ManaMaterial material, int lineClass)
            {
                var pixels = roads.Width;
                var widthMetres = 11.0f;
                var lanes = 3.0f;
                var roadClass = lineClass % RoadKinds;
                if (roadClass == (int)RoadClass.Trunk)
                {
                    pixels *= 0.85f;
                    widthMetres = 8.0f;
                    lanes = 2.0f;
                }
                else if (roadClass == (int)RoadClass.Primary)
                {
                    pixels *= 0.7f;
                    widthMetres = 7.5f;
                    lanes = 2.0f;
                }
                if (lineClass / RoadKinds % 2 == 1)
                {
                    widthMetres = 4.5f;
                    lanes = 1.0f;
                    pixels *= 0.65f;
                }
                if (!Detailed)
                {
                    widthMetres = 0.0f;
                }
                var colour = ColorOf(lineClass);
                material.SetShaderUniform("widthPerDistance", 0.5f * pixels * MathF.Max(PixelAngle, 1e-5f));
                material.SetShaderUniform("halfWidth", 0.5f * widthMetres / MetresPerUnit);
                material.SetShaderUniform("metresPerUnit", MetresPerUnit);
                material.SetShaderUniform("lanes", lanes);
                material.SetShaderUniform("mapStyle", roads.MapStyle ? 1.0f : 0.0f);
                material.SetShaderUniform("mapColor", new Vector3(colour.X, colour.Y, colour.Z));
                var pull = TowardCamera;
                var coverage = 0.0f;
                if (Detailed)
                {
                    pull = 0.0002f;
                    coverage = DetailDrawMetres;
                }
                else if (_hasDetail)
                {
                    coverage = -DetailDrawMetres;
                }
                material.SetShaderUniform("detailCoverage", coverage);
                material.SetShaderUniform("towardCamera", pull * (1.0f + 0.05f * (RoadKinds - roadClass)));
            }
        }

        private sealed class WaterLayer(PlanetRoadsComponent roads) : PlanetLineLayer(roads, "Water", "Water", 1)
        {
            // No triangle side longer than this (metres), so the water follows the ground's curve and slopes between.
            private const double MaxEdgeMetres = 400.0;

            // A triangle with every corner on the shore reads as all shore (foam, shallows) however wide it is; one wider
            // than this gets a corner in its middle.
            private const double ShoreSplitMetres = 30.0;

            // Shore distances are looked for this far out (metres); water farther from any shore is simply that far.
            private const double ShoreSearchMetres = 3000.0;

            // Under roads and borders: they cross water, never the other way.
            private const float TowardCamera = 0.0028f;

            protected override int MaxLoads => 3;

            protected override (List<((int X, int Y) Key, int Level)> Wanted, HashSet<(int X, int Y)> Keep) Plan(Vector3 below, float altitudeKm)
            {
                return roads.PlanTiles(below, altitudeKm, roads.WaterBelow);
            }

            protected override async Task<PlanetDrapedLineSet?> LoadAsync((int X, int Y) key, int level, CancellationToken cancellation)
            {
                var data = await ReadTileAsync(key, cancellation).ConfigureAwait(false);
                List<WaterArea> areas;
                try
                {
                    areas = OsmRoadTiles.ParseWater(data);
                }
                catch (Exception)
                {
                    DropCached(key);
                    throw;
                }

                var (_, south, _, north) = OsmRoadTiles.Bounds(TileZoom, key.X, key.Y);
                var latitude = (south + north) * 0.5 * Math.PI / 180.0;
                var pieces = new List<(int Class, IReadOnlyList<(double Longitude, double Latitude)> Points)>();
                var triangles = new List<uint[]>();
                var levelFrom = new List<int>();
                var shores = new List<float[]>();
                foreach (var area in areas)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var corners = Earcut.Triangulate(area.Points, area.HoleStarts);
                    if (corners.Count < 3)
                    {
                        continue;
                    }
                    var metresPerUnit = 40_075_016.7 * Math.Cos(latitude) / ((1 << TileZoom) * area.Extent);
                    var (points, finer) = Subdivide(area.Points, corners, MaxEdgeMetres / metresPerUnit);
                    var ringCount = area.Points.Length / 2;
                    SplitShoreTriangles(points, finer, ringCount, ShoreSplitMetres / metresPerUnit);
                    shores.Add(ShoreDistances(area.Points, area.HoleStarts, points, ringCount, metresPerUnit));
                    var degrees = new (double Longitude, double Latitude)[points.Count / 2];
                    for (var i = 0; i < degrees.Length; i++)
                    {
                        degrees[i] = OsmRoadTiles.TilePointToDegrees(TileZoom, key.X, key.Y, points[i * 2], points[i * 2 + 1], area.Extent);
                    }
                    pieces.Add((0, degrees));
                    // Lakes, ponds and reservoirs lie level; a river runs downhill, so it follows the ground.
                    if (area.Class == "river")
                    {
                        levelFrom.Add(-1);
                    }
                    else
                    {
                        levelFrom.Add(area.Points.Length / 2);
                    }
                    triangles.Add(finer.Select(corner => (uint)corner).ToArray());
                }

                // No step: the points are the triangles' corners, and cutting the "lines" between them would break that.
                var set = PlanetDrapedLineSet.From(pieces, ClassCount, float.MaxValue);
                set.Triangles = [triangles.ToArray()];
                set.LevelFrom = [levelFrom.ToArray()];
                set.Shore = [shores.ToArray()];
                return set;
            }

            // Splits every triangle with a side over maxEdge into four, and each neighbour along the sides cut, round
            // after round, so no corner of one sits on the side of another (that would open a crack once laid on the ground).
            private static (List<double> Points, List<int> Triangles) Subdivide(double[] source, List<int> corners, double maxEdge)
            {
                var points = new List<double>(source);
                var triangles = corners;
                var maxSquared = maxEdge * maxEdge;
                for (var round = 0; round < 12 && triangles.Count < 600_000; round++)
                {
                    var middles = new Dictionary<(int, int), int>();
                    for (var t = 0; t < triangles.Count; t += 3)
                    {
                        var a = triangles[t];
                        var b = triangles[t + 1];
                        var c = triangles[t + 2];
                        if (Math.Max(Squared(points, a, b), Math.Max(Squared(points, b, c), Squared(points, c, a))) > maxSquared)
                        {
                            Middle(points, middles, a, b);
                            Middle(points, middles, b, c);
                            Middle(points, middles, c, a);
                        }
                    }
                    if (middles.Count == 0)
                    {
                        break;
                    }

                    var next = new List<int>(triangles.Count * 2);
                    for (var t = 0; t < triangles.Count; t += 3)
                    {
                        var a = triangles[t];
                        var b = triangles[t + 1];
                        var c = triangles[t + 2];
                        var hasAb = middles.TryGetValue(Key(a, b), out var ab);
                        var hasBc = middles.TryGetValue(Key(b, c), out var bc);
                        var hasCa = middles.TryGetValue(Key(c, a), out var ca);
                        if (hasAb && hasBc && hasCa)
                        {
                            next.AddRange([a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca]);
                        }
                        else if (hasAb && hasBc)
                        {
                            next.AddRange([ab, b, bc, a, ab, bc, a, bc, c]);
                        }
                        else if (hasBc && hasCa)
                        {
                            next.AddRange([bc, c, ca, a, b, bc, a, bc, ca]);
                        }
                        else if (hasCa && hasAb)
                        {
                            next.AddRange([a, ab, ca, ab, b, c, ab, c, ca]);
                        }
                        else if (hasAb)
                        {
                            next.AddRange([a, ab, c, ab, b, c]);
                        }
                        else if (hasBc)
                        {
                            next.AddRange([a, b, bc, a, bc, c]);
                        }
                        else if (hasCa)
                        {
                            next.AddRange([a, b, ca, ca, b, c]);
                        }
                        else
                        {
                            next.AddRange([a, b, c]);
                        }
                    }
                    triangles = next;
                }
                return (points, triangles);
            }

            private static void SplitShoreTriangles(List<double> points, List<int> triangles, int ringCount, double minimumSize)
            {
                var count = triangles.Count;
                for (var t = 0; t < count; t += 3)
                {
                    var a = triangles[t];
                    var b = triangles[t + 1];
                    var c = triangles[t + 2];
                    if (a >= ringCount || b >= ringCount || c >= ringCount)
                    {
                        continue;
                    }
                    // Twice the area over the longest side: about how far the middle can be from the shore.
                    var doubled = Math.Abs((points[b * 2] - points[a * 2]) * (points[c * 2 + 1] - points[a * 2 + 1])
                        - (points[b * 2 + 1] - points[a * 2 + 1]) * (points[c * 2] - points[a * 2]));
                    var longest = Math.Sqrt(Math.Max(Squared(points, a, b), Math.Max(Squared(points, b, c), Squared(points, c, a))));
                    if (longest <= 0.0 || doubled / longest < minimumSize)
                    {
                        continue;
                    }
                    var middle = points.Count / 2;
                    points.Add((points[a * 2] + points[b * 2] + points[c * 2]) / 3.0);
                    points.Add((points[a * 2 + 1] + points[b * 2 + 1] + points[c * 2 + 1]) / 3.0);
                    triangles[t + 2] = middle;
                    triangles.AddRange([b, c, middle, c, a, middle]);
                }
            }

            // Each point's distance to the nearest edge of any ring, in metres: shore points 0, the rest searched for
            // through a grid of the edges out to ShoreSearchMetres.
            private static float[] ShoreDistances(double[] rings, int[] holeStarts, List<double> points, int ringCount, double metresPerUnit)
            {
                var distances = new float[points.Count / 2];
                var reach = ShoreSearchMetres / metresPerUnit;
                var cell = reach / 4.0;
                var grid = new Dictionary<(int, int), List<int>>();
                var starts = new List<int> { 0 };
                starts.AddRange(holeStarts);
                starts.Add(ringCount);
                // Edges by their start point; each ring closes back on its first point.
                var edgeEnd = new int[ringCount];
                for (var r = 0; r + 1 < starts.Count; r++)
                {
                    for (var i = starts[r]; i < starts[r + 1]; i++)
                    {
                        var next = i + 1;
                        if (next == starts[r + 1])
                        {
                            next = starts[r];
                        }
                        edgeEnd[i] = next;
                        var x0 = (int)Math.Floor(Math.Min(rings[i * 2], rings[next * 2]) / cell);
                        var x1 = (int)Math.Floor(Math.Max(rings[i * 2], rings[next * 2]) / cell);
                        var y0 = (int)Math.Floor(Math.Min(rings[i * 2 + 1], rings[next * 2 + 1]) / cell);
                        var y1 = (int)Math.Floor(Math.Max(rings[i * 2 + 1], rings[next * 2 + 1]) / cell);
                        for (var gx = x0; gx <= x1; gx++)
                        {
                            for (var gy = y0; gy <= y1; gy++)
                            {
                                if (!grid.TryGetValue((gx, gy), out var list))
                                {
                                    list = new List<int>();
                                    grid[(gx, gy)] = list;
                                }
                                list.Add(i);
                            }
                        }
                    }
                }

                for (var p = ringCount; p < distances.Length; p++)
                {
                    var px = points[p * 2];
                    var py = points[p * 2 + 1];
                    var best = reach * reach;
                    var cx = (int)Math.Floor(px / cell);
                    var cy = (int)Math.Floor(py / cell);
                    for (var gx = cx - 4; gx <= cx + 4; gx++)
                    {
                        for (var gy = cy - 4; gy <= cy + 4; gy++)
                        {
                            if (!grid.TryGetValue((gx, gy), out var list))
                            {
                                continue;
                            }
                            foreach (var i in list)
                            {
                                best = Math.Min(best, SegmentSquared(px, py, rings[i * 2], rings[i * 2 + 1], rings[edgeEnd[i] * 2], rings[edgeEnd[i] * 2 + 1]));
                            }
                        }
                    }
                    distances[p] = (float)(Math.Sqrt(best) * metresPerUnit);
                }
                return distances;
            }

            private static double SegmentSquared(double px, double py, double ax, double ay, double bx, double by)
            {
                var dx = bx - ax;
                var dy = by - ay;
                var length = dx * dx + dy * dy;
                var t = 0.0;
                if (length > 0.0)
                {
                    t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / length, 0.0, 1.0);
                }
                var ex = ax + dx * t - px;
                var ey = ay + dy * t - py;
                return ex * ex + ey * ey;
            }

            private static (int, int) Key(int a, int b)
            {
                if (a < b)
                {
                    return (a, b);
                }
                return (b, a);
            }

            private static void Middle(List<double> points, Dictionary<(int, int), int> middles, int a, int b)
            {
                var key = Key(a, b);
                if (middles.ContainsKey(key))
                {
                    return;
                }
                middles[key] = points.Count / 2;
                points.Add((points[a * 2] + points[b * 2]) * 0.5);
                points.Add((points[a * 2 + 1] + points[b * 2 + 1]) * 0.5);
            }

            private static double Squared(List<double> points, int a, int b)
            {
                var dx = points[a * 2] - points[b * 2];
                var dy = points[a * 2 + 1] - points[b * 2 + 1];
                return dx * dx + dy * dy;
            }

            protected override string ClassName(int lineClass) => "Water";

            protected override ManaProceduralMesh CreateMesh(ManaContext context, PlanetDrapedLineSet geometry, int lineClass)
            {
                return new PlanetWaterMesh(context, geometry.Points[lineClass], geometry.Triangles![lineClass], geometry.Shore![lineClass], MetresPerUnit);
            }

            protected override Vector4 ColorOf(int lineClass) => Vector4.One;

            protected override bool Shows(int lineClass, float altitudeKm)
            {
                return roads.Water && altitudeKm <= roads.WaterBelow;
            }

            protected override void SetLook(ManaMaterial material, int lineClass)
            {
                material.SetShaderUniform("towardCamera", TowardCamera);
            }
        }
    }
}
