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
    /// so it follows the terrain at any detail. Desktop only (the cache needs a disk).
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

        [JsonIgnore]
        public string Status { get; private set; } = "Starting";

        private static readonly HttpClient Http = CreateHttp();
        private readonly CancellationTokenSource _cancel = new();
        private Task<string?>? _work;
        private string? _builtKey;
        private string? _readyPath;

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
                if (OperatingSystem.IsBrowser())
                {
                    Status = "Desktop only";
                    return;
                }
                _work = Task.Run(() => BuildAsync(key, _cancel.Token));
            }

            if (_readyPath != null && planet.ImageryPath != _readyPath)
            {
                planet.SetImagery(_readyPath);
            }
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            _cancel.Cancel();
            if (Owner is PlanetNode planet && planet.ImageryPath != null && planet.ImageryPath == _readyPath)
            {
                planet.SetImagery(null);
            }
        }

        private int PictureWidth => Math.Clamp(Width, 1024, 8192) / 2 * 2;

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
                    Status = $"{Layer} from the saved picture";
                    return finished;
                }

                var width = PictureWidth;
                var download = Path.Combine(CacheFolder, Safe($"{Layer}-{width}") + ".source.jpg");
                if (!File.Exists(download))
                {
                    Status = $"Fetching {Layer} ({width} x {width / 2}) from NASA GIBS";
                    Console.WriteLine($"[Imagery] {Status}");
                    var bytes = await FetchAsync(Layer, width, cancellation).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(download, bytes, cancellation).ConfigureAwait(false);
                }

                Status = "Painting borders";
                var image = ManaImageCodec.Load(download);
                if (States)
                {
                    PlanetBorderPainter.Paint(image, ReadRings("ne_50m_admin_1_states_provinces"), StateWidth, StateColor);
                }
                if (Borders)
                {
                    PlanetBorderPainter.Paint(image, ReadRings("ne_50m_admin_0_countries"), BorderWidth, BorderColor);
                }

                ManaImageCodec.SavePng(image, finished, jpegQuality: 88);
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

        private static async Task<byte[]> FetchAsync(string layer, int width, CancellationToken cancellation)
        {
            var url = $"{GibsUrl}?SERVICE=WMS&REQUEST=GetMap&VERSION=1.3.0&LAYERS={Uri.EscapeDataString(layer)}&STYLES=" +
                $"&CRS=EPSG:4326&BBOX=-90,-180,90,180&WIDTH={width}&HEIGHT={width / 2}&FORMAT=image/jpeg";
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
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ManaEngine-PlanetImagery/1.0");
            return http;
        }
    }
}
