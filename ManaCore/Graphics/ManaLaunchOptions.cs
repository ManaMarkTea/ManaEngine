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

        /// <summary>Set by <c>--precache-thumbnails</c>: make every missing asset thumbnail, headless, and exit.</summary>
        public bool PrecacheThumbnails { get; private init; }

        /// <summary>Set by <c>--mute</c> (or <c>MANA_MUTE=1</c>): no sound at all -- scripted test runs pass it.</summary>
        public bool Mute { get; private init; }

        public static ManaLaunchOptions Parse(string[] args)
        {
            var graphicsBackend = ParseGraphicsBackend(Environment.GetEnvironmentVariable("MANA_GRAPHICS_BACKEND"));
            var webGpuBackend = Environment.GetEnvironmentVariable("MANA_WGPU_BACKEND");
            string? playLevelPath = null;
            string? editLevelPath = null;
            string? commandFilePath = null;
            var precacheThumbnails = false;
            var playMenu = false;
            var mute = Environment.GetEnvironmentVariable("MANA_MUTE") is "1" or "true";

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
                Mute = mute,
                PlayMenu = playMenu
            };
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
                "opengl" or "gl" => ManaGraphicsBackendKind.OpenGL,
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
