using System.IO.Compression;
using Mana;
using Mana.Code.Materials;
using ManaGeoTools.Code.Shapefile;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Natural Earth's coastlines, country borders and state lines drawn on a planet as lines a constant width on screen,
    /// laid on the ground, rather than painted into its picture: a painted line is as wide as a picture's pixel, a
    /// smear several kilometres wide from close up. Cut into 10-degree cells over the visible half of the world, each
    /// thinned to about a third of a screen pixel's ground at its distance, so far cells are a few points and near ones
    /// the 10 m lines in full (50 m in the browser, whose download would be 20 MB more).
    /// </summary>
    internal sealed class PlanetBorderLayer(PlanetImageryComponent imagery) : PlanetLineLayer(imagery, "Borders", "Borders", 2)
    {
        private const int Countries = 0;
        private const int States = 1;
        private const double CellDegrees = 10.0;
        private const int Levels = 6;

        // Below the roads (draped_line.wgsl's pull toward the camera): where a road follows a border the road wins.
        private const float TowardCamera = 0.003f;

        private static readonly Lazy<Task<Dictionary<(int X, int Y), List<(int Class, (double Longitude, double Latitude)[] Points)>>>> Index =
            new(() => Task.Run(BuildIndex));

        protected override int MaxLoads => 3;

        /// <summary>Gap between kept points at a level of detail: 100 m, then four times more each level.</summary>
        private static double LevelMetres(int level)
        {
            return 100.0 * Math.Pow(4.0, level);
        }

        protected override (List<((int X, int Y) Key, int Level)> Wanted, HashSet<(int X, int Y)> Keep) Plan(Vector3 below, float altitudeKm)
        {
            var wanted = new List<((int X, int Y) Key, int Level, float Distance)>();
            var keep = new HashSet<(int X, int Y)>();
            if (!Index.Value.IsCompletedSuccessfully || (!imagery.Borders && !imagery.States))
            {
                return (new List<((int X, int Y) Key, int Level)>(), keep);
            }

            // Out to the horizon, and a little past it so turning round finds the lines there.
            var radiusKm = PlanetLoader.EarthRadiusMeters / 1000.0f;
            var height = MathF.Max(0.0f, altitudeKm);
            var horizonKm = radiusKm * MathF.Acos(radiusKm / (radiusKm + height));
            var loadKm = horizonKm * 1.1f + 300.0f;
            var keepKm = loadKm * 1.25f + 300.0f;

            foreach (var key in Index.Value.Result.Keys)
            {
                var west = key.X * CellDegrees - 180.0;
                var south = key.Y * CellDegrees - 90.0;
                var distanceKm = DistanceKm(below, west, south, west + CellDegrees, south + CellDegrees);
                if (distanceKm <= keepKm)
                {
                    keep.Add(key);
                }
                if (distanceKm <= loadKm)
                {
                    wanted.Add((key, LevelFor(distanceKm, height), distanceKm));
                }
            }
            wanted.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
            return (wanted.Select(entry => (entry.Key, entry.Level)).ToList(), keep);
        }

        // Points about three screen pixels apart at the cell's nearest edge.
        private int LevelFor(float distanceKm, float altitudeKm)
        {
            var slantMetres = MathF.Sqrt(distanceKm * distanceKm + altitudeKm * altitudeKm) * 1000.0f;
            var spacing = slantMetres * PixelAngle * 3.0f;
            var level = 0;
            while (level < Levels - 1 && LevelMetres(level + 1) <= spacing)
            {
                level++;
            }
            return level;
        }

        protected override async Task<PlanetDrapedLineSet?> LoadAsync((int X, int Y) key, int level, CancellationToken cancellation)
        {
            var index = await Index.Value.ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var spacing = LevelMetres(level);
            var lines = new List<(int Class, IReadOnlyList<(double Longitude, double Latitude)> Points)>();
            if (index.TryGetValue(key, out var pieces))
            {
                foreach (var (lineClass, points) in pieces)
                {
                    lines.Add((lineClass, Thin(points, spacing)));
                }
            }
            // Cut finer where points are far apart, so the line follows hills between them; no finer than 300 m.
            var stepAngle = (float)(Math.Max(spacing, 300.0) / PlanetLoader.EarthRadiusMeters);
            return PlanetDrapedLineSet.From(lines, ClassCount, stepAngle);
        }

        // Keeps a point once it is <paramref name="spacing"/> metres from the last kept one, and always both ends, so the
        // pieces of a line cut at cell edges still meet.
        private static IReadOnlyList<(double Longitude, double Latitude)> Thin((double Longitude, double Latitude)[] points, double spacing)
        {
            if (points.Length <= 2)
            {
                return points;
            }
            var kept = new List<(double Longitude, double Latitude)>(points.Length) { points[0] };
            var metresPerDegree = PlanetLoader.EarthRadiusMeters * Math.PI / 180.0;
            var last = points[0];
            for (var i = 1; i < points.Length - 1; i++)
            {
                var point = points[i];
                var dx = (point.Longitude - last.Longitude) * Math.Cos(point.Latitude * Math.PI / 180.0);
                var dy = point.Latitude - last.Latitude;
                if (Math.Sqrt(dx * dx + dy * dy) * metresPerDegree >= spacing)
                {
                    kept.Add(point);
                    last = point;
                }
            }
            kept.Add(points[^1]);
            return kept;
        }

        protected override string ClassName(int lineClass)
        {
            if (lineClass == Countries)
            {
                return "Borders";
            }
            return "State lines";
        }

        protected override Vector4 ColorOf(int lineClass)
        {
            if (lineClass == Countries)
            {
                return imagery.BorderColor;
            }
            return imagery.StateColor;
        }

        protected override bool Shows(int lineClass, float altitudeKm)
        {
            if (lineClass == Countries)
            {
                return imagery.Borders;
            }
            return imagery.States && altitudeKm <= imagery.StatesBelow;
        }

        // A constant width on screen at any distance.
        protected override void SetLook(ManaMaterial material, int lineClass)
        {
            var pixels = imagery.BorderWidth;
            var pull = TowardCamera;
            if (lineClass == States)
            {
                pixels = imagery.StateWidth;
                pull *= 0.95f;
            }
            material.SetShaderUniform("widthPerDistance", 0.5f * pixels * MathF.Max(PixelAngle, 1e-5f));
            material.SetShaderUniform("minHalfWidth", 0.0f);
            material.SetShaderUniform("maxHalfWidth", 1.0e7f);
            material.SetShaderUniform("towardCamera", pull);
            material.SetShaderUniform("faceCamera", 1.0f);
        }

        // Every ring, closed and cut where it crosses from one cell into the next (the crossing stays with the cell it
        // leaves, so the pieces join end to end).
        private static Dictionary<(int X, int Y), List<(int Class, (double Longitude, double Latitude)[] Points)>> BuildIndex()
        {
            var index = new Dictionary<(int X, int Y), List<(int Class, (double Longitude, double Latitude)[] Points)>>();
            var started = System.Diagnostics.Stopwatch.StartNew();
            var points = 0;
            foreach (var (lineClass, name) in new[] { (Countries, "admin_0_countries"), (States, "admin_1_states_provinces") })
            {
                foreach (var ring in ReadRings(LinesFile(name)))
                {
                    if (ring.Count < 2)
                    {
                        continue;
                    }
                    points += ring.Count;
                    var run = new List<(double Longitude, double Latitude)> { ring[0] };
                    var runCell = CellOf(ring[0]);
                    for (var i = 1; i <= ring.Count; i++)
                    {
                        var point = ring[i % ring.Count];
                        run.Add(point);
                        var cell = CellOf(point);
                        if (cell != runCell)
                        {
                            Add(index, runCell, lineClass, run);
                            run = new List<(double Longitude, double Latitude)> { point };
                            runCell = cell;
                        }
                    }
                    Add(index, runCell, lineClass, run);
                }
            }
            Console.WriteLine($"[Borders] {points:N0} points in {index.Count} cells, read in {started.Elapsed.TotalSeconds:0.0} s.");
            return index;
        }

        private static void Add(Dictionary<(int X, int Y), List<(int Class, (double Longitude, double Latitude)[] Points)>> index,
            (int X, int Y) cell, int lineClass, List<(double Longitude, double Latitude)> run)
        {
            if (run.Count < 2)
            {
                return;
            }
            if (!index.TryGetValue(cell, out var list))
            {
                list = new List<(int Class, (double Longitude, double Latitude)[] Points)>();
                index[cell] = list;
            }
            list.Add((lineClass, run.ToArray()));
        }

        private static (int X, int Y) CellOf((double Longitude, double Latitude) point)
        {
            var columns = (int)(360.0 / CellDegrees);
            var rows = (int)(180.0 / CellDegrees);
            var x = Math.Clamp((int)Math.Floor((point.Longitude + 180.0) / CellDegrees), 0, columns - 1);
            var y = Math.Clamp((int)Math.Floor((point.Latitude + 90.0) / CellDegrees), 0, rows - 1);
            return (x, y);
        }

        private static string LinesFile(string name)
        {
            if (OperatingSystem.IsBrowser())
            {
                return "ne_50m_" + name;
            }
            return "ne_10m_" + name;
        }

        private static IEnumerable<IReadOnlyList<(double Longitude, double Latitude)>> ReadRings(string name)
        {
            var zip = ManaTexture.ResolveTexturePath(Path.Combine("Assets", "GeoData", name + ".zip"));
            if (!File.Exists(zip))
            {
                Console.WriteLine($"[Borders] {name}.zip is missing; drawn without it.");
                return [];
            }

            using var archive = ZipFile.OpenRead(zip);
            using var stream = archive.GetEntry(name + ".shp")?.Open();
            if (stream == null)
            {
                return [];
            }
            return ShapefilePolygonReader.ReadRings(stream).ToList();
        }
    }
}
