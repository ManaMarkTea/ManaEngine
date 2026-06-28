namespace ManaCore.Graphics
{
    public interface IManaEnvironmentReflectionMap : IDisposable
    {
        ManaGraphicsBackendKind Backend { get; }
        ManaEnvironmentReflectionSource Source { get; }
        IManaGpuTexture? EnvironmentMap { get; }
        IManaGpuTexture? IrradianceMap { get; }
        IManaGpuTexture? PrefilteredReflectionMap { get; }
        IManaGpuTexture? BrdfLut { get; }
        bool IsReady { get; }
    }
}
