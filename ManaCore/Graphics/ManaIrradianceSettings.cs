using System.Numerics;

namespace ManaCore.Graphics
{
    public sealed class ManaIrradianceSettings
    {
        public float Exposure { get; set; } = 0.92f;
        public float IblIntensity { get; set; } = 0.90f;
        public float LightIntensity { get; set; } = 1.35f;
        public Vector4 LightColor { get; set; } = Vector4.One;
        public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(0.35f, 0.82f, 0.45f));
        public bool DrawDebugWorld { get; set; }
        public float DebugMetallic { get; set; } = 1.0f;
        public float DebugAmbient { get; set; } = 0.25f;
        public float DebugRoughness { get; set; } = 0.1f;
        public Vector4 DebugAlbedo { get; set; } = Vector4.One;
        public float DebugSceneScale { get; set; } = 1.0f;
        public float WebGpuDebugMode { get; set; } = 0.0f;
        public bool WireframeEnabled { get; set; }
        public bool VertexNormalsEnabled { get; set; }

        /// <summary>Length of each vertex-normal debug line, in world units.</summary>
        public float VertexNormalLength { get; set; } = 1.0f;

        public ManaIrradianceSettings Clone()
        {
            return new ManaIrradianceSettings
            {
                Exposure = Exposure,
                IblIntensity = IblIntensity,
                LightIntensity = LightIntensity,
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
                VertexNormalLength = VertexNormalLength
            };
        }
    }
}
