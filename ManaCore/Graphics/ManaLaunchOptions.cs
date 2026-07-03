namespace ManaCore.Graphics
{
    public sealed class ManaLaunchOptions
    {
        public ManaGraphicsBackendKind GraphicsBackend { get; private init; } = ManaGraphicsBackendKind.WebGpu;
        public string? WebGpuBackend { get; private init; }

        public static ManaLaunchOptions Parse(string[] args)
        {
            var graphicsBackend = ParseGraphicsBackend(Environment.GetEnvironmentVariable("MANA_GRAPHICS_BACKEND"));
            var webGpuBackend = Environment.GetEnvironmentVariable("MANA_WGPU_BACKEND");

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
                }
            }

            return new ManaLaunchOptions
            {
                GraphicsBackend = graphicsBackend,
                WebGpuBackend = string.IsNullOrWhiteSpace(webGpuBackend) ? null : webGpuBackend
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
