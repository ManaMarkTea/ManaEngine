using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#else
using ManaGraphics.Code.Graphics.Browser;
#endif
using Silk.NET.Windowing;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// The native WebGPU handles: instance, the window's surface, adapter, device and queue, made in that order and
    /// released in reverse. On desktop the adapter and device arrive synchronously in the constructor; in the browser they
    /// are promises, awaited by <see cref="CompleteAsync"/>. Borrows the API wrapper, which outlives every device.
    /// </summary>
    // Not unsafe at class level: the browser's bring-up awaits, so pointer members are marked unsafe one by one.
    internal sealed class ManaWebGpuNativeDevice : IDisposable
    {
        private readonly WebGPU _api;
        private string? _requestFailure;

        public unsafe ManaWebGpuNativeDevice(WebGPU api, IWindow window, string? requestedBackend)
        {
            _api = api;
            try
            {
                ManaConsole.WriteDiagnostic("Creating WebGPU instance.");
#if MANA_BROWSER
                // A real null pointer: a pointer to a zeroed descriptor hangs Emscripten's wgpuCreateInstance.
                Instance = api.CreateInstance((InstanceDescriptor*)null);
#else
                ManaWgpuNativeTrace.EnableIfRequested();
                InstanceBackend = ParseInstanceBackend(requestedBackend);
                var instanceExtras = new InstanceExtras
                {
                    Chain = new ChainedStruct { SType = (SType)NativeSType.STypeInstanceExtras },
                    Backends = InstanceBackend
                };
                var instanceDescriptor = new InstanceDescriptor { NextInChain = &instanceExtras.Chain };
                Instance = api.CreateInstance(&instanceDescriptor);
#endif
                if (Instance == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU instance.");
                }

                ManaConsole.WriteDiagnostic("Creating WebGPU surface.");
#if MANA_BROWSER
                Surface = ManaBrowserWebGpuSurface.CreateFromCanvas(window, api, Instance);
#else
                Surface = ManaWebGpuNativeHost.Current is { } host
                    ? host.CreateSurface(api, Instance)
                    : WebGPUSurface.CreateWebGPUSurface(window, api, Instance);
#endif
                if (Surface == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU surface.");
                }

#if !MANA_BROWSER
                ManaConsole.WriteDiagnosticElapsed("Requesting WebGPU adapter.");
                RequestAdapter();
                ManaConsole.WriteDiagnosticElapsed("Adapter granted; requesting WebGPU device.");
                RequestDevice();
                ManaConsole.WriteDiagnosticElapsed("Device granted.");
                CompleteDevice();
#endif
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public unsafe Instance* Instance { get; private set; }
        public unsafe Surface* Surface { get; private set; }
        public unsafe Adapter* Adapter { get; private set; }
        public unsafe Device* Device { get; private set; }
        public unsafe Queue* Queue { get; private set; }

#if !MANA_BROWSER
        public InstanceBackend InstanceBackend { get; }

        /// <summary>wgpu-native's own extension (device poll, resource reports); null when this build lacks it.</summary>
        public Wgpu? NativeExtension { get; private set; }
#endif

        /// <summary>The window surface's preferred format, as the sRGB variant everything renders through.</summary>
        public unsafe TextureFormat PreferredSurfaceFormat()
        {
            var format = _api.SurfaceGetPreferredFormat(Surface, Adapter);
#if MANA_BROWSER
            // The canvas is configured with the sRGB view format, matching desktop's sRGB surface.
            format = format switch
            {
                TextureFormat.Bgra8Unorm => TextureFormat.Bgra8UnormSrgb,
                TextureFormat.Rgba8Unorm => TextureFormat.Rgba8UnormSrgb,
                _ => format
            };
#endif
            return format;
        }

        /// <summary>Runs pending GPU callbacks (readbacks); wgpu-native's own poll when this build has it.</summary>
        public unsafe void PumpCallbacks()
        {
#if !MANA_BROWSER
            if (NativeExtension != null)
            {
                NativeExtension.DevicePoll(Device, false, null);
                return;
            }
#endif
            _api.InstanceProcessEvents(Instance);
        }

        // The queue, and on desktop the native extension and the uncaptured-error log.
        private unsafe void CompleteDevice()
        {
            Queue = _api.DeviceGetQueue(Device);
#if !MANA_BROWSER
            if (_api.TryGetDeviceExtension(Device, out Wgpu? extension))
            {
                NativeExtension = extension;
            }
            else
            {
                ManaConsole.WriteDiagnostic("WGPU native extension unavailable; falling back to InstanceProcessEvents for async GPU callback pumping.");
            }
            _api.DeviceSetUncapturedErrorCallback(Device, new PfnErrorCallback(&OnError), null);
#endif
            // Not in the browser: the error callback goes through the dynCall path that fails there, and the browser
            // prints validation errors to the console anyway.
        }

#if !MANA_BROWSER
        // Native callbacks are static [UnmanagedCallersOnly] methods, never delegates: iOS's ahead-of-time code cannot make
        // a native entry point for a delegate. wgpu-native answers before the request returns, so the handle is freed after.
        private unsafe void RequestAdapter()
        {
            var options = new RequestAdapterOptions
            {
                CompatibleSurface = Surface,
                PowerPreference = PowerPreference.HighPerformance
            };

            var self = GCHandle.Alloc(this);
            try
            {
                _api.InstanceRequestAdapter(Instance, &options, new PfnRequestAdapterCallback(&OnRequestAdapter), (void*)GCHandle.ToIntPtr(self));
            }
            finally
            {
                self.Free();
            }
            if (Adapter == null)
            {
                throw new InvalidOperationException($"Failed to request WebGPU adapter. {_requestFailure}");
            }
        }

        private unsafe void RequestDevice()
        {
            var descriptor = new DeviceDescriptor();
            var self = GCHandle.Alloc(this);
            try
            {
                _api.AdapterRequestDevice(Adapter, &descriptor, new PfnRequestDeviceCallback(&OnRequestDevice), (void*)GCHandle.ToIntPtr(self));
            }
            finally
            {
                self.Free();
            }
            if (Device == null)
            {
                throw new InvalidOperationException($"Failed to request WebGPU device. {_requestFailure}");
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static unsafe void OnRequestAdapter(RequestAdapterStatus status, Adapter* adapter, byte* message, void* userdata)
        {
            var self = (ManaWebGpuNativeDevice)GCHandle.FromIntPtr((nint)userdata).Target!;
            if (status == RequestAdapterStatus.Success)
            {
                self.Adapter = adapter;
                return;
            }
            self._requestFailure = $"{status} {ReadMessage(message)}";
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static unsafe void OnRequestDevice(RequestDeviceStatus status, Device* device, byte* message, void* userdata)
        {
            var self = (ManaWebGpuNativeDevice)GCHandle.FromIntPtr((nint)userdata).Target!;
            if (status == RequestDeviceStatus.Success)
            {
                self.Device = device;
                return;
            }
            self._requestFailure = $"{status} {ReadMessage(message)}";
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static unsafe void OnError(ErrorType type, byte* message, void* userdata)
        {
            ManaConsole.WriteDiagnostic($"[WebGPU] {type}: {ReadMessage(message)}");
        }

        private static unsafe string ReadMessage(byte* message)
        {
            return message == null ? string.Empty : Marshal.PtrToStringUTF8((nint)message) ?? string.Empty;
        }

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
                    "opengl" or "gl" => throw new ArgumentException("OpenGL support has been removed. Choose Vulkan, Metal or Direct3D."),
                    "all" => InstanceBackend.All,
                    "primary" => InstanceBackend.Primary,
                    "secondary" => InstanceBackend.Secondary,
                    _ => InstanceBackend.Primary
                };
            }

            // Every Apple platform has only Metal: a Vulkan instance cannot make a surface on the phone's CAMetalLayer.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst())
            {
                return InstanceBackend.Metal;
            }

            return InstanceBackend.Vulkan;
        }
#else
        // The browser never uses the native request callbacks (an Emscripten dynCall bug breaks them): the adapter and
        // device come from JS promises through ManaBrowserWebGpuCallbackBridge, their ids standing in as the handles.
        // Request IDs route each result to this device only; a cancelled one is rejected, and JS releases it.
        private TaskCompletionSource<bool>? _adapterRequest;
        private TaskCompletionSource<bool>? _deviceRequest;
        private int _adapterRequestId;
        private int _deviceRequestId;
        private bool _cancelled;

        /// <summary>
        /// Requests the adapter and device, then runs <paramref name="ready"/>. On failure or cancellation runs
        /// <paramref name="failed"/> and rethrows.
        /// </summary>
        public async Task CompleteAsync(Action ready, Action failed)
        {
            try
            {
                ThrowIfCancelled();
                _adapterRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _adapterRequestId = ManaWebGpuInitializationRequests.Register(OnAdapterResult);
                ManaBrowserWebGpuCallbackBridge.RequestAdapter(_adapterRequestId);
                await _adapterRequest.Task;
                ThrowIfCancelled();

                _deviceRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _deviceRequestId = ManaWebGpuInitializationRequests.Register(OnDeviceResult);
                ManaBrowserWebGpuCallbackBridge.RequestDevice(AdapterId(), _deviceRequestId);
                await _deviceRequest.Task;
                ThrowIfCancelled();

                CompleteDevice();
                ready();
            }
            catch
            {
                failed();
                throw;
            }
            finally
            {
                EndRequests();
            }
        }

        /// <summary>Stops a pending bring-up: late results are rejected and released by JS.</summary>
        public void CancelInitialization()
        {
            _cancelled = true;
            EndRequests();
        }

        private void ThrowIfCancelled()
        {
            if (_cancelled)
            {
                throw new ObjectDisposedException(nameof(ManaWebGpuContext));
            }
        }

        private void EndRequests()
        {
            ManaWebGpuInitializationRequests.Cancel(_adapterRequestId);
            ManaWebGpuInitializationRequests.Cancel(_deviceRequestId);
            _adapterRequestId = 0;
            _deviceRequestId = 0;
            _adapterRequest?.TrySetCanceled();
            _deviceRequest?.TrySetCanceled();
        }

        private unsafe int AdapterId() => (int)(nint)Adapter;

        private unsafe bool OnAdapterResult(int adapterId)
        {
            if (_cancelled)
            {
                return false;
            }
            if (adapterId == 0)
            {
                _adapterRequest?.TrySetException(new InvalidOperationException("WebGPU adapter request failed (see browser console for the JS-side error)."));
                return true;
            }

            Adapter = (Adapter*)(nint)adapterId;
            _adapterRequest?.TrySetResult(true);
            return true;
        }

        private unsafe bool OnDeviceResult(int deviceId)
        {
            if (_cancelled)
            {
                return false;
            }
            if (deviceId == 0)
            {
                _deviceRequest?.TrySetException(new InvalidOperationException("WebGPU device request failed (see browser console for the JS-side error)."));
                return true;
            }

            Device = (Device*)(nint)deviceId;
            _deviceRequest?.TrySetResult(true);
            return true;
        }
#endif

        public unsafe void Dispose()
        {
#if MANA_BROWSER
            CancelInitialization();
#endif
            if (Queue != null)
            {
                _api.QueueRelease(Queue);
                Queue = null;
            }

            if (Device != null)
            {
                _api.DeviceRelease(Device);
                Device = null;
            }
#if !MANA_BROWSER
            // A view over the API's library for the released device, not a separate owner.
            NativeExtension = null;
#endif

            if (Adapter != null)
            {
                _api.AdapterRelease(Adapter);
                Adapter = null;
            }

            if (Surface != null)
            {
                _api.SurfaceRelease(Surface);
                Surface = null;
            }

            if (Instance != null)
            {
                _api.InstanceRelease(Instance);
                Instance = null;
            }
        }
    }
}
