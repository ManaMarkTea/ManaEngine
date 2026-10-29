using System.Numerics;

namespace ManaCore.Graphics
{
    public enum ManaSkyDaySystem
    {
        Solar,
        Sidereal
    }

    public sealed class ManaIrradianceSettings
    {
        public float Exposure { get; set; } = 0.92f;
        public float IblIntensity { get; set; } = 0.90f;
        public float LightIntensity { get; set; } = 1.35f;

        /// <summary>Screen-space ambient occlusion: how much creases and contacts darken the ambient light, 0 off.</summary>
        public float AmbientOcclusion { get; set; } = 1.5f;

        /// <summary>How far, in metres, a surface looks for what hides its sky: corners and contacts within this darken.</summary>
        public float AmbientOcclusionRadius { get; set; } = 0.5f;

        /// <summary>How edges are smoothed: SMAA by default (crisp, and smooths shadow edges too); FXAA is cheaper, MSAA sharper.</summary>
        public ManaAntiAliasing AntiAliasing { get; set; } = ManaAntiAliasing.Smaa;
        public Vector4 LightColor { get; set; } = Vector4.One;
        public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(0.35f, 0.82f, 0.45f));
        public bool DrawDebugWorld { get; set; }
        public float DebugMetallic { get; set; } = 1.0f;
        public float DebugAmbient { get; set; } = 0.25f;
        public float DebugRoughness { get; set; } = 0.1f;
        public Vector4 DebugAlbedo { get; set; } = Vector4.One;
        public float DebugSceneScale { get; set; } = 1.0f;
        public float WebGpuDebugMode { get; set; } = 0.0f;

        /// <summary>
        /// The PBR shader's debug views, in the order it numbers them (<see cref="WebGpuDebugMode"/>): the index is what
        /// reaches the GPU, so the order is not cosmetic.
        /// </summary>
        public static readonly string[] DebugModeNames =
        {
            "Off",
            "View Vector (V)",
            "World Normal (N)",
            "Reflection (R)",
            "Cubemap Dir",
            "Reflection Radiance",
            "BRDF Scale",
            "Linear Albedo",
            "F0",
            "Fresnel",
            "BRDF LUT",
            "Diffuse IBL",
            "Specular IBL",
            "Direct Lighting",
            "Final Linear",
            "Light Count",
            "Light Cells",
            "Shadow Cascades",
            "View Depth",
            "Ambient Occlusion"
        };

        public bool WireframeEnabled { get; set; }
        public bool VertexNormalsEnabled { get; set; }

        /// <summary>Length of each vertex-normal debug line, in world units.</summary>
        public float VertexNormalLength { get; set; } = 1.0f;

        /// <summary>
        /// What the background shows. Every mode but Photo is computed per pixel -- the environment image is only
        /// a 512-pixel cube and reads as a smear behind the scene. Reflections still use the image.
        /// </summary>
        public ManaSkyMode Sky { get; set; } = ManaSkyMode.Day;

        /// <summary>The simulated local date and wall-clock time; lunar phase is evaluated in UTC.</summary>
        public DateTimeOffset SkyDateTime { get; set; } = InitialSkyDateTime();

        /// <summary>Whether one sky rotation follows a solar day or a sidereal day.</summary>
        public ManaSkyDaySystem DaySystem { get; set; } = ManaSkyDaySystem.Solar;

        /// <summary>Where in the day the cycle is, 0..1: 0 is sunrise (6 am), 0.25 noon, 0.5 sunset, 0.75 midnight.</summary>
        public float TimeOfDay { get; set; } = 0.12f;

        /// <summary>
        /// How fast the cycle runs, in simulated seconds per real second: 60 is a minute a second, 3600 an hour a
        /// second. Negative runs backwards; 0 holds it still.
        /// </summary>
        public float TimeSpeed { get; set; } = 300.0f;

        /// <summary>Follows the wall clock instead: the sky is as it is outside right now.</summary>
        public bool RealTime { get; set; }

        /// <summary>Draws the constellation figures over the night sky's real stars (Day and Night only).</summary>
        public bool ConstellationLines { get; set; } = true;

        /// <summary>While true (the editor holds it with Alt), every constellation's lines show, not only those in view. Never saved.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool ShowAllConstellations { get; set; }

        /// <summary>The breeze, in metres a second, before gusts (<see cref="ManaWind"/>). 0 is a still day.</summary>
        public float WindSpeed { get; set; } = 2.5f;

        /// <summary>Where the wind blows toward, in degrees clockwise from -Z (north, seen from above).</summary>
        public float WindDirection { get; set; } = 60.0f;

        /// <summary>0 holds the breeze steady; 1 lulls and gusts to about twice it.</summary>
        public float Gustiness { get; set; } = 0.35f;

        // When ManaSky.Tick last moved the clock on, so each frame advances it by the time since.
        internal double LastTickSeconds = -1.0;

        // ManaWind.Tick's own clock and how far the air has carried things -- kept here so speed changes never jump them.
        internal double LastWindTickSeconds = -1.0;
        internal Vector2 WindDrift;
        internal float WindDistance;

        // How far the clouds have drifted, in seconds of wind -- see ManaSky.Tick.
        internal double CloudSeconds;

        private static DateTimeOffset InitialSkyDateTime()
        {
            var now = DateTimeOffset.Now;
            return new DateTimeOffset(now.Year, now.Month, now.Day, 8, 52, 48, now.Offset);
        }

        public ManaIrradianceSettings Clone()
        {
            return new ManaIrradianceSettings
            {
                Exposure = Exposure,
                IblIntensity = IblIntensity,
                LightIntensity = LightIntensity,
                AmbientOcclusion = AmbientOcclusion,
                AmbientOcclusionRadius = AmbientOcclusionRadius,
                AntiAliasing = AntiAliasing,
                LightColor = LightColor,
                LightDirection = LightDirection,
                DrawDebugWorld = DrawDebugWorld,
                DebugMetallic = DebugMetallic,
                DebugAmbient = DebugAmbient,
                DebugRoughness = DebugRoughness,
                DebugAlbedo = DebugAlbedo,
                DebugSceneScale = DebugSceneScale,
                WebGpuDebugMode = WebGpuDebugMode,
                WireframeEnabled = WireframeEnabled,
                VertexNormalsEnabled = VertexNormalsEnabled,
                VertexNormalLength = VertexNormalLength,
                Sky = Sky,
                SkyDateTime = SkyDateTime,
                DaySystem = DaySystem,
                TimeOfDay = TimeOfDay,
                TimeSpeed = TimeSpeed,
                RealTime = RealTime,
                WindSpeed = WindSpeed,
                WindDirection = WindDirection,
                Gustiness = Gustiness,
                ConstellationLines = ConstellationLines
            };
        }
    }
}
