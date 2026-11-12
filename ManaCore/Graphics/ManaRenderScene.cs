using System.Numerics;

namespace ManaCore.Graphics
{
    public sealed class ManaRenderScene
    {
        public required ManaCameraData Camera { get; set; }
        public required IReadOnlyList<ManaRenderMeshInstance> Meshes { get; init; }
        public IReadOnlyList<ManaGpuParticleEmitter> GpuParticles { get; init; } = [];
        public ManaIrradianceSettings IrradianceSettings { get; init; } = new();
        public ManaEnvironmentReflectionSource EnvironmentReflectionSource { get; init; } = new();
        public Vector4[] LightPositions { get; init; } = new Vector4[4];
        public Vector4[] LightColors { get; init; } = new Vector4[4];

        /// <summary>Placed lights a frame can draw; clustered lighting (<see cref="ManaLightClusters"/>) gives each pixel only those near it.</summary>
        public const int MaxLocalLights = 4096;

        // Lights placed in the scene -- lamps, torches, glows -- apart from the grid above: xyz position, w range;
        // colour pre-multiplied by intensity, its w the light's shadow slot plus one (0 casts none).
        public IReadOnlyList<(Vector4 Position, Vector4 Color)> LocalLights { get; init; } = [];

        /// <summary>Point lights that cast shadows this frame, nearest the camera first: at most this many.</summary>
        public const int MaxPointShadows = 8;

        /// <summary>Each shadow slot's light: xyz position, w range (LocalLights' colour w names the slot).</summary>
        public IReadOnlyList<Vector4> PointShadows { get; init; } = [];

        /// <summary>
        /// Gives the shadow-casting lights (<paramref name="casters"/>, indices into <paramref name="lights"/>) nearest the
        /// camera a slot each, marked in their colour's w as slot + 1, and returns the slots' lights for PointShadows.
        /// </summary>
        public static List<Vector4> AssignPointShadows(List<(Vector4 Position, Vector4 Color)> lights, List<int> casters, Vector3 camera)
        {
            var slots = new List<Vector4>();
            float Distance(int light) => Vector3.DistanceSquared(new Vector3(lights[light].Position.X, lights[light].Position.Y, lights[light].Position.Z), camera);
            casters.Sort((a, b) => Distance(a).CompareTo(Distance(b)));
            for (var i = 0; i < casters.Count && slots.Count < MaxPointShadows; i++)
            {
                var (position, color) = lights[casters[i]];
                lights[casters[i]] = (position, color with { W = slots.Count + 1 });
                slots.Add(position);
            }
            return slots;
        }

        // A single directional "sun" light, separate from the 4-slot point light grid above. At most
        // one is honored per frame -- see ManaWorld.CollectLightsFromNode.
        public bool HasSun { get; init; }
        public Vector3 SunDirection { get; init; } = -Vector3.UnitY;
        public Vector4 SunColor { get; init; } = Vector4.One;
        public bool SunCastsShadows { get; init; }

        /// <summary>The retro screen filter a RetroFilter script asks for; null shows the picture as usual.</summary>
        public ManaRetroFilterSettings? RetroFilter { get; init; }

        /// <summary>The post effects on this frame that cover only some meshes, a bit per slot: each gets a mask drawn.</summary>
        public byte ScopedPostEffects { get; init; }

        /// <summary>The level's baked light probes, when it has a ProbeVolume; null otherwise.</summary>
        public ManaProbeVolumeSettings? ProbeVolume { get; init; }

        /// <summary>The sky this frame -- see <see cref="ManaSky.Evaluate"/>.</summary>
        public ManaSky.State Sky { get; init; } = new(ManaSkyMode.Day, Vector3.UnitY, 1.0f, Vector3.One);

        /// <summary>The wind this frame -- see <see cref="ManaWind.Tick"/>.</summary>
        public ManaWind.State Wind { get; init; } = ManaWind.State.Still;
    }
}
