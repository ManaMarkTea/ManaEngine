using System.Numerics;

namespace ManaCore.Graphics
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
        public byte[]? AlbedoTextureBytes { get; init; }
        public Vector4? AlbedoFactor { get; init; }

        // The rest of the PBR maps. Separate paths rather than one packed texture, because that is how a
        // material stores them and how anyone picking one thinks about them; the backend is free to bind
        // a neutral 1x1 for any that are absent.
        //
        // Only albedo and emission are colour. The others carry measurements -- a direction, a
        // coefficient, a coverage -- so a backend must upload them as linear data rather than sRGB or the
        // transfer curve silently corrupts the values.
        public string? NormalTexturePath { get; init; }
        public string? EmissionTexturePath { get; init; }
        public string? MetallicTexturePath { get; init; }
        public string? RoughnessTexturePath { get; init; }
        public string? OcclusionTexturePath { get; init; }
        public string? OpacityTexturePath { get; init; }
    }
}
