using ManaCore.Imaging;
using ManaUI;

namespace ManaTests;

/// <summary>The picture codec on desktop: PNG round trips exactly, JPEG reads, resizing keeps colour, junk is refused.</summary>
public static class ImageCodecChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var image = new ManaImage(5, 3);
        for (var i = 0; i < 15; i++)
        {
            image.Pixels[i * 4] = (byte)(i * 17);
            image.Pixels[i * 4 + 1] = (byte)(255 - i * 9);
            image.Pixels[i * 4 + 2] = (byte)(i * 3);
            image.Pixels[i * 4 + 3] = (byte)(i % 3 == 0 ? 0 : 255 - i);
        }

        var back = ManaImageCodec.Load(ManaImageCodec.EncodePng(image));
        check("a PNG round trip keeps every byte, transparency included",
            back.Width == 5 && back.Height == 3 && back.Pixels.AsSpan().SequenceEqual(image.Pixels), $"{back.Width}x{back.Height}");

        var path = Path.Combine(Path.GetTempPath(), $"mana-codec-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            var flat = new ManaImage(32, 16);
            for (var i = 0; i < 32 * 16; i++)
            {
                flat.Pixels[i * 4] = 200;
                flat.Pixels[i * 4 + 1] = 40;
                flat.Pixels[i * 4 + 2] = 90;
                flat.Pixels[i * 4 + 3] = 255;
            }

            ManaImageCodec.SavePng(flat, Path.Combine(path, "a.jpg"));
            var jpeg = ManaImageCodec.Load(Path.Combine(path, "a.jpg"));
            check("a saved JPEG reads back close to what was written",
                jpeg.Width == 32 && Math.Abs(jpeg.Pixels[0] - 200) < 6 && Math.Abs(jpeg.Pixels[1] - 40) < 6 && jpeg.Pixels[3] == 255,
                $"{jpeg.Pixels[0]},{jpeg.Pixels[1]},{jpeg.Pixels[2]}");

            ManaImageCodec.SavePng(flat, Path.Combine(path, "deep", "b.png"));
            check("saving makes the folders it needs and the size reads from the header",
                ManaImageCodec.Size(Path.Combine(path, "deep", "b.png")) == (32, 16), "");
        }
        finally
        {
            Directory.Delete(path, true);
        }

        var small = flatColour(40, 40, 10, 200, 30, 255).Resized(10, 8);
        check("resizing a flat picture keeps its colour and size", small.Width == 10 && small.Height == 8
            && Math.Abs(small.Pixels[0] - 10) <= 1 && Math.Abs(small.Pixels[1] - 200) <= 1 && small.Pixels[3] == 255, $"{small.Pixels[0]},{small.Pixels[1]},{small.Pixels[2]},{small.Pixels[3]}");

        var padded = flatColour(8, 8, 255, 0, 0, 255);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 4; x < 8; x++)
            {
                var i = (y * 8 + x) * 4;
                padded.Pixels[i] = 0;
                padded.Pixels[i + 1] = 0;
                padded.Pixels[i + 2] = 0;
                padded.Pixels[i + 3] = 0;
            }
        }
        var edge = padded.Resized(4, 4);
        check("transparent padding does not darken the colour beside it", edge.Pixels[0] > 250 && edge.Pixels[1] < 5, $"{edge.Pixels[0]},{edge.Pixels[1]}");

        var refused = false;
        try
        {
            ManaImageCodec.Load(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        }
        catch (InvalidDataException)
        {
            refused = true;
        }
        check("data that is not a picture throws InvalidDataException", refused, "");

        var moon = FindUp("Assets/Textures/Sky/moon_lroc_1k.jpg");
        if (moon != null)
        {
            var decoded = ManaImageCodec.Load(moon);
            check("the bundled moon JPEG decodes to a real picture", decoded.Width >= 512 && decoded.Pixels.Any(b => b > 40), $"{decoded.Width}x{decoded.Height}");
        }
    }

    private static ManaImage flatColour(int w, int h, byte r, byte g, byte b, byte a)
    {
        var image = new ManaImage(w, h);
        for (var i = 0; i < w * h; i++)
        {
            image.Pixels[i * 4] = r;
            image.Pixels[i * 4 + 1] = g;
            image.Pixels[i * 4 + 2] = b;
            image.Pixels[i * 4 + 3] = a;
        }
        return image;
    }

    private static string? FindUp(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
