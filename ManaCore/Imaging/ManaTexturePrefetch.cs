using System.Collections.Concurrent;

namespace ManaCore.Imaging;

/// <summary>
/// Pictures decoded and mip-mapped ahead, off the render thread, for the texture cache to take instead of doing it
/// there -- an 8K planet picture took 12 s of frozen frame to decode and mip in place. Whoever knows a big picture is
/// coming (the planet's imagery) prepares it before handing its path over; the texture cache takes it once.
/// </summary>
public static class ManaTexturePrefetch
{
    public sealed record Prepared(ManaImage Image, List<(byte[] Pixels, int Width, int Height)> Mips);

    private static readonly ConcurrentDictionary<string, Prepared> Ready = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Decodes <paramref name="path"/> and builds its colour mips; call it off the render thread.</summary>
    public static void Prepare(string path) => Store(path, ManaImageCodec.Load(path));

    /// <summary>
    /// Flips a picture already in memory (one just painted -- it is taken over) for GPU coordinates, as the texture cache's
    /// own decode does, builds its mips and keeps it for <paramref name="path"/>.
    /// </summary>
    public static void Store(string path, ManaImage image)
    {
        image.FlipVertical();
        Ready[Key(path)] = new Prepared(image, ManaMipChain.Build(image.Pixels, image.Width, image.Height, srgb: true));
    }

    /// <summary>The prepared picture for <paramref name="path"/>, removed so it is uploaded once and then let go.</summary>
    public static bool TryTake(string path, out Prepared prepared)
    {
        return Ready.TryRemove(Key(path), out prepared!);
    }

    private static string Key(string path) => Path.GetFullPath(path);
}
