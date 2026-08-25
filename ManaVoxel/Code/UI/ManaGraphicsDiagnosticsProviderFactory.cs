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

#if !MANA_BROWSER
            if (context.OpenGl != null)
            {
                return context.GameMode.GraphicsDiagnosticsProvider ??
                    new ManaOpenGlDiagnosticsProvider(context);
            }
#endif

            return null;
        }
    }
}
