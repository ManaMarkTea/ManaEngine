#if MANA_BROWSER
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;

namespace ManaGraphics.Code.Graphics
{
    // Requests the adapter and device through plain JS, around an Emscripten dynCall bug in the normal callback
    // path. Request IDs keep a late promise out of a fresh context; JS releases rejected handles.
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
