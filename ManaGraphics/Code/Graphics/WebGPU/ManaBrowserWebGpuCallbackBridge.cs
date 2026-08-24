#if MANA_BROWSER
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;

namespace ManaGraphics.Code.Graphics
{
    // Bridges wgpuInstanceRequestAdapter/wgpuAdapterRequestDevice around a confirmed Emscripten
    // runtime bug in their normal callback-delivery path (RuntimeError: getWasmTableEntry(...)
    // is not a function; see mana_webgpu.c's mana_request_adapter/mana_request_device for the
    // full writeup and WebPort-Plan.md M2). Called directly via [LibraryImport] (like the M0
    // spike's plain functions), not resolved through ManaBrowserWebGpuNativeContext -- these
    // aren't part of Silk.NET.WebGPU's API surface at all, just a private detour around it.
    //
    // [JSExport] methods must be static, so this routes results to whichever ManaWebGpuContext
    // is currently mid-request via a single static "current" slot -- safe because only one
    // browser WebGPU context is ever live at a time (same pattern as ManaBrowserWindow.Current).
    public static partial class ManaBrowserWebGpuCallbackBridge
    {
        internal static ManaWebGpuContext? PendingContext;

        [LibraryImport("mana_webgpu", EntryPoint = "mana_request_adapter")]
        internal static partial void RequestAdapter();

        [LibraryImport("mana_webgpu", EntryPoint = "mana_request_device")]
        internal static partial void RequestDevice(int adapterId);

        // Diagnostic only -- see mana_webgpu.c's mana_log_shader_compilation_info_impl. No C#-side
        // handler needed: it just logs straight to the browser console.
        [LibraryImport("mana_webgpu", EntryPoint = "mana_log_shader_compilation_info")]
        internal static partial void LogShaderCompilationInfo(int shaderModuleId);

        [JSExport]
        internal static void OnAdapterResult(int adapterId)
        {
            PendingContext?.OnAdapterResultFromJs(adapterId);
        }

        [JSExport]
        internal static void OnDeviceResult(int deviceId)
        {
            PendingContext?.OnDeviceResultFromJs(deviceId);
        }
    }
}
#endif
