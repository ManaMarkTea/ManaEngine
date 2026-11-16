using System.Numerics;

namespace ManaCore.Graphics
{
    public sealed class ManaMeshCpuData
    {
        public required uint[] Indexes { get; init; }
        public required Vector3[] Positions { get; init; }
        public required Vector3[] Normals { get; init; }
        public required Vector4[] Colors { get; init; }

        /// <summary>
        /// What the GPU's vertex colour carries in place of <see cref="Colors"/>, when its shader reads something else
        /// there (a terrain's four layer weights). Everything on the CPU -- light probes, picking -- keeps the colours.
        /// </summary>
        public Vector4[]? ShaderColors { get; init; }
        public required Vector2[] TextureCoordinates0 { get; init; }
        public required Vector2[] TextureCoordinates1 { get; init; }
        public string? AlbedoTexturePath { get; init; }
        public byte[]? AlbedoTextureBytes { get; init; }

        /// <summary>A picture redrawn as the game runs, used over the path and bytes when set (a live pixel effect).</summary>
        public ManaDynamicTexture? AlbedoDynamicTexture { get; init; }
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

        private ulong? _geometryHash;
        private static long _nextUniqueHash;

        /// <summary>
        /// Geometry rewritten every frame (aircraft markers, particles made on the CPU): it never matches another mesh,
        /// so it is given a hash of its own instead of having megabytes of vertices fingerprinted per frame.
        /// </summary>
        public bool Unshared { get; init; }

        /// <summary>
        /// A fingerprint of the geometry -- every vertex stream and the indices -- so two meshes built alike (a thousand
        /// lamps from one shape) can be drawn as one instanced draw. Worked out once; the data never changes.
        /// </summary>
        public ulong GeometryHash
        {
            get
            {
                if (_geometryHash is { } known)
                {
                    return known;
                }

                if (Unshared)
                {
                    // Odd numbers from the top of the range, apart from any fingerprint likely to come up.
                    var unique = ulong.MaxValue - 2UL * (ulong)Interlocked.Increment(ref _nextUniqueHash);
                    _geometryHash = unique;
                    return unique;
                }

                var hash = 14695981039346656037UL;
                Mix(ref hash, Positions);
                Mix(ref hash, Normals);
                Mix(ref hash, Colors);
                Mix(ref hash, TextureCoordinates0);
                Mix(ref hash, TextureCoordinates1);
                Mix(ref hash, Indexes);
                _geometryHash = hash;
                return hash;
            }
        }

        // Eight bytes a step with a multiply-and-fold after each, the length mixed in first so arrays that merely run
        // into each other differ. Byte-at-a-time FNV took 30 ms a frame on a 100k-vertex mesh.
        private static void Mix<T>(ref ulong hash, T[] values) where T : unmanaged
        {
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan());
            hash = Step(hash, (ulong)bytes.Length);
            var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>(bytes);
            foreach (var word in words)
            {
                hash = Step(hash, word);
            }

            for (var i = words.Length * 8; i < bytes.Length; i++)
            {
                hash = Step(hash, bytes[i]);
            }
        }

        private static ulong Step(ulong hash, ulong word)
        {
            hash = (hash ^ word) * 0x9E3779B97F4A7C15UL;
            return hash ^ (hash >> 29);
        }

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
