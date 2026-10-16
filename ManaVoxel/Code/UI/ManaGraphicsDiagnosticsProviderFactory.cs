using Mana.Code.Graphics;
using ManaCore.Graphics;

namespace ManaVoxel.Code.UI
{
    public static class ManaGraphicsDiagnosticsProviderFactory
    {
        public static ManaGraphicsDiagnosticsProvider? Create(
            ManaContext context,
            ManaWebGpuContext? webGpuContext)
        {
            if (webGpuContext != null)
            {
                return webGpuContext;
            }


            return null;
        }
    }
}
