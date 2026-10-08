namespace ManaCore.Imaging;

/// <summary>
/// Reads and writes pictures (PNG, JPEG and the rest). Desktop decodes with ImageSharp
/// (<c>ManaImageCodec.Desktop.cs</c>); the browser with its own decoder (<c>ManaImageCodec.Browser.cs</c>),
/// which only works asynchronously -- so there, a picture is decoded ahead by <see cref="PrepareAsync(byte[])"/>
/// (which the asset catalog does for whatever it downloads) and the synchronous <see cref="Load(byte[])"/>
/// hands it over. Unreadable data throws <see cref="InvalidDataException"/> everywhere.
/// </summary>
public static partial class ManaImageCodec
{
    // What SavePng writes in the browser: a header, the size, then the pixels as they are.
    private static ReadOnlySpan<byte> RawMagic => "MANARGBA"u8;

    public static ManaImage Load(string path) => Load(File.ReadAllBytes(path));

    public static ManaImage Load(byte[] bytes) => Decode(bytes);

    public static ManaImage Load(ReadOnlySpan<byte> bytes) => Decode(bytes.ToArray());

    /// <summary>Saves as PNG on desktop. In the browser, whose encoder is asynchronous too, as raw pixels that
    /// <see cref="Load(string)"/> reads back -- its files live in memory and are only ever read by this.</summary>
    public static void SavePng(ManaImage image, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        Encode(image, path);
    }

    /// <summary>Decodes a picture ahead of a synchronous <see cref="Load(byte[])"/> -- needed in the browser, a no-op elsewhere.</summary>
    public static Task PrepareAsync(byte[] bytes) => PrepareCore(bytes);

    /// <summary>The file's picture, decoded ahead; a missing or unreadable file is skipped.</summary>
    public static async Task PrepareFileAsync(string path)
    {
        if (!OperatingSystem.IsBrowser() || !File.Exists(path))
        {
            return;
        }
        await PrepareCore(await File.ReadAllBytesAsync(path));
    }

    /// <summary>
    /// A PNG's pixel size from its header, without decoding it -- or the size of a picture <see cref="SavePng"/>
    /// wrote in the browser, which is raw. Null for anything else.
    /// </summary>
    public static (int Width, int Height)? Size(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[24];
            if (stream.Read(header) < 16)
            {
                return null;
            }
            if (header[..8].SequenceEqual(RawMagic))
            {
                return (BitConverter.ToInt32(header[8..12]), BitConverter.ToInt32(header[12..16]));
            }
            if (header[0] != 0x89 || header[1] != (byte)'P')
            {
                return null;
            }
            return ((header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19],
                (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Whether a file's extension is a picture this codec reads.</summary>
    public static bool IsPicture(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif";

    // Averages every source pixel a target pixel covers, weighted by alpha so transparent ones don't darken edges.
    private static ManaImage ResizeByArea(ManaImage source, int width, int height)
    {
        var result = new ManaImage(width, height);
        var scaleX = source.Width / (float)width;
        var scaleY = source.Height / (float)height;
        for (var y = 0; y < height; y++)
        {
            var y0 = y * scaleY;
            var y1 = MathF.Max(y0 + 1e-3f, (y + 1) * scaleY);
            for (var x = 0; x < width; x++)
            {
                var x0 = x * scaleX;
                var x1 = MathF.Max(x0 + 1e-3f, (x + 1) * scaleX);
                float r = 0, g = 0, b = 0, a = 0, area = 0;
                for (var sy = (int)y0; sy < Math.Min(source.Height, (int)MathF.Ceiling(y1)); sy++)
                {
                    var wy = MathF.Min(y1, sy + 1) - MathF.Max(y0, sy);
                    for (var sx = (int)x0; sx < Math.Min(source.Width, (int)MathF.Ceiling(x1)); sx++)
                    {
                        var w = wy * (MathF.Min(x1, sx + 1) - MathF.Max(x0, sx));
                        var i = (sy * source.Width + sx) * 4;
                        var pa = source.Pixels[i + 3] * w;
                        r += source.Pixels[i] * pa;
                        g += source.Pixels[i + 1] * pa;
                        b += source.Pixels[i + 2] * pa;
                        a += pa;
                        area += w;
                    }
                }

                var o = (y * width + x) * 4;
                if (a > 0.0f)
                {
                    result.Pixels[o] = (byte)Math.Clamp(MathF.Round(r / a), 0, 255);
                    result.Pixels[o + 1] = (byte)Math.Clamp(MathF.Round(g / a), 0, 255);
                    result.Pixels[o + 2] = (byte)Math.Clamp(MathF.Round(b / a), 0, 255);
                }
                result.Pixels[o + 3] = (byte)Math.Clamp(MathF.Round(a / MathF.Max(area, 1e-6f)), 0, 255);
            }
        }
        return result;
    }
}
