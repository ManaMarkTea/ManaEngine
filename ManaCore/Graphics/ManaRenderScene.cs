using System.Numerics;

namespace ManaCore.Graphics
{
    public sealed class ManaRenderScene
    {
        public required ManaCameraData Camera { get; set; }
        public required IReadOnlyList<ManaRenderMeshInstance> Meshes { get; init; }
        public ManaIrradianceSettings IrradianceSettings { get; init; } = new();
        public ManaEnvironmentReflectionSource EnvironmentReflectionSource { get; init; } = new();
        public Vector4[] LightPositions { get; init; } = new Vector4[4];
        public Vector4[] LightColors { get; init; } = new Vector4[4];
    }
}
