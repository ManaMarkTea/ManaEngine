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

        /// <summary>Sample the maps without blending texels -- pixel art stays crisp instead of blurring.</summary>
        public bool NearestFiltering { get; init; }

        private (Vector3 Min, Vector3 Max)? _bounds;

        /// <summary>The box round <see cref="Positions"/>, worked out once (the data never changes after it is made).</summary>
        public (Vector3 Min, Vector3 Max) Bounds
        {
            get
            {
                if (_bounds is { } known)
                {
                    return known;
                }

                // Read as floats: the iPhone's AOT code loads a Vector3 element 16 bytes wide, past the array's end.
                var values = System.Runtime.InteropServices.MemoryMarshal.Cast<Vector3, float>(Positions.AsSpan());
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                for (var i = 0; i + 2 < values.Length; i += 3)
                {
                    min.X = MathF.Min(min.X, values[i]);
                    min.Y = MathF.Min(min.Y, values[i + 1]);
                    min.Z = MathF.Min(min.Z, values[i + 2]);
                    max.X = MathF.Max(max.X, values[i]);
                    max.Y = MathF.Max(max.Y, values[i + 1]);
                    max.Z = MathF.Max(max.Z, values[i + 2]);
                }
                if (values.Length < 3)
                {
                    min = Vector3.Zero;
                    max = Vector3.Zero;
                }
                _bounds = (min, max);
                return (min, max);
            }
        }

        /// <summary>The box round this mesh placed by <paramref name="world"/>, from its own box's eight corners.</summary>
        public (Vector3 Min, Vector3 Max) WorldBounds(Matrix4x4 world)
        {
            var (min, max) = Bounds;
            var worldMin = new Vector3(float.MaxValue);
            var worldMax = new Vector3(float.MinValue);
            for (var corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? min.X : max.X,
                    (corner & 2) == 0 ? min.Y : max.Y,
                    (corner & 4) == 0 ? min.Z : max.Z);
                var placed = Vector3.Transform(point, world);
                worldMin = Vector3.Min(worldMin, placed);
                worldMax = Vector3.Max(worldMax, placed);
            }
            return (worldMin, worldMax);
        }
    }
}
