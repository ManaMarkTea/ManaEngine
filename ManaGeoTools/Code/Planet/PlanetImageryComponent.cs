using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Serialization;
using Mana;
using Mana.Code.Nodes;
using ManaCore.Imaging;
using ManaGeoTools.Code.Shapefile;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Dresses a planet in a satellite picture: NASA GIBS's Blue Marble (or another of its global layers), fetched
    /// once and kept as one JPEG in Library/PlanetImagery -- about 3 MB at 8192 wide. The patches wear it through their
    /// lat/long coordinates, so it follows the terrain at any detail. In the browser the picture is 4096 wide (memory),
    /// kept for the session only. Natural Earth's coastlines, country and state borders are drawn over it as lines
    /// (<see cref="PlanetBorderLayer"/>), never painted in: a painted line is a pixel wide, kilometres from close up.
    /// </summary>
    public sealed class PlanetImageryComponent : ManaComponent
    {
        public const string ScriptType = "PlanetImagery";

        private const string GibsUrl = "https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi";

        // Bumped when the painting changes, so every saved picture is made again.
        private const int PaintVersion = 3;

        /// <summary>Where downloads and finished pictures are kept between runs.</summary>
        public static string CacheFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "Library", "PlanetImagery");

        public bool Enabled { get; set; } = true;

        /// <summary>A GIBS layer with a global, undated picture: BlueMarble_ShadedRelief_Bathymetry, BlueMarble_NextGeneration, BlueMarble_ShadedRelief.</summary>
        public string Layer { get; set; } = "BlueMarble_ShadedRelief_Bathymetry";

        /// <summary>Picture width in pixels (half as tall); 8192 is about 5 km a pixel and the most a GPU texture is sure to take.</summary>
        public int Width { get; set; } = 8192;

        /// <summary>Coastlines and country borders.</summary>
        public bool Borders { get; set; } = true;

        /// <summary>States and provinces.</summary>
        public bool States { get; set; } = true;

        /// <summary>Height in km below which state lines show; above it the whole world's are a tangle.</summary>
        public float StatesBelow { get; set; } = 3000.0f;

        public Vector4 BorderColor { get; set; } = new(1.0f, 1.0f, 1.0f, 1.0f);

        public Vector4 StateColor { get; set; } = new(0.72f, 0.72f, 0.72f, 1.0f);

        /// <summary>Line width on screen, in pixels.</summary>
        public float BorderWidth { get; set; } = 1.6f;

        public float StateWidth { get; set; } = 1.0f;

        /// <summary>
        /// Sharper pictures of the ground near the camera: tiles of a few hundred kilometres at the layer's own
        /// resolution (Blue Marble's 500 m, ten times the whole picture's), fetched as the camera goes and kept in
        /// Library/PlanetImagery/Tiles.
        /// </summary>
        public bool Detail { get; set; } = true;

        [JsonIgnore]
        public string Status { get; private set; } = "Starting";

        private static readonly HttpClient Http = CreateHttp();
        private readonly CancellationTokenSource _cancel = new();
        private Task<string?>? _work;
        private Task<string?>? _starter;
        private string? _builtKey;
        private string? _readyPath;
        private PlanetDetailImagery? _detail;
        private string _lastDetailCounts = "";

        private readonly PlanetBorderLayer _borders;

        public PlanetImageryComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Planet Imagery";
            _borders = new PlanetBorderLayer(this);
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            if (Owner is not PlanetNode planet)
            {
                return;
            }

            _borders.Update(planet, Context.World.MainCamera, Borders || States);

            if (!Enabled)
            {
                if (planet.ImageryPath != null && planet.ImageryPath == _readyPath)
                {
                    planet.SetImagery(null);
                }
                return;
            }

            if (_work is { IsCompleted: true } finished)
            {
                _work = null;
                if (finished.IsCompletedSuccessfully && finished.Result is { } path)
                {
                    _readyPath = path;
                }
                Console.WriteLine($"[Imagery] {Status}");
            }

            // Settings edited in the Inspector change the key, which paints a new picture.
            var key = SettingsKey();
            if (_work == null && key != _builtKey)
            {
                _builtKey = key;
                _work = Task.Run(() => BuildAsync(key, _cancel.Token));
            }

            if (_readyPath != null && planet.ImageryPath != _readyPath)
            {
                planet.SetImagery(_readyPath);
            }
            else if (_readyPath == null)
            {
                WearStarter(planet);
            }

            if (_readyPath != null && planet.ImageryPath == _readyPath && Context.World.MainCamera is { } camera)
            {
                if (Detail)
                {
                    _detail ??= new PlanetDetailImagery(BuildTileAsync);
                }
                _detail?.Update(planet, camera, key, PictureWidth, Detail);
                if (_detail != null)
                {
                    var (ready, fetching, failed) = _detail.Counts;
                    var counts = $"{ready} sharp tiles, {fetching} coming, {failed} failed";
                    if (counts != _lastDetailCounts && fetching == 0)
                    {
                        Console.WriteLine($"[Imagery] {counts}");
                    }
                    _lastDetailCounts = counts;
                }
            }
        }

        // One sharper picture: the layer for the tile's box from GIBS, saved, decoded and mip-mapped here.
        private async Task<string?> BuildTileAsync(PlanetImageryTileSpec spec, CancellationToken cancellation)
        {
            try
            {
                var split = spec.Key.IndexOf('/');
                var folder = Path.Combine(CacheFolder, "Tiles", Safe(spec.Key[..split]));
                var path = Path.Combine(folder, Safe(spec.Key[(split + 1)..]) + ".jpg");
                if (File.Exists(path))
                {
                    ManaTexturePrefetch.Prepare(path);
                    return path;
                }

                var area = spec.Area;
                var box = string.Create(CultureInfo.InvariantCulture,
                    $"{area.MinLatitude:0.######},{area.MinLongitude:0.######},{area.MaxLatitude:0.######},{area.MaxLongitude:0.######}");
                var bytes = await FetchAsync(Layer, box, spec.Width, spec.Height, cancellation).ConfigureAwait(false);
                var image = await ManaImageCodec.LoadOnceAsync(bytes).ConfigureAwait(false);

                Directory.CreateDirectory(folder);
                ManaImageCodec.SavePng(image, path, jpegQuality: 90);
                ManaTexturePrefetch.Store(path, image);
                return path;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Imagery] Tile {spec.Key} failed: {ex.Message}");
                return null;
            }
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            _cancel.Cancel();
            _detail?.Cancel();
            _borders.Remove();
            if (Owner is PlanetNode planet && planet.ImageryPath != null && planet.ImageryPath == _readyPath)
            {
                planet.SetImagery(null);
            }
        }

        // A browser tab has the memory for a 4096 picture and its mips, not an 8192 one.
        private int PictureWidth => Math.Clamp(Width, 1024, MaxPictureWidth) / 2 * 2;

        private static int MaxPictureWidth
        {
            get
            {
                if (OperatingSystem.IsBrowser())
                {
                    return 4096;
                }
                return 8192;
            }
        }

        private string SettingsKey()
        {
            return $"{Layer}-{PictureWidth}-v{PaintVersion}";
        }

        private async Task<string?> BuildAsync(string key, CancellationToken cancellation)
        {
            try
            {
                Directory.CreateDirectory(CacheFolder);
                var finished = Path.Combine(CacheFolder, Safe(key) + ".jpg");
                if (File.Exists(finished))
                {
                    // Decoded and mip-mapped here, so the render thread only uploads it.
                    ManaTexturePrefetch.Prepare(finished);
                    Status = $"{Layer} from the saved picture";
                    return finished;
                }

                var width = PictureWidth;
                var download = Path.Combine(CacheFolder, Safe($"{Layer}-{width}") + ".source.jpg");
                byte[] source;
                if (File.Exists(download))
                {
                    source = await File.ReadAllBytesAsync(download, cancellation).ConfigureAwait(false);
                }
                else
                {
                    Status = $"Fetching {Layer} ({width} x {width / 2}) from NASA GIBS";
                    Console.WriteLine($"[Imagery] {Status}");
                    source = await FetchAsync(Layer, "-90,-180,90,180", width, width / 2, cancellation).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(download, source, cancellation).ConfigureAwait(false);
                }

                var image = await ManaImageCodec.LoadOnceAsync(source).ConfigureAwait(false);
                ManaImageCodec.SavePng(image, finished, jpegQuality: 88);
                ManaTexturePrefetch.Store(finished, image);
                RemoveOtherPictures(finished, download);
                Status = $"{Layer}, {width} x {width / 2}";
                return finished;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Status = $"No picture: {ex.Message}";
                return null;
            }
        }

        /// <summary>A small finished picture bundled with the app (Tools/Planet/make_starter_imagery.py), worn while the sharp one is made.</summary>
        public const string StarterPicture = "Assets/Planet/earth_starter.jpg";

        // Decoded on a worker and handed to the texture cache, so the globe is never plain green while GIBS is fetched.
        private void WearStarter(PlanetNode planet)
        {
            if (_starter == null)
            {
                var path = ManaTexture.ResolveTexturePath(StarterPicture);
                if (!File.Exists(path))
                {
                    _starter = Task.FromResult<string?>(null);
                    return;
                }
                _starter = Task.Run(async () =>
                {
                    try
                    {
                        var image = await ManaImageCodec.LoadOnceAsync(await File.ReadAllBytesAsync(path).ConfigureAwait(false)).ConfigureAwait(false);
                        ManaTexturePrefetch.Store(path, image);
                        return path;
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException)
                    {
                        Console.WriteLine($"[Imagery] No starter picture: {ex.Message}");
                        return (string?)null;
                    }
                });
            }
            if (_starter is { IsCompletedSuccessfully: true, Result: { } starter } && planet.ImageryPath == null)
            {
                planet.SetImagery(starter);
            }
        }

        // WMS 1.3.0 in EPSG:4326 takes the box latitude first: south, west, north, east.
        private static async Task<byte[]> FetchAsync(string layer, string box, int width, int height, CancellationToken cancellation)
        {
            var url = $"{GibsUrl}?SERVICE=WMS&REQUEST=GetMap&VERSION=1.3.0&LAYERS={Uri.EscapeDataString(layer)}&STYLES=" +
                $"&CRS=EPSG:4326&BBOX={box}&WIDTH={width}&HEIGHT={height}&FORMAT=image/jpeg";
            using var response = await Http.GetAsync(url, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
            // A bad layer name comes back as a 200 with an XML error in it.
            if (response.Content.Headers.ContentType?.MediaType != "image/jpeg")
            {
                var text = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 300));
                throw new InvalidDataException($"GIBS sent no picture for {layer}: {text}");
            }
            return bytes;
        }

        // Only the painting in use is kept; downloads stay, so restyling the borders fetches nothing.
        private static void RemoveOtherPictures(string keep, string download)
        {
            foreach (var file in Directory.EnumerateFiles(CacheFolder, "*.jpg"))
            {
                if (file == keep || file == download || file.EndsWith(".source.jpg", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Still open as a texture this run; it goes next time.
                }
            }
        }

        private static string Safe(string name)
        {
            foreach (var bad in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(bad, '_');
            }
            return name;
        }

        private static HttpClient CreateHttp()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            // The browser sends its own and refuses ours.
            if (!OperatingSystem.IsBrowser())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ManaEngine-PlanetImagery/1.0");
            }
            return http;
        }
    }
}
