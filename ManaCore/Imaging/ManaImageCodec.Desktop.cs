#if !MANA_BROWSER
using StbImageSharp;
using StbImageWriteSharp;
using ReadComponents = StbImageSharp.ColorComponents;
using WriteComponents = StbImageWriteSharp.ColorComponents;

namespace ManaCore.Imaging;

public static partial class ManaImageCodec
{
    private static ManaImage Decode(byte[] bytes)
    {
        try
        {
            var result = ImageResult.FromMemory(bytes, ReadComponents.RedGreenBlueAlpha);
            return new ManaImage(result.Width, result.Height, result.Data);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Stb throws InvalidOperationException (or an index error) on data it cannot read.
            throw new InvalidDataException(exception.Message, exception);
        }
    }

    private static void Encode(ManaImage image, string path)
    {
        using var stream = File.Create(path);
        var writer = new ImageWriter();
        if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            // Recorded video frames: thousands of them, so small and quick beats lossless. JPEG has no alpha.
            writer.WriteJpg(image.Pixels, image.Width, image.Height, WriteComponents.RedGreenBlueAlpha, stream, 95);
            return;
        }
        writer.WritePng(image.Pixels, image.Width, image.Height, WriteComponents.RedGreenBlueAlpha, stream);
    }

    /// <summary>The picture as PNG bytes, in memory.</summary>
    public static byte[] EncodePng(ManaImage image)
    {
        using var stream = new MemoryStream();
        new ImageWriter().WritePng(image.Pixels, image.Width, image.Height, WriteComponents.RedGreenBlueAlpha, stream);
        return stream.ToArray();
    }

    // Catmull-Rom (what the bicubic every desktop picture was once scaled with is), widened when shrinking so
    // every source pixel counts, on alpha-premultiplied colour so transparent pixels don't bleed into edges.
    internal static ManaImage Resize(ManaImage source, int width, int height)
    {
        var across = ResizeAxis(ToPremultiplied(source), source.Width, source.Height, width, horizontal: true);
        var down = ResizeAxis(across, width, source.Height, height, horizontal: false);
        var result = new ManaImage(width, height);
        for (var i = 0; i < width * height; i++)
        {
            var a = Math.Clamp(down[i * 4 + 3], 0.0f, 1.0f);
            for (var c = 0; c < 3; c++)
            {
                var value = a > 1e-6f ? down[i * 4 + c] / a : 0.0f;
                result.Pixels[i * 4 + c] = (byte)Math.Clamp(MathF.Round(value * 255.0f), 0, 255);
            }
            result.Pixels[i * 4 + 3] = (byte)Math.Clamp(MathF.Round(a * 255.0f), 0, 255);
        }
        return result;
    }

    private static float[] ToPremultiplied(ManaImage source)
    {
        var pixels = new float[source.Width * source.Height * 4];
        for (var i = 0; i < source.Width * source.Height; i++)
        {
            var a = source.Pixels[i * 4 + 3] / 255.0f;
            pixels[i * 4] = source.Pixels[i * 4] / 255.0f * a;
            pixels[i * 4 + 1] = source.Pixels[i * 4 + 1] / 255.0f * a;
            pixels[i * 4 + 2] = source.Pixels[i * 4 + 2] / 255.0f * a;
            pixels[i * 4 + 3] = a;
        }
        return pixels;
    }

    private static float CatmullRom(float x)
    {
        x = MathF.Abs(x);
        if (x < 1.0f)
        {
            return 1.5f * x * x * x - 2.5f * x * x + 1.0f;
        }
        return x < 2.0f ? -0.5f * x * x * x + 2.5f * x * x - 4.0f * x + 2.0f : 0.0f;
    }

    // One pass of the separable resample: `length` samples along the axis become `target`.
    private static float[] ResizeAxis(float[] pixels, int width, int height, int target, bool horizontal)
    {
        var length = horizontal ? width : height;
        var outWidth = horizontal ? target : width;
        var outHeight = horizontal ? height : target;
        var result = new float[outWidth * outHeight * 4];
        var scale = length / (float)target;
        var filterScale = MathF.Max(scale, 1.0f);
        var support = 2.0f * filterScale;
        var weights = new float[(int)MathF.Ceiling(support) * 2 + 2];

        for (var o = 0; o < target; o++)
        {
            var centre = (o + 0.5f) * scale;
            var first = Math.Max(0, (int)MathF.Floor(centre - support + 0.5f));
            var last = Math.Min(length - 1, (int)MathF.Floor(centre + support - 0.5f));
            var total = 0.0f;
            for (var s = first; s <= last; s++)
            {
                weights[s - first] = CatmullRom((s + 0.5f - centre) / filterScale);
                total += weights[s - first];
            }
            if (MathF.Abs(total) < 1e-6f)
            {
                continue;
            }

            var lines = horizontal ? height : width;
            for (var line = 0; line < lines; line++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (var s = first; s <= last; s++)
                {
                    var w = weights[s - first];
                    var i = (horizontal ? line * width + s : s * width + line) * 4;
                    r += pixels[i] * w;
                    g += pixels[i + 1] * w;
                    b += pixels[i + 2] * w;
                    a += pixels[i + 3] * w;
                }
                var d = (horizontal ? line * outWidth + o : o * outWidth + line) * 4;
                result[d] = r / total;
                result[d + 1] = g / total;
                result[d + 2] = b / total;
                result[d + 3] = a / total;
            }
        }
        return result;
    }

    private static Task PrepareCore(byte[] bytes) => Task.CompletedTask;
}
#endif
