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

        // A single directional "sun" light, separate from the 4-slot point light grid above. At most
        // one is honored per frame -- see ManaWorld.CollectLightsFromNode.
        public bool HasSun { get; init; }
        public Vector3 SunDirection { get; init; } = -Vector3.UnitY;
        public Vector4 SunColor { get; init; } = Vector4.One;
        public bool SunCastsShadows { get; init; }
    }
}
