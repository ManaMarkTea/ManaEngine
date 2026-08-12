namespace ManaUI
{
    /// <summary>
    /// Loads and caches fonts, and owns the atlas they all share.
    /// <para>
    /// Search directories are supplied by the host application rather than hard-coded, so ManaUI stays
    /// independent of any particular project's asset layout.
    /// </para>
    /// </summary>
    public sealed class ManaFontManager : IDisposable
    {
        private static readonly string[] Extensions = { ".ttf", ".otf" };

        private readonly Dictionary<string, byte[]> _fileCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string File, int Size, int Scale), ManaFont> _fonts = new();
        private readonly List<string> _searchDirectories = new();

        public ManaFontManager(ManaGlyphAtlas? atlas = null)
        {
            Atlas = atlas ?? new ManaGlyphAtlas();
        }

        public ManaGlyphAtlas Atlas { get; }

        public IReadOnlyList<string> SearchDirectories => _searchDirectories;

        /// <summary>Family used when a request names nothing specific.</summary>
        public string DefaultFamily { get; set; } = "Roboto";

        public float DefaultPixelSize { get; set; } = 15.0f;

        /// <summary>Directories are searched in the order added; later additions are lower priority.</summary>
        public void AddSearchDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var full = Path.GetFullPath(directory);
            if (Directory.Exists(full) && !_searchDirectories.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                _searchDirectories.Add(full);
            }
        }

        public ManaFont GetDefault()
        {
            return Get(DefaultFamily, DefaultPixelSize);
        }

        /// <summary>
        /// Resolves a family (optionally with a style suffix, e.g. "Roboto-Medium") at a logical pixel size,
        /// rasterised for <paramref name="renderScale"/> device pixels per logical pixel.
        /// <para>
        /// Both size and scale are quantised before they reach the cache key -- the rasteriser cannot
        /// distinguish finer steps anyway, and without it a DPI slider would fill the atlas with dozens of
        /// near-identical glyph sets.
        /// </para>
        /// </summary>
        public ManaFont Get(string family, float pixelSize, float renderScale = 1.0f)
        {
            var rounded = Math.Max(1, (int)MathF.Round(pixelSize));
            var quantisedScale = Math.Max(1, (int)MathF.Round(MathF.Max(0.1f, renderScale) * 4.0f));

            var path = ResolvePath(family)
                ?? ResolvePath(DefaultFamily)
                ?? throw new FileNotFoundException(
                    $"No font file for '{family}' or fallback '{DefaultFamily}' in: {string.Join("; ", _searchDirectories)}");

            var key = (path, rounded, quantisedScale);
            if (_fonts.TryGetValue(key, out var existing))
            {
                return existing;
            }

            if (!_fileCache.TryGetValue(path, out var data))
            {
                data = File.ReadAllBytes(path);
                _fileCache[path] = data;
            }

            var font = new ManaFont(
                Path.GetFileNameWithoutExtension(path),
                data,
                rounded,
                Atlas,
                quantisedScale / 4.0f);
            _fonts[key] = font;
            return font;
        }

        /// <summary>Finds a font file for a family name, trying the bare name then a "-Regular" suffix.</summary>
        private string? ResolvePath(string family)
        {
            if (string.IsNullOrWhiteSpace(family))
            {
                return null;
            }

            // An explicit path wins over any search.
            if (Path.IsPathRooted(family) && File.Exists(family))
            {
                return family;
            }

            foreach (var directory in _searchDirectories)
            {
                foreach (var candidate in Candidates(family))
                {
                    foreach (var extension in Extensions)
                    {
                        var direct = Path.Combine(directory, candidate + extension);
                        if (File.Exists(direct))
                        {
                            return direct;
                        }

                        // Shipped families usually sit in a folder of their own (Fonts/Roboto/Roboto-Regular.ttf).
                        var nested = Directory.EnumerateFiles(directory, candidate + extension, SearchOption.AllDirectories)
                            .FirstOrDefault();
                        if (nested != null)
                        {
                            return nested;
                        }
                    }
                }
            }

            return null;
        }

        private static IEnumerable<string> Candidates(string family)
        {
            yield return family;

            if (!family.Contains('-'))
            {
                yield return family + "-Regular";
            }
        }

        public void Dispose()
        {
            foreach (var font in _fonts.Values)
            {
                font.Dispose();
            }

            _fonts.Clear();
            _fileCache.Clear();
        }
    }
}
