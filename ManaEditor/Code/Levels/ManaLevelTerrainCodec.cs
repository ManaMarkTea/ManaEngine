using System.Buffers.Binary;
using System.IO.Compression;
using Mana.Code.Nodes;

namespace ManaEditor.Code.Levels
{
    /// <summary>Packs and unpacks a <see cref="ManaTerrainNode"/> for <see cref="ManaLevelTerrain"/>.</summary>
    public static class ManaLevelTerrainCodec
    {
        public static ManaLevelTerrain Encode(ManaTerrainNode terrain)
        {
            var heights = new byte[terrain.Heights.Length * 4];
            for (var i = 0; i < terrain.Heights.Length; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(heights.AsSpan(i * 4), terrain.Heights[i]);
            }

            return new ManaLevelTerrain
            {
                Samples = [terrain.SamplesX, terrain.SamplesZ],
                Spacing = terrain.Spacing,
                Heights = Pack(heights),
                Splat = Pack(terrain.Splat.ToArray()),
                Layers = terrain.LayerColors.Select(c => new[] { c.X, c.Y, c.Z, c.W }).ToList(),
                // Written only when off, so a level from before the pictures saves unchanged.
                Textured = terrain.Textured ? null : false
            };
        }

        /// <summary>Loads the saved grid into a terrain; anything missing or the wrong size starts flat and unpainted.</summary>
        public static void Decode(ManaLevelTerrain settings, ManaTerrainNode terrain)
        {
            var samplesX = 129;
            var samplesZ = 129;
            if (settings.Samples is { Length: 2 } samples)
            {
                samplesX = samples[0];
                samplesZ = samples[1];
            }

            float[]? heights = null;
            var heightBytes = Unpack(settings.Heights);
            if (heightBytes != null && heightBytes.Length % 4 == 0)
            {
                heights = new float[heightBytes.Length / 4];
                for (var i = 0; i < heights.Length; i++)
                {
                    heights[i] = BinaryPrimitives.ReadSingleLittleEndian(heightBytes.AsSpan(i * 4));
                }
            }

            Vector4[]? layers = null;
            if (settings.Layers != null)
            {
                layers = settings.Layers.Where(c => c.Length >= 3)
                    .Select(c => new Vector4(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1.0f)).ToArray();
            }

            terrain.Textured = settings.Textured ?? true;
            terrain.Load(samplesX, samplesZ, settings.Spacing, heights, Unpack(settings.Splat), layers);
        }

        private static string Pack(byte[] data)
        {
            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(data);
            }

            return Convert.ToBase64String(output.ToArray());
        }

        private static byte[]? Unpack(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            try
            {
                using var input = new MemoryStream(Convert.FromBase64String(text));
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                return output.ToArray();
            }
            catch (Exception exception) when (exception is FormatException or InvalidDataException)
            {
                return null;
            }
        }
    }
}
