namespace ManaCore.Graphics
{
    public sealed class ManaEnvironmentReflectionMap : IManaEnvironmentReflectionMap
    {
        public ManaEnvironmentReflectionMap(
            ManaGraphicsBackendKind backend,
            ManaEnvironmentReflectionSource source,
            IManaGpuTexture? environmentMap,
            IManaGpuTexture? irradianceMap,
            IManaGpuTexture? prefilteredReflectionMap,
            IManaGpuTexture? brdfLut)
        {
            Backend = backend;
            Source = source.Clone();
            EnvironmentMap = environmentMap;
            IrradianceMap = irradianceMap;
            PrefilteredReflectionMap = prefilteredReflectionMap;
            BrdfLut = brdfLut;
        }

        public ManaGraphicsBackendKind Backend { get; }
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
