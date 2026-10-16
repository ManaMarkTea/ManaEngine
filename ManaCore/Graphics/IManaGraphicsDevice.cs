namespace ManaCore.Graphics
{
    public enum ManaGraphicsBackendKind
    {
        None,
        WebGpu = 2
    }

    public interface IManaGraphicsDevice
    {
        ManaGraphicsBackendKind Backend { get; }
        string DisplayName { get; }
    }
}
