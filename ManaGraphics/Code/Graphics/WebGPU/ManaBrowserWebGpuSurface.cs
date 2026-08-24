#if MANA_BROWSER
using System.Text;
using ManaGraphics.Code.Graphics.Browser;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;

namespace ManaGraphics.Code.Graphics
{
    // Desktop's WebGPUSurface.CreateWebGPUSurface (Silk.NET.WebGPU.Extensions.WGPU) pulls a
    // native platform window handle (HWND/NSWindow/X11) that doesn't exist for a browser canvas.
    // This is the browser equivalent -- a WGPUSurfaceDescriptorFromCanvasHtmlSelector chained
    // struct passed to wgpuInstanceCreateSurface -- proven working end to end in the M0 spike
    // (see WebPort-Plan.md). Not yet confirmed working through this exact real-package path
    // (M0 used a hand-written throwaway C program, not Silk.NET.WebGPU's own struct/SType).
    public static unsafe class ManaBrowserWebGpuSurface
    {
        public static Surface* CreateFromCanvas(IWindow window, WebGPU api, Instance* instance)
        {
            var selector = window is ManaBrowserWindow browserWindow ? browserWindow.CanvasSelector : "#manaCanvas";
            System.Console.WriteLine($"[ManaBrowserWebGpuSurface] selector='{selector}', instance=0x{(nint)instance:X}");
            var selectorBytes = Encoding.UTF8.GetBytes(selector + "\0");
            fixed (byte* selectorPtr = selectorBytes)
            {
                var canvasChain = new SurfaceDescriptorFromCanvasHTMLSelector
                {
                    Chain = new ChainedStruct
                    {
                        SType = SType.SurfaceDescriptorFromCanvasHtmlSelector
                    },
                    Selector = selectorPtr
                };

                var descriptor = new SurfaceDescriptor
                {
                    NextInChain = (ChainedStruct*)&canvasChain
                };

                System.Console.WriteLine("[ManaBrowserWebGpuSurface] Calling api.InstanceCreateSurface...");
                var result = api.InstanceCreateSurface(instance, &descriptor);
                System.Console.WriteLine($"[ManaBrowserWebGpuSurface] InstanceCreateSurface returned: 0x{(nint)result:X}");
                return result;
            }
        }
    }
}
#endif
