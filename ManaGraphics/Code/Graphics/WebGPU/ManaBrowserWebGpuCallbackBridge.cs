#if MANA_BROWSER
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;

namespace ManaGraphics.Code.Graphics
{
    // Bridges wgpuInstanceRequestAdapter/wgpuAdapterRequestDevice around a confirmed Emscripten
    // runtime bug in their normal callback-delivery path (RuntimeError: getWasmTableEntry(...)
    // is not a function; see mana_webgpu.c's mana_request_adapter/mana_request_device for the
    // details). Called directly via [LibraryImport], not resolved through ManaBrowserWebGpuNativeContext -- these
    // aren't part of Silk.NET.WebGPU's API surface at all, just a private detour around it.
    //
    // Request IDs keep late promises from routing into a fresh context; JS releases rejected handles.
    public static partial class ManaBrowserWebGpuCallbackBridge
    {
        [LibraryImport("mana_webgpu", EntryPoint = "mana_request_adapter")]
        internal static partial void RequestAdapter(int requestId);

        [LibraryImport("mana_webgpu", EntryPoint = "mana_request_device")]
        internal static partial void RequestDevice(int adapterId, int requestId);

        // Diagnostic only -- see mana_webgpu.c's mana_log_shader_compilation_info_impl. No C#-side
        // handler needed: it just logs straight to the browser console.
        [LibraryImport("mana_webgpu", EntryPoint = "mana_log_shader_compilation_info")]
        internal static partial void LogShaderCompilationInfo(int shaderModuleId);

        // Diagnostic only -- see mana_webgpu.c's mana_debug_readback_buffer_impl.
        [LibraryImport("mana_webgpu", EntryPoint = "mana_debug_readback_buffer")]
        internal static partial void DebugReadbackBuffer(int deviceId, int sourceBufferId, int byteSize);

        [JSExport]
        internal static bool OnAdapterResult(int requestId, int adapterId) =>
            ManaWebGpuInitializationRequests.Complete(requestId, adapterId);

        [JSExport]
        internal static bool OnDeviceResult(int requestId, int deviceId) =>
            ManaWebGpuInitializationRequests.Complete(requestId, deviceId);
    }
}
#endif
