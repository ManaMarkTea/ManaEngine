namespace ManaGraphics.Code.Graphics
{
    public enum ManaGraphicsBackend
    {
        OpenGL,
        WebGpu
    }

    public sealed class ManaLaunchOptions
    {
        public ManaGraphicsBackend GraphicsBackend { get; private init; } = ManaGraphicsBackend.OpenGL;
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
                    graphicsBackend = ManaGraphicsBackend.WebGpu;
                    continue;
                }

                if (TryReadOptionValue(args, ref i, "--graphics", out var graphicsValue) ||
                    TryReadOptionValue(args, ref i, "--graphics-backend", out graphicsValue))
                {
                    graphicsBackend = ParseGraphicsBackend(graphicsValue);
                    continue;
                }

                if (TryReadOptionValue(args, ref i, "--wgpu-backend", out var wgpuBackendValue) ||
                    TryReadOptionValue(args, ref i, "--webgpu-backend", out wgpuBackendValue))
                {
                    webGpuBackend = wgpuBackendValue;
                }
            }

            return new ManaLaunchOptions
            {
                GraphicsBackend = graphicsBackend,
                WebGpuBackend = string.IsNullOrWhiteSpace(webGpuBackend) ? null : webGpuBackend
            };
        }

        private static ManaGraphicsBackend ParseGraphicsBackend(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return ManaGraphicsBackend.OpenGL;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "webgpu" or "wgpu" => ManaGraphicsBackend.WebGpu,
                "opengl" or "gl" => ManaGraphicsBackend.OpenGL,
                _ => ManaGraphicsBackend.OpenGL
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
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = arg[prefix.Length..];
                return true;
            }

            return false;
        }
    }
}
