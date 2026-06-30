namespace ManaCore.Graphics
{
    public interface IManaGpuTexture : IDisposable
    {
        ManaGraphicsBackendKind Backend { get; }
        uint Width { get; }
        uint Height { get; }
        bool IsLoaded { get; }
    }

    public interface IManaBindableGpuTexture : IManaGpuTexture
    {
        void Load();
        void UpdateTextureSize();
        void Bind(int channel);
        void UnBind(int channel);
    }
}
