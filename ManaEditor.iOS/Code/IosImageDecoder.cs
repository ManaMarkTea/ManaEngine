using CoreGraphics;
using ImageIO;
using ManaCore.Imaging;

namespace ManaEditor.iOS;

/// <summary>
/// Pictures decoded by iOS's own ImageIO rather than the Stb port: the ahead-of-time compiled Stb tints JPEGs green on
/// the phone (the level pictures), and the system decoder is the faster one besides.
/// </summary>
public static class IosImageDecoder
{
    public static void Install() => ManaImageCodec.PlatformDecoder = Decode;

    private static ManaImage? Decode(byte[] bytes)
    {
        using var data = NSData.FromArray(bytes);
        using var source = CGImageSource.FromData(data);
        using var image = source?.CreateImage(0, new CGImageOptions());
        if (image == null || image.Width <= 0 || image.Height <= 0)
        {
            return null;
        }

        var width = (int)image.Width;
        var height = (int)image.Height;
        var pixels = new byte[width * height * 4];
        using (var colorSpace = CGColorSpace.CreateSrgb())
        using (var context = new CGBitmapContext(pixels, width, height, 8, width * 4, colorSpace, CGImageAlphaInfo.PremultipliedLast))
        {
            // The bitmap's first row is the picture's top, as ManaImage wants.
            context.DrawImage(new CGRect(0, 0, width, height), image);
        }

        // CoreGraphics draws premultiplied; ManaImage is straight alpha.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            if (alpha is 0 or 255)
            {
                continue;
            }
            for (var c = 0; c < 3; c++)
            {
                pixels[i + c] = (byte)Math.Min(255, pixels[i + c] * 255 / alpha);
            }
        }
        return new ManaImage(width, height, pixels);
    }
}
