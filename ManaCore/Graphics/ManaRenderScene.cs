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

        public const int MaxLocalLights = 16;

        // Lights placed in the scene -- lamps, torches -- apart from the grid above: xyz position, w range.
        // Colours are pre-multiplied by intensity; an unused slot is black.
        public Vector4[] LocalLightPositions { get; init; } = new Vector4[MaxLocalLights];
        public Vector4[] LocalLightColors { get; init; } = new Vector4[MaxLocalLights];

        // A single directional "sun" light, separate from the 4-slot point light grid above. At most
        // one is honored per frame -- see ManaWorld.CollectLightsFromNode.
        public bool HasSun { get; init; }
        public Vector3 SunDirection { get; init; } = -Vector3.UnitY;
        public Vector4 SunColor { get; init; } = Vector4.One;
        public bool SunCastsShadows { get; init; }

        /// <summary>The sky this frame -- see <see cref="ManaSky.Evaluate"/>.</summary>
        public ManaSky.State Sky { get; init; } = new(ManaSkyMode.Day, Vector3.UnitY, 1.0f, Vector3.One);
    }
}
