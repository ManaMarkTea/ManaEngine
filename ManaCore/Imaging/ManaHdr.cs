namespace ManaCore.Imaging;

/// <summary>
/// A Radiance .hdr picture (RGBE, the new-style run-length encoding nearly every tool writes) as linear float RGB,
/// rows top first -- the environment maps scene packs ship their skies in.
/// </summary>
public sealed class ManaHdr
{
    private ManaHdr(int width, int height, float[] rgb)
    {
        Width = width;
        Height = height;
        Rgb = rgb;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Three floats a pixel, linear.</summary>
    public float[] Rgb { get; }

    public static ManaHdr Read(byte[] bytes)
    {
        var position = 0;
        string Line()
        {
            var start = position;
            while (position < bytes.Length && bytes[position] != (byte)'\n')
            {
                position++;
            }
            var line = System.Text.Encoding.ASCII.GetString(bytes, start, position - start);
            position++;
            return line;
        }

        if (!Line().StartsWith("#?", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Not a Radiance HDR file.");
        }
        while (position < bytes.Length && Line().Length > 0)
        {
        }
        var size = Line().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (size.Length != 4 || size[0] != "-Y" || size[2] != "+X")
        {
            throw new InvalidDataException("HDR orientation is not the usual -Y h +X w.");
        }
        var height = int.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture);
        var width = int.Parse(size[3], System.Globalization.CultureInfo.InvariantCulture);

        var rgb = new float[width * height * 3];
        var line = new byte[width * 4];
        for (var row = 0; row < height; row++)
        {
            if (position + 4 > bytes.Length || bytes[position] != 2 || bytes[position + 1] != 2)
            {
                throw new InvalidDataException("Only run-length encoded HDR files are read.");
            }
            position += 4;
            for (var channel = 0; channel < 4; channel++)
            {
                var x = 0;
                while (x < width)
                {
                    if (position >= bytes.Length)
                    {
                        throw new InvalidDataException("HDR data is cut short.");
                    }
                    int count = bytes[position++];
                    if (count > 128)
                    {
                        count -= 128;
                        var value = bytes[position++];
                        for (var i = 0; i < count && x < width; i++, x++)
                        {
                            line[x * 4 + channel] = value;
                        }
                    }
                    else
                    {
                        for (var i = 0; i < count && x < width; i++, x++)
                        {
                            line[x * 4 + channel] = bytes[position++];
                        }
                    }
                }
            }
            for (var x = 0; x < width; x++)
            {
                var exponent = line[x * 4 + 3];
                var scale = 0.0f;
                if (exponent > 0)
                {
                    scale = MathF.ScaleB(1.0f, exponent - 136);
                }
                var o = (row * width + x) * 3;
                rgb[o] = line[x * 4] * scale;
                rgb[o + 1] = line[x * 4 + 1] * scale;
                rgb[o + 2] = line[x * 4 + 2] * scale;
            }
        }
        return new ManaHdr(width, height, rgb);
    }
}
