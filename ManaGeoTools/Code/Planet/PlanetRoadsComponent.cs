using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Mana;
using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
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
        private const int RoadClassCount = 3;

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

        /// <summary>Motorway width on screen in pixels; trunk and primary roads are drawn a little thinner.</summary>
        public float Width { get; set; } = 3.0f;

        public Vector4 MotorwayColor { get; set; } = new(1.0f, 0.55f, 0.15f, 1.0f);

        public Vector4 TrunkColor { get; set; } = new(0.95f, 0.3f, 0.25f, 1.0f);

        public Vector4 PrimaryColor { get; set; } = new(1.0f, 0.85f, 0.35f, 1.0f);

        /// <summary>Lakes, rivers and ponds from the same tiles, laid on the ground.</summary>
        public bool Water { get; set; } = true;

        /// <summary>Height in km below which the water shows.</summary>
        public float WaterBelow { get; set; } = 150.0f;

        /// <summary>About the blue of the satellite picture's sea, so lakes and sea read alike.</summary>
        public Vector4 WaterColor { get; set; } = new(0.11f, 0.27f, 0.52f, 1.0f);

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
        public bool Drawing => _layer.Drawing;

        private static readonly HttpClient Http = CreateHttp();

        // Downloads under way, so the road and water layers asking for one tile share a single fetch and file write.
        private static readonly ConcurrentDictionary<(int X, int Y), Task<byte[]>> Fetching = new();

        private readonly RoadLayer _layer;
        private readonly WaterLayer _water;

        public PlanetRoadsComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Planet Roads";
            _layer = new RoadLayer(this);
            _water = new WaterLayer(this);
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            if (Owner is PlanetNode planet)
            {
                _layer.Update(planet, Context.World.MainCamera, Enabled);
                _water.Update(planet, Context.World.MainCamera, Enabled && Water);
            }
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            _layer.Remove();
            _water.Remove();
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
    private (List<((int X, int Y) Key, int Level)> Wanted, HashSet<(int X, int Y)> Keep) PlanTiles(Vector3 below, float altitudeKm, float showBelow)
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
                    var key = (X: rawX % count, Y: y);
                    if (keep.Contains(key))
                    {
                        continue;
                    }
                    var (west, south, east, north) = OsmRoadTiles.Bounds(TileZoom, key.X, y);
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
        private static async Task<byte[]> ReadTileAsync((int X, int Y) key, CancellationToken cancellation)
        {
            string? path = null;
            if (!OperatingSystem.IsBrowser())
            {
                path = Path.Combine(CacheFolder, "Tiles", $"z{TileZoom}", $"{key.X}_{key.Y}.pbf");
                if (File.Exists(path))
                {
                    return await File.ReadAllBytesAsync(path, cancellation).ConfigureAwait(false);
                }
            }
            var fetch = Fetching.GetOrAdd(key, _ => FetchTileAsync(key, path));
            try
            {
                return await fetch.WaitAsync(cancellation).ConfigureAwait(false);
            }
            finally
            {
                if (fetch.IsCompleted)
                {
                    Fetching.TryRemove(new KeyValuePair<(int X, int Y), Task<byte[]>>(key, fetch));
                }
            }
        }

        private static async Task<byte[]> FetchTileAsync((int X, int Y) key, string? path)
        {
            var data = await OsmRoadTiles.FetchAsync(Http, TileZoom, key.X, key.Y, CancellationToken.None).ConfigureAwait(false);
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
        private static void DropCached((int X, int Y) key)
        {
            if (OperatingSystem.IsBrowser())
            {
                return;
            }
            var path = Path.Combine(CacheFolder, "Tiles", $"z{TileZoom}", $"{key.X}_{key.Y}.pbf");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private sealed class RoadLayer(PlanetRoadsComponent roads) : PlanetLineLayer(roads, "Roads", "Roads", RoadClassCount)
        {
            // Read (or fetched and kept) and cut finer.
            protected override (List<((int X, int Y) Key, int Level)> Wanted, HashSet<(int X, int Y)> Keep) Plan(Vector3 below, float altitudeKm)
            {
                return roads.PlanTiles(below, altitudeKm, roads.ShowBelow);
            }

            protected override async Task<PlanetDrapedLineSet?> LoadAsync((int X, int Y) key, int level, CancellationToken cancellation)
            {
                var data = await ReadTileAsync(key, cancellation).ConfigureAwait(false);
                List<RoadLine> lines;
                try
                {
                    lines = OsmRoadTiles.Parse(data, TileZoom, key.X, key.Y);
                }
                catch (Exception)
                {
                    DropCached(key);
                    throw;
                }
                var stepAngle = DensifyMetres / PlanetLoader.EarthRadiusMeters;
                return PlanetDrapedLineSet.From(lines.Select(road => ((int)road.Class, (IReadOnlyList<(double, double)>)road.Points)),
                    RoadClassCount, stepAngle);
            }

            protected override string ClassName(int lineClass) => $"{(RoadClass)lineClass} roads";

            protected override Vector4 ColorOf(int lineClass)
            {
                switch ((RoadClass)lineClass)
                {
                    case RoadClass.Motorway:
                        return roads.MotorwayColor;
                    case RoadClass.Trunk:
                        return roads.TrunkColor;
                    default:
                        return roads.PrimaryColor;
                }
            }

            protected override bool Shows(int lineClass, float altitudeKm)
            {
                switch ((RoadClass)lineClass)
                {
                    case RoadClass.Motorway:
                        return roads.Motorways && altitudeKm <= roads.ShowBelow;
                    case RoadClass.Trunk:
                        return roads.Trunks && altitudeKm <= roads.ShowBelow;
                    default:
                        return roads.Primaries && altitudeKm <= roads.PrimaryBelow;
                }
            }

            // Constant width on screen from afar; up close no narrower than the road itself.
            protected override void SetLook(ManaMaterial material, int lineClass)
            {
                var pixels = roads.Width;
                var groundMetres = 12.0f;
                if (lineClass == (int)RoadClass.Trunk)
                {
                    pixels *= 0.85f;
                    groundMetres = 10.0f;
                }
                else if (lineClass == (int)RoadClass.Primary)
                {
                    pixels *= 0.7f;
                    groundMetres = 8.0f;
                }
                material.SetShaderUniform("widthPerDistance", 0.5f * pixels * MathF.Max(PixelAngle, 1e-5f));
                material.SetShaderUniform("minHalfWidth", 0.5f * groundMetres / MetresPerUnit);
                material.SetShaderUniform("maxHalfWidth", 1.0e7f);
                // The bigger road a hair nearer, so where two classes share a line one wins rather than both flickering.
                material.SetShaderUniform("towardCamera", TowardCamera * (1.0f + 0.05f * (RoadClassCount - lineClass)));
                material.SetShaderUniform("faceCamera", 1.0f);
            }
        }

        private sealed class WaterLayer(PlanetRoadsComponent roads) : PlanetLineLayer(roads, "Water", "Water", 1)
        {
            // No triangle side longer than this (metres), so the water follows the ground's curve and slopes between.
            private const double MaxEdgeMetres = 400.0;

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
                return new PlanetWaterMesh(context, geometry.Points[lineClass], geometry.Triangles![lineClass], roads.WaterColor);
            }

            protected override Vector4 ColorOf(int lineClass) => roads.WaterColor;

            protected override bool Shows(int lineClass, float altitudeKm)
            {
                return roads.Water && altitudeKm <= roads.WaterBelow;
            }

            protected override void SetLook(ManaMaterial material, int lineClass)
            {
                material.SetShaderUniform("widthPerDistance", 0.0f);
                material.SetShaderUniform("minHalfWidth", 0.0f);
                material.SetShaderUniform("maxHalfWidth", 0.0f);
                material.SetShaderUniform("towardCamera", TowardCamera);
                material.SetShaderUniform("faceCamera", 0.0f);
            }
        }
    }
}
