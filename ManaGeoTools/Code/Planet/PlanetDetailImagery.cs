using System.Collections.Concurrent;
using System.Diagnostics;
using Mana;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>One sharper picture to fetch: its key, where it lies in the whole picture, its lat/long box and size.</summary>
    public sealed record PlanetImageryTileSpec(string Key, float MinU, float MaxU, float MinV, float MaxV, GeoArea Area, int Width, int Height, float MetresPerPixel = 0.0f);

    /// <summary>
    /// Sharper pictures near the camera. The planet's whole picture is about 5 km a pixel; close up, patches wear
    /// tiles of a few hundred kilometres at the layer's own resolution (Blue Marble's 500 m), fetched for their
    /// lat/long box. A tile is a fixed cell of a cube face, so every patch inside it shares one picture (one GPU
    /// texture); at most <see cref="MaxTiles"/> are worn at once, nearest first, and the rest go back to the whole one.
    /// </summary>
    public sealed class PlanetDetailImagery
    {
        // Tiles are the largest cube cells at most this wide on the ground (about 420 km on Earth).
        private const float TileGroundMetres = 450_000.0f;

        // Blue Marble's own resolution; asking finer only enlarges its pixels.
        private const float SourceMetresPerPixel = 500.0f;
        private const int MaxTileHeight = 1024;
        private const int MaxTileWidth = 2048;

        // Pictures worn at once (a few MB of GPU memory each), and how far down the list one is kept before it goes.
        public const int MaxTiles = 16;
        private const int KeepTiles = 24;
        private const int MaxFetches = 3;

        // Past this latitude a tile's box is mostly longitude; the whole picture serves the poles.
        private const float MaxLatitude = 80.0f;
        private const double UpdateSeconds = 0.25;

        private readonly Func<PlanetImageryTileSpec, CancellationToken, Task<string?>> _build;
        private readonly Dictionary<string, TileState> _tiles = new();
        private readonly ConcurrentDictionary<PlanetPatchMesh, byte> _dressing = new();
        private readonly CancellationTokenSource _cancel = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastUpdate = double.NegativeInfinity;
        private string? _settingsKey;

        public PlanetDetailImagery(Func<PlanetImageryTileSpec, CancellationToken, Task<string?>> build)
        {
            _build = build;
        }

        /// <summary>Tiles fetched and ready, being fetched, and failed -- for the Inspector and the log.</summary>
        public (int Ready, int Fetching, int Failed) Counts
        {
            get
            {
                int ready = 0, fetching = 0, failed = 0;
                foreach (var tile in _tiles.Values)
                {
                    if (tile.Path != null)
                    {
                        ready++;
                    }
                    else if (tile.Failed)
                    {
                        failed++;
                    }
                    else if (tile.Work != null)
                    {
                        fetching++;
                    }
                }
                return (ready, fetching, failed);
            }
        }

        public void Cancel() => _cancel.Cancel();

        /// <summary>
        /// A few times a second: works out which tiles the patches near the camera want, starts fetching the nearest
        /// missing ones and dresses (or undresses) patches whose picture should change. <paramref name="settingsKey"/>
        /// names the look (layer, borders); a new one starts every tile afresh.
        /// </summary>
        public void Update(PlanetNode planet, CameraComponent camera, string settingsKey, int wholeWidth, bool enabled = true)
        {
            if (planet.ImageryPath == null || _clock.Elapsed.TotalSeconds - _lastUpdate < UpdateSeconds)
            {
                return;
            }
            _lastUpdate = _clock.Elapsed.TotalSeconds;
            if (settingsKey != _settingsKey)
            {
                _settingsKey = settingsKey;
                _tiles.Clear();
            }

            if (planet.TryGetLeafMeshes() is not { } meshes || !Matrix4x4.Invert(planet.WorldTransform, out var worldToLocal))
            {
                return;
            }

            var cameraLocal = Vector3.Transform(camera.Owner.Position, worldToLocal);
            var radius = planet.Radius;
            var altitude = MathF.Max(1.0f, cameraLocal.Length() - radius);
            // Ground a screen pixel covers per metre of distance, against what a pixel of the whole picture covers.
            var screenHeight = 1080.0f;
            if (camera.Context.Window is { } window && window.Size.Y > 0)
            {
                screenHeight = window.Size.Y;
            }
            var pixelAngle = 2.0f * MathF.Tan(camera.FieldOfViewDegrees * MathF.PI / 360.0f) / screenHeight;
            var wholeMetresPerPixel = 2.0f * MathF.PI * radius / MathF.Max(1, wholeWidth);
            var tileSize = TileCubeSize(planet.PatchesPerFaceEdge, radius);

            var candidates = new List<(PlanetPatchMesh Mesh, PlanetImageryTileSpec? Spec, float Distance)>(meshes.Length);
            foreach (var mesh in meshes)
            {
                var spec = TileFor(mesh, tileSize, radius, settingsKey);
                var distance = Vector3.Distance(cameraLocal, CenterOf(mesh) * radius);
                // Worth more than the whole picture only where a screen pixel covers less ground than one of its pixels.
                if (!enabled || (spec != null && distance * pixelAngle * 1.5f > wholeMetresPerPixel))
                {
                    spec = null;
                }
                candidates.Add((mesh, spec, distance));
            }
            candidates.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

            var order = new Dictionary<string, int>();
            foreach (var (_, spec, _) in candidates)
            {
                if (spec != null && !order.ContainsKey(spec.Key))
                {
                    order[spec.Key] = order.Count;
                    if (!_tiles.ContainsKey(spec.Key))
                    {
                        _tiles[spec.Key] = new TileState(spec);
                    }
                }
            }

            // A tile nobody wears any more has had its texture let go; decoded again on a worker before it comes back,
            // or the render thread would decode it.
            var worn = new HashSet<string>();
            foreach (var mesh in meshes)
            {
                if (mesh.Detail is { } detail)
                {
                    worn.Add(detail.Key);
                }
            }
            foreach (var tile in _tiles.Values)
            {
                if (worn.Contains(tile.Spec.Key))
                {
                    tile.Worn = true;
                }
                else if (tile.Worn && tile.Path is { } path && order.ContainsKey(tile.Spec.Key))
                {
                    tile.Worn = false;
                    tile.Path = null;
                    tile.Work = Task.Run<string?>(() =>
                    {
                        ManaCore.Imaging.ManaTexturePrefetch.Prepare(path);
                        return path;
                    });
                }
            }

            StartFetches(order);

            var changes = new List<(PlanetPatchMesh Mesh, PlanetImageryDetail? Detail)>();
            foreach (var (mesh, spec, _) in candidates)
            {
                if (_dressing.ContainsKey(mesh))
                {
                    continue;
                }

                var current = mesh.Detail;
                PlanetImageryDetail? wanted = null;
                if (spec != null && order.TryGetValue(spec.Key, out var rank) && _tiles.TryGetValue(spec.Key, out var tile) && tile.Detail != null)
                {
                    if (rank < MaxTiles || (current?.Key == spec.Key && rank < KeepTiles))
                    {
                        wanted = tile.Detail;
                    }
                }
                if (current?.Key != wanted?.Key)
                {
                    _dressing[mesh] = 0;
                    changes.Add((mesh, wanted));
                }
            }

            if (changes.Count > 0)
            {
                planet.DressDetail(changes, mesh => _dressing.TryRemove(mesh, out _));
            }
        }

        // Nearest tiles first, a few at a time; a failed one is not asked for again this run. A download for a place the
        // camera has left is called off (it starts again if the camera comes back), so the nearest ones are not kept waiting.
        private void StartFetches(Dictionary<string, int> order)
        {
            var running = 0;
            foreach (var tile in _tiles.Values)
            {
                if (tile.Work is { IsCompleted: false })
                {
                    if (!order.TryGetValue(tile.Spec.Key, out var wanted) || wanted >= KeepTiles)
                    {
                        tile.Cancel?.Cancel();
                        tile.Cancel = null;
                        tile.Work = null;
                        continue;
                    }
                    running++;
                }
                else if (tile.Work is { } finished && tile.Path == null && !tile.Failed)
                {
                    if (finished.IsCompletedSuccessfully && finished.Result is { } path)
                    {
                        tile.Path = path;
                    }
                    else
                    {
                        tile.Failed = true;
                    }
                }
            }

            foreach (var (key, rank) in order.OrderBy(pair => pair.Value))
            {
                if (running >= MaxFetches || rank >= MaxTiles)
                {
                    break;
                }
                var tile = _tiles[key];
                if (tile.Work != null || tile.Failed)
                {
                    continue;
                }
                tile.Cancel = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
                var token = tile.Cancel.Token;
                tile.Work = Task.Run(() => _build(tile.Spec, token), token);
                running++;
            }
        }

        /// <summary>The cube-face size of a tile: a root patch halved until it is at most <see cref="TileGroundMetres"/> across.</summary>
        public static float TileCubeSize(int patchesPerFaceEdge, float radius)
        {
            // A face is two units across and a quarter of the way round the planet.
            var groundPerUnit = MathF.PI * radius / 4.0f;
            var size = 2.0f / Math.Max(1, patchesPerFaceEdge);
            while (size * groundPerUnit > TileGroundMetres && size > 1e-4f)
            {
                size *= 0.5f;
            }
            return size;
        }

        /// <summary>The tile a patch lies in, or null when the patch is bigger than a tile, near a pole or across the date line.</summary>
        public static PlanetImageryTileSpec? TileFor(PlanetPatchMesh mesh, float tileSize, float radius, string settingsKey)
        {
            if (mesh.MaxU - mesh.MinU > tileSize * 1.001f || mesh.MaxV - mesh.MinV > tileSize * 1.001f)
            {
                return null;
            }

            var column = (int)MathF.Floor((0.5f * (mesh.MinU + mesh.MaxU) + 1.0f) / tileSize);
            var row = (int)MathF.Floor((0.5f * (mesh.MinV + mesh.MaxV) + 1.0f) / tileSize);
            var minU = -1.0f + column * tileSize;
            var minV = -1.0f + row * tileSize;
            var normal = mesh.FaceNormal;
            var face = $"{MathF.Round(normal.X):0}{MathF.Round(normal.Y):0}{MathF.Round(normal.Z):0}";
            var key = $"{settingsKey}/n{face}-{MathF.Round(2.0f / tileSize):0}-{column}-{row}";
            return Spec(key, normal, minU, minU + tileSize, minV, minV + tileSize, radius);
        }

        private static PlanetImageryTileSpec? Spec(string key, Vector3 normal, float minU, float maxU, float minV, float maxV, float radius)
        {
            var axisA = new Vector3(normal.Y, normal.Z, normal.X);
            var axisB = Vector3.Cross(normal, axisA);
            Vector2 At(float u, float v) => PlanetPatchMesh.GlobalUv(CubeSphere.WarpCubeToSphere(normal + u * axisA + v * axisB));

            var centerU = At(0.5f * (minU + maxU), 0.5f * (minV + maxV)).X;
            var low = new Vector2(float.MaxValue);
            var high = new Vector2(float.MinValue);
            const int steps = 16;
            for (var i = 0; i <= steps; i++)
            {
                var t = (float)i / steps;
                foreach (var uv in (ReadOnlySpan<Vector2>)[
                    At(float.Lerp(minU, maxU, t), minV), At(float.Lerp(minU, maxU, t), maxV),
                    At(minU, float.Lerp(minV, maxV, t)), At(maxU, float.Lerp(minV, maxV, t))])
                {
                    var unwrapped = new Vector2(PlanetPatchMesh.UnwrapU(uv.X, centerU), uv.Y);
                    low = Vector2.Min(low, unwrapped);
                    high = Vector2.Max(high, unwrapped);
                }
            }

            // A little over the cell, so the bowed edges and the skirts stay inside the picture.
            var margin = (high - low) * 0.01f;
            low -= margin;
            high += margin;
            var maxLatitude = 90.0 - low.Y * 180.0;
            var minLatitude = 90.0 - high.Y * 180.0;
            if (low.X < 0.0f || high.X > 1.0f || maxLatitude > MaxLatitude || minLatitude < -MaxLatitude)
            {
                return null;
            }

            var area = new GeoArea(low.X * 360.0 - 180.0, high.X * 360.0 - 180.0, minLatitude, maxLatitude);
            var latitudeSpan = area.MaxLatitude - area.MinLatitude;
            var longitudeSpan = area.MaxLongitude - area.MinLongitude;
            // Pixels square on the ground at the tile's middle: square in degrees wasted a third of each tile at the
            // Alps' latitude on east-west detail the source does not have (and its upload hitched).
            var height = (int)Math.Clamp(Math.Ceiling(latitudeSpan * Math.PI / 180.0 * radius / SourceMetresPerPixel), 64, MaxTileHeight);
            var middleLatitude = 0.5 * (area.MinLatitude + area.MaxLatitude) * Math.PI / 180.0;
            var width = (int)Math.Max(16, Math.Round(height * longitudeSpan * Math.Cos(middleLatitude) / latitudeSpan));
            if (width > MaxTileWidth)
            {
                width = MaxTileWidth;
                height = Math.Max(16, (int)Math.Round(width * latitudeSpan / longitudeSpan));
            }
            var metresPerPixel = (float)(latitudeSpan * Math.PI / 180.0 * radius / height);
            return new PlanetImageryTileSpec(key, low.X, high.X, low.Y, high.Y, area, width, height, metresPerPixel);
        }

        private static Vector3 CenterOf(PlanetPatchMesh mesh)
        {
            var normal = mesh.FaceNormal;
            var axisA = new Vector3(normal.Y, normal.Z, normal.X);
            var axisB = Vector3.Cross(normal, axisA);
            return CubeSphere.WarpCubeToSphere(normal + 0.5f * (mesh.MinU + mesh.MaxU) * axisA + 0.5f * (mesh.MinV + mesh.MaxV) * axisB);
        }

        private sealed class TileState(PlanetImageryTileSpec spec)
        {
            public PlanetImageryTileSpec Spec { get; } = spec;
            public Task<string?>? Work { get; set; }
            public string? Path { get; set; }
            public bool Failed { get; set; }
            public bool Worn { get; set; }
            public CancellationTokenSource? Cancel { get; set; }

            public PlanetImageryDetail? Detail
            {
                get
                {
                    if (Path == null)
                    {
                        return null;
                    }
                    _detail ??= new PlanetImageryDetail(Spec.Key, Path, Spec.MinU, Spec.MaxU, Spec.MinV, Spec.MaxV, Spec.MetresPerPixel);
                    return _detail;
                }
            }

            private PlanetImageryDetail? _detail;
        }
    }
}
