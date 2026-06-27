namespace ManaCore.Graphics
{
    public interface IManaGpuMesh : IDisposable
    {
        ManaGraphicsBackendKind Backend { get; }
        uint IndexCount { get; }
    }
}
