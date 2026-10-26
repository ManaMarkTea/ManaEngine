using System.Buffers.Binary;
using System.Text;
using ManaCore.Imaging;
using ManaUI;

namespace ManaTests;

/// <summary>DDS: headers read, BC1-5 decode, and flipping the blocks matches flipping the decoded picture at every mip.</summary>
public static class DdsChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var random = new Random(7);
        foreach (var (fourCc, blockBytes) in new[] { ("DXT1", 8), ("DXT3", 16), ("DXT5", 16), ("ATI1", 8), ("ATI2", 16) })
        {
            // 8x8 with four levels: 8, 4, then 2 and 1 texels tall, which fill only the top rows of their blocks.
            var bytes = Build(fourCc, 8, 8, LevelSizes(8, 8, 4, blockBytes), random);
            var dds = ManaDds.Read(bytes);
            var expected = Enumerable.Range(0, dds.Levels.Length).Select(level => dds.Decode(level).FlipVertical()).ToArray();
            var flipped = dds.TryFlipVertical();
            var same = flipped && Enumerable.Range(0, dds.Levels.Length)
                .All(level => dds.Decode(level).Pixels.AsSpan().SequenceEqual(expected[level].Pixels));
            check($"{fourCc}: flipping the blocks matches flipping the decoded picture at every mip", same, $"levels {dds.Levels.Length}");
        }

        var read = ManaDds.Read(Build("DXT5", 16, 8, LevelSizes(16, 8, 5, 16), random));
        check("the header gives the format, size and every level", read.Format == ManaDdsFormat.Bc3 && read.Width == 16
            && read.Height == 8 && read.Levels.Length == 5 && read.Levels[0].Length == 4 * 2 * 16, $"{read.Format} {read.Width}x{read.Height} {read.Levels.Length}");

        var cut = Build("DXT1", 8, 8, LevelSizes(8, 8, 4, 8), random);
        var shortened = ManaDds.Read(cut.AsSpan(0, cut.Length - 10).ToArray());
        check("a file cut short keeps the levels it has whole", shortened.Levels.Length == 2, $"{shortened.Levels.Length}");

        // BC1: red and blue endpoints, the first row all red and the second all blue.
        var bc1 = Build("DXT1", 4, 4, new[] { 8 }, random);
        BinaryPrimitives.WriteUInt16LittleEndian(bc1.AsSpan(128), 0xF800);
        BinaryPrimitives.WriteUInt16LittleEndian(bc1.AsSpan(130), 0x001F);
        bc1[132] = 0x00;
        bc1[133] = 0x55;
        var colours = ManaImageCodec.Load(bc1);
        check("BC1 decodes its endpoint colours through the image codec",
            colours.Pixels.AsSpan(0, 4).SequenceEqual(new byte[] { 255, 0, 0, 255 })
            && colours.Pixels.AsSpan(16, 4).SequenceEqual(new byte[] { 0, 0, 255, 255 }), string.Join(",", colours.Pixels.Take(8)));

        // BC5: a flat (128, 128) normal points straight out, so blue is rebuilt near 255.
        var bc5 = Build("ATI2", 4, 4, new[] { 16 }, random);
        bc5.AsSpan(128, 16).Clear();
        bc5[128] = bc5[129] = 128;
        bc5[136] = bc5[137] = 128;
        var normal = ManaDds.Read(bc5).Decode();
        check("BC5 rebuilds blue as the normal's z", normal.Pixels[2] >= 254 && normal.Pixels[3] == 255, $"{normal.Pixels[2]}");

        var uneven = ManaDds.Read(Build("DXT1", 8, 6, LevelSizes(8, 6, 1, 8), random));
        var before = (byte[])uneven.Levels[0].Clone();
        check("a height that splits blocks is not flipped in blocks", !uneven.TryFlipVertical() && uneven.Levels[0].AsSpan().SequenceEqual(before), "");

        var bgra = Build(null, 2, 1, new[] { 8 }, random);
        new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }.CopyTo(bgra, 128);
        var rgba = ManaDds.Read(bgra).Decode();
        check("uncompressed BGRA reads as RGBA", rgba.Pixels.AsSpan().SequenceEqual(new byte[] { 3, 2, 1, 4, 7, 6, 5, 8 }), string.Join(",", rgba.Pixels));

        var refused = false;
        try
        {
            ManaDds.Read(Build("ETC2", 4, 4, new[] { 8 }, random));
        }
        catch (InvalidDataException)
        {
            refused = true;
        }
        check("an unsupported format is refused as unreadable", refused, "");
    }

    private static int[] LevelSizes(int width, int height, int levels, int blockBytes) =>
        Enumerable.Range(0, levels)
            .Select(level => ((Math.Max(1, width >> level) + 3) / 4) * ((Math.Max(1, height >> level) + 3) / 4) * blockBytes)
            .ToArray();

    /// <summary>A DDS of random texel data: a FourCC, or null for 32-bit BGRA (where sizes are pixel bytes).</summary>
    private static byte[] Build(string? fourCc, int width, int height, int[] levelSizes, Random random)
    {
        var bytes = new byte[128 + levelSizes.Sum()];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(bytes, 0);
        var header = bytes.AsSpan(4);
        BinaryPrimitives.WriteUInt32LittleEndian(header, 124);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 0x1007 | 0x20000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], (uint)levelSizes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[72..], 32);
        if (fourCc != null)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header[76..], 0x4);
            Encoding.ASCII.GetBytes(fourCc).CopyTo(bytes, 84);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header[76..], 0x41);
            BinaryPrimitives.WriteUInt32LittleEndian(header[84..], 32);
            BinaryPrimitives.WriteUInt32LittleEndian(header[88..], 0x00FF0000);
            BinaryPrimitives.WriteUInt32LittleEndian(header[92..], 0x0000FF00);
            BinaryPrimitives.WriteUInt32LittleEndian(header[96..], 0x000000FF);
            BinaryPrimitives.WriteUInt32LittleEndian(header[100..], 0xFF000000);
        }
        random.NextBytes(bytes.AsSpan(128));
        return bytes;
    }
}
