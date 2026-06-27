using ManaCore.Graphics;

namespace Mana.Code.Graphics
{
    public sealed class ManaOpenGlMesh : IOpenGlGpuMesh
    {
        public ManaOpenGlMesh(ManaVertexBufferObject vertexBufferObject)
        {
            VertexBufferObject = vertexBufferObject;
        }

        public ManaGraphicsBackendKind Backend => ManaGraphicsBackendKind.OpenGL;
        public uint IndexCount => (uint)VertexBufferObject.indexes.Length;
        public ManaVertexBufferObject VertexBufferObject { get; }

        public void Draw()
        {
            VertexBufferObject.Bind();
            VertexBufferObject.DrawElements();
            VertexBufferObject.UnBind();
        }

        public void UpdateDynamicData(Vector3[] positions, Vector3[] normals, Vector4[] colors)
        {
            VertexBufferObject.UpdateDynamicData(positions, normals, colors);
        }

        public void Dispose()
        {
        }
    }
}
