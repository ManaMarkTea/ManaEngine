using System.Diagnostics;
using Mana;
using Mana.Code.Components;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using Mana.Code.Shapes;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Lines laid on a planet's drawn ground, in cells loaded around the camera: each cell's lines are read on a worker,
    /// laid on the patches drawn then, drawn as screen-wide ribbons (draped_line.wgsl), laid again whenever the patches
    /// under them change, and let go once the camera leaves. A cell can be built at several levels of detail; a new
    /// level replaces the old in one step, so nothing blinks. Roads and borders are the two kinds.
    /// </summary>
    internal abstract class PlanetLineLayer
    {
        private const int BuildsPerFrame = 2;
        private const double UpdateSeconds = 0.25;
        private const double FailedRetrySeconds = 20.0;

        private readonly ManaComponent _owner;
        private readonly string _groupName;
        private readonly string _logTag;
        private readonly Dictionary<(int X, int Y), LineCell> _cells = new();
        private readonly CancellationTokenSource _cancel = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastUpdate = double.NegativeInfinity;
        private ManaSceneNode? _group;
        private Task<DrapeResult>? _drapeWork;
        private string _lastStatus = "";

        protected PlanetLineLayer(ManaComponent owner, string groupName, string logTag, int classCount)
        {
            _owner = owner;
            _groupName = groupName;
            _logTag = logTag;
            ClassCount = classCount;
        }

        protected int ClassCount { get; }

        /// <summary>Loads running at once.</summary>
        protected virtual int MaxLoads => 6;

        /// <summary>Screen height a pixel covers per unit of distance, for widths and detail.</summary>
        protected float PixelAngle { get; private set; }

        /// <summary>Metres a local unit of the planet stands for.</summary>
        protected float MetresPerUnit { get; private set; } = 1.0f;

        public string Status { get; private set; } = "";

        /// <summary>Some lines are on screen.</summary>
        public bool Drawing { get; private set; }

        /// <summary>
        /// The cells to load, nearest first, each with the level of detail it should have (0 the finest), and the wider set
        /// to keep, so a cell at the edge does not come and go as the camera wobbles.
        /// </summary>
        protected abstract (List<((int X, int Y) Key, int Level)> Wanted, HashSet<(int X, int Y)> Keep) Plan(Vector3 below, float altitudeKm);

        /// <summary>A cell's lines at a level of detail, not yet laid on the ground; on a worker.</summary>
        protected abstract Task<PlanetDrapedLineSet?> LoadAsync((int X, int Y) key, int level, CancellationToken cancellation);

        protected abstract Vector4 ColorOf(int lineClass);

        protected abstract bool Shows(int lineClass, float altitudeKm);

        /// <summary>Sets the draped_line.wgsl uniforms for a class: widths and the pull toward the camera.</summary>
        protected abstract void SetLook(ManaMaterial material, int lineClass);

        protected virtual string ClassName(int lineClass) => $"Class {lineClass}";

        /// <summary>Ground distance in km from the point below the camera to a lat/long box: to its middle less half its diagonal, so a box counts from its nearest edge.</summary>
        protected static float DistanceKm(Vector3 below, double west, double south, double east, double north)
        {
            var middleLatitude = (south + north) * 0.5;
            var centre = LatLong.FromLongitudeAndLatitude((float)((west + east) * 0.5), (float)middleLatitude).ToUnitSphere();
            var widthKm = (east - west) * 111.0 * Math.Cos(middleLatitude * Math.PI / 180.0);
            var heightKm = (north - south) * 111.0;
            var halfDiagonalKm = (float)(0.5 * Math.Sqrt(widthKm * widthKm + heightKm * heightKm));
            var angle = MathF.Acos(Math.Clamp(Vector3.Dot(centre, below), -1.0f, 1.0f));
            return MathF.Max(0.0f, angle * PlanetLoader.EarthRadiusMeters / 1000.0f - halfDiagonalKm);
        }

        /// <summary>Whether a cell built at <paramref name="built"/> should be built again for <paramref name="wanted"/>: finer at once, coarser only two steps out, so a cell on the line between levels is not rebuilt back and forth.</summary>
        private static bool Rebuilds(int built, int wanted)
        {
            return wanted < built || wanted > built + 1;
        }

        public void Update(PlanetNode planet, CameraComponent? camera, bool enabled)
        {
            AdoptFinished(planet);

            var now = _clock.Elapsed.TotalSeconds;
            if (now - _lastUpdate < UpdateSeconds)
            {
                return;
            }
            _lastUpdate = now;

            if (!enabled || camera == null || !Matrix4x4.Invert(planet.WorldTransform, out var worldToLocal))
            {
                UnloadAll();
                return;
            }

            var cameraLocal = Vector3.Transform(camera.Owner.Position, worldToLocal);
            MetresPerUnit = PlanetLoader.EarthRadiusMeters / planet.Radius;
            var altitudeKm = (cameraLocal.Length() - planet.Radius) * MetresPerUnit / 1000.0f;
            var below = Vector3.Normalize(cameraLocal);

            var screenHeight = 1080.0f;
            if (_owner.Context.Window is { } window && window.Size.Y > 0)
            {
                screenHeight = window.Size.Y;
            }
            var pixelAngle = 2.0f * MathF.Tan(camera.FieldOfViewDegrees * MathF.PI / 360.0f) / screenHeight;
            var resize = MathF.Abs(pixelAngle - PixelAngle) > PixelAngle * 0.02f;
            PixelAngle = pixelAngle;

            var (wanted, keep) = Plan(below, altitudeKm);
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
                Console.WriteLine($"[{_logTag}] Let go of {dropped} cell(s) at {altitudeKm:0.#} km; {_cells.Count} kept.");
            }

            var leaves = planet.TryGetLeafMeshes();
            if (leaves != null)
            {
                StartLoads(wanted, leaves, planet.Radius, now);
                StartDrape(leaves, planet.Radius);
            }

            ApplyLook(altitudeKm, resize);
            ReportStatus(altitudeKm);
        }

        public void Remove()
        {
            _cancel.Cancel();
            UnloadAll();
            if (_group != null)
            {
                lock (_owner.Context.World.SceneLock)
                {
                    _group.Parent?.RemoveChild(_group);
                }
                _group = null;
            }
        }

        private void StartLoads(List<((int X, int Y) Key, int Level)> wanted, PlanetPatchMesh[] leaves, float seaRadius, double now)
        {
            var running = _cells.Values.Count(cell => cell.Work != null);
            foreach (var (key, level) in wanted)
            {
                if (!_cells.TryGetValue(key, out var cell))
                {
                    cell = new LineCell(key, ClassCount);
                    _cells[key] = cell;
                }
                if (running >= MaxLoads)
                {
                    continue;
                }
                if (cell.Work != null || now < cell.RetryAt)
                {
                    continue;
                }
                if (cell.Geometry != null && !Rebuilds(cell.Level, level))
                {
                    continue;
                }

                cell.Cancel = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
                var token = cell.Cancel.Token;
                cell.WorkLevel = level;
                cell.Work = Task.Run(async () =>
                {
                    var geometry = await LoadAsync(key, level, token).ConfigureAwait(false);
                    geometry?.Drape(leaves, seaRadius, geometry.Points);
                    return geometry;
                }, token);
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
                    Build(planet, cell, geometry);
                    built++;
                }
                else if (!work.IsCanceled)
                {
                    var reason = work.Exception?.InnerException?.Message ?? "nothing came back";
                    Console.WriteLine($"[{_logTag}] Cell {cell.Key} failed: {reason}; again in {FailedRetrySeconds:0} s.");
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

        // One node per class under the planet's group; a cell built again swaps its nodes in the same lock.
        private void Build(PlanetNode planet, LineCell cell, PlanetDrapedLineSet geometry)
        {
            var context = _owner.Context;
            lock (context.World.SceneLock)
            {
                if (_group == null)
                {
                    _group = new ManaSceneNode(context, Matrix4x4.Identity)
                    {
                        Name = _groupName,
                        IncludeInEnvironmentCapture = false
                    };
                    planet.AddChild(_group);
                    _group.UpdateTransform();
                }

                RemoveNodes(cell);
                for (var c = 0; c < ClassCount; c++)
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
                    var mesh = new ManaLineMesh(context, polylines, 1.0f, ColorOf(c), 0.0f, ManaLineProfile.ScreenRibbon)
                    {
                        Name = $"{ClassName(c)} {cell.Key.X},{cell.Key.Y}"
                    };
                    var node = new ManaSceneNode(context, Matrix4x4.Identity)
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
                    SetLook(renderer.Material, c);
                }
            }

            cell.Geometry = geometry;
            cell.Level = cell.WorkLevel;
            cell.LeafSignature = 0;
        }

        // Lays again, on a worker and one batch at a time, the cells whose ground changed since they were laid -- a patch
        // below split, merged or sharpened.
        private void StartDrape(PlanetPatchMesh[] leaves, float seaRadius)
        {
            if (_drapeWork != null)
            {
                return;
            }

            var batch = new List<(LineCell Cell, PlanetDrapedLineSet Geometry, int Signature)>();
            foreach (var cell in _cells.Values)
            {
                if (cell.Geometry is not { } geometry || cell.Nodes.All(node => node == null))
                {
                    continue;
                }
                var signature = geometry.Signature(leaves);
                if (signature != cell.LeafSignature)
                {
                    batch.Add((cell, geometry, signature));
                }
            }
            if (batch.Count == 0)
            {
                return;
            }

            _drapeWork = Task.Run(() =>
            {
                var laid = new List<(LineCell Cell, PlanetDrapedLineSet Geometry, int Signature, Vector3[][][] Points)>(batch.Count);
                foreach (var (cell, geometry, signature) in batch)
                {
                    var points = geometry.EmptyCopy();
                    geometry.Drape(leaves, seaRadius, points);
                    laid.Add((cell, geometry, signature, points));
                }
                return new DrapeResult(laid);
            });
        }

        private void ApplyDrape(DrapeResult result)
        {
            foreach (var (cell, geometry, signature, points) in result.Cells)
            {
                // Built again (another level) or let go since: these points belong to lines no longer drawn.
                if (cell.Geometry != geometry || !_cells.TryGetValue(cell.Key, out var current) || current != cell)
                {
                    continue;
                }
                for (var c = 0; c < ClassCount; c++)
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

        private void ApplyLook(float altitudeKm, bool resize)
        {
            foreach (var cell in _cells.Values)
            {
                for (var c = 0; c < ClassCount; c++)
                {
                    if (cell.Renderers[c] is not { } renderer)
                    {
                        continue;
                    }
                    renderer.IsVisible = Shows(c, altitudeKm);
                    if (resize)
                    {
                        SetLook(renderer.Material, c);
                    }
                }
            }
        }

        private void ReportStatus(float altitudeKm)
        {
            var ready = _cells.Values.Count(cell => cell.Geometry != null);
            Drawing = _cells.Values.Any(cell => cell.Renderers.Any(renderer => renderer is { IsVisible: true }));
            var loading = _cells.Values.Count(cell => cell.Work != null);
            Status = $"{ready} cells drawn, {loading} loading";
            if (Status != _lastStatus && loading == 0 && _cells.Count > 0)
            {
                Console.WriteLine($"[{_logTag}] {Status} at {altitudeKm:0.#} km.");
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

        private void Unload(LineCell cell)
        {
            cell.Cancel?.Cancel();
            cell.Cancel = null;
            cell.Work = null;
            lock (_owner.Context.World.SceneLock)
            {
                RemoveNodes(cell);
            }
            cell.Geometry = null;
        }

        // Caller holds the scene lock.
        private void RemoveNodes(LineCell cell)
        {
            for (var c = 0; c < ClassCount; c++)
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

        private sealed class LineCell((int X, int Y) key, int classCount)
        {
            public (int X, int Y) Key { get; } = key;
            public Task<PlanetDrapedLineSet?>? Work { get; set; }
            public int WorkLevel { get; set; }
            public CancellationTokenSource? Cancel { get; set; }
            public PlanetDrapedLineSet? Geometry { get; set; }
            public int Level { get; set; }
            public double RetryAt { get; set; } = double.NegativeInfinity;
            public int LeafSignature { get; set; }
            public ManaSceneNode?[] Nodes { get; } = new ManaSceneNode?[classCount];
            public MeshRendererComponent?[] Renderers { get; } = new MeshRendererComponent?[classCount];
            public ManaLineMesh?[] Meshes { get; } = new ManaLineMesh?[classCount];
        }

        private sealed record DrapeResult(List<(LineCell Cell, PlanetDrapedLineSet Geometry, int Signature, Vector3[][][] Points)> Cells);
    }
}
