using System.Numerics;

namespace ManaCore.Graphics
{
    public enum ManaEnvironmentProjection
    {
        Equirectangular,
        CubeMap
    }

    public enum ManaEnvironmentSourceKind
    {
        Image,
        SceneCapture
    }

    public sealed class ManaEnvironmentReflectionSource
    {
        public const string DefaultSourcePath = "Assets/Textures/Environments/kloofendal_48d_partly_cloudy_puresky_4k.png";

        public ManaEnvironmentSourceKind SourceKind { get; init; } = ManaEnvironmentSourceKind.SceneCapture;
        public string SourcePath { get; init; } = DefaultSourcePath;
        public ManaEnvironmentProjection Projection { get; init; } = ManaEnvironmentProjection.Equirectangular;
        public bool BuildReflectionCubeMap { get; init; } = true;
        public int EnvironmentCubeSize { get; init; } = 512;
        public int IrradianceCubeSize { get; init; } = 32;
        public int PrefilterCubeSize { get; init; } = 512;
        public int BrdfLutSize { get; init; } = 512;
        public Vector3 SceneCapturePosition { get; init; } = new(128.0f, 32.0f, 128.0f);
        public float SceneCaptureNearPlane { get; init; } = 0.1f;
        public float SceneCaptureFarPlane { get; init; } = 512.0f;
        public int SceneCaptureRefreshIntervalFrames { get; init; } = 15;

        public ManaEnvironmentReflectionSource Clone()
        {
            return new ManaEnvironmentReflectionSource
            {
                SourceKind = SourceKind,
                SourcePath = SourcePath,
                Projection = Projection,
                BuildReflectionCubeMap = BuildReflectionCubeMap,
                EnvironmentCubeSize = EnvironmentCubeSize,
                IrradianceCubeSize = IrradianceCubeSize,
                PrefilterCubeSize = PrefilterCubeSize,
                BrdfLutSize = BrdfLutSize,
                SceneCapturePosition = SceneCapturePosition,
                SceneCaptureNearPlane = SceneCaptureNearPlane,
                SceneCaptureFarPlane = SceneCaptureFarPlane,
                SceneCaptureRefreshIntervalFrames = SceneCaptureRefreshIntervalFrames
            };
        }
    }
}
