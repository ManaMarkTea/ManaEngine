namespace ManaCore.Imaging;

/// <summary>
/// A decoded picture: straight (not premultiplied) RGBA, 8 bits a channel, rows top to bottom. What
/// <see cref="ManaImageCodec"/> hands back on every platform, so callers never touch a decoder's own types.
/// </summary>
public sealed class ManaImage
{
    public ManaImage(int width, int height)
        : this(width, height, new byte[checked(width * height * 4)])
    {
    }

    public ManaImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height * 4)
        {
            throw new ArgumentException($"A {width}x{height} image needs {width * height * 4} bytes, not {pixels.Length}.");
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public ManaImage Clone() => new(Width, Height, (byte[])Pixels.Clone());

    /// <summary>Turns the rows upside down in place -- what a GPU texture coordinate wants.</summary>
    public ManaImage FlipVertical()
    {
        var stride = Width * 4;
        var row = new byte[stride];
        for (int top = 0, bottom = Height - 1; top < bottom; top++, bottom--)
        {
            var a = Pixels.AsSpan(top * stride, stride);
            var b = Pixels.AsSpan(bottom * stride, stride);
            a.CopyTo(row);
            b.CopyTo(a);
            row.CopyTo(b);
        }
        return this;
    }

    /// <summary>A rectangle of this image, clipped to it.</summary>
    public ManaImage Cropped(int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        width = Math.Clamp(width, 1, Width - x);
        height = Math.Clamp(height, 1, Height - y);
        var result = new ManaImage(width, height);
        for (var row = 0; row < height; row++)
        {
            Pixels.AsSpan(((y + row) * Width + x) * 4, width * 4).CopyTo(result.Pixels.AsSpan(row * width * 4));
        }
        return result;
    }

    /// <summary>This image scaled to the given size, in a new one.</summary>
    public ManaImage Resized(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        return width == Width && height == Height ? Clone() : ManaImageCodec.Resize(this, width, height);
    }

    /// <summary>Draws <paramref name="source"/> over this image with its top-left corner at (x, y), blending by its alpha.</summary>
    public void Draw(ManaImage source, int x, int y)
    {
        for (var row = 0; row < source.Height; row++)
        {
            var ty = y + row;
            if (ty < 0 || ty >= Height)
            {
                continue;
            }

            for (var column = 0; column < source.Width; column++)
            {
                var tx = x + column;
                if (tx < 0 || tx >= Width)
                {
                    continue;
                }

                var s = (row * source.Width + column) * 4;
                var d = (ty * Width + tx) * 4;
                var sa = source.Pixels[s + 3] / 255.0f;
                if (sa >= 1.0f)
                {
                    source.Pixels.AsSpan(s, 4).CopyTo(Pixels.AsSpan(d, 4));
                    continue;
                }

                var da = Pixels[d + 3] / 255.0f;
                var outA = sa + da * (1.0f - sa);
                if (outA <= 0.0f)
                {
                    continue;
                }

                for (var c = 0; c < 3; c++)
                {
                    var blended = (source.Pixels[s + c] * sa + Pixels[d + c] * da * (1.0f - sa)) / outA;
                    Pixels[d + c] = (byte)Math.Clamp(MathF.Round(blended), 0, 255);
                }
                Pixels[d + 3] = (byte)Math.Clamp(MathF.Round(outA * 255.0f), 0, 255);
            }
        }
    }
}
