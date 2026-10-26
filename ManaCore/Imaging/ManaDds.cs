using System.Buffers.Binary;

namespace ManaCore.Imaging;

/// <summary>How a <see cref="ManaDds"/> stores its texels: one of the BC block formats, or plain RGBA.</summary>
public enum ManaDdsFormat
{
    Bc1,
    Bc2,
    Bc3,
    Bc4,
    Bc5,
    Rgba8
}

/// <summary>
/// A DDS file as stored: its mip levels, top row first, still block-compressed -- so a GPU that samples BC formats takes
/// them as they are (4-8x smaller than RGBA, mips included), and anything else calls <see cref="Decode"/>.
/// Reads BC1-BC5 (DXT1-5, ATI1/2 or a DX10 header) and 32-bit RGBA; BC6H/BC7 are refused as unreadable for now.
/// </summary>
public sealed class ManaDds
{
    private ManaDds(ManaDdsFormat format, bool srgb, int width, int height, byte[][] levels)
    {
        Format = format;
        Srgb = srgb;
        Width = width;
        Height = height;
        Levels = levels;
    }

    public ManaDdsFormat Format { get; }

    /// <summary>The DX10 header said sRGB. Legacy headers cannot say, so the caller decides from what the map is for.</summary>
    public bool Srgb { get; }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Each mip level's data, largest first: rows of 4x4 blocks, or of RGBA pixels for <see cref="ManaDdsFormat.Rgba8"/>.</summary>
    public byte[][] Levels { get; }

    public bool IsBlockCompressed => Format != ManaDdsFormat.Rgba8;

    /// <summary>Bytes in one 4x4 block (or one pixel, uncompressed).</summary>
    public int BlockBytes => Format switch
    {
        ManaDdsFormat.Bc1 or ManaDdsFormat.Bc4 => 8,
        ManaDdsFormat.Rgba8 => 4,
        _ => 16
    };

    public static bool IsDds(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 128 && bytes[0] == (byte)'D' && bytes[1] == (byte)'D' && bytes[2] == (byte)'S' && bytes[3] == (byte)' ';

    public static int LevelWidth(int width, int level) => Math.Max(1, width >> level);

    /// <summary>Blocks across (or pixels across, uncompressed) at a level.</summary>
    public int LevelColumns(int level)
    {
        var width = LevelWidth(Width, level);
        if (IsBlockCompressed)
        {
            return (width + 3) / 4;
        }
        return width;
    }

    public int LevelRows(int level)
    {
        var height = LevelWidth(Height, level);
        if (IsBlockCompressed)
        {
            return (height + 3) / 4;
        }
        return height;
    }

    /// <summary>Reads the header and levels; throws <see cref="InvalidDataException"/> for anything it cannot read.</summary>
    public static ManaDds Read(byte[] bytes)
    {
        if (!IsDds(bytes))
        {
            throw new InvalidDataException("Not a DDS file.");
        }

        var header = bytes.AsSpan(4, 124);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        var mipCount = 1;
        if ((flags & 0x20000) != 0)
        {
            mipCount = Math.Max(1, (int)BinaryPrimitives.ReadUInt32LittleEndian(header[24..]));
        }
        var pixelFlags = BinaryPrimitives.ReadUInt32LittleEndian(header[76..]);
        var fourCc = System.Text.Encoding.ASCII.GetString(header.Slice(80, 4));
        var bitCount = BinaryPrimitives.ReadUInt32LittleEndian(header[84..]);
        var offset = 128;
        var srgb = false;
        ManaDdsFormat format;
        var swapRedBlue = false;

        if ((pixelFlags & 0x4) != 0 && fourCc == "DX10")
        {
            if (bytes.Length < 148)
            {
                throw new InvalidDataException("DDS DX10 header is cut short.");
            }
            var dxgi = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(128));
            offset = 148;
            (format, srgb) = dxgi switch
            {
                71 => (ManaDdsFormat.Bc1, false),
                72 => (ManaDdsFormat.Bc1, true),
                74 => (ManaDdsFormat.Bc2, false),
                75 => (ManaDdsFormat.Bc2, true),
                77 => (ManaDdsFormat.Bc3, false),
                78 => (ManaDdsFormat.Bc3, true),
                80 => (ManaDdsFormat.Bc4, false),
                83 => (ManaDdsFormat.Bc5, false),
                28 => (ManaDdsFormat.Rgba8, false),
                29 => (ManaDdsFormat.Rgba8, true),
                _ => throw new InvalidDataException($"DDS format DXGI {dxgi} is not supported.")
            };
        }
        else if ((pixelFlags & 0x4) != 0)
        {
            format = fourCc switch
            {
                "DXT1" => ManaDdsFormat.Bc1,
                "DXT2" or "DXT3" => ManaDdsFormat.Bc2,
                "DXT4" or "DXT5" => ManaDdsFormat.Bc3,
                "ATI1" or "BC4U" => ManaDdsFormat.Bc4,
                "ATI2" or "BC5U" => ManaDdsFormat.Bc5,
                _ => throw new InvalidDataException($"DDS format {fourCc} is not supported.")
            };
        }
        else if ((pixelFlags & 0x40) != 0 && bitCount == 32)
        {
            // Masked RGB: the usual uncompressed DDS is BGRA, which is swapped to RGBA here.
            format = ManaDdsFormat.Rgba8;
            swapRedBlue = BinaryPrimitives.ReadUInt32LittleEndian(header[88..]) == 0x00FF0000;
        }
        else
        {
            throw new InvalidDataException("DDS pixel format is not supported.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("DDS has no size.");
        }

        var dds = new ManaDds(format, srgb, width, height, Array.Empty<byte[]>());
        var levels = new List<byte[]>();
        for (var level = 0; level < mipCount; level++)
        {
            var size = dds.LevelColumns(level) * dds.LevelRows(level) * dds.BlockBytes;
            if (offset + size > bytes.Length)
            {
                // A short file keeps the levels it has; none at all is unreadable.
                break;
            }
            levels.Add(bytes.AsSpan(offset, size).ToArray());
            offset += size;
        }
        if (levels.Count == 0)
        {
            throw new InvalidDataException("DDS data is cut short.");
        }
        if (swapRedBlue)
        {
            foreach (var data in levels)
            {
                for (var i = 0; i < data.Length; i += 4)
                {
                    (data[i], data[i + 2]) = (data[i + 2], data[i]);
                }
            }
        }

        return new ManaDds(format, srgb, width, height, levels.ToArray());
    }

    /// <summary>
    /// Turns every level upside down in place, as <see cref="ManaImage.FlipVertical"/> does for a decoded picture: block
    /// rows are reversed and each block's own rows with them. False (nothing changed) when a level's height is not a
    /// whole number of blocks -- the rows would straddle blocks -- so the caller decodes instead.
    /// </summary>
    public bool TryFlipVertical()
    {
        if (IsBlockCompressed)
        {
            for (var level = 0; level < Levels.Length; level++)
            {
                var height = LevelWidth(Height, level);
                if (height > 4 && height % 4 != 0)
                {
                    return false;
                }
            }
        }

        for (var level = 0; level < Levels.Length; level++)
        {
            var data = Levels[level];
            var rows = LevelRows(level);
            var stride = LevelColumns(level) * BlockBytes;
            var swap = new byte[stride];
            for (int top = 0, bottom = rows - 1; top < bottom; top++, bottom--)
            {
                var a = data.AsSpan(top * stride, stride);
                var b = data.AsSpan(bottom * stride, stride);
                a.CopyTo(swap);
                b.CopyTo(a);
                swap.CopyTo(b);
            }
            if (!IsBlockCompressed)
            {
                continue;
            }

            // A mip under four texels tall only fills the top rows of its blocks, and only those turn over.
            var texelRows = Math.Min(4, LevelWidth(Height, level));
            for (var block = 0; block < data.Length; block += BlockBytes)
            {
                FlipBlock(data.AsSpan(block, BlockBytes), texelRows);
            }
        }
        return true;
    }

    private void FlipBlock(Span<byte> block, int rows)
    {
        switch (Format)
        {
            case ManaDdsFormat.Bc1:
                FlipColorRows(block, rows);
                break;
            case ManaDdsFormat.Bc2:
                // Explicit 4-bit alpha, two bytes a row, then a BC1 colour block.
                for (int top = 0, bottom = rows - 1; top < bottom; top++, bottom--)
                {
                    (block[top * 2], block[bottom * 2]) = (block[bottom * 2], block[top * 2]);
                    (block[top * 2 + 1], block[bottom * 2 + 1]) = (block[bottom * 2 + 1], block[top * 2 + 1]);
                }
                FlipColorRows(block[8..], rows);
                break;
            case ManaDdsFormat.Bc3:
                FlipChannelRows(block, rows);
                FlipColorRows(block[8..], rows);
                break;
            case ManaDdsFormat.Bc4:
                FlipChannelRows(block, rows);
                break;
            case ManaDdsFormat.Bc5:
                FlipChannelRows(block, rows);
                FlipChannelRows(block[8..], rows);
                break;
        }
    }

    // BC1 colour: two endpoints, then one byte of 2-bit indices per row.
    private static void FlipColorRows(Span<byte> block, int rows)
    {
        for (int top = 0, bottom = rows - 1; top < bottom; top++, bottom--)
        {
            (block[4 + top], block[4 + bottom]) = (block[4 + bottom], block[4 + top]);
        }
    }

    // BC4 channel: two endpoints, then 48 bits of 3-bit indices -- 12 bits a row.
    private static void FlipChannelRows(Span<byte> block, int rows)
    {
        ulong bits = 0;
        for (var i = 0; i < 6; i++)
        {
            bits |= (ulong)block[2 + i] << (8 * i);
        }
        ulong flipped = bits;
        for (var row = 0; row < rows; row++)
        {
            var source = (bits >> (12 * row)) & 0xFFF;
            var target = rows - 1 - row;
            flipped &= ~(0xFFFUL << (12 * target));
            flipped |= source << (12 * target);
        }
        for (var i = 0; i < 6; i++)
        {
            block[2 + i] = (byte)(flipped >> (8 * i));
        }
    }

    /// <summary>
    /// One level as straight RGBA, for wherever the GPU cannot take the blocks (the browser, a phone, thumbnails). BC4
    /// reads as red; BC5 as red and green with blue rebuilt as a unit normal's z, which is what two-channel maps hold.
    /// </summary>
    public ManaImage Decode(int level = 0)
    {
        var width = LevelWidth(Width, level);
        var height = LevelWidth(Height, level);
        var data = Levels[level];
        var image = new ManaImage(width, height);
        if (!IsBlockCompressed)
        {
            data.AsSpan(0, width * height * 4).CopyTo(image.Pixels);
            return image;
        }

        Span<byte> texels = stackalloc byte[64];
        var columns = LevelColumns(level);
        for (var by = 0; by < LevelRows(level); by++)
        {
            for (var bx = 0; bx < columns; bx++)
            {
                var block = data.AsSpan((by * columns + bx) * BlockBytes, BlockBytes);
                DecodeBlock(block, texels);
                for (var y = 0; y < 4 && by * 4 + y < height; y++)
                {
                    for (var x = 0; x < 4 && bx * 4 + x < width; x++)
                    {
                        texels.Slice((y * 4 + x) * 4, 4).CopyTo(image.Pixels.AsSpan(((by * 4 + y) * width + bx * 4 + x) * 4, 4));
                    }
                }
            }
        }
        return image;
    }

    private void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> texels)
    {
        Span<byte> channel = stackalloc byte[16];
        switch (Format)
        {
            case ManaDdsFormat.Bc1:
                DecodeColor(block, texels, allowTransparent: true);
                break;
            case ManaDdsFormat.Bc2:
                DecodeColor(block[8..], texels, allowTransparent: false);
                for (var i = 0; i < 16; i++)
                {
                    var nibble = (block[i / 2] >> (4 * (i % 2))) & 0xF;
                    texels[i * 4 + 3] = (byte)(nibble * 17);
                }
                break;
            case ManaDdsFormat.Bc3:
                DecodeColor(block[8..], texels, allowTransparent: false);
                DecodeChannel(block, channel);
                for (var i = 0; i < 16; i++)
                {
                    texels[i * 4 + 3] = channel[i];
                }
                break;
            case ManaDdsFormat.Bc4:
                DecodeChannel(block, channel);
                for (var i = 0; i < 16; i++)
                {
                    texels[i * 4] = channel[i];
                    texels[i * 4 + 1] = 0;
                    texels[i * 4 + 2] = 0;
                    texels[i * 4 + 3] = 255;
                }
                break;
            case ManaDdsFormat.Bc5:
                Span<byte> green = stackalloc byte[16];
                DecodeChannel(block, channel);
                DecodeChannel(block[8..], green);
                for (var i = 0; i < 16; i++)
                {
                    var x = channel[i] / 127.5f - 1.0f;
                    var y = green[i] / 127.5f - 1.0f;
                    var z = MathF.Sqrt(MathF.Max(0.0f, 1.0f - x * x - y * y));
                    texels[i * 4] = channel[i];
                    texels[i * 4 + 1] = green[i];
                    texels[i * 4 + 2] = (byte)MathF.Round((z + 1.0f) * 127.5f);
                    texels[i * 4 + 3] = 255;
                }
                break;
        }
    }

    private static void DecodeColor(ReadOnlySpan<byte> block, Span<byte> texels, bool allowTransparent)
    {
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        Span<byte> palette = stackalloc byte[16];
        Expand565(c0, palette[..4]);
        Expand565(c1, palette.Slice(4, 4));
        for (var channel = 0; channel < 3; channel++)
        {
            int a = palette[channel], b = palette[4 + channel];
            if (c0 > c1 || !allowTransparent)
            {
                palette[8 + channel] = (byte)((2 * a + b + 1) / 3);
                palette[12 + channel] = (byte)((a + 2 * b + 1) / 3);
            }
            else
            {
                palette[8 + channel] = (byte)((a + b + 1) / 2);
                palette[12 + channel] = 0;
            }
        }
        palette[11] = 255;
        palette[15] = 255;
        if (c0 <= c1 && allowTransparent)
        {
            palette[15] = 0;
        }
        var indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
        for (var i = 0; i < 16; i++)
        {
            var index = (int)((indices >> (2 * i)) & 3);
            palette.Slice(index * 4, 4).CopyTo(texels.Slice(i * 4, 4));
        }
    }

    private static void Expand565(ushort color, Span<byte> rgba)
    {
        var r = (color >> 11) & 31;
        var g = (color >> 5) & 63;
        var b = color & 31;
        rgba[0] = (byte)((r << 3) | (r >> 2));
        rgba[1] = (byte)((g << 2) | (g >> 4));
        rgba[2] = (byte)((b << 3) | (b >> 2));
        rgba[3] = 255;
    }

    private static void DecodeChannel(ReadOnlySpan<byte> block, Span<byte> values)
    {
        int a = block[0], b = block[1];
        Span<byte> palette = stackalloc byte[8];
        palette[0] = (byte)a;
        palette[1] = (byte)b;
        if (a > b)
        {
            for (var i = 1; i < 7; i++)
            {
                palette[i + 1] = (byte)(((7 - i) * a + i * b + 3) / 7);
            }
        }
        else
        {
            for (var i = 1; i < 5; i++)
            {
                palette[i + 1] = (byte)(((5 - i) * a + i * b + 2) / 5);
            }
            palette[6] = 0;
            palette[7] = 255;
        }
        ulong bits = 0;
        for (var i = 0; i < 6; i++)
        {
            bits |= (ulong)block[2 + i] << (8 * i);
        }
        for (var i = 0; i < 16; i++)
        {
            values[i] = palette[(int)((bits >> (3 * i)) & 7)];
        }
    }
}
