using ManaCore.Diagnostics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics;

public unsafe sealed partial class ManaWebGpuContext
{
    public long LastGpuParticleCount => _particlePass?.Drawn ?? 0;
    public long RequestedGpuParticleCount => _particlePass?.Requested ?? 0;

    private static bool UseMobileRendering
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

    /// <summary>The sky shader: 0 simple gradients, 1 those plus the real star catalogue, 2 the full atmosphere.</summary>
    private static int SkyMode
    {
        get
        {
#if MANA_BROWSER
            return Browser.ManaBrowserInterop.GetSkyMode();
#else
            return 2;
#endif
        }
    }
}
