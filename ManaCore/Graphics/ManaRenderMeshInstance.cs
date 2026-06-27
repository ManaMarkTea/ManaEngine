using System.Numerics;

namespace ManaCore.Graphics
{
    public sealed class ManaRenderMeshInstance
    {
        public required string Name { get; init; }
        public required ManaMeshCpuData MeshData { get; init; }
        public required Matrix4x4 WorldTransform { get; init; }
        public string? ShaderName { get; init; }
    }
}
