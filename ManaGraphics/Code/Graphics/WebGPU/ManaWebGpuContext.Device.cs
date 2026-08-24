using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
#if !MANA_BROWSER
        private void RequestAdapter(string? requestedBackend)
        {
            var options = new RequestAdapterOptions
            {
                CompatibleSurface = _surface,
                PowerPreference = PowerPreference.HighPerformance
            };

            _api.InstanceRequestAdapter(_instance, &options, _requestAdapterCallback, null);
            if (_adapter == null)
            {
                throw new InvalidOperationException("Failed to request WebGPU adapter.");
            }
        }

        private void RequestDevice()
        {
            var descriptor = new DeviceDescriptor();
            _api.AdapterRequestDevice(_adapter, &descriptor, _requestDeviceCallback, null);
            if (_device == null)
            {
                throw new InvalidOperationException("Failed to request WebGPU device.");
            }
        }
#else
        // wgpu-native's desktop implementation invokes the callback synchronously, within the same
        // call stack as InstanceRequestAdapter/AdapterRequestDevice, which is what lets the
        // desktop RequestAdapter/RequestDevice above check _adapter/_device immediately after
        // calling it. A real browser's navigator.gpu.requestAdapter()/requestDevice() resolve on a
        // later JS microtask via Emscripten's library_webgpu.js calling back into this same
        // callback -- genuinely asynchronous, not just async-shaped. These two just fire the
        // native request; ManaWebGpuContext.BrowserAsync.cs's RequestAdapterAsync/RequestDeviceAsync
        // await a TaskCompletionSource that OnRequestAdapter/OnRequestDevice complete, once the
        // callback actually arrives -- split into a separate non-unsafe partial file because this
        // whole class is declared `unsafe`, and C# doesn't allow `await` inside an unsafe context
        // at all, regardless of whether the awaiting method itself touches a pointer. Not yet
        // confirmed against a real browser: the M0 spike only proved the shim mechanism for plain
        // synchronous functions, not this callback-after-a-microtask path (see WebPort-Plan.md M2).
        private System.Threading.Tasks.TaskCompletionSource<bool>? _adapterRequestTcs;
        private System.Threading.Tasks.TaskCompletionSource<bool>? _deviceRequestTcs;

        private void StartAdapterRequest()
        {
            var options = new RequestAdapterOptions
            {
                CompatibleSurface = _surface,
                PowerPreference = PowerPreference.HighPerformance
            };

            _api.InstanceRequestAdapter(_instance, &options, _requestAdapterCallback, null);
        }

        private void StartDeviceRequest()
        {
            var descriptor = new DeviceDescriptor();
            _api.AdapterRequestDevice(_adapter, &descriptor, _requestDeviceCallback, null);
        }
#endif

        private void OnRequestAdapter(RequestAdapterStatus status, Adapter* adapter, byte* message, void* userdata)
        {
#if MANA_BROWSER
            Console.WriteLine($"[OnRequestAdapter] callback fired! status={status}");
#endif
            if (status == RequestAdapterStatus.Success)
            {
                _adapter = adapter;
#if MANA_BROWSER
                _adapterRequestTcs?.TrySetResult(true);
#endif
                return;
            }

#if MANA_BROWSER
            _adapterRequestTcs?.TrySetException(
                new InvalidOperationException($"WebGPU adapter request failed: {status} {ReadMessage(message)}"));
#else
            throw new InvalidOperationException($"WebGPU adapter request failed: {status} {ReadMessage(message)}");
#endif
        }

        private void OnRequestDevice(RequestDeviceStatus status, Device* device, byte* message, void* userdata)
        {
            if (status == RequestDeviceStatus.Success)
            {
                _device = device;
#if MANA_BROWSER
                _deviceRequestTcs?.TrySetResult(true);
#endif
                return;
            }

#if MANA_BROWSER
            _deviceRequestTcs?.TrySetException(
                new InvalidOperationException($"WebGPU device request failed: {status} {ReadMessage(message)}"));
#else
            throw new InvalidOperationException($"WebGPU device request failed: {status} {ReadMessage(message)}");
#endif
        }

        private void OnError(ErrorType type, byte* message, void* userdata)
        {
            ManaConsole.WriteDiagnostic($"[WebGPU] {type}: {ReadMessage(message)}");
        }

#if !MANA_BROWSER
        private static InstanceBackend ParseInstanceBackend(string? requestedBackend)
        {
            if (!string.IsNullOrWhiteSpace(requestedBackend))
            {
                return requestedBackend.Trim().ToLowerInvariant() switch
                {
                    "d3d12" or "dx12" => InstanceBackend.DX12,
                    "d3d11" or "dx11" => InstanceBackend.DX11,
                    "metal" => InstanceBackend.Metal,
                    "vulkan" or "vk" => InstanceBackend.Vulkan,
                    "opengl" or "gl" => InstanceBackend.GL,
                    "all" => InstanceBackend.All,
                    "primary" => InstanceBackend.Primary,
                    "secondary" => InstanceBackend.Secondary,
                    _ => InstanceBackend.Primary
                };
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return InstanceBackend.Metal;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return InstanceBackend.Vulkan;
                //return InstanceBackend.DX12;
            }

            return InstanceBackend.Vulkan;
        }
#endif

        private static string ReadMessage(byte* message)
        {
            return message == null ? string.Empty : Marshal.PtrToStringUTF8((nint)message) ?? string.Empty;
        }

        private static bool CanCreateDemoMeshPipeline(string? requestedBackend)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(requestedBackend))
            {
                return true;
            }

            return requestedBackend.Trim().ToLowerInvariant() is "vulkan" or "vk";
        }

        private static bool IsDemoMeshPipelineEnabled()
        {
            var value = Environment.GetEnvironmentVariable("MANA_WEBGPU_DEMO_MESH") ??
                Environment.GetEnvironmentVariable("MANA_WEBGPU_UNSAFE_DEMO_MESH");
            return value == "1" ||
                value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ||
                value?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
        }

    }
}
