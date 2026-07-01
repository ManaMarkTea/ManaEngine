using System.Numerics;

namespace ManaCore.Graphics
{
    public sealed class ManaRenderMeshInstance
    {
        public string? InstanceKey { get; init; }
        public string? MeshKey { get; init; }
        public ulong MeshVersion { get; init; }
        public required string Name { get; init; }
        public required ManaMeshCpuData MeshData { get; init; }
        public required Matrix4x4 WorldTransform { get; init; }
        public string? ShaderName { get; init; }
        public Vector4 Albedo { get; init; } = Vector4.One;
        public float Metallic { get; init; }
        public float Roughness { get; init; } = 1.0f;
        public float AmbientOcclusion { get; init; } = 1.0f;
        public Vector4 Emission { get; init; } = Vector4.Zero;
        public float EmissionStrength { get; init; }
        public float? PbrDebugMode { get; init; }
        public bool IncludeInEnvironmentCapture { get; init; } = true;
        public bool IsTransparent { get; init; }
        public bool DisableDepthWrite { get; init; }
        public bool OutlineEnabled { get; init; }
        public Vector4 OutlineColor { get; init; } = new(1.0f, 0.82f, 0.18f, 1.0f);
        public float OutlineWidth { get; init; } = 2.0f;
    }
}
