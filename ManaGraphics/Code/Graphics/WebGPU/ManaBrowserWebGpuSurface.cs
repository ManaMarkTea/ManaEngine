#if MANA_BROWSER
using Silk.NET.WebGPU;
using Silk.NET.Windowing;

namespace ManaGraphics.Code.Graphics
{
    // Desktop's WebGPUSurface.CreateWebGPUSurface (Silk.NET.WebGPU.Extensions.WGPU) pulls a
    // native platform window handle (HWND/NSWindow/X11) that doesn't exist for a browser canvas.
    // The browser equivalent -- a WGPUSurfaceDescriptorFromCanvasHTMLSelector chained struct --
    // was proven working end to end in the M0 spike (see WebPort-Plan.md). Blocked here on two
    // things neither of which exist yet: the browser IWindow (M1, to get a canvas selector out
    // of `window`) and the wgpuInstanceCreateSurface/wgpuCreateInstance browser shim
    // (ManaBrowserWebGpuNativeContext, M2).
    public static unsafe class ManaBrowserWebGpuSurface
    {
        public static Surface* CreateFromCanvas(IWindow window, WebGPU api, Instance* instance)
        {
            throw new System.PlatformNotSupportedException(
                "Browser canvas surface creation not implemented yet -- needs the browser IWindow " +
                "(M1) and the WebGPU shim (M2). See WebPort-Plan.md.");
        }
    }
}
#endif
