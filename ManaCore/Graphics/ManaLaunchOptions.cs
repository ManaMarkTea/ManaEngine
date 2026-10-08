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

        /// <summary>Set by <c>--level &lt;level&gt;</c>: start the editor with that level already open.</summary>
        public string? EditLevelPath { get; private init; }

        /// <summary>
        /// Set by <c>--command-file &lt;path&gt;</c>: drives the editor from a JSON script (camera moves,
        /// screenshots, quit) instead of interactive input -- see ManaCommandRunner.
        /// </summary>
        public string? CommandFilePath { get; private init; }

        /// <summary>Set by <c>--precache-thumbnails</c>: make every missing asset thumbnail, headless, and exit.</summary>
        public bool PrecacheThumbnails { get; private init; }

        /// <summary>Set by <c>--train-yard-wars</c>: run that couch game instead of the editor, with <c>--players</c> and <c>--seed</c>.</summary>
        public bool TrainYardWars { get; private init; }

        public int Players { get; private init; } = 4;

        /// <summary>The map; random when not given.</summary>
        public int? Seed { get; private init; }

        public static ManaLaunchOptions Parse(string[] args)
        {
            var graphicsBackend = ParseGraphicsBackend(Environment.GetEnvironmentVariable("MANA_GRAPHICS_BACKEND"));
            var webGpuBackend = Environment.GetEnvironmentVariable("MANA_WGPU_BACKEND");
            string? playLevelPath = null;
            string? editLevelPath = null;
            string? commandFilePath = null;
            var precacheThumbnails = false;
            var trainYardWars = false;
            var players = 4;
            int? seed = null;

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

                if (arg.Equals("--train-yard-wars", StringComparison.OrdinalIgnoreCase))
                {
                    trainYardWars = true;
                    continue;
                }

                if (TryReadOptionValue(args, ref i, "--players", out var playersValue) && int.TryParse(playersValue, out var playerCount))
                {
                    players = playerCount;
                    continue;
                }

                if (TryReadOptionValue(args, ref i, "--seed", out var seedValue) && int.TryParse(seedValue, out var seedNumber))
                {
                    seed = seedNumber;
                    continue;
                }

                if (arg.Equals("--precache-thumbnails", StringComparison.OrdinalIgnoreCase))
                {
                    precacheThumbnails = true;
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
                TrainYardWars = trainYardWars,
                Players = players,
                Seed = seed
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
