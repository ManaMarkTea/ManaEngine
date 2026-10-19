using System.Runtime.InteropServices;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// What every render pass needs from the context: the API, device and queue, the target formats and shader
    /// loading. Borrowed -- the context owns the device and releases it after every pass is disposed.
    /// </summary>
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
                Path.Combine(Environment.CurrentDirectory, "Mana", relativePath),
                Path.Combine(Environment.CurrentDirectory, "..", "Mana", relativePath),
                Path.Combine(AppContext.BaseDirectory, relativePath),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mana", relativePath)
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
        public ShaderModule* CreateShaderModule(string fileName)
        {
            var path = ResolveAssetPath(ShaderPath(fileName));
            var source = ManaWgslPreprocessor.LoadResolved(path);
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
