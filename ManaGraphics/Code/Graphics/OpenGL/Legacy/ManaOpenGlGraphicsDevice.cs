using ManaCore.Graphics;
using Silk.NET.OpenGL;

namespace Mana.Code.Graphics
{
    public sealed class ManaOpenGlGraphicsDevice : IOpenGlGraphicsDevice, IDisposable
    {
        public ManaOpenGlGraphicsDevice(GL gl)
        {
            Gl = gl;
        }

        public ManaGraphicsBackendKind Backend => ManaGraphicsBackendKind.OpenGL;
        public string DisplayName => "OpenGL";
        public GL Gl { get; }

        public IManaImmediateGpuMesh CreateMesh(ManaContext context, ManaMeshCpuData meshData, BufferUsageARB usageHint = BufferUsageARB.StaticDraw)
        {
            var vertexBufferObject = new ManaVertexBufferObject(
                context,
                meshData.Indexes,
                meshData.Positions,
                meshData.Normals,
                meshData.Colors,
                meshData.TextureCoordinates0,
                meshData.TextureCoordinates1,
                usageHint);
            vertexBufferObject.ConstructVBO();
            return new ManaOpenGlMesh(vertexBufferObject);
        }

        public void Dispose()
        {
            Gl.Dispose();
        }
    }
}
