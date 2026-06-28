using ManaCore.Graphics;

namespace Mana.Code.Graphics
{
    public sealed class ManaOpenGlEnvironmentReflectionMap : IManaEnvironmentReflectionMap
    {
        public ManaOpenGlEnvironmentReflectionMap(
            ManaEnvironmentReflectionSource source,
            ManaTexture? environmentMap,
            ManaTexture? irradianceMap,
            ManaTexture? prefilteredReflectionMap,
            ManaTexture? brdfLut)
        {
            Source = source.Clone();
            EnvironmentMap = environmentMap?.GpuTexture;
            IrradianceMap = irradianceMap?.GpuTexture;
            PrefilteredReflectionMap = prefilteredReflectionMap?.GpuTexture;
            BrdfLut = brdfLut?.GpuTexture;
        }

        public ManaGraphicsBackendKind Backend => ManaGraphicsBackendKind.OpenGL;
        public ManaEnvironmentReflectionSource Source { get; }
        public IManaGpuTexture? EnvironmentMap { get; }
        public IManaGpuTexture? IrradianceMap { get; }
        public IManaGpuTexture? PrefilteredReflectionMap { get; }
        public IManaGpuTexture? BrdfLut { get; }
        public bool IsReady => EnvironmentMap?.IsLoaded == true &&
            IrradianceMap?.IsLoaded == true &&
            PrefilteredReflectionMap?.IsLoaded == true &&
            BrdfLut?.IsLoaded == true;

        public void Dispose()
        {
        }
    }
}
