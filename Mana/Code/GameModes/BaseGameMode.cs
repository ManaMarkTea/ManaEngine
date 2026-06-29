using ImGuiNET;
using Mana.Code.Graphics;
using ManaCore.Graphics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Mana.Code
{
    public abstract class BaseGameMode
    {
        public enum ImGuiFontPreset
        {
            Default,
            SegoeUI,
            SegoeSemibold,
            Bahnschrift,
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

        public static uint UtilityDockId { get; set; }
        public static uint OutlinerDockId { get; set; }
        public ImGuiFontPreset CurrentImGuiFontPreset { get; protected set; } = ImGuiFontPreset.Default;
        public ImGuiMonoSizeTier CurrentMonoSizeTier { get; private set; } = ImGuiMonoSizeTier.Small;
        public bool EmojiFallbackEnabled { get; private set; } = true;
        private ImGuiFontPreset? _pendingImGuiFontPreset;
        private HashSet<ImGuiFontPreset>? _availableImGuiFontPresets;
        private ImVector _emojiGlyphRanges;
        private bool _emojiGlyphRangesBuilt;

        private bool _dockLayoutInitialized;
        private bool _dockLayoutResetRequested;

        public long UpdateFrame = 0;
        public long RenderFrame = 0;
        public ManaGraphicsDiagnosticsProvider? GraphicsDiagnosticsProvider { get; protected set; }

        public ManaCore.Graphics.IManaImGuiController _ImGui = null!;
        public IWindow _Window = null!;
        public GL GL = null!;
        public GameModeGraphicsLevelDebugger _GlDebugger = null!;

        public ManaContext Context = null!;

        private static readonly GraphicsAPI RequestedGraphicsApi = GraphicsAPI.Default;


        public ManaGraphicsBackendKind GraphicsBackend { get; }

        public BaseGameMode(ManaGraphicsBackendKind graphicsBackend = ManaGraphicsBackendKind.OpenGL)
        {
            GraphicsBackend = graphicsBackend;
        }

        public virtual void LoadForOpenGlBackend(IWindow window, GL gl, IInputContext input)
        {
            _Window = window;
            GL = gl;

            LogLoadDiagnostics(input);

            var world = new ManaWorld();
            Context = new ManaContext(_Window, GL, input, world);
            Context.GameMode = this;

            world.Initialize(Context);

            GL.Enable(EnableCap.DepthTest);

            _GlDebugger = new GameModeGraphicsLevelDebugger(GL);
            GraphicsDiagnosticsProvider = new ManaOpenGlDiagnosticsProvider(Context);
            GraphicsDiagnosticsProvider.CaptureInitializationSnapshot();
            _ImGui = new Mana.Code.Graphics.ManaOpenGlImGuiController(GL, _Window, input);
            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            ConfigImStyle();
            CurrentImGuiFontPreset = ImGuiFontPreset.Default;

            if (GetAvailableImGuiFontPresets().Contains(ImGuiFontPreset.SegoeUIMono))
            {
                RequestImGuiFontPreset(ImGuiFontPreset.SegoeUIMono);
            }

            // Initialize the physics engine
            Context.Physics = new ManaPhysics(Context);
            Load();
        }

        public virtual void LoadForGraphicsBackend(IWindow window, IInputContext input, IManaGraphicsDevice graphicsDevice, bool initializeWorldRenderer = false)
        {
            var world = new ManaWorld();
            Context = new ManaContext(window, input, world, graphicsDevice);
            Context.GameMode = this;

            world.Initialize(Context, initializeWorldRenderer);

            Context.Physics = new ManaPhysics(Context);
            LoadContent();
        }

        public virtual void Load()
        {
            LoadContent();
        }

        protected virtual void LoadContent()
        {
        }

        private void LogLoadDiagnostics(IInputContext input)
        {
            var majorVersion = GL.GetInteger(GLEnum.MajorVersion);
            var minorVersion = GL.GetInteger(GLEnum.MinorVersion);
            var requestedApi = DescribeGraphicsApi(RequestedGraphicsApi);
            var windowApi = DescribeGraphicsApi(_Window.API);
            var debugCallbacks = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "disabled on macOS before OpenGL 4.3"
                : "enabled after startup diagnostics";

            WriteStartupLog("Mana: startup diagnostics");
            WriteStartupLog($"Mana: Requested API: {requestedApi}");
            WriteStartupLog($"Mana: Window API: {windowApi}");
            WriteStartupLog(
                $"Mana: OpenGL {majorVersion}.{minorVersion} | Version='{GetGlString(StringName.Version)}' | GLSL='{GetGlString(StringName.ShadingLanguageVersion)}'");
            WriteStartupLog($"Mana: GPU: {GetGlString(StringName.Renderer)}");
            WriteStartupLog($"Mana: Vendor: {GetGlString(StringName.Vendor)}");
            WriteStartupLog(
                $"Mana: Window: size={_Window.Size.X}x{_Window.Size.Y}, framebuffer={_Window.FramebufferSize.X}x{_Window.FramebufferSize.Y}, " +
                $"fps={_Window.FramesPerSecond:0.##}, updateHz={_Window.UpdatesPerSecond:0.##}, vsync={_Window.VSync}, " +
                $"depth={_Window.PreferredDepthBufferBits?.ToString() ?? "default"}, stencil={_Window.PreferredStencilBufferBits?.ToString() ?? "default"}");
            WriteStartupLog(
                $"Mana: Input: keyboards={input.Keyboards.Count}, mice={input.Mice.Count}, gamepads={input.Gamepads.Count}, joysticks={input.Joysticks.Count}, other={input.OtherDevices.Count}");
            WriteStartupLog(
                $"Mana: GL limits: maxTexture={GL.GetInteger(GLEnum.MaxTextureSize)}, maxCubeMap={GL.GetInteger(GLEnum.MaxCubeMapTextureSize)}, " +
                $"maxTextureUnits={GL.GetInteger(GLEnum.MaxCombinedTextureImageUnits)}, maxVertexAttribs={GL.GetInteger(GLEnum.MaxVertexAttribs)}, " +
                $"maxDrawBuffers={GL.GetInteger(GLEnum.MaxDrawBuffers)}, maxColorAttachments={GL.GetInteger(GLEnum.MaxColorAttachments)}, " +
                $"maxSamples={GL.GetInteger(GLEnum.MaxSamples)}, maxRenderbuffer={GL.GetInteger(GLEnum.MaxRenderbufferSize)}");
            WriteStartupLog($"Mana: Runtime: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");
            WriteStartupLog($"Mana: GL debug callback: {debugCallbacks}");
            WriteStartupLog("Mana: GL 3.3 note: Silk.NET GraphicsAPI.Default requests OpenGL 3.3 Core/ForwardCompatible, matching the project's #version 330 shaders.");
        }

        private string GetGlString(StringName name)
        {
            try
            {
                return GL.GetStringS(name);
            }
            catch (Exception ex)
            {
                return $"unavailable ({ex.GetType().Name})";
            }
        }

        private static string DescribeGraphicsApi(GraphicsAPI api)
        {
            return $"{api.API} {api.Version.MajorVersion}.{api.Version.MinorVersion} {api.Profile}/{api.Flags}";
        }

        private static void WriteStartupLog(string message)
        {
            Console.WriteLine(message);
            Trace.WriteLine(message);
        }

        public void ApplyImGuiFontPreset(ImGuiFontPreset preset)
        {
            var io = ImGui.GetIO();
            io.Fonts.Clear();
            bool loaded = false;
            var fontDirectories = GetSystemFontDirectories();
            var (fontSize, candidates) = GetFontCandidatesForPreset(preset);

            if (IsMonospacePreset(preset) && CurrentMonoSizeTier == ImGuiMonoSizeTier.Small)
            {
                fontSize -= 1.0f;
            }

            switch (preset)
            {
                case ImGuiFontPreset.Default:
                    io.Fonts.AddFontDefault();
                    loaded = true;
                    break;
                default:
                    loaded = TryLoadImGuiFontCandidates(io, fontDirectories, fontSize, candidates);
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

            var fontDirectories = GetSystemFontDirectories();
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

        private static bool TryLoadImGuiFontCandidates(ImGuiIOPtr io, IEnumerable<string> fontDirectories, float sizePixels, params string[] fileNames)
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
                        return true;
                    }
                }
            }

            return false;
        }

        private unsafe bool TryMergeEmojiFallbackFont(ImGuiIOPtr io, IEnumerable<string> fontDirectories, float baseFontSize)
        {
            BuildEmojiGlyphRangesIfNeeded(io);
            if (_emojiGlyphRanges.Data == IntPtr.Zero)
            {
                return false;
            }

            var emojiCandidates = GetEmojiFontCandidates();
            var emojiFontSize = MathF.Max(13.0f, baseFontSize - 0.5f);

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

        private static IEnumerable<string> GetSystemFontDirectories()
        {
            var dirs = new List<string>();

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

            return dirs;
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

        protected void ConfigImStyle()
        {
            ApplyLightImGuiTheme();
        }

        public void ApplyLightImGuiTheme()
        {
            var color_for_text = new Vector3(52.0f / 255.0f, 68.0f / 255.0f, 86.0f / 255.0f);
            var color_for_head = new Vector3(41.0f / 255.0f, 133.0f / 255.0f, 220.0f / 255.0f);
            var color_for_area = new Vector3(232.0f / 255.0f, 239.0f / 255.0f, 247.0f / 255.0f);
            var color_for_body = new Vector3(247.0f / 255.0f, 250.0f / 255.0f, 253.0f / 255.0f);
            var color_for_pops = new Vector3(255.0f / 255.0f, 255.0f / 255.0f, 255.0f / 255.0f);
            ConfigImStyle(color_for_text, color_for_head, color_for_area, color_for_body, color_for_pops);
        }

        public void ApplyDarkImGuiTheme()
        {
            var color_for_text = new Vector3(236.0f / 255.0f, 240.0f / 255.0f, 241.0f / 255.0f);
            var color_for_head = new Vector3(61.0f / 255.0f, 128.0f / 255.0f, 185.0f / 255.0f);
            var color_for_area = new Vector3(57.0f / 255.0f, 79.0f / 255.0f, 105.0f / 255.0f);
            var color_for_body = new Vector3(44.0f / 255.0f, 62.0f / 255.0f, 80.0f / 255.0f);
            var color_for_pops = new Vector3(33.0f / 255.0f, 46.0f / 255.0f, 60.0f / 255.0f);
            ConfigImStyle(color_for_text, color_for_head, color_for_area, color_for_body, color_for_pops);
        }

        public void ConfigImStyle(Vector3 color_for_text, Vector3 color_for_head, Vector3 color_for_area, Vector3 color_for_body, Vector3 color_for_pops)
        {
            ImGuiStylePtr style = ImGui.GetStyle();

            style.WindowRounding = 8.0f;
            style.ChildRounding = 8.0f;
            style.FrameRounding = 7.0f;
            style.GrabRounding = 7.0f;
            style.TabRounding = 7.0f;
            style.PopupRounding = 8.0f;
            style.ScrollbarRounding = 8.0f;
            style.FrameBorderSize = 0.0f;
            style.WindowBorderSize = 1.0f;
            style.WindowPadding = new Vector2(10.0f, 10.0f);
            style.FramePadding = new Vector2(8.0f, 6.0f);
            style.ItemSpacing = new Vector2(8.0f, 7.0f);

            style.Colors[(int)ImGuiCol.Text] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 1.00f);
            style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 0.56f);
            style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(color_for_body.X, color_for_body.Y, color_for_body.Z, 0.98f);
            style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 0.82f);
            style.Colors[(int)ImGuiCol.Border] = new Vector4(0.78f, 0.84f, 0.91f, 0.65f);
            style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0, 0, 0, 0.00f);
            style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 1.00f);
            style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.82f, 0.90f, 0.98f, 1.00f);
            style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.74f, 0.86f, 0.97f, 1.00f);
            style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.90f, 0.94f, 0.99f, 1.00f);
            style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.94f, 0.96f, 0.99f, 0.86f);
            style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.84f, 0.91f, 0.98f, 1.00f);
            style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.93f, 0.96f, 0.99f, 1.00f);
            style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 1.00f);
            style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.69f, 0.79f, 0.91f, 0.70f);
            style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.55f, 0.71f, 0.89f, 0.88f);
            style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.95f);
            
            style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.75f);
            style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.Button] = new Vector4(0.82f, 0.90f, 0.98f, 1.00f);
            style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.74f, 0.86f, 0.97f, 1.00f);
            style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.90f);
            style.Colors[(int)ImGuiCol.Header] = new Vector4(0.79f, 0.88f, 0.98f, 0.95f);
            style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.70f, 0.84f, 0.97f, 1.00f);
            style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.90f);
            style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 1.00f);
            style.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.77f, 0.83f, 0.90f, 0.95f);
            style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.78f, 0.84f, 0.91f, 0.45f);
            style.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.00f);
            style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 0.40f);
            
            style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.26f, 0.48f, 0.73f, 0.90f);
            style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.35f, 0.56f, 0.82f, 0.92f);
            style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.25f);
            style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(color_for_pops.X, color_for_pops.Y, color_for_pops.Z, 0.98f);
            style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.92f, 0.96f, 0.99f, 1.00f);
            style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.82f, 0.91f, 0.99f, 1.00f);
            style.Colors[(int)ImGuiCol.TabActive] = new Vector4(0.78f, 0.89f, 0.98f, 1.00f);
            style.Colors[(int)ImGuiCol.TabUnfocused] = new Vector4(0.93f, 0.96f, 0.99f, 1.00f);
            style.Colors[(int)ImGuiCol.TabUnfocusedActive] = new Vector4(0.86f, 0.92f, 0.98f, 1.00f);
            style.Colors[(int)ImGuiCol.DockingPreview] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.22f);
            style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.77f, 0.83f, 0.90f, 0.95f);
            style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.62f, 0.76f, 0.92f, 0.30f);
            style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.50f, 0.69f, 0.89f, 0.65f);
            style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.85f);

            /*
            style.Colors[(int)ImGuiCol.ComboBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 1.00f);
            style.Colors[(int)ImGuiCol.Column] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.32f);
            style.Colors[(int)ImGuiCol.ColumnHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.78f);
            style.Colors[(int)ImGuiCol.ColumnActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.Res.ZeGrip] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.15f);
            style.Colors[(int)ImGuiCol.Res.ZeGripHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.78f);
            style.Colors[(int)ImGuiCol.Res.ZeGripActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.CloseButton] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 0.16f);
            style.Colors[(int)ImGuiCol.CloseButtonHovered] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 0.39f);
            style.Colors[(int)ImGuiCol.CloseButtonActive] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 1.00f);
            style.Colors[(int)ImGuiCol.ModalWindowDarkening] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 0.73f);*/
        }


        public virtual void Run()
        {
            new GameSilkOpenGl(this, "ManaGL - SceneViewerMode", RequestedGraphicsApi).Run();
        }

        public void update(double deltaTime)
        {
            ManaTime.GlobalTime.UpdateTime(deltaTime);

            UpdateFrame++;

            Context.Physics.Update();

            Update(deltaTime);

            Context.World.Update();
        }

        public abstract void Update(double deltaTime);

        protected virtual void DrawMainMenuBar()
        {
        }

        public void ResetImGuiLayoutToDefaults()
        {
            ManaImGuiSettingsStorage.DeleteIniFile();
            ImGui.LoadIniSettingsFromMemory(string.Empty);
            _dockLayoutInitialized = false;
            _dockLayoutResetRequested = true;
            UtilityDockId = 0;
            OutlinerDockId = 0;
        }

        protected virtual void DrawImGui(double deltaTime)
        {
        }

        protected virtual void InitializeDockLayout(uint dockspaceId, Vector2 dockspaceSize)
        {
        }

        protected void DrawDockSpace()
        {
            var viewport = ImGui.GetMainViewport();
            var dockspaceFlags = ImGuiDockNodeFlags.PassthruCentralNode;
            var windowFlags = ImGuiWindowFlags.NoDocking |
                              ImGuiWindowFlags.NoTitleBar |
                              ImGuiWindowFlags.NoCollapse |
                              ImGuiWindowFlags.NoResize |
                              ImGuiWindowFlags.NoMove |
                              ImGuiWindowFlags.NoBringToFrontOnFocus |
                              ImGuiWindowFlags.NoNavFocus |
                              ImGuiWindowFlags.NoBackground;

            ImGui.SetNextWindowPos(viewport.WorkPos);
            ImGui.SetNextWindowSize(viewport.WorkSize);
            ImGui.SetNextWindowViewport(viewport.ID);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

            ImGui.Begin("MainDockSpaceHost", windowFlags);
            ImGui.PopStyleVar(3);

            var dockspaceId = ImGui.GetID("MainDockSpace");
            ImGui.DockSpace(dockspaceId, Vector2.Zero, dockspaceFlags);

            if (!_dockLayoutInitialized)
            {
                var useDefaultLayout = _dockLayoutResetRequested || !File.Exists(ManaImGuiSettingsStorage.IniFilePath);
                if (useDefaultLayout)
                {
                    if (_dockLayoutResetRequested)
                    {
                        ImGuiDockNative.DockBuilderRemoveNode(dockspaceId);
                        ImGuiDockNative.DockBuilderAddNode(dockspaceId, dockspaceFlags);
                    }

                    InitializeDockLayout(dockspaceId, viewport.WorkSize);
                    ImGui.SaveIniSettingsToDisk(ManaImGuiSettingsStorage.IniFilePath);
                }

                _dockLayoutResetRequested = false;
                _dockLayoutInitialized = true;
            }

            ImGui.End();
        }

        public void ProcessPendingFontPreset()
        {
            if (_pendingImGuiFontPreset.HasValue)
            {
                ApplyImGuiFontPreset(_pendingImGuiFontPreset.Value);
                _pendingImGuiFontPreset = null;
            }
        }

        public virtual void Render(double deltaTime)
        {
            RenderFrame++;

            ProcessPendingFontPreset();

            _ImGui.Update((float)deltaTime);
            DrawMainMenuBar();
            DrawDockSpace();
            DrawImGui(deltaTime);
            Context.World.DrawImGui();

            Context.World.Draw();
            _ImGui.Render();
        }

        public virtual void Closing()
        {
            _ImGui?.Dispose();
            Context.Input?.Dispose();
            Context.gl?.Dispose();
        }
    }

    public static class ImGuiDockNative
    {
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderSplitNode")]
        private static extern uint DockBuilderSplitNodeNative(uint nodeId, ImGuiDir splitDir, float sizeRatioForNodeAtDir, out uint outIdAtDir, out uint outIdAtOppositeDir);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderAddNode")]
        private static extern uint DockBuilderAddNodeNative(uint nodeId, ImGuiDockNodeFlags flags);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderRemoveNode")]
        private static extern void DockBuilderRemoveNodeNative(uint nodeId);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderDockWindow")]
        private static extern void DockBuilderDockWindowNative(byte[] windowName, uint nodeId);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderSetNodeSize")]
        private static extern void DockBuilderSetNodeSizeNative(uint nodeId, Vector2 size);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderFinish")]
        private static extern void DockBuilderFinishNative(uint nodeId);

        public static uint DockBuilderSplitNode(uint nodeId, ImGuiDir splitDir, float sizeRatioForNodeAtDir, out uint outIdAtDir, out uint outIdAtOppositeDir)
        {
            return DockBuilderSplitNodeNative(nodeId, splitDir, sizeRatioForNodeAtDir, out outIdAtDir, out outIdAtOppositeDir);
        }

        public static uint DockBuilderAddNode(uint nodeId, ImGuiDockNodeFlags flags)
        {
            return DockBuilderAddNodeNative(nodeId, flags);
        }

        public static void DockBuilderRemoveNode(uint nodeId)
        {
            DockBuilderRemoveNodeNative(nodeId);
        }

        public static void DockBuilderDockWindow(string windowName, uint nodeId)
        {
            var utf8Name = Encoding.UTF8.GetBytes(windowName + "\0");
            DockBuilderDockWindowNative(utf8Name, nodeId);
        }

        public static void DockBuilderSetNodeSize(uint nodeId, Vector2 size)
        {
            DockBuilderSetNodeSizeNative(nodeId, size);
        }

        public static void DockBuilderFinish(uint nodeId)
        {
            DockBuilderFinishNative(nodeId);
        }
    }


    public class GameModeGraphicsLevelDebugger
    {
        private DebugProc _debugProcCallback;
        private GCHandle _debugProcCallbackHandle;

        public GameModeGraphicsLevelDebugger(GL GL)
        {
            _debugProcCallback = DebugCallback;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Mac os only supports 4.1, debugging in in 4.3
                GL.Enable(EnableCap.DebugOutput);
                GL.Enable(EnableCap.DebugOutputSynchronous);
                GL.DebugMessageControl(DebugSource.DontCare, DebugType.DontCare, DebugSeverity.DontCare, 0, new uint[0],
                    true);

                _debugProcCallbackHandle = GCHandle.Alloc(_debugProcCallback);
                var userParam = IntPtr.Zero;
                GL.DebugMessageCallback(_debugProcCallback, in userParam);
            }
        }

        private void DebugCallback(GLEnum source, GLEnum type, int id, GLEnum severity, int length, IntPtr message, IntPtr userParam)
        {
            string messageString = Marshal.PtrToStringAnsi(message, length);

            if (type == GLEnum.DebugTypeOther)
            {
                return;
            }

            Trace.WriteLine($"{severity} {type} | {messageString}");

            if (type == GLEnum.DebugTypeError)
            {
                if (messageString.Contains("UniformMatrix*"))
                {
                    return;
                }

                if (messageString.Contains("linked"))
                {
                    return;
                }

                throw new Exception(messageString);
            }
        }
    }

}


