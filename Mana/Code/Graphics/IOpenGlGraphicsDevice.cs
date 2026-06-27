using ManaCore.Graphics;
using Silk.NET.OpenGL;

namespace Mana.Code.Graphics
{
    public interface IOpenGlGraphicsDevice : IManaGraphicsDevice
    {
        GL Gl { get; }
        IOpenGlGpuMesh CreateMesh(ManaContext context, ManaMeshCpuData meshData, BufferUsageARB usageHint = BufferUsageARB.StaticDraw);
    }
}
