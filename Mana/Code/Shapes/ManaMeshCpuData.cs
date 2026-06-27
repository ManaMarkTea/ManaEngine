namespace Mana.Code.Shapes
{
    public sealed class ManaMeshCpuData
    {
        public required uint[] Indexes { get; init; }
        public required Vector3[] Positions { get; init; }
        public required Vector3[] Normals { get; init; }
        public required Vector4[] Colors { get; init; }
        public required Vector2[] TextureCoordinates0 { get; init; }
        public required Vector2[] TextureCoordinates1 { get; init; }
        public string? AlbedoTexturePath { get; init; }
        public Vector4? AlbedoFactor { get; init; }
    }
}