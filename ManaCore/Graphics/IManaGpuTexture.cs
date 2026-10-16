namespace ManaCore.Graphics
{
    public interface IManaGpuTexture : IDisposable
    {
        ManaGraphicsBackendKind Backend { get; }
        uint Width { get; }
        uint Height { get; }
        bool IsLoaded { get; }
    }
}
