using System.Text.Json.Serialization;
using Mana;
using Mana.Code.Materials;
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
        private readonly RoadLayer _layer;

        public PlanetRoadsComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Planet Roads";
            _layer = new RoadLayer(this);
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            if (Owner is PlanetNode planet)
            {
                _layer.Update(planet, Context.World.MainCamera, Enabled);
            }
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            _layer.Remove();
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

        private sealed class RoadLayer(PlanetRoadsComponent roads) : PlanetLineLayer(roads, "Roads", "Roads", RoadClassCount)
        {
            // The tiles to fetch, nearest first, and the wider set to keep. None at all above the height roads show from.
            protected override (List<((int X, int Y) Key, int Level)> Wanted, HashSet<(int X, int Y)> Keep) Plan(Vector3 below, float altitudeKm)
            {
                var wanted = new List<((int X, int Y) Key, float Distance)>();
                var keep = new HashSet<(int X, int Y)>();
                if (altitudeKm > roads.ShowBelow * 1.25f)
                {
                    return (new List<((int X, int Y) Key, int Level)>(), keep);
                }

                // Low down the horizon is still tens of kilometres off, so never less than 60 km; what is loaded stays
                // while it is within reach of the farthest load, so coming down does not drop the roads in view.
                var loadKm = Math.Clamp(altitudeKm * 1.5f, 60.0f, MathF.Max(60.0f, roads.LoadRadius));
                if (altitudeKm > roads.ShowBelow)
                {
                    loadKm = 0.0f;
                }
                var keepKm = MathF.Max(60.0f, roads.LoadRadius) * 1.2f;

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
                        var distanceKm = DistanceKm(below, west, south, east, north);
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

            // Read (or fetched and kept) and cut finer.
            protected override async Task<PlanetDrapedLineSet?> LoadAsync((int X, int Y) key, int level, CancellationToken cancellation)
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

                List<RoadLine> lines;
                try
                {
                    lines = OsmRoadTiles.Parse(data, TileZoom, key.X, key.Y);
                }
                catch (Exception) when (path != null && File.Exists(path))
                {
                    File.Delete(path);
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
    }
}
