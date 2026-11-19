using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Mana;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaGeoTools.Code.Osm;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// OpenStreetMap's larger roads (motorway, trunk, primary) drawn on a planet near the camera. Once the camera is
    /// below <see cref="ShowBelow"/> km the zoom-9 vector tiles around it (~50-80 km across) are fetched from
    /// OpenFreeMap (kept in Library/PlanetRoads on desktop), drawn as lines on the terrain and re-laid on it as the
    /// patches below refine, and let go again when the camera leaves. Primary roads show only lower down.
    /// </summary>
    public sealed class PlanetRoadsComponent : ManaComponent
    {
        public const string ScriptType = "PlanetRoads";

        /// <summary>Where fetched tiles are kept between runs (desktop only; the browser fetches again).</summary>
        public static string CacheFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "Library", "PlanetRoads");

        // Zoom 9 carries every primary road, its points to ~15 m, in tiles of ~150 KB around a city.
        private const int TileZoom = 9;

        // A CDN: a few at once is fine.
        private const int MaxFetches = 6;
        private const int MaxCells = 48;
        private const int BuildsPerFrame = 2;
        private const double UpdateSeconds = 0.25;
        private const double FailedRetrySeconds = 20.0;

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

        /// <summary>Motorway width on screen in pixels; trunk and primary roads are drawn a little thinner.</summary>
        public float Width { get; set; } = 3.0f;

        public Vector4 MotorwayColor { get; set; } = new(1.0f, 0.55f, 0.15f, 1.0f);

        public Vector4 TrunkColor { get; set; } = new(0.95f, 0.3f, 0.25f, 1.0f);

        public Vector4 PrimaryColor { get; set; } = new(1.0f, 0.85f, 0.35f, 1.0f);

        [JsonIgnore]
        public string Status { get; private set; } = "Waiting for the camera to come down";

        /// <summary>Some roads are on screen -- the cue to show OpenStreetMap's credit.</summary>
        [JsonIgnore]
        public bool Drawing { get; private set; }

        private static readonly HttpClient Http = CreateHttp();
        private readonly Dictionary<(int X, int Y), RoadCell> _cells = new();
        private readonly CancellationTokenSource _cancel = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastUpdate = double.NegativeInfinity;
        private ManaSceneNode? _group;
        private Task<DrapeResult>? _drapeWork;
        private float _pixelAngle;
        private string _lastStatus = "";

        public PlanetRoadsComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Planet Roads";
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            if (Owner is not PlanetNode planet)
            {
                return;
            }

            AdoptFinished(planet);

            var now = _clock.Elapsed.TotalSeconds;
            if (now - _lastUpdate < UpdateSeconds)
            {
                return;
            }
            _lastUpdate = now;

            if (!Enabled || Context.World.MainCamera is not { } camera || !Matrix4x4.Invert(planet.WorldTransform, out var worldToLocal))
            {
                UnloadAll();
                return;
            }

            var cameraLocal = Vector3.Transform(camera.Owner.Position, worldToLocal);
            var metresPerUnit = PlanetLoader.EarthRadiusMeters / planet.Radius;
            var altitudeKm = (cameraLocal.Length() - planet.Radius) * metresPerUnit / 1000.0f;
            var below = Vector3.Normalize(cameraLocal);

            var (wanted, keep) = WantedCells(below, altitudeKm);
            var dropped = 0;
            foreach (var (key, cell) in _cells.ToList())
            {
                if (!keep.Contains(key))
                {
                    if (cell.Geometry != null)
                    {
                        dropped++;
                    }
                    Unload(cell);
                    _cells.Remove(key);
                }
            }
            if (dropped > 0)
            {
                Console.WriteLine($"[Roads] Let go of {dropped} tile(s) at {altitudeKm:0.#} km; {_cells.Count} kept.");
            }

            var leaves = planet.TryGetLeafMeshes();
            if (leaves != null)
            {
                StartFetches(wanted, leaves, planet.Radius, metresPerUnit, now);
                StartDrape(leaves, planet.Radius);
            }

            ApplyLook(camera, altitudeKm);
            ReportStatus(altitudeKm);
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            _cancel.Cancel();
            UnloadAll();
            if (_group != null)
            {
                lock (Context.World.SceneLock)
                {
                    _group.Parent?.RemoveChild(_group);
                }
                _group = null;
            }
        }

        // The squares to fetch, nearest first, and the (wider) set to keep, so a square at the edge does not come and
        // go as the camera wobbles. None at all above the height roads show from: they are all let go.
        private (List<(int X, int Y)> Wanted, HashSet<(int X, int Y)> Keep) WantedCells(Vector3 below, float altitudeKm)
        {
            var wanted = new List<((int X, int Y) Key, float Distance)>();
            var keep = new HashSet<(int X, int Y)>();
            if (altitudeKm > ShowBelow * 1.25f)
            {
                return (new List<(int, int)>(), keep);
            }

            // Low down the horizon is still tens of kilometres off, so never less than 60 km; what is loaded stays
            // while it is within reach of the farthest load, so coming down does not drop the roads in view.
            var loadKm = Math.Clamp(altitudeKm * 1.5f, 60.0f, MathF.Max(60.0f, LoadRadius));
            if (altitudeKm > ShowBelow)
            {
                loadKm = 0.0f;
            }
            var keepKm = MathF.Max(60.0f, LoadRadius) * 1.2f;

            var latitude = MathF.Asin(Math.Clamp(below.Y, -1.0f, 1.0f)) * 180.0f / MathF.PI;
            var longitude = MathF.Atan2(-below.X, -below.Z) * 180.0f / MathF.PI;
            var spanLatitude = keepKm / 111.0f;
            var spanLongitude = MathF.Min(179.0f, keepKm / (111.0f * MathF.Max(0.05f, MathF.Cos(latitude * MathF.PI / 180.0f))));
            var count = 1 << TileZoom;
            var (westX, northY) = OsmRoadTiles.TileAt(longitude - spanLongitude, Math.Min(85.0, latitude + spanLatitude), TileZoom);
            var (eastX, southY) = OsmRoadTiles.TileAt(longitude + spanLongitude, Math.Max(-85.0, latitude - spanLatitude), TileZoom);
            if (eastX < westX)
            {
                eastX += count;
            }

            for (var y = northY; y <= southY; y++)
            {
                for (var rawX = westX; rawX <= eastX; rawX++)
                {
                    var x = rawX % count;
                    var key = (x, y);
                    if (keep.Contains(key))
                    {
                        continue;
                    }
                    var (west, south, east, north) = OsmRoadTiles.Bounds(TileZoom, x, y);
                    var middleLatitude = (south + north) * 0.5;
                    var centre = LatLong.FromLongitudeAndLatitude((float)((west + east) * 0.5), (float)middleLatitude).ToUnitSphere();
                    // Half the tile's diagonal on the ground, so a tile counts from its nearest edge rather than its middle.
                    var widthKm = (east - west) * 111.0 * Math.Cos(middleLatitude * Math.PI / 180.0);
                    var heightKm = (north - south) * 111.0;
                    var halfDiagonalKm = (float)(0.5 * Math.Sqrt(widthKm * widthKm + heightKm * heightKm));
                    var angle = MathF.Acos(Math.Clamp(Vector3.Dot(centre, below), -1.0f, 1.0f));
                    var distanceKm = MathF.Max(0.0f, angle * PlanetLoader.EarthRadiusMeters / 1000.0f - halfDiagonalKm);
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
            return (wanted.Select(entry => entry.Key).ToList(), keep);
        }

        private void StartFetches(List<(int X, int Y)> wanted, PlanetPatchMesh[] leaves, float seaRadius, float metresPerUnit, double now)
        {
            var running = _cells.Values.Count(cell => cell.Work != null);
            foreach (var key in wanted)
            {
                if (!_cells.TryGetValue(key, out var cell))
                {
                    cell = new RoadCell(key);
                    _cells[key] = cell;
                }
                if (running >= MaxFetches)
                {
                    continue;
                }
                if (cell.Work != null || cell.Geometry != null || now < cell.RetryAt)
                {
                    continue;
                }

                cell.Cancel = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
                var token = cell.Cancel.Token;
                cell.Work = Task.Run(() => LoadAsync(cell.Key, leaves, seaRadius, metresPerUnit, token), token);
                running++;
            }
        }

        private void AdoptFinished(PlanetNode planet)
        {
            var built = 0;
            foreach (var cell in _cells.Values)
            {
                if (cell.Work is not { IsCompleted: true } work)
                {
                    continue;
                }
                if (built >= BuildsPerFrame && work.IsCompletedSuccessfully && work.Result != null)
                {
                    continue;
                }

                cell.Work = null;
                cell.Cancel?.Dispose();
                cell.Cancel = null;
                if (work.IsCompletedSuccessfully && work.Result is { } geometry)
                {
                    cell.Geometry = geometry;
                    Build(planet, cell);
                    built++;
                }
                else if (!work.IsCanceled)
                {
                    var reason = work.Exception?.InnerException?.Message ?? "no reply";
                    Console.WriteLine($"[Roads] Tile {cell.Key} failed: {reason}; again in {FailedRetrySeconds:0} s.");
                    cell.RetryAt = _clock.Elapsed.TotalSeconds + FailedRetrySeconds;
                }
            }

            if (_drapeWork is { IsCompleted: true } drape)
            {
                _drapeWork = null;
                if (drape.IsCompletedSuccessfully)
                {
                    ApplyDrape(drape.Result);
                }
            }
        }

        // Read (or fetched and kept), cut finer and laid on the patches drawn when the fetch began; laid again once the
        // ground below has changed (StartDrape).
        private static async Task<CellGeometry?> LoadAsync((int X, int Y) key, PlanetPatchMesh[] leaves, float seaRadius,
            float metresPerUnit, CancellationToken cancellation)
        {
            byte[]? data = null;
            string? path = null;
            if (!OperatingSystem.IsBrowser())
            {
                path = Path.Combine(CacheFolder, "Tiles", $"z{TileZoom}", $"{key.X}_{key.Y}.pbf");
                if (File.Exists(path))
                {
                    data = await File.ReadAllBytesAsync(path, cancellation).ConfigureAwait(false);
                }
            }

            if (data == null)
            {
                data = await OsmRoadTiles.FetchAsync(Http, TileZoom, key.X, key.Y, cancellation).ConfigureAwait(false);
                if (path != null)
                {
                    // Written aside and moved in, so a run stopped mid-write never leaves half a tile to be read next time.
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllBytesAsync(path + ".part", data, cancellation).ConfigureAwait(false);
                    File.Move(path + ".part", path, overwrite: true);
                }
            }

            List<RoadLine> roads;
            try
            {
                roads = OsmRoadTiles.Parse(data, TileZoom, key.X, key.Y);
            }
            catch (Exception) when (path != null && File.Exists(path))
            {
                File.Delete(path);
                throw;
            }
            var geometry = CellGeometry.From(roads, DensifyMetres / metresPerUnit / seaRadius);
            geometry.Drape(leaves, seaRadius, geometry.Points);
            return geometry;
        }

        // One set of lines per class, each a node of its own under the planet's Roads group.
        private void Build(PlanetNode planet, RoadCell cell)
        {
            if (cell.Geometry is not { } geometry)
            {
                return;
            }

            lock (Context.World.SceneLock)
            {
                if (_group == null)
                {
                    _group = new ManaSceneNode(Context, Matrix4x4.Identity)
                    {
                        Name = "Roads",
                        IncludeInEnvironmentCapture = false
                    };
                    planet.AddChild(_group);
                    _group.UpdateTransform();
                }

                for (var c = 0; c < CellGeometry.ClassCount; c++)
                {
                    var lines = geometry.Points[c];
                    if (lines.Length == 0)
                    {
                        continue;
                    }
                    var polylines = new List<ManaLinePolyline>(lines.Length);
                    foreach (var points in lines)
                    {
                        polylines.Add(new ManaLinePolyline(points, closed: false));
                    }
                    var roadClass = (RoadClass)c;
                    var mesh = new ManaLineMesh(Context, polylines, 1.0f, ColorOf(roadClass), 0.0f, ManaLineProfile.ScreenRibbon)
                    {
                        Name = $"{roadClass} roads {cell.Key.X},{cell.Key.Y}"
                    };
                    var node = new ManaSceneNode(Context, Matrix4x4.Identity)
                    {
                        Name = mesh.Name,
                        IncludeInEnvironmentCapture = false
                    };
                    var renderer = new MeshRendererComponent(node, mesh);
                    node.Components.Add(renderer);
                    _group.AddChild(node);
                    node.UpdateTransform();
                    cell.Meshes[c] = mesh;
                    cell.Renderers[c] = renderer;
                    cell.Nodes[c] = node;
                    SetWidth(renderer, roadClass);
                }
            }

            Console.WriteLine($"[Roads] Tile {cell.Key}: {geometry.Points[0].Length} motorway, {geometry.Points[1].Length} trunk, " +
                $"{geometry.Points[2].Length} primary lines, {geometry.PointCount} points.");
        }

        // Re-lays the squares whose ground changed since they were laid -- a patch below split, merged or sharpened -- on a
        // worker, one batch at a time.
        private void StartDrape(PlanetPatchMesh[] leaves, float seaRadius)
        {
            if (_drapeWork != null)
            {
                return;
            }

            var batch = new List<(RoadCell Cell, int Signature)>();
            foreach (var cell in _cells.Values)
            {
                if (cell.Geometry is not { } geometry || cell.Nodes.All(node => node == null))
                {
                    continue;
                }
                var signature = geometry.Signature(leaves);
                if (signature != cell.LeafSignature)
                {
                    batch.Add((cell, signature));
                }
            }
            if (batch.Count == 0)
            {
                return;
            }

            _drapeWork = Task.Run(() =>
            {
                var laid = new List<(RoadCell Cell, int Signature, Vector3[][][] Points)>(batch.Count);
                foreach (var (cell, signature) in batch)
                {
                    var geometry = cell.Geometry!;
                    var points = geometry.EmptyCopy();
                    geometry.Drape(leaves, seaRadius, points);
                    laid.Add((cell, signature, points));
                }
                return new DrapeResult(laid);
            });
        }

        private void ApplyDrape(DrapeResult result)
        {
            foreach (var (cell, signature, points) in result.Cells)
            {
                if (cell.Geometry is not { } geometry || !_cells.TryGetValue(cell.Key, out var current) || current != cell)
                {
                    continue;
                }
                for (var c = 0; c < CellGeometry.ClassCount; c++)
                {
                    var lines = geometry.Points[c];
                    for (var i = 0; i < lines.Length; i++)
                    {
                        Array.Copy(points[c][i], lines[i], lines[i].Length);
                    }
                    if (cell.Meshes[c] is { } mesh && !mesh.TryRefreshRibbonGeometry())
                    {
                        mesh.RebuildGeometry();
                    }
                }
                cell.LeafSignature = signature;
            }
        }

        private void ApplyLook(CameraComponent camera, float altitudeKm)
        {
            var screenHeight = 1080.0f;
            if (Context.Window is { } window && window.Size.Y > 0)
            {
                screenHeight = window.Size.Y;
            }
            var pixelAngle = 2.0f * MathF.Tan(camera.FieldOfViewDegrees * MathF.PI / 360.0f) / screenHeight;
            var resize = MathF.Abs(pixelAngle - _pixelAngle) > _pixelAngle * 0.02f;
            _pixelAngle = pixelAngle;

            foreach (var cell in _cells.Values)
            {
                for (var c = 0; c < CellGeometry.ClassCount; c++)
                {
                    if (cell.Renderers[c] is not { } renderer)
                    {
                        continue;
                    }
                    var roadClass = (RoadClass)c;
                    renderer.IsVisible = Shows(roadClass, altitudeKm);
                    if (resize)
                    {
                        SetWidth(renderer, roadClass);
                    }
                }
            }
        }

        private bool Shows(RoadClass roadClass, float altitudeKm)
        {
            switch (roadClass)
            {
                case RoadClass.Motorway:
                    return Motorways && altitudeKm <= ShowBelow;
                case RoadClass.Trunk:
                    return Trunks && altitudeKm <= ShowBelow;
                default:
                    return Primaries && altitudeKm <= PrimaryBelow;
            }
        }

        // Constant width on screen from afar; up close no narrower than the road itself.
        private void SetWidth(MeshRendererComponent renderer, RoadClass roadClass)
        {
            var pixels = Width;
            var groundMetres = 12.0f;
            if (roadClass == RoadClass.Trunk)
            {
                pixels *= 0.85f;
                groundMetres = 10.0f;
            }
            else if (roadClass == RoadClass.Primary)
            {
                pixels *= 0.7f;
                groundMetres = 8.0f;
            }
            var metresPerUnit = 1.0f;
            if (Owner is PlanetNode planet)
            {
                metresPerUnit = PlanetLoader.EarthRadiusMeters / planet.Radius;
            }
            var material = renderer.Material;
            material.SetShaderUniform("widthPerDistance", 0.5f * pixels * MathF.Max(_pixelAngle, 1e-5f));
            material.SetShaderUniform("minHalfWidth", 0.5f * groundMetres / metresPerUnit);
            material.SetShaderUniform("maxHalfWidth", 1.0e7f);
            // The bigger road a hair nearer, so where two classes share a line one wins rather than both flickering.
            material.SetShaderUniform("towardCamera", TowardCamera * (1.0f + 0.05f * (CellGeometry.ClassCount - (int)roadClass)));
            material.SetShaderUniform("faceCamera", 1.0f);
        }

        private Vector4 ColorOf(RoadClass roadClass)
        {
            switch (roadClass)
            {
                case RoadClass.Motorway:
                    return MotorwayColor;
                case RoadClass.Trunk:
                    return TrunkColor;
                default:
                    return PrimaryColor;
            }
        }

        private void ReportStatus(float altitudeKm)
        {
            var ready = _cells.Values.Count(cell => cell.Geometry != null);
            Drawing = _cells.Values.Any(cell => cell.Renderers.Any(renderer => renderer is { IsVisible: true }));
            var loading = _cells.Values.Count(cell => cell.Work != null);
            if (_cells.Count == 0)
            {
                Status = $"Roads show below {ShowBelow:0} km";
            }
            else
            {
                Status = $"{ready} tiles drawn, {loading} loading";
            }
            if (Status != _lastStatus && loading == 0 && _cells.Count > 0)
            {
                Console.WriteLine($"[Roads] {Status} at {altitudeKm:0.#} km.");
            }
            _lastStatus = Status;
        }

        private void UnloadAll()
        {
            Drawing = false;
            foreach (var cell in _cells.Values)
            {
                Unload(cell);
            }
            _cells.Clear();
        }

        private void Unload(RoadCell cell)
        {
            cell.Cancel?.Cancel();
            cell.Cancel = null;
            cell.Work = null;
            lock (Context.World.SceneLock)
            {
                for (var c = 0; c < CellGeometry.ClassCount; c++)
                {
                    if (cell.Nodes[c] is { } node)
                    {
                        node.Parent?.RemoveChild(node);
                    }
                    cell.Nodes[c] = null;
                    cell.Renderers[c] = null;
                    cell.Meshes[c] = null;
                }
            }
            cell.Geometry = null;
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

        private sealed class RoadCell((int X, int Y) key)
        {
            public (int X, int Y) Key { get; } = key;
            public Task<CellGeometry?>? Work { get; set; }
            public CancellationTokenSource? Cancel { get; set; }
            public CellGeometry? Geometry { get; set; }
            public double RetryAt { get; set; } = double.NegativeInfinity;
            public int LeafSignature { get; set; }
            public ManaSceneNode?[] Nodes { get; } = new ManaSceneNode?[CellGeometry.ClassCount];
            public MeshRendererComponent?[] Renderers { get; } = new MeshRendererComponent?[CellGeometry.ClassCount];
            public ManaLineMesh?[] Meshes { get; } = new ManaLineMesh?[CellGeometry.ClassCount];
        }

        private sealed record DrapeResult(List<(RoadCell Cell, int Signature, Vector3[][][] Points)> Cells);

        /// <summary>
        /// A square's roads per class: each line's points on the ground (the arrays its line mesh reads) and, fixed for
        /// good, each point's direction from the planet's centre and its cube-face (u, v), so laying them again is a
        /// height lookup.
        /// </summary>
        private sealed class CellGeometry
        {
            public const int ClassCount = 3;

            public Vector3[][][] Points { get; } = new Vector3[ClassCount][][];
            private readonly Vector3[][][] _directions = new Vector3[ClassCount][][];
            private readonly (int Face, float U, float V)[][][] _faceUvs = new (int, float, float)[ClassCount][][];
            private readonly Dictionary<int, (float MinU, float MaxU, float MinV, float MaxV)> _boxes = new();

            public int PointCount { get; private set; }

            /// <param name="stepAngle">The longest gap between points, as an angle at the planet's centre.</param>
            public static CellGeometry From(List<RoadLine> roads, float stepAngle)
            {
                var geometry = new CellGeometry();
                var directions = new List<Vector3[]>[ClassCount];
                for (var c = 0; c < ClassCount; c++)
                {
                    directions[c] = new List<Vector3[]>();
                }

                var dense = new List<Vector3>();
                foreach (var road in roads)
                {
                    dense.Clear();
                    var previous = Vector3.Zero;
                    foreach (var (longitude, latitude) in road.Points)
                    {
                        var direction = LatLong.FromLongitudeAndLatitude((float)longitude, (float)latitude).ToUnitSphere();
                        if (dense.Count > 0)
                        {
                            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(previous, direction), -1.0f, 1.0f));
                            var steps = Math.Min(256, (int)MathF.Ceiling(angle / MathF.Max(stepAngle, 1e-9f)));
                            for (var k = 1; k < steps; k++)
                            {
                                dense.Add(Vector3.Normalize(Vector3.Lerp(previous, direction, (float)k / steps)));
                            }
                        }
                        dense.Add(direction);
                        previous = direction;
                    }
                    directions[(int)road.Class].Add(dense.ToArray());
                }

                for (var c = 0; c < ClassCount; c++)
                {
                    var lines = directions[c].ToArray();
                    geometry._directions[c] = lines;
                    geometry.Points[c] = new Vector3[lines.Length][];
                    geometry._faceUvs[c] = new (int, float, float)[lines.Length][];
                    for (var i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        geometry.Points[c][i] = new Vector3[line.Length];
                        var uvs = new (int Face, float U, float V)[line.Length];
                        for (var p = 0; p < line.Length; p++)
                        {
                            if (CubeSphere.TryUnwarpSphereToFaceUv(line[p], out var face, out var u, out var v))
                            {
                                uvs[p] = (face, u, v);
                                geometry.Grow(face, u, v);
                            }
                            else
                            {
                                uvs[p] = (-1, 0.0f, 0.0f);
                            }
                        }
                        geometry._faceUvs[c][i] = uvs;
                        geometry.PointCount += line.Length;
                    }
                }
                return geometry;
            }

            private void Grow(int face, float u, float v)
            {
                if (_boxes.TryGetValue(face, out var box))
                {
                    _boxes[face] = (MathF.Min(box.MinU, u), MathF.Max(box.MaxU, u), MathF.Min(box.MinV, v), MathF.Max(box.MaxV, v));
                }
                else
                {
                    _boxes[face] = (u, u, v, v);
                }
            }

            public Vector3[][][] EmptyCopy()
            {
                var copy = new Vector3[ClassCount][][];
                for (var c = 0; c < ClassCount; c++)
                {
                    copy[c] = new Vector3[Points[c].Length][];
                    for (var i = 0; i < Points[c].Length; i++)
                    {
                        copy[c][i] = new Vector3[Points[c][i].Length];
                    }
                }
                return copy;
            }

            // The patches drawn under this square.
            private List<(int Face, PlanetPatchMesh Mesh)> Under(PlanetPatchMesh[] leaves)
            {
                var under = new List<(int, PlanetPatchMesh)>();
                foreach (var mesh in leaves)
                {
                    foreach (var (face, box) in _boxes)
                    {
                        if (mesh.FaceNormal == CubeSphere.FaceNormals[face]
                            && mesh.MinU <= box.MaxU && mesh.MaxU >= box.MinU && mesh.MinV <= box.MaxV && mesh.MaxV >= box.MinV)
                        {
                            under.Add((face, mesh));
                        }
                    }
                }
                return under;
            }

            /// <summary>Changes whenever a patch under the square is replaced or its ground rebuilt.</summary>
            public int Signature(PlanetPatchMesh[] leaves)
            {
                var signature = 17;
                foreach (var (_, mesh) in Under(leaves))
                {
                    var cpu = 0;
                    if (mesh.CpuData != null)
                    {
                        cpu = RuntimeHelpers.GetHashCode(mesh.CpuData);
                    }
                    signature ^= (RuntimeHelpers.GetHashCode(mesh) * 31) + cpu;
                }
                return signature;
            }

            /// <summary>Each point at the drawn ground's height below it (sea level where no patch is found), into <paramref name="into"/>.</summary>
            public void Drape(PlanetPatchMesh[] leaves, float seaRadius, Vector3[][][] into)
            {
                var under = Under(leaves);
                for (var c = 0; c < ClassCount; c++)
                {
                    for (var i = 0; i < _directions[c].Length; i++)
                    {
                        var directions = _directions[c][i];
                        var uvs = _faceUvs[c][i];
                        var points = into[c][i];
                        for (var p = 0; p < directions.Length; p++)
                        {
                            var radius = seaRadius;
                            var (face, u, v) = uvs[p];
                            foreach (var (meshFace, mesh) in under)
                            {
                                if (meshFace != face || u < mesh.MinU || u > mesh.MaxU || v < mesh.MinV || v > mesh.MaxV)
                                {
                                    continue;
                                }
                                if (PlanetNode.GroundRadius(mesh, u, v) is { } ground)
                                {
                                    radius = ground;
                                }
                                break;
                            }
                            points[p] = directions[p] * radius;
                        }
                    }
                }
            }
        }
    }
}
