namespace ManaCore.Graphics
{
    public enum ManaEnvironmentProjection
    {
        Equirectangular,
        CubeMap
    }

    public sealed class ManaEnvironmentReflectionSource
    {
        public const string DefaultSourcePath = "Assets/Textures/Environments/kloofendal_48d_partly_cloudy_puresky_4k.png";

        public string SourcePath { get; init; } = DefaultSourcePath;
        public ManaEnvironmentProjection Projection { get; init; } = ManaEnvironmentProjection.Equirectangular;
        public bool BuildReflectionCubeMap { get; init; } = true;
        public int EnvironmentCubeSize { get; init; } = 512;
        public int IrradianceCubeSize { get; init; } = 32;
        public int PrefilterCubeSize { get; init; } = 512;
        public int BrdfLutSize { get; init; } = 512;

        public ManaEnvironmentReflectionSource Clone()
        {
            return new ManaEnvironmentReflectionSource
            {
                SourcePath = SourcePath,
                Projection = Projection,
                BuildReflectionCubeMap = BuildReflectionCubeMap,
                EnvironmentCubeSize = EnvironmentCubeSize,
                IrradianceCubeSize = IrradianceCubeSize,
                PrefilterCubeSize = PrefilterCubeSize,
                BrdfLutSize = BrdfLutSize
            };
        }
    }
}
