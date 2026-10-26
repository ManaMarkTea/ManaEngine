using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
#if MANA_BROWSER
using ManaGraphics.Code.Graphics.Browser;
#endif

namespace ManaGraphics.Code.Graphics
{
    /// <summary>What every pass borrows from the context: API, device, queue, formats and shader loading.</summary>
    internal sealed unsafe class ManaWebGpuDevice
    {
        public ManaWebGpuDevice(WebGPU api, Device* device, Queue* queue, TextureFormat surfaceFormat, TextureFormat depthFormat)
        {
            Api = api;
            Device = device;
            Queue = queue;
            SurfaceFormat = surfaceFormat;
            DepthFormat = depthFormat;
        }

        public WebGPU Api { get; }
        public Device* Device { get; }
        public Queue* Queue { get; }
        public TextureFormat SurfaceFormat { get; }
        public TextureFormat DepthFormat { get; }

        /// <summary>BC textures sample directly (DDS uploads as stored); otherwise they are decoded to RGBA first.</summary>
        public bool SupportsBlockCompression { get; init; }

        /// <summary>The browser's phone profile: smaller reflection cubes and a particle budget.</summary>
        public static bool UseMobileRendering
        {
            get
            {
#if MANA_BROWSER
                return Browser.ManaBrowserInterop.IsMobileRendering();
#else
                return false;
#endif
            }
        }

        /// <summary>MANA_UNCAPPED=1: no frame limiter and no vsync, so a benchmark measures the frame rather than the display.</summary>
        public static bool Uncapped { get; } = Environment.GetEnvironmentVariable("MANA_UNCAPPED") == "1";

        /// <summary>Texture-to-buffer copies need each row padded to a multiple of 256 bytes.</summary>
        public static uint AlignCopyRow(uint rowBytes) => (rowBytes + 255u) / 256u * 256u;

        public static string ShaderPath(string fileName) => Path.Combine("Assets", "Shaders", "WebGpu", fileName);

        public static string ResolveAssetPath(string relativePath)
        {
            return TryResolveAssetPath(relativePath)
                ?? throw new FileNotFoundException($"Could not locate asset file '{relativePath}'.");
        }

        /// <summary>The file, or null -- for one that may legitimately be absent, like the web build's environment photo.</summary>
        public static string? TryResolveAssetPath(string relativePath)
        {
            string[] candidates =
            {
                Path.Combine(Environment.CurrentDirectory, relativePath),
                Path.Combine(Environment.CurrentDirectory, "..", relativePath),
                Path.Combine(AppContext.BaseDirectory, relativePath),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return null;
        }

        /// <summary>Compiles a WGSL file under Assets/Shaders/WebGpu (includes resolved). The caller owns the module.</summary>
        /// <param name="defines">Names #if blocks in the shader test for, to build a variant of it (MANA_INSTANCED).</param>
        public ShaderModule* CreateShaderModule(string fileName, params string[] defines)
        {
            var path = ResolveAssetPath(ShaderPath(fileName));
            var source = ManaWgslPreprocessor.LoadResolved(path, defines);
            nint sourcePointer = Marshal.StringToCoTaskMemUTF8(source);

            try
            {
                var wgslDescriptor = new ShaderModuleWGSLDescriptor
                {
                    Chain = new ChainedStruct
                    {
                        SType = SType.ShaderModuleWgslDescriptor
                    },
                    Code = (byte*)sourcePointer
                };

                var shaderDescriptor = new ShaderModuleDescriptor
                {
                    NextInChain = &wgslDescriptor.Chain
                };

                var shaderModule = Api.DeviceCreateShaderModule(Device, &shaderDescriptor);
                if (shaderModule == null)
                {
                    throw new InvalidOperationException($"Failed to create WebGPU shader module from {path}.");
                }

#if MANA_BROWSER
                // Kept as a standing diagnostic: logs any WGSL compilation warnings/errors straight
                // to the browser console. See mana_webgpu.c's mana_log_shader_compilation_info_impl.
                ManaBrowserWebGpuCallbackBridge.LogShaderCompilationInfo((int)(nint)shaderModule);
#endif

                return shaderModule;
            }
            finally
            {
                Marshal.FreeCoTaskMem(sourcePointer);
            }
        }
    }
}
