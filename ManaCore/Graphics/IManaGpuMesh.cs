using System.Numerics;

namespace ManaCore.Graphics
{
    public interface IManaGpuMesh : IDisposable
    {
        ManaGraphicsBackendKind Backend { get; }
        uint IndexCount { get; }
        uint VertexCount { get; }
    }

    public interface IManaImmediateGpuMesh : IManaGpuMesh
    {
        void Draw();
        void UpdateDynamicData(
            Vector3[] positions,
            Vector3[] normals,
            Vector4[] colors);
    }
}
