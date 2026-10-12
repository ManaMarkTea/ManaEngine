using System.Buffers.Binary;
using System.IO.Compression;

namespace ManaLidar;

/// <summary>
/// A PNG writer with no dependencies, so Core can always embed its texture. The apps pass their platform's JPEG
/// encoder instead (ScanExportOptions.EncodeImage), which is several times smaller for photo content.
/// </summary>
public static class ScanPng
{
    public static byte[] EncodeRgb(int width, int height, byte[] rgb)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bits per channel
        header[9] = 2; // RGB
        Chunk(output, "IHDR", header);

        using (var pixels = new MemoryStream())
        {
            using (var zlib = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
            {
                // Filter "Up" on every row: photos change slowly downwards, so the differences compress well.
                var stride = width * 3;
                var row = new byte[stride + 1];
                for (var y = 0; y < height; y++)
                {
                    row[0] = (byte)(y == 0 ? 0 : 2);
                    for (var i = 0; i < stride; i++)
                    {
                        var value = rgb[y * stride + i];
                        row[i + 1] = y == 0 ? value : (byte)(value - rgb[(y - 1) * stride + i]);
                    }
                    zlib.Write(row);
                }
            }
            Chunk(output, "IDAT", pixels.ToArray());
        }

        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(four, data.Length);
        output.Write(four);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc32(Crc32(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(four, crc);
        output.Write(four);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return c;
    }).ToArray();

    private static uint Crc32(uint crc, byte[] data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }
}
