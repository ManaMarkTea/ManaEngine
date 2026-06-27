using ManaCore.Graphics;

namespace Mana.Code.Graphics
{
    public interface IOpenGlGpuTexture : IManaGpuTexture
    {
        uint Handle { get; }
        void Load();
        void UpdateTextureSize();
        void Bind(int channel);
        void UnBind(int channel);
    }
}
