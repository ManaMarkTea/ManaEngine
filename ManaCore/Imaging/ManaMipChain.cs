namespace ManaCore.Imaging;

/// <summary>
/// The smaller copies of an RGBA8 picture a GPU samples in the distance, each half the last down to 1 x 1. Each pixel
/// is the average of the two by two above it (an odd edge repeats its last row or column); colour is averaged as light,
/// not as stored sRGB numbers, which would darken every distant texture.
/// </summary>
public static class ManaMipChain
{
    private static readonly float[] ToLinear = BuildToLinear();
    private static readonly byte[] ToStored = BuildToStored();
    private const int StoredSteps = 4096;

    /// <summary>How many levels a picture of this size has, itself included.</summary>
    public static int LevelCount(int width, int height) =>
        1 + (int)Math.Floor(Math.Log2(Math.Max(1, Math.Max(width, height))));

    /// <summary>The levels below the picture (not the picture itself), largest first.</summary>
    public static List<(byte[] Pixels, int Width, int Height)> Build(byte[] pixels, int width, int height, bool srgb)
    {
        var levels = new List<(byte[] Pixels, int Width, int Height)>();
        var source = pixels;
        var sourceWidth = width;
        var sourceHeight = height;
        while (sourceWidth > 1 || sourceHeight > 1)
        {
            var nextWidth = Math.Max(1, sourceWidth / 2);
            var nextHeight = Math.Max(1, sourceHeight / 2);
            var next = new byte[nextWidth * nextHeight * 4];
            for (var y = 0; y < nextHeight; y++)
            {
                var y0 = Math.Min(y * 2, sourceHeight - 1);
                var y1 = Math.Min(y * 2 + 1, sourceHeight - 1);
                for (var x = 0; x < nextWidth; x++)
                {
                    var x0 = Math.Min(x * 2, sourceWidth - 1);
                    var x1 = Math.Min(x * 2 + 1, sourceWidth - 1);
                    var a = (y0 * sourceWidth + x0) * 4;
                    var b = (y0 * sourceWidth + x1) * 4;
                    var c = (y1 * sourceWidth + x0) * 4;
                    var d = (y1 * sourceWidth + x1) * 4;
                    var o = (y * nextWidth + x) * 4;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        if (srgb)
                        {
                            var light = (ToLinear[source[a + channel]] + ToLinear[source[b + channel]]
                                + ToLinear[source[c + channel]] + ToLinear[source[d + channel]]) * 0.25f;
                            next[o + channel] = ToStored[(int)(light * (StoredSteps - 1) + 0.5f)];
                        }
                        else
                        {
                            next[o + channel] = (byte)((source[a + channel] + source[b + channel] + source[c + channel] + source[d + channel] + 2) / 4);
                        }
                    }
                    next[o + 3] = (byte)((source[a + 3] + source[b + 3] + source[c + 3] + source[d + 3] + 2) / 4);
                }
            }
            levels.Add((next, nextWidth, nextHeight));
            source = next;
            sourceWidth = nextWidth;
            sourceHeight = nextHeight;
        }
        return levels;
    }

    private static float[] BuildToLinear()
    {
        var table = new float[256];
        for (var i = 0; i < 256; i++)
        {
            var stored = i / 255.0;
            double light;
            if (stored <= 0.04045)
            {
                light = stored / 12.92;
            }
            else
            {
                light = Math.Pow((stored + 0.055) / 1.055, 2.4);
            }
            table[i] = (float)light;
        }
        return table;
    }

    private static byte[] BuildToStored()
    {
        var table = new byte[StoredSteps];
        for (var i = 0; i < StoredSteps; i++)
        {
            var light = i / (double)(StoredSteps - 1);
            double stored;
            if (light <= 0.0031308)
            {
                stored = light * 12.92;
            }
            else
            {
                stored = 1.055 * Math.Pow(light, 1.0 / 2.4) - 0.055;
            }
            table[i] = (byte)Math.Clamp((int)Math.Round(stored * 255.0), 0, 255);
        }
        return table;
    }
}
