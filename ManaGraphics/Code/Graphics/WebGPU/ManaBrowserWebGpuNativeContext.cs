#if MANA_BROWSER
using Silk.NET.Core.Contexts;

namespace ManaGraphics.Code.Graphics
{
    // Bridges Silk.NET.WebGPU's real, unmodified API onto browser-wasm. Silk.NET's own default
    // loader (WebGPULibraryNameContainer) dynamically loads a native shared-library file at
    // runtime, which doesn't exist as a separate artifact in a statically linked browser-wasm
    // binary -- see WebPort-Plan.md M2 for the full writeup, including a proven, working recipe
    // for exactly this (a forwarding shim per wgpu* function in a NativeFileReference'd .c file,
    // a "registration manifest" of throwaway [LibraryImport] declarations so the pinvoke table
    // generator keeps them, and NativeLibrary.Load/GetExport here to resolve them dynamically).
    //
    // STUB: the shim itself (the ~70 forwarding functions this needs, enumerated in the plan)
    // has NOT been generated yet. Every lookup here fails until it is. Deliberately left this way
    // rather than generating ~70 functions blind: several of them (InstanceRequestAdapter,
    // AdapterRequestDevice, DeviceSetUncapturedErrorCallback, BufferMapAsync) take callback
    // function-pointer parameters, and the M0 spike only proved the mechanism for plain
    // synchronous functions -- the callback ABI needs its own live-browser check before scaling
    // the pattern up, not a blind transcription of 70 signatures with no way to verify them.
    public sealed class ManaBrowserWebGpuNativeContext : INativeContext
    {
        public nint GetProcAddress(string proc, int? slot = null)
        {
            throw new System.PlatformNotSupportedException(
                $"Browser WebGPU shim for '{proc}' not implemented yet -- see WebPort-Plan.md M2.");
        }

        public bool TryGetProcAddress(string proc, out nint addr, int? slot = null)
        {
            addr = 0;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
#endif
