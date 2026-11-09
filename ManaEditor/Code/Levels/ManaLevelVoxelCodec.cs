using Mana;

namespace ManaEditor.Code.Levels
{
    /// <summary>
    /// Packs and unpacks a voxel grid for <see cref="ManaLevelVoxels"/>.
    /// <para>
    /// Coordinates are stored as the same half-unit grid key <see cref="ManaVoxelShape"/> already uses to
    /// decide whether two voxels are the same voxel, so nothing the shape can tell apart is lost, and
    /// nothing it cannot is wasted. Colours go through a palette because voxel art has a handful of them
    /// and hundreds of thousands of voxels.
    /// </para>
    /// </summary>
    public static class ManaLevelVoxelCodec
    {
        private const int BytesPerVoxel = 8;

        // Half-unit keys, so this is +-16383 world units of voxel grid -- far past the 256-unit editor
        // grid, and a clear error rather than silent wraparound if something ever exceeds it.
        private const int MaxKey = short.MaxValue;

        public static ManaLevelVoxels Encode(IReadOnlyList<ManaVoxelData> voxels)
        {
            var palette = new List<float[]>();
            var paletteIndexByColor = new Dictionary<(float R, float G, float B, float A), int>();
            var data = new byte[voxels.Count * BytesPerVoxel];

            for (var i = 0; i < voxels.Count; i++)
            {
                var voxel = voxels[i];
                var colorKey = (voxel.Color.X, voxel.Color.Y, voxel.Color.Z, voxel.Color.W);
                if (!paletteIndexByColor.TryGetValue(colorKey, out var paletteIndex))
                {
                    if (palette.Count > ushort.MaxValue)
                    {
                        throw new InvalidDataException(
                            $"Voxel grid uses more than {ushort.MaxValue} distinct colours, which the level format cannot index.");
                    }

                    paletteIndex = palette.Count;
                    palette.Add(new[] { voxel.Color.X, voxel.Color.Y, voxel.Color.Z, voxel.Color.W });
                    paletteIndexByColor[colorKey] = paletteIndex;
                }

                var offset = i * BytesPerVoxel;
                WriteKey(data, offset, voxel.Position.X);
                WriteKey(data, offset + 2, voxel.Position.Y);
                WriteKey(data, offset + 4, voxel.Position.Z);
                BitConverter.TryWriteBytes(data.AsSpan(offset + 6, 2), (ushort)paletteIndex);
            }

            return new ManaLevelVoxels
            {
                Palette = palette,
                Count = voxels.Count,
                Data = Convert.ToBase64String(data)
            };
        }

        public static List<ManaVoxelData> Decode(ManaLevelVoxels encoded)
        {
            var data = Convert.FromBase64String(encoded.Data);
            var count = data.Length / BytesPerVoxel;
            if (encoded.Count != 0 && encoded.Count != count)
            {
                throw new InvalidDataException(
                    $"Voxel blob holds {count} voxels but the level says {encoded.Count}.");
            }

            var voxels = new List<ManaVoxelData>(count);
            for (var i = 0; i < count; i++)
            {
                var offset = i * BytesPerVoxel;
                var paletteIndex = BitConverter.ToUInt16(data, offset + 6);
                if (paletteIndex >= encoded.Palette.Count)
                {
                    throw new InvalidDataException(
                        $"Voxel {i} references palette entry {paletteIndex}, but the palette holds {encoded.Palette.Count}.");
                }

                var color = encoded.Palette[paletteIndex];
                if (color.Length < 4)
                {
                    throw new InvalidDataException($"Palette entry {paletteIndex} is not an RGBA colour.");
                }

                voxels.Add(new ManaVoxelData
                {
                    Position = new Vector3(
                        ReadKey(data, offset),
                        ReadKey(data, offset + 2),
                        ReadKey(data, offset + 4)),
                    Color = new Vector4(color[0], color[1], color[2], color[3])
                });
            }

            return voxels;
        }

        private static void WriteKey(byte[] data, int offset, float coordinate)
        {
            var key = (int)MathF.Round(coordinate * 2.0f);
            if (key is > MaxKey or < short.MinValue)
            {
                throw new InvalidDataException(
                    $"Voxel coordinate {coordinate} is outside the range a level file can store.");
            }

            BitConverter.TryWriteBytes(data.AsSpan(offset, 2), (short)key);
        }

        private static float ReadKey(byte[] data, int offset) => BitConverter.ToInt16(data, offset) * 0.5f;
    }
}
