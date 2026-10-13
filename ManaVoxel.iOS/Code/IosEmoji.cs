using CoreGraphics;
using CoreText;
using ManaUI;

namespace ManaVoxel.iOS;

/// <summary>
/// Colour emoji from the phone's own Apple Color Emoji, drawn by CoreText -- as the web build uses the browser's -- so
/// the app need not carry Noto (10 MB). Each glyph is drawn once per size and cropped to its ink.
/// </summary>
public sealed class IosEmoji : IManaColorGlyphSource
{
    private readonly Dictionary<(int Codepoint, int Size), ManaColorGlyphImage?> _drawn = new();
    private readonly object _lock = new();

    public static void Install() => ManaFontManager.PlatformColorEmoji = new IosEmoji();

    public ManaColorGlyphImage? Draw(int codepoint, float pixelsPerEm)
    {
        var size = Math.Max(4, (int)MathF.Round(pixelsPerEm));
        lock (_lock)
        {
            if (!_drawn.TryGetValue((codepoint, size), out var image))
            {
                image = Render(codepoint, size);
                _drawn[(codepoint, size)] = image;
            }
            return image;
        }
    }

    private static ManaColorGlyphImage? Render(int codepoint, int size)
    {
        if (codepoint is < 0 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
        {
            return null;
        }
        var text = char.ConvertFromUtf32(codepoint);
        using var font = new CTFont("AppleColorEmoji", size);
        // Only what the emoji font itself has: CoreText would otherwise fall back to a box from another font.
        var glyphs = new ushort[text.Length];
        if (!font.GetGlyphsForCharacters(text.ToCharArray(), glyphs))
        {
            return null;
        }

        using var attributed = new NSAttributedString(text, new CTStringAttributes { Font = font });
        using var line = new CTLine(attributed);
        var advance = line.GetTypographicBounds(out _, out _, out _);

        // Room for an emoji that overhangs its em either side; the crop takes the margin back off.
        var pad = (size + 3) / 4;
        var width = size * 2 + pad * 2;
        var height = size + size / 2 + pad * 2;
        var baseline = pad + size;
        var pixels = new byte[width * height * 4];
        using (var colorSpace = CGColorSpace.CreateDeviceRGB())
        using (var context = new CGBitmapContext(pixels, width, height, 8, width * 4, colorSpace, CGImageAlphaInfo.PremultipliedLast))
        {
            // CoreGraphics counts y up from the bottom; the bitmap's first row is the top.
            context.TextPosition = new CGPoint(pad, height - baseline);
            line.Draw(context);
            context.Flush();
        }

        if (advance <= 0 || !TryBounds(pixels, width, height, out var left, out var top, out var right, out var bottom))
        {
            return null;
        }
        var croppedWidth = right - left + 1;
        var croppedHeight = bottom - top + 1;
        var cropped = new byte[croppedWidth * croppedHeight * 4];
        for (var y = 0; y < croppedHeight; y++)
        {
            Array.Copy(pixels, ((top + y) * width + left) * 4, cropped, y * croppedWidth * 4, croppedWidth * 4);
        }
        Unpremultiply(cropped);
        return new ManaColorGlyphImage(cropped, croppedWidth, croppedHeight, left - pad, baseline - top, (float)advance);
    }

    // ManaUI wants straight alpha; CoreGraphics draws premultiplied.
    private static void Unpremultiply(byte[] pixels)
    {
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
    }

    private static bool TryBounds(byte[] pixels, int width, int height, out int left, out int top, out int right, out int bottom)
    {
        left = width;
        top = height;
        right = -1;
        bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[(y * width + x) * 4 + 3] == 0)
                {
                    continue;
                }
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        return right >= left && bottom >= top;
    }
}
