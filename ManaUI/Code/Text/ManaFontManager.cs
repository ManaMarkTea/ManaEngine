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
        private readonly Dictionary<string, string?> _pathCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string File, int Size, int Scale), ManaFont> _fonts = new();
        private readonly List<string> _searchDirectories = new();

        public ManaFontManager(ManaGlyphAtlas? atlas = null, ManaImageAtlas? images = null)
        {
            Atlas = atlas ?? new ManaGlyphAtlas();
            Images = images ?? new ManaImageAtlas();
        }

        public ManaGlyphAtlas Atlas { get; }

        /// <summary>
        /// Where colour glyphs are packed. Handed to every font, because any of them might turn out to be
        /// an emoji font and the alternative is a font that silently cannot draw what it has.
        /// </summary>
        public ManaImageAtlas Images { get; }

        public IReadOnlyList<string> SearchDirectories => _searchDirectories;

        /// <summary>Family used when a request names nothing specific.</summary>
        public string DefaultFamily { get; set; } = "Roboto";

        public float DefaultPixelSize { get; set; } = 15.0f;

        /// <summary>
        /// Families consulted, in order, for codepoints the requested font does not have.
        /// <para>
        /// This is how the library gets arrows and symbols without shipping a font for them: Roboto covers
        /// Latin and nothing else, so a glyph like U+25B6 comes from whichever of these has it. Entries
        /// that cannot be resolved are skipped silently -- the list is a preference, and a machine without
        /// a given font should fall through rather than fail.
        /// </para>
        /// </summary>
        public List<string> FallbackFamilies { get; } = new()
        {
            // Windows. Segoe UI Symbol carries the geometric shapes and dingbats; Segoe UI itself covers a
            // good deal more than Roboto on its own.
            "seguisym",
            "segoeui",

            // macOS.
            "Apple Symbols",
            "Arial Unicode",
            "SFNSMono",

            // Common Linux packages.
            "DejaVuSans",
            "NotoSansSymbols2-Regular"
        };

        /// <summary>
        /// Colour emoji fonts, tried in order. Kept apart from <see cref="FallbackFamilies"/> because one
        /// of these cannot be loaded as an ordinary font at all: a CBDT emoji font has no glyf or loca
        /// table, so stb refuses the file outright and there is nothing for a ManaFont to wrap.
        /// </summary>
        public List<string> ColorEmojiFamilies { get; } = new()
        {
            "NotoColorEmoji",
            "seguiemj"
        };

        private ManaColorBitmapTable? _colorEmoji;
        private bool _colorEmojiResolved;

        /// <summary>
        /// The colour emoji source, loaded on first use. Null when none of the candidates is present or
        /// none of them uses embedded bitmaps -- a COLR/CPAL font resolves to a file but answers nothing
        /// here, which is the honest result rather than a crash.
        /// </summary>
        public ManaColorBitmapTable? ColorEmoji
        {
            get
            {
                if (_colorEmojiResolved)
                {
                    return _colorEmoji;
                }

                _colorEmojiResolved = true;

                foreach (var family in ColorEmojiFamilies)
                {
                    var path = ResolveCachedPath(family);
                    if (path == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (!_fileCache.TryGetValue(path, out var data))
                        {
                            data = File.ReadAllBytes(path);
                            _fileCache[path] = data;
                        }

                        var table = ManaColorBitmapTable.TryRead(data);
                        if (table != null)
                        {
                            _colorEmoji = table;
                            break;
                        }
                    }
                    catch (IOException)
                    {
                        // Unreadable file; try the next candidate.
                    }
                }

                return _colorEmoji;
            }
        }

        /// <summary>
        /// Emoji drawn by the platform, used when no emoji font is found -- the browser build sets the
        /// page's own emoji font here instead of downloading Noto.
        /// </summary>
        public static IManaColorGlyphSource? PlatformColorEmoji { get; set; }

        /// <summary>Where fonts get their colour emoji: an emoji font if there is one, else the platform's.</summary>
        public IManaColorGlyphSource? ColorEmojiSource => (IManaColorGlyphSource?)ColorEmoji ?? PlatformColorEmoji;

        /// <summary>
        /// Family names that resolve to a real file, for a font picker to offer.
        /// <para>
        /// Filtered to families that actually load: the search path includes the system font folder, which
        /// on any machine holds a pile of files stb cannot read, and offering one that fails when chosen is
        /// worse than not offering it.
        /// </para>
        /// </summary>
        public IReadOnlyList<string> AvailableFamilies(IEnumerable<string> candidates)
        {
            var available = new List<string>();

            foreach (var family in candidates)
            {
                if (ResolveCachedPath(family) == null)
                {
                    continue;
                }

                try
                {
                    Get(family, DefaultPixelSize);
                    available.Add(family);
                }
                catch (Exception)
                {
                    // Resolvable but unreadable -- a bitmap-only font, or one stb does not support.
                }
            }

            return available;
        }

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

                // A new directory can resolve a family that previously could not be found, so the
                // remembered answers no longer hold.
                _pathCache.Clear();
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
        public ManaFont Get(string family, float pixelSize, float renderScale = 1.0f) => Get(family, pixelSize, renderScale, allowSmallCaps: true);

        /// <summary>Faces with only capitals, whose lower case is drawn as small capitals (see <see cref="ManaFont.SmallCapsFont"/>).</summary>
        public HashSet<string> CapsOnlyFamilies { get; } = new(StringComparer.OrdinalIgnoreCase) { "KenneyFuture", "KenneyFutureNarrow" };

        /// <summary>How big a small capital is next to a full one.</summary>
        public float SmallCapsScale { get; set; } = 0.8f;

        private ManaFont Get(string family, float pixelSize, float renderScale, bool allowSmallCaps)
        {
            var rounded = Math.Max(1, (int)MathF.Round(pixelSize));
            var quantisedScale = Math.Max(1, (int)MathF.Round(MathF.Max(0.1f, renderScale) * 4.0f));

            var path = ResolveCachedPath(family)
                ?? ResolveCachedPath(DefaultFamily)
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
                quantisedScale / 4.0f)
            {
                ImageAtlas = Images,
                ColorEmoji = ColorEmojiSource
            };

            // Cached before the chain is built, because a fallback is itself a font from this manager and
            // resolving one re-enters here. Without this a cycle in the fallback list would recurse
            // forever; with it, the second visit finds the entry already present.
            _fonts[key] = font;
            AttachFallbacks(font, path, rounded, quantisedScale);
            if (allowSmallCaps && CapsOnlyFamilies.Contains(font.Name))
            {
                font.SmallCapsFont = Get(family, MathF.Max(1.0f, pixelSize * SmallCapsScale), renderScale, allowSmallCaps: false);
            }

            return font;
        }

        /// <summary>
        /// Resolves each fallback family at the same size and scale and hangs it off the font. Families
        /// that do not exist on this machine are skipped -- the list spans three operating systems, so most
        /// of it is expected to miss.
        /// </summary>
        private void AttachFallbacks(ManaFont font, string primaryPath, int size, int quantisedScale)
        {
            if (_resolvingFallbacks)
            {
                return;
            }

            _resolvingFallbacks = true;

            try
            {
                foreach (var family in FallbackFamilies)
                {
                    var path = ResolveCachedPath(family);
                    if (path == null || string.Equals(path, primaryPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        font.AddFallback(Get(path, size, quantisedScale / 4.0f));
                    }
                    catch (Exception)
                    {
                        // A font file that stb cannot read is not worth failing a UI over; the next
                        // fallback, or the tofu box, will do.
                    }
                }
            }
            finally
            {
                _resolvingFallbacks = false;
            }
        }

        private bool _resolvingFallbacks;

        /// <summary>
        /// Family name to file path, resolved once and remembered.
        /// <para>
        /// The lookup underneath walks the search directories recursively, and one of them is the system
        /// font folder. Callers ask for a font on every measure and every render of every text element, so
        /// without this cache a frame performs a full directory scan per label -- which measured at about
        /// three milliseconds a frame regardless of how much UI was on screen. Misses are cached too, since
        /// a family that does not exist is just as expensive to look for the second time.
        /// </para>
        /// </summary>
        private string? ResolveCachedPath(string family)
        {
            if (string.IsNullOrWhiteSpace(family))
            {
                return null;
            }

            if (_pathCache.TryGetValue(family, out var cached))
            {
                return cached;
            }

            var resolved = ResolvePath(family);
            _pathCache[family] = resolved;
            return resolved;
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
