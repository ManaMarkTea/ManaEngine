namespace ManaCore.Graphics
{
    public enum ManaGraphicsBackendKind
    {
        None,
        OpenGL,
        WebGpu
    }

    public interface IManaGraphicsDevice
    {
        ManaGraphicsBackendKind Backend { get; }
        string DisplayName { get; }
    }
}
