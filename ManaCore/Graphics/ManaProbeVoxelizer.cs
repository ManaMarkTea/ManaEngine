using System.Collections.Concurrent;
using System.Numerics;
using ManaCore.Imaging;

namespace ManaCore.Graphics
{
    /// <summary>
    /// A probe volume's scenery cut into voxels: per voxel the surface's linear albedo and how much of it there is
    /// (alpha), its averaged normal, and what it gives off. Read on the CPU from the meshes the renderer already holds,
    /// colours from a small copy of each picture -- the probes' rays need the light a surface sends back, not detail.
    /// </summary>
    public sealed class ManaProbeVoxels
    {
        public required int X { get; init; }
        public required int Y { get; init; }
        public required int Z { get; init; }

        /// <summary>RGBA8, x fastest: linear albedo, alpha 255 where a surface passes through.</summary>
        public required byte[] Albedo { get; init; }

        /// <summary>RGBA8: the surfaces' mean normal as xyz * 0.5 + 0.5; a length near 0 is a wall seen from both sides.</summary>
        public required byte[] Normal { get; init; }

        /// <summary>RGBA16F: light given off, linear, in the shader's units.</summary>
        public required Half[] Emission { get; init; }

        public int Occupied { get; init; }

        public int Index(int x, int y, int z) => x + X * (y + Y * z);
    }

    public static class ManaProbeVoxelizer
    {
        // Colours are read from a picture this small: a voxel is a metre, a texel of this a few centimetres.
        private const int PictureSize = 64;

        /// <summary>
        /// Voxelizes every opaque or cut-out mesh in the box. Triangles are sampled at half-voxel steps, so a large
        /// floor fills every voxel it crosses and a small prop marks the one it sits in.
        /// </summary>
        public static ManaProbeVoxels Build(ManaProbeVolumeSettings volume, IReadOnlyList<ManaRenderMeshInstance> meshes, CancellationToken token = default)
        {
            var (nx, ny, nz) = volume.VoxelCounts;
            var count = nx * ny * nz;
            // rgb albedo, weight, normal xyz, emission rgb.
            var sums = new float[count * 10];
            var stripes = new object[Math.Max(1, count >> 12) + 1];
            for (var i = 0; i < stripes.Length; i++)
            {
                stripes[i] = new object();
            }

            var pictures = new ConcurrentDictionary<string, ManaImage?>();
            // Cubic voxels from the box's low corner; the grid may run a little past the box's far side.
            var scale = new Vector3(1.0f / MathF.Max(volume.VoxelSize, 0.05f));
            var step = volume.VoxelSize * 0.5f;
            var boxMin = volume.Min;
            var boxMax = volume.Min + volume.Size;

            Parallel.ForEach(meshes, new ParallelOptions { CancellationToken = token }, mesh =>
            {
                if (mesh.Unlit || mesh.IsTransparent || mesh.OpacityMode == ManaOpacityMode.Blend || mesh.OpacityMode == ManaOpacityMode.Additive)
                {
                    return;
                }
                var data = mesh.MeshData;
                if (data.Positions.Length == 0 || data.Indexes.Length < 3)
                {
                    return;
                }

                var world = mesh.WorldTransform;
                var albedoPicture = Picture(pictures, data.AlbedoTexturePath, data.AlbedoTextureBytes);
                var emissionPicture = Picture(pictures, data.EmissionTexturePath, null);
                var materialAlbedo = Linear(new Vector3(mesh.Albedo.X, mesh.Albedo.Y, mesh.Albedo.Z));
                var emissionColour = Linear(new Vector3(mesh.Emission.X, mesh.Emission.Y, mesh.Emission.Z)) * mesh.EmissionStrength;
                var glows = mesh.EmissionStrength > 0.0f && emissionColour.LengthSquared() > 1e-8f;
                var uvScale = new Vector2(mesh.UvTransform.X, mesh.UvTransform.Y);
                var uvOffset = new Vector2(mesh.UvTransform.Z, mesh.UvTransform.W);

                var positions = new Vector3[data.Positions.Length];
                var normals = new Vector3[data.Positions.Length];
                var anyInside = false;
                for (var i = 0; i < positions.Length; i++)
                {
                    positions[i] = Vector3.Transform(data.Positions[i], world);
                    var normal = i < data.Normals.Length ? Vector3.TransformNormal(data.Normals[i], world) : Vector3.UnitY;
                    normals[i] = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
                    anyInside |= positions[i].X >= boxMin.X && positions[i].Y >= boxMin.Y && positions[i].Z >= boxMin.Z
                        && positions[i].X <= boxMax.X && positions[i].Y <= boxMax.Y && positions[i].Z <= boxMax.Z;
                }
                if (!anyInside && positions.Length < 64)
                {
                    return;
                }

                for (var t = 0; t + 2 < data.Indexes.Length; t += 3)
                {
                    var ia = (int)data.Indexes[t];
                    var ib = (int)data.Indexes[t + 1];
                    var ic = (int)data.Indexes[t + 2];
                    if (ia >= positions.Length || ib >= positions.Length || ic >= positions.Length)
                    {
                        continue;
                    }
                    var a = positions[ia];
                    var b = positions[ib];
                    var c = positions[ic];
                    var longest = MathF.Max(Vector3.Distance(a, b), MathF.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
                    var k = Math.Clamp((int)MathF.Ceiling(longest / step), 1, 4096);
                    // Each sample stands for its share of the triangle's area: what tells a wall from a wire.
                    var sampleArea = Vector3.Cross(b - a, c - a).Length() * 0.5f / (k * (k + 1) * 0.5f);
                    for (var i = 0; i < k; i++)
                    {
                        for (var j = 0; j < k - i; j++)
                        {
                            // Points spread over the triangle, one per small cell of a k-by-k barycentric grid.
                            var u = (i + 1.0f / 3.0f) / k;
                            var v = (j + 1.0f / 3.0f) / k;
                            var w = 1.0f - u - v;
                            var p = a * w + b * u + c * v;
                            var cell = (p - boxMin) * scale;
                            var x = (int)MathF.Floor(cell.X);
                            var y = (int)MathF.Floor(cell.Y);
                            var z = (int)MathF.Floor(cell.Z);
                            if (x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz)
                            {
                                continue;
                            }

                            var uv = At(data.TextureCoordinates0, ia, Vector2.Zero) * w + At(data.TextureCoordinates0, ib, Vector2.Zero) * u
                                + At(data.TextureCoordinates0, ic, Vector2.Zero) * v;
                            uv = uv * uvScale + uvOffset;
                            var colour = At(data.Colors, ia, Vector4.One) * w + At(data.Colors, ib, Vector4.One) * u + At(data.Colors, ic, Vector4.One) * v;
                            var texel = Sample(albedoPicture, uv);
                            var albedo = Linear(new Vector3(colour.X, colour.Y, colour.Z)) * materialAlbedo * new Vector3(texel.X, texel.Y, texel.Z);
                            var coverage = texel.W * colour.W;
                            if (mesh.OpacityMode == ManaOpacityMode.Cutout && coverage < mesh.AlphaCutoff)
                            {
                                continue;
                            }
                            var normal = normals[ia] * w + normals[ib] * u + normals[ic] * v;
                            var emission = Vector3.Zero;
                            if (glows)
                            {
                                var source = albedo;
                                if (emissionPicture != null)
                                {
                                    var glow = Sample(emissionPicture, uv);
                                    source = new Vector3(glow.X, glow.Y, glow.Z);
                                }
                                emission = emissionColour * source;
                            }

                            var index = x + nx * (y + ny * z);
                            lock (stripes[index >> 12])
                            {
                                var o = index * 10;
                                sums[o] += albedo.X * sampleArea;
                                sums[o + 1] += albedo.Y * sampleArea;
                                sums[o + 2] += albedo.Z * sampleArea;
                                sums[o + 3] += sampleArea;
                                sums[o + 4] += normal.X * sampleArea;
                                sums[o + 5] += normal.Y * sampleArea;
                                sums[o + 6] += normal.Z * sampleArea;
                                sums[o + 7] += emission.X * sampleArea;
                                sums[o + 8] += emission.Y * sampleArea;
                                sums[o + 9] += emission.Z * sampleArea;
                            }
                        }
                    }
                }
            });

            var albedoBytes = new byte[count * 4];
            var normalBytes = new byte[count * 4];
            var emissionHalves = new Half[count * 4];
            var occupied = 0;
            // A voxel is solid to the probes' rays once half a face of surface runs through it (alpha 0.5 is a quarter):
            // a wall crossing it has a whole face or more; a wire, a leaf or a hanging lamp's chain does not.
            var solidArea = volume.VoxelSize * volume.VoxelSize * 0.5f;
            for (var index = 0; index < count; index++)
            {
                var o = index * 10;
                var weight = sums[o + 3];
                if (weight <= 1e-9f)
                {
                    normalBytes[index * 4] = 128;
                    normalBytes[index * 4 + 1] = 128;
                    normalBytes[index * 4 + 2] = 128;
                    continue;
                }
                var solidity = Math.Clamp(weight / solidArea, 0.0f, 1.0f);
                if (solidity >= 0.5f)
                {
                    occupied++;
                }
                albedoBytes[index * 4] = Byte(sums[o] / weight);
                albedoBytes[index * 4 + 1] = Byte(sums[o + 1] / weight);
                albedoBytes[index * 4 + 2] = Byte(sums[o + 2] / weight);
                albedoBytes[index * 4 + 3] = Byte(solidity);
                // Not normalized: opposite faces in one voxel cancel, and the shaders read the short normal as two-sided.
                var normal = new Vector3(sums[o + 4], sums[o + 5], sums[o + 6]) / weight;
                normalBytes[index * 4] = Byte(normal.X * 0.5f + 0.5f);
                normalBytes[index * 4 + 1] = Byte(normal.Y * 0.5f + 0.5f);
                normalBytes[index * 4 + 2] = Byte(normal.Z * 0.5f + 0.5f);
                normalBytes[index * 4 + 3] = 255;
                emissionHalves[index * 4] = (Half)(sums[o + 7] / weight);
                emissionHalves[index * 4 + 1] = (Half)(sums[o + 8] / weight);
                emissionHalves[index * 4 + 2] = (Half)(sums[o + 9] / weight);
            }

            return new ManaProbeVoxels { X = nx, Y = ny, Z = nz, Albedo = albedoBytes, Normal = normalBytes, Emission = emissionHalves, Occupied = occupied };
        }

        private static ManaImage? Picture(ConcurrentDictionary<string, ManaImage?> cache, string? path, byte[]? bytes)
        {
            string key;
            if (!string.IsNullOrEmpty(path))
            {
                key = "path:" + path;
            }
            else if (bytes is { Length: > 0 })
            {
                key = $"bytes:{bytes.Length}:{(bytes.Length > 64 ? BitConverter.ToInt64(bytes, bytes.Length / 2) : 0)}";
            }
            else
            {
                return null;
            }

            return cache.GetOrAdd(key, _ =>
            {
                try
                {
                    var source = bytes;
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (!File.Exists(path))
                        {
                            return null;
                        }
                        source = File.ReadAllBytes(path);
                    }
                    return Small(source!);
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
                {
                    return null;
                }
            });
        }

        // A DDS gives its own small mip; anything else is decoded and stepped down.
        private static ManaImage Small(byte[] bytes)
        {
            if (ManaDds.IsDds(bytes))
            {
                var dds = ManaDds.Read(bytes);
                var level = 0;
                while (level + 1 < dds.Levels.Length && ManaDds.LevelWidth(dds.Width, level) > PictureSize)
                {
                    level++;
                }
                return dds.Decode(level);
            }

            var image = ManaImageCodec.Load(bytes);
            var width = Math.Min(image.Width, PictureSize);
            var height = Math.Min(image.Height, PictureSize);
            var small = new ManaImage(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var from = ((y * image.Height / height) * image.Width + x * image.Width / width) * 4;
                    image.Pixels.AsSpan(from, 4).CopyTo(small.Pixels.AsSpan((y * width + x) * 4, 4));
                }
            }
            return small;
        }

        /// <summary>A picture's texel at an engine UV, linear rgb and alpha; white without a picture.</summary>
        private static Vector4 Sample(ManaImage? picture, Vector2 uv)
        {
            if (picture == null)
            {
                return Vector4.One;
            }
            // The engine flips V on import and flips pictures on upload; the CPU copy is unflipped, top row first.
            var u = uv.X - MathF.Floor(uv.X);
            var v = 1.0f - (uv.Y - MathF.Floor(uv.Y));
            var x = Math.Clamp((int)(u * picture.Width), 0, picture.Width - 1);
            var y = Math.Clamp((int)(v * picture.Height), 0, picture.Height - 1);
            var o = (y * picture.Width + x) * 4;
            var p = picture.Pixels;
            return new Vector4(SrgbToLinear(p[o] / 255.0f), SrgbToLinear(p[o + 1] / 255.0f), SrgbToLinear(p[o + 2] / 255.0f), p[o + 3] / 255.0f);
        }

        private static float SrgbToLinear(float value)
        {
            if (value <= 0.04045f)
            {
                return value / 12.92f;
            }
            return MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
        }

        // The shader's own vertex and material colour decode.
        private static Vector3 Linear(Vector3 colour) =>
            new(MathF.Pow(MathF.Max(colour.X, 0), 2.2f), MathF.Pow(MathF.Max(colour.Y, 0), 2.2f), MathF.Pow(MathF.Max(colour.Z, 0), 2.2f));

        private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);

        private static T At<T>(T[] values, int index, T fallback)
        {
            if (index < values.Length)
            {
                return values[index];
            }
            return fallback;
        }
    }
}
