namespace ManaCore.Graphics
{
    public sealed class ManaLaunchOptions
    {
        public ManaGraphicsBackendKind GraphicsBackend { get; private init; } = ManaGraphicsBackendKind.WebGpu;
        public string? WebGpuBackend { get; private init; }

        /// <summary>
        /// Set by <c>--play &lt;level&gt;</c>: skip the editor and run that level as the game. This is what
        /// the editor's Play In New Window launches, so the two paths stay the same one.
        /// </summary>
        public string? PlayLevelPath { get; private init; }

        /// <summary>Set by <c>--play-menu</c>: the game opening on its start screen of levels, as the phone app does.</summary>
        public bool PlayMenu { get; private init; }

        /// <summary>Set by <c>--level &lt;level&gt;</c>: start the editor with that level already open.</summary>
        public string? EditLevelPath { get; private init; }

        /// <summary>
        /// Set by <c>--command-file &lt;path&gt;</c>: drives the editor from a JSON script (camera moves,
        /// screenshots, quit) instead of interactive input -- see ManaCommandRunner.
        /// </summary>
        public string? CommandFilePath { get; private init; }

        /// <summary>Set by <c>--import &lt;pack.zip&gt;</c>: import a downloaded scene pack into Library/Imported, headless, and exit.</summary>
        public string? ImportPath { get; private init; }

        /// <summary>Set by <c>--precache-thumbnails</c>: make every missing asset thumbnail, headless, and exit.</summary>
        public bool PrecacheThumbnails { get; private init; }

        /// <summary>Set by <c>--mute</c> (or <c>MANA_MUTE=1</c>): no sound at all -- scripted test runs pass it.</summary>
        public bool Mute { get; private init; }

        /// <summary>
        /// Set by <c>--window-size 1080x1920</c> (or <c>MANA_WINDOW_SIZE</c>): the main window's size in pixels -- a
        /// portrait window for phone videos, say. Process-wide, as there is one main window; null keeps 1920 x 1080.
        /// </summary>
        public static (int Width, int Height)? WindowSize { get; private set; }

        public static ManaLaunchOptions Parse(string[] args)
        {
            var graphicsBackend = ParseGraphicsBackend(Environment.GetEnvironmentVariable("MANA_GRAPHICS_BACKEND"));
            var webGpuBackend = Environment.GetEnvironmentVariable("MANA_WGPU_BACKEND");
            string? playLevelPath = null;
            string? editLevelPath = null;
            string? commandFilePath = null;
            var precacheThumbnails = false;
            string? importPath = null;
            var playMenu = false;
            var mute = Environment.GetEnvironmentVariable("MANA_MUTE") is "1" or "true";
            if (ParseWindowSize(Environment.GetEnvironmentVariable("MANA_WINDOW_SIZE")) is { } sizeFromEnvironment)
            {
                WindowSize = sizeFromEnvironment;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.Equals("--webgpu", StringComparison.OrdinalIgnoreCase))
                {
                    graphicsBackend = ManaGraphicsBackendKind.WebGpu;
                    continue;
                }

                if (TryReadOptionValue(args, ref i, "--graphics", out var graphicsValue) ||
                    TryReadOptionValue(args, ref i, "--graphics-backend", out graphicsValue))
                {
                    graphicsBackend = ParseGraphicsBackend(graphicsValue);
                    continue;
                }

                if (TryReadOptionValue(args, ref i, "--wgpu-backend", out var webGpuBackendValue) ||
                    TryReadOptionValue(args, ref i, "--webgpu-backend", out webGpuBackendValue))
                {
                    webGpuBackend = webGpuBackendValue;
                    continue;
                }

                if (TryReadOptionValue(args, ref i, "--play", out var playValue))
                {
                    playLevelPath = playValue;
                    continue;
                }

                if (TryReadOptionValue(args, ref i, "--level", out var editValue))
                {
                    editLevelPath = editValue;
                    continue;
                }

                if (arg.Equals("--play-menu", StringComparison.OrdinalIgnoreCase))
                {
                    playMenu = true;
                    continue;
                }

                if (TryReadOptionValue(args, ref i, "--import", out var importValue))
                {
                    importPath = importValue;
                    continue;
                }

                if (arg.Equals("--precache-thumbnails", StringComparison.OrdinalIgnoreCase))
                {
                    precacheThumbnails = true;
                    continue;
                }

                if (arg.Equals("--mute", StringComparison.OrdinalIgnoreCase))
                {
                    mute = true;
                    continue;
                }

                if (TryReadOptionValue(args, ref i, "--window-size", out var windowSizeValue))
                {
                    WindowSize = ParseWindowSize(windowSizeValue)
                        ?? throw new ArgumentException($"--window-size '{windowSizeValue}' is not WIDTHxHEIGHT, e.g. 1080x1920.");
                    continue;
                }

                if (TryReadOptionValue(args, ref i, "--command-file", out var commandFileValue))
                {
                    commandFilePath = commandFileValue;
                }
            }

            return new ManaLaunchOptions
            {
                GraphicsBackend = graphicsBackend,
                WebGpuBackend = string.IsNullOrWhiteSpace(webGpuBackend) ? null : webGpuBackend,
                PlayLevelPath = string.IsNullOrWhiteSpace(playLevelPath) ? null : playLevelPath,
                EditLevelPath = string.IsNullOrWhiteSpace(editLevelPath) ? null : editLevelPath,
                CommandFilePath = string.IsNullOrWhiteSpace(commandFilePath) ? null : commandFilePath,
                PrecacheThumbnails = precacheThumbnails,
                ImportPath = string.IsNullOrWhiteSpace(importPath) ? null : importPath,
                Mute = mute,
                PlayMenu = playMenu
            };
        }

        /// <summary>"1080x1920" as (1080, 1920); null for anything else.</summary>
        public static (int Width, int Height)? ParseWindowSize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }
            var parts = value.Trim().ToLowerInvariant().Split('x');
            if (parts.Length != 2
                || !int.TryParse(parts[0], out var width)
                || !int.TryParse(parts[1], out var height)
                || width < 64 || height < 64 || width > 16384 || height > 16384)
            {
                return null;
            }
            return (width, height);
        }

        private static ManaGraphicsBackendKind ParseGraphicsBackend(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return ManaGraphicsBackendKind.WebGpu;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "webgpu" or "wgpu" => ManaGraphicsBackendKind.WebGpu,
                "opengl" or "gl" => throw new ArgumentException("OpenGL support has been removed. Use WebGPU (--graphics webgpu)."),
                _ => ManaGraphicsBackendKind.WebGpu
            };
        }

        private static bool TryReadOptionValue(string[] args, ref int index, string optionName, out string? value)
        {
            value = null;
            var arg = args[index];
            if (arg.Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length)
                {
                    return false;
                }

                value = args[++index];
                return true;
            }

            var prefix = optionName + "=";
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            value = arg[prefix.Length..];
            return true;
        }
    }
}
