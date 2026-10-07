#if !MANA_BROWSER
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ManaCore.Imaging;

public static partial class ManaImageCodec
{
    private static ManaImage Decode(byte[] bytes)
    {
        try
        {
            using var image = Image.Load<Rgba32>(bytes);
            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            return new ManaImage(image.Width, image.Height, pixels);
        }
        catch (ImageFormatException exception)
        {
            throw new InvalidDataException(exception.Message, exception);
        }
    }

    private static void Encode(ManaImage image, string path)
    {
        using var encoded = Image.LoadPixelData<Rgba32>(image.Pixels, image.Width, image.Height);
        encoded.SaveAsPng(path);
    }

    // ImageSharp's bicubic, which every desktop picture was scaled with before this codec existed.
    internal static ManaImage Resize(ManaImage source, int width, int height)
    {
        using var image = Image.LoadPixelData<Rgba32>(source.Pixels, source.Width, source.Height);
        image.Mutate(context => context.Resize(width, height));
        var pixels = new byte[width * height * 4];
        image.CopyPixelDataTo(pixels);
        return new ManaImage(width, height, pixels);
    }

    private static Task PrepareCore(byte[] bytes) => Task.CompletedTask;
}
#endif
