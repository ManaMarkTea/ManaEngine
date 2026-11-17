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
    /// once, with Natural Earth's coastlines, country and state borders painted into it, and kept as one JPEG in
    /// Library/PlanetImagery -- about 3 MB at 8192 wide. The patches wear it through their lat/long coordinates,
    /// so it follows the terrain at any detail. In the browser the picture is 4096 wide (memory), kept for the session only,
    /// and detail tiles get the 50 m borders, since the 10 m files are 20 MB to download.
    /// </summary>
    public sealed class PlanetImageryComponent : ManaComponent
    {
        public const string ScriptType = "PlanetImagery";

        private const string GibsUrl = "https://gibs.earthdata.nasa.gov/wms/epsg4326/best/wms.cgi";

        // Bumped when the painting changes, so every saved picture is made again.
        private const int PaintVersion = 2;

        /// <summary>Where downloads and finished pictures are kept between runs.</summary>
        public static string CacheFolder { get; set; } = Path.Combine(Environment.CurrentDirectory, "Library", "PlanetImagery");

        public bool Enabled { get; set; } = true;

        /// <summary>A GIBS layer with a global, undated picture: BlueMarble_ShadedRelief_Bathymetry, BlueMarble_NextGeneration, BlueMarble_ShadedRelief.</summary>
        public string Layer { get; set; } = "BlueMarble_ShadedRelief_Bathymetry";

        /// <summary>Picture width in pixels (half as tall); 8192 is about 5 km a pixel and the most a GPU texture is sure to take.</summary>
        public int Width { get; set; } = 8192;

        /// <summary>Coastlines and country borders.</summary>
        public bool Borders { get; set; } = true;

        /// <summary>States and provinces, where Natural Earth has them at this scale (the larger countries).</summary>
        public bool States { get; set; } = true;

        public Vector4 BorderColor { get; set; } = new(1.0f, 1.0f, 1.0f, 0.9f);

        public Vector4 StateColor { get; set; } = new(1.0f, 1.0f, 1.0f, 0.45f);

        /// <summary>Line width on the ground, in kilometres.</summary>
        public float BorderWidth { get; set; } = 9.0f;

        public float StateWidth { get; set; } = 6.0f;

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
        private string? _builtKey;
        private string? _readyPath;
        private PlanetDetailImagery? _detail;
        private string _lastDetailCounts = "";

        // The 10 m Natural Earth lines, read once (on the first tile's worker) for every tile after.
        private static readonly Lazy<List<IReadOnlyList<(double Longitude, double Latitude)>>> DetailCountries =
            new(() => ReadRings(DetailLines("admin_0_countries")).ToList());
        private static readonly Lazy<List<IReadOnlyList<(double Longitude, double Latitude)>>> DetailStates =
            new(() => ReadRings(DetailLines("admin_1_states_provinces")).ToList());

        private static string DetailLines(string name)
        {
            if (OperatingSystem.IsBrowser())
            {
                return "ne_50m_" + name;
            }
            return "ne_10m_" + name;
        }

        public PlanetImageryComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Planet Imagery";
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();
            if (Owner is not PlanetNode planet)
            {
                return;
            }

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

        // One sharper picture: the layer for the tile's box from GIBS, borders from the 10 m lines drawn a couple of
        // pixels wide (the 50 m ones would miss the coast by kilometres at this scale), saved, decoded and mip-mapped here.
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
                // The browser decodes only asynchronously, ahead of the synchronous load.
                await ManaImageCodec.PrepareAsync(bytes).ConfigureAwait(false);
                var image = ManaImageCodec.Load(bytes);
                // Ground per pixel, north to south (pixels are square on the ground).
                var kilometresPerPixel = (float)((area.MaxLatitude - area.MinLatitude) / 180.0 * 20037.5 / spec.Height);
                if (States)
                {
                    PlanetBorderPainter.Paint(image, DetailStates.Value, MathF.Min(StateWidth, 1.1f * kilometresPerPixel), StateColor, area);
                }
                if (Borders)
                {
                    PlanetBorderPainter.Paint(image, DetailCountries.Value, MathF.Min(BorderWidth, 1.5f * kilometresPerPixel), BorderColor, area);
                }

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
            var borders = "";
            if (Borders)
            {
                borders += $"-b{Hex(BorderColor)}w{BorderWidth:0.#}";
            }
            if (States)
            {
                borders += $"-s{Hex(StateColor)}w{StateWidth:0.#}";
            }
            return $"{Layer}-{PictureWidth}{borders}-v{PaintVersion}";
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

                Status = "Painting borders";
                await ManaImageCodec.PrepareAsync(source).ConfigureAwait(false);
                var image = ManaImageCodec.Load(source);
                if (States)
                {
                    PlanetBorderPainter.Paint(image, ReadRings("ne_50m_admin_1_states_provinces"), StateWidth, StateColor);
                }
                if (Borders)
                {
                    PlanetBorderPainter.Paint(image, ReadRings("ne_50m_admin_0_countries"), BorderWidth, BorderColor);
                }

                ManaImageCodec.SavePng(image, finished, jpegQuality: 88);
                ManaTexturePrefetch.Store(finished, image);
                RemoveOtherPictures(finished, download);
                Status = $"{Layer}, {width} x {width / 2}, borders painted";
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

        private static IEnumerable<IReadOnlyList<(double Longitude, double Latitude)>> ReadRings(string name)
        {
            var zip = ManaTexture.ResolveTexturePath(Path.Combine("Assets", "GeoData", name + ".zip"));
            if (!File.Exists(zip))
            {
                Console.WriteLine($"[Imagery] {name}.zip is missing; drawn without it.");
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

        private static string Hex(Vector4 color)
        {
            static int Byte(float value) => (int)MathF.Round(Math.Clamp(value, 0.0f, 1.0f) * 255.0f);
            return $"{Byte(color.X):x2}{Byte(color.Y):x2}{Byte(color.Z):x2}{Byte(color.W):x2}";
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
