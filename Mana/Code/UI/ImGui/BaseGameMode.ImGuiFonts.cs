using ImGuiNET;
using ManaCore.Graphics;
using System.Runtime.InteropServices;

namespace Mana.Code
{
    public abstract partial class BaseGameMode
    {
        public enum ImGuiFontPreset
        {
            Default,
            SegoeUI,
            SegoeSemibold,
            Bahnschrift,
            BarlowCondensed,
            Roboto,
            RobotoSemiCondensed,
            RobotoCondensed,
            Calibri,
            Candara,
            Corbel,
            Verdana,
            Tahoma,
            Trebuchet,
            SegoeUIMono,
            Consolas,
            CascadiaMono,
            JetBrainsMono,
            FiraCode,
            SourceCodePro,
            CourierNew
        }

        public enum ImGuiMonoSizeTier
        {
            Small,
            Regular
        }

        public ImGuiFontPreset CurrentImGuiFontPreset { get; protected set; } = ImGuiFontPreset.Default;
        public ImGuiMonoSizeTier CurrentMonoSizeTier { get; private set; } = ImGuiMonoSizeTier.Small;
        public ImFontPtr BarlowCondensedImGuiFont { get; private set; }
        public bool EmojiFallbackEnabled { get; private set; } = true;
        private ImGuiFontPreset? _pendingImGuiFontPreset;
        private HashSet<ImGuiFontPreset>? _availableImGuiFontPresets;
        private ImVector _emojiGlyphRanges;
        private bool _emojiGlyphRangesBuilt;
        public void ApplyImGuiFontPreset(ImGuiFontPreset preset)
        {
            var io = ImGui.GetIO();
            io.Fonts.Clear();
            bool loaded = false;
            BarlowCondensedImGuiFont = default;
            var fontDirectories = GetFontDirectories();
            var (fontSize, candidates) = GetFontCandidatesForPreset(preset);

            if (IsMonospacePreset(preset) && CurrentMonoSizeTier == ImGuiMonoSizeTier.Small)
            {
                fontSize -= 1.0f;
            }

            // Every size below is a real rasterization size in pixels, so DPI has to be folded in here
            // rather than stretched on afterwards -- see SyncImGuiUiScale on why not io.FontGlobalScale.
            // Rounded to a whole pixel because hinting lands better on integer sizes.
            fontSize = ScaleFontSize(fontSize);

            switch (preset)
            {
                case ImGuiFontPreset.Default:
                    io.Fonts.AddFontDefault();
                    loaded = true;
                    break;
                default:
                    loaded = !TryLoadImGuiFontCandidates(io, fontDirectories, fontSize, candidates)
                        .Equals(default(ImFontPtr));
                    break;
            }

            if (!loaded)
            {
                io.Fonts.AddFontDefault();
                CurrentImGuiFontPreset = ImGuiFontPreset.Default;
            }
            else
            {
                CurrentImGuiFontPreset = preset;
            }

            if (EmojiFallbackEnabled)
            {
                TryMergeEmojiFallbackFont(io, fontDirectories, fontSize);
            }

            BarlowCondensedImGuiFont = TryLoadImGuiFontCandidates(
                io,
                fontDirectories,
                ScaleFontSize(17.0f),
                "BarlowCondensed-Regular.ttf");

            // Build the atlas in-place; avoid invoking private ImGuiController internals via reflection.
            io.Fonts.Build();
            TryRecreateImGuiFontDeviceTexture();
        }

        private void TryRecreateImGuiFontDeviceTexture()
        {
            _ImGui?.RecreateFontDeviceTexture();
        }

        public void RequestImGuiFontPreset(ImGuiFontPreset preset)
        {
            _pendingImGuiFontPreset = preset;
        }

        /// <summary>
        /// Queues a font-atlas rebuild without changing which preset is selected -- for when the SIZE
        /// inputs changed rather than the typeface (a DPI change, see SyncImGuiUiScale).
        ///
        /// Deliberately re-requests any already-pending preset in preference to the current one: startup
        /// queues its preferred preset before the first frame runs, and CurrentImGuiFontPreset doesn't
        /// catch up until ApplyImGuiFontPreset actually runs, so reading the current value here would
        /// quietly discard that queued choice and fall back to the default font.
        /// </summary>
        public void RequestImGuiFontRebuild()
        {
            _pendingImGuiFontPreset ??= CurrentImGuiFontPreset;
        }

        public void SetImGuiMonoSizeTier(ImGuiMonoSizeTier tier)
        {
            if (CurrentMonoSizeTier == tier)
            {
                return;
            }

            CurrentMonoSizeTier = tier;

            if (IsMonospacePreset(CurrentImGuiFontPreset))
            {
                RequestImGuiFontPreset(CurrentImGuiFontPreset);
            }
        }

        public void SetEmojiFallbackEnabled(bool enabled)
        {
            if (EmojiFallbackEnabled == enabled)
            {
                return;
            }

            EmojiFallbackEnabled = enabled;
            RequestImGuiFontPreset(CurrentImGuiFontPreset);
        }

        public static bool IsMonospacePreset(ImGuiFontPreset preset)
        {
            switch (preset)
            {
                case ImGuiFontPreset.SegoeUIMono:
                case ImGuiFontPreset.Consolas:
                case ImGuiFontPreset.CascadiaMono:
                case ImGuiFontPreset.JetBrainsMono:
                case ImGuiFontPreset.FiraCode:
                case ImGuiFontPreset.SourceCodePro:
                case ImGuiFontPreset.CourierNew:
                    return true;
                default:
                    return false;
            }
        }

        public IReadOnlyCollection<ImGuiFontPreset> GetAvailableImGuiFontPresets()
        {
            if (_availableImGuiFontPresets != null)
            {
                return _availableImGuiFontPresets;
            }

            _availableImGuiFontPresets = new HashSet<ImGuiFontPreset>
            {
                ImGuiFontPreset.Default
            };

            var fontDirectories = GetFontDirectories();
            foreach (ImGuiFontPreset preset in Enum.GetValues(typeof(ImGuiFontPreset)))
            {
                if (preset == ImGuiFontPreset.Default)
                {
                    continue;
                }

                var (_, candidates) = GetFontCandidatesForPreset(preset);
                foreach (var fontsDir in fontDirectories)
                {
                    if (string.IsNullOrEmpty(fontsDir) || !Directory.Exists(fontsDir))
                    {
                        continue;
                    }

                    foreach (var fileName in candidates)
                    {
                        var path = Path.Combine(fontsDir, fileName);
                        if (!File.Exists(path))
                        {
                            continue;
                        }

                        if (!IsSupportedFontFile(path))
                        {
                            continue;
                        }

                        _availableImGuiFontPresets.Add(preset);
                        goto NextPreset;
                    }
                }

            NextPreset:
                ;
            }

            return _availableImGuiFontPresets;
        }

        private static ImFontPtr TryLoadImGuiFontCandidates(ImGuiIOPtr io, IEnumerable<string> fontDirectories, float sizePixels, params string[] fileNames)
        {
            foreach (var fontsDir in fontDirectories)
            {
                if (string.IsNullOrEmpty(fontsDir) || !Directory.Exists(fontsDir))
                {
                    continue;
                }

                foreach (var fileName in fileNames)
                {
                    var path = Path.Combine(fontsDir, fileName);
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    if (!IsSupportedFontFile(path))
                    {
                        continue;
                    }

                    var font = io.Fonts.AddFontFromFileTTF(path, sizePixels);
                    if (!font.Equals(default(ImFontPtr)))
                    {
                        return font;
                    }
                }
            }

            return default;
        }

        private unsafe bool TryMergeEmojiFallbackFont(ImGuiIOPtr io, IEnumerable<string> fontDirectories, float baseFontSize)
        {
            BuildEmojiGlyphRangesIfNeeded(io);
            if (_emojiGlyphRanges.Data == IntPtr.Zero)
            {
                return false;
            }

            var emojiCandidates = GetEmojiFontCandidates();
            // baseFontSize arrives already DPI-scaled (see ApplyImGuiFontPreset), so only the 13px floor
            // needs scaling here -- it's a minimum legible size, which is a physical constraint.
            var emojiFontSize = MathF.Max(ScaleFontSize(13.0f), baseFontSize - 0.5f);

            foreach (var fontsDir in fontDirectories)
            {
                if (string.IsNullOrEmpty(fontsDir) || !Directory.Exists(fontsDir))
                {
                    continue;
                }

                foreach (var fileName in emojiCandidates)
                {
                    var path = Path.Combine(fontsDir, fileName);
                    if (!File.Exists(path) || !IsSupportedFontFile(path))
                    {
                        continue;
                    }

                    var cfg = ImGuiNative.ImFontConfig_ImFontConfig();
                    if (cfg == null)
                    {
                        return false;
                    }

                    try
                    {
                        cfg->MergeMode = 1;
                        cfg->PixelSnapH = 1;
                        cfg->GlyphMinAdvanceX = emojiFontSize;

                        var configPtr = new ImFontConfigPtr(cfg);
                        var merged = io.Fonts.AddFontFromFileTTF(path, emojiFontSize, configPtr, _emojiGlyphRanges.Data);
                        if (!merged.Equals(default(ImFontPtr)))
                        {
                            return true;
                        }
                    }
                    finally
                    {
                        ImGuiNative.ImFontConfig_destroy(cfg);
                    }
                }
            }

            return false;
        }

        private unsafe void BuildEmojiGlyphRangesIfNeeded(ImGuiIOPtr io)
        {
            if (_emojiGlyphRangesBuilt)
            {
                return;
            }

            var builderNative = ImGuiNative.ImFontGlyphRangesBuilder_ImFontGlyphRangesBuilder();
            if (builderNative == null)
            {
                _emojiGlyphRangesBuilt = true;
                return;
            }

            var builder = new ImFontGlyphRangesBuilderPtr(builderNative);
            builder.AddRanges(io.Fonts.GetGlyphRangesDefault());

            // Common symbols and frequently used emoji-like characters.
            builder.AddText("😀😁😂🤣😃😄😅😆😉😊🙂🙃😍🤔👍🔥✨🎯✅❌⚠❤️💙🧠🚀");

            builder.BuildRanges(out _emojiGlyphRanges);
            builder.Destroy();
            _emojiGlyphRangesBuilt = true;
        }

        private static string[] GetEmojiFontCandidates()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new[] { "seguiemj.ttf", "Segoe UI Emoji.ttf", "seguisym.ttf" };
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new[] { "Apple Color Emoji.ttf", "AppleColorEmoji.ttf", "NotoEmoji-Regular.ttf" };
            }

            return new[] { "NotoColorEmoji.ttf", "NotoEmoji-Regular.ttf", "EmojiOneColor-SVGinOT.ttf", "Symbola.ttf" };
        }

        private static bool IsSupportedFontFile(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> GetFontDirectories()
        {
            var dirs = new List<string>();
            var bundledFontsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");

            if (Directory.Exists(bundledFontsDirectory))
            {
                dirs.Add(bundledFontsDirectory);
                dirs.AddRange(Directory.EnumerateDirectories(
                    bundledFontsDirectory,
                    "*",
                    SearchOption.AllDirectories));
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                dirs.Add(Environment.GetFolderPath(Environment.SpecialFolder.Fonts));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                dirs.Add("/System/Library/Fonts");
                dirs.Add("/System/Library/Fonts/Supplemental");
                dirs.Add("/Library/Fonts");
            }
            else
            {
                dirs.Add("/usr/share/fonts/truetype");
                dirs.Add("/usr/share/fonts/truetype/dejavu");
                dirs.Add("/usr/share/fonts/truetype/liberation");
                dirs.Add("/usr/share/fonts/opentype");
            }

            return dirs.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static (float Size, string[] Candidates) GetFontCandidatesForPreset(ImGuiFontPreset preset)
        {
            switch (preset)
            {
                case ImGuiFontPreset.SegoeUI:
                    return (17.0f, new[] { "segoeui.ttf", "SFNS.ttf", "Helvetica.ttc", "Arial.ttf", "arial.ttf", "DejaVuSans.ttf" });
                case ImGuiFontPreset.SegoeSemibold:
                    return (17.0f, new[] { "seguisb.ttf", "SFNS.ttf", "Helvetica.ttc", "Arial Bold.ttf", "arialbd.ttf", "DejaVuSans-Bold.ttf" });
                case ImGuiFontPreset.Bahnschrift:
                    return (17.0f, new[] { "bahnschrift.ttf", "Avenir Next.ttc", "Helvetica.ttc", "Arial.ttf", "DejaVuSans.ttf" });
                case ImGuiFontPreset.BarlowCondensed:
                    return (17.0f, new[] { "BarlowCondensed-Regular.ttf" });
                case ImGuiFontPreset.Roboto:
                    return (17.0f, new[] { "Roboto-Regular.ttf" });
                case ImGuiFontPreset.RobotoSemiCondensed:
                    return (17.0f, new[] { "Roboto_SemiCondensed-Regular.ttf" });
                case ImGuiFontPreset.RobotoCondensed:
                    return (17.0f, new[] { "Roboto_Condensed-Regular.ttf" });
                case ImGuiFontPreset.Calibri:
                    return (17.0f, new[] { "calibri.ttf", "SFNS.ttf", "Helvetica.ttc", "Arial.ttf", "DejaVuSans.ttf" });
                case ImGuiFontPreset.Candara:
                    return (17.0f, new[] { "candara.ttf", "Avenir Next.ttc", "Helvetica.ttc", "Arial.ttf", "DejaVuSans.ttf" });
                case ImGuiFontPreset.Corbel:
                    return (17.0f, new[] { "corbel.ttf", "Avenir Next.ttc", "Helvetica.ttc", "Arial.ttf", "DejaVuSans.ttf" });
                case ImGuiFontPreset.Verdana:
                    return (16.5f, new[] { "verdana.ttf", "Geneva.ttf", "Helvetica.ttc", "Arial.ttf", "DejaVuSans.ttf" });
                case ImGuiFontPreset.Tahoma:
                    return (16.5f, new[] { "tahoma.ttf", "Helvetica.ttc", "Arial.ttf", "DejaVuSans.ttf" });
                case ImGuiFontPreset.Trebuchet:
                    return (17.0f, new[] { "trebuc.ttf", "Trebuchet MS.ttf", "Helvetica.ttc", "Arial.ttf", "DejaVuSans.ttf" });
                case ImGuiFontPreset.SegoeUIMono:
                    return (15.5f, new[] { "SegoeUIMono-Regular.ttf", "SegoeUIMono.ttf", "CascadiaMono.ttf", "consola.ttf", "DejaVuSansMono.ttf" });
                case ImGuiFontPreset.Consolas:
                    return (15.5f, new[] { "consola.ttf", "Menlo.ttc", "Monaco.ttf", "DejaVuSansMono.ttf", "LiberationMono-Regular.ttf" });
                case ImGuiFontPreset.CascadiaMono:
                    return (15.5f, new[] { "CascadiaMono.ttf", "CascadiaCode.ttf", "Menlo.ttc", "Monaco.ttf", "DejaVuSansMono.ttf", "LiberationMono-Regular.ttf" });
                case ImGuiFontPreset.JetBrainsMono:
                    return (15.0f, new[] { "JetBrainsMono-Regular.ttf", "JetBrainsMono.ttf", "Menlo.ttc", "Monaco.ttf", "DejaVuSansMono.ttf", "LiberationMono-Regular.ttf" });
                case ImGuiFontPreset.FiraCode:
                    return (15.0f, new[] { "FiraCode-Regular.ttf", "FiraCode.ttf", "Menlo.ttc", "Monaco.ttf", "DejaVuSansMono.ttf", "LiberationMono-Regular.ttf" });
                case ImGuiFontPreset.SourceCodePro:
                    return (15.0f, new[] { "SourceCodePro-Regular.ttf", "SourceCodePro.ttf", "Menlo.ttc", "Monaco.ttf", "DejaVuSansMono.ttf", "LiberationMono-Regular.ttf" });
                case ImGuiFontPreset.CourierNew:
                    return (15.5f, new[] { "cour.ttf", "Courier New.ttf", "Menlo.ttc", "Monaco.ttf", "DejaVuSansMono.ttf", "LiberationMono-Regular.ttf" });
                default:
                    return (17.0f, new[] { "segoeui.ttf", "SFNS.ttf", "Helvetica.ttc", "Arial.ttf", "arial.ttf", "DejaVuSans.ttf" });
            }
        }

        private static float ScaleFontSize(float sizePixels)
        {
            return MathF.Round(sizePixels * ManaCore.Graphics.ManaImGuiSettingsStorage.CurrentUiScale);
        }

        public void ProcessPendingFontPreset()
        {
            if (_pendingImGuiFontPreset.HasValue)
            {
                ApplyImGuiFontPreset(_pendingImGuiFontPreset.Value);
                _pendingImGuiFontPreset = null;
            }
        }

    }
}
