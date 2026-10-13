using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;

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

            var self = GCHandle.Alloc(this);
            try
            {
                _api.InstanceRequestAdapter(_instance, &options, new PfnRequestAdapterCallback(&OnRequestAdapter), (void*)GCHandle.ToIntPtr(self));
            }
            finally
            {
                self.Free();
            }
            if (_adapter == null)
            {
                throw new InvalidOperationException($"Failed to request WebGPU adapter. {_requestFailure}");
            }
        }

        private void RequestDevice()
        {
            var descriptor = new DeviceDescriptor();
            var self = GCHandle.Alloc(this);
            try
            {
                _api.AdapterRequestDevice(_adapter, &descriptor, new PfnRequestDeviceCallback(&OnRequestDevice), (void*)GCHandle.ToIntPtr(self));
            }
            finally
            {
                self.Free();
            }
            if (_device == null)
            {
                throw new InvalidOperationException($"Failed to request WebGPU device. {_requestFailure}");
            }
        }
#else
        // wgpuInstanceRequestAdapter/wgpuAdapterRequestDevice's own callback-delivery path hits a
        // confirmed Emscripten runtime bug in this exact SDK/bundling (RuntimeError:
        // getWasmTableEntry(...) is not a function -- not fixed by AOT compilation or by forcing
        // getWasmTableEntry past tree-shaking), so the browser build never uses OnRequestAdapter/
        // OnRequestDevice (below, #if !MANA_BROWSER) at all. Instead, ManaWebGpuContext.
        // BrowserAsync.cs's RequestAdapterAsync/RequestDeviceAsync go through
        // ManaBrowserWebGpuCallbackBridge (EM_JS-injected plain JS calling
        // navigator.gpu.requestAdapter()/adapter.requestDevice() directly, registering the result
        // into library_webgpu.js's own id tables, delivered back via [JSExport] -- the same
        // mechanism ManaBrowserInterop.cs already used successfully for the RAF loop/resize).
        // SetAdapterFromId/SetDeviceFromId below just assign the resulting integer id as the
        // opaque Adapter*/Device* handle Silk.NET's other (still-working) calls expect. Split into
        // a separate non-unsafe partial file (ManaWebGpuContext.BrowserAsync.cs) because this
        // whole class is declared `unsafe`, and C# doesn't allow `await` inside an unsafe context
        // at all, regardless of whether the awaiting method itself touches a pointer.
        private System.Threading.Tasks.TaskCompletionSource<bool>? _adapterRequestTcs;
        private System.Threading.Tasks.TaskCompletionSource<bool>? _deviceRequestTcs;

        internal int AdapterIdForBrowser => (int)(nint)_adapter;

        internal void SetAdapterFromId(int adapterId)
        {
            _adapter = (Adapter*)(nint)adapterId;
        }

        internal void SetDeviceFromId(int deviceId)
        {
            _device = (Device*)(nint)deviceId;
        }
#endif

#if !MANA_BROWSER
        // Native callbacks are static [UnmanagedCallersOnly] methods, never delegates: iOS's ahead-of-time code
        // cannot make a native entry point for a delegate. wgpu-native answers before the request returns.
        private string? _requestFailure;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnRequestAdapter(RequestAdapterStatus status, Adapter* adapter, byte* message, void* userdata)
        {
            var self = (ManaWebGpuContext)GCHandle.FromIntPtr((nint)userdata).Target!;
            if (status == RequestAdapterStatus.Success)
            {
                self._adapter = adapter;
                return;
            }
            self._requestFailure = $"{status} {ReadMessage(message)}";
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnRequestDevice(RequestDeviceStatus status, Device* device, byte* message, void* userdata)
        {
            var self = (ManaWebGpuContext)GCHandle.FromIntPtr((nint)userdata).Target!;
            if (status == RequestDeviceStatus.Success)
            {
                self._device = device;
                return;
            }
            self._requestFailure = $"{status} {ReadMessage(message)}";
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void OnError(ErrorType type, byte* message, void* userdata)
        {
            ManaConsole.WriteDiagnostic($"[WebGPU] {type}: {ReadMessage(message)}");
        }
#endif

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
