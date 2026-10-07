#if MANA_BROWSER
using System.Buffers.Binary;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using ManaCore.Diagnostics;

namespace ManaCore.Imaging;

[SupportedOSPlatform("browser")]
public static partial class ManaImageCodec
{
    private const int RawHeader = 16;

    // Pictures decoded ahead, by content; dropped once nothing has been prepared or loaded for a while,
    // since by then whatever was loading has finished. Pinned ones (the bundled files) stay.
    private static readonly Dictionary<string, (ManaImage Image, bool Pinned)> Decoded = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Task> Decoding = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ReportedMisses = new(StringComparer.Ordinal);
    private static readonly TimeSpan IdleBeforeTrim = TimeSpan.FromSeconds(30);
    private static long _lastActivity;
    private static bool _trimScheduled;

    [JSImport("decodeImage", "mana_assets")]
    private static partial Task<JSObject> DecodeInBrowser(byte[] bytes);

    private static ManaImage Decode(byte[] bytes)
    {
        if (TryReadRaw(bytes) is { } raw)
        {
            return raw;
        }

        Touch();
        var key = Key(bytes);
        if (Decoded.TryGetValue(key, out var entry))
        {
            return entry.Image.Clone();
        }

        // Nothing decoded it ahead: decode it now for next time, and say so once, since this picture is missing until then.
        _ = PrepareCore(bytes);
        if (ReportedMisses.Add(key))
        {
            ManaLog.WriteDiagnostic($"[Images] a {bytes.Length}-byte picture was loaded before it was decoded; it shows once loaded again.");
        }
        throw new InvalidDataException("The picture has not been decoded yet.");
    }

    private static void Encode(ManaImage image, string path)
    {
        var bytes = new byte[RawHeader + image.Width * image.Height * 4];
        RawMagic.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), image.Height);
        image.Pixels.AsSpan(0, image.Width * image.Height * 4).CopyTo(bytes.AsSpan(RawHeader));
        File.WriteAllBytes(path, bytes);
    }

    internal static ManaImage Resize(ManaImage source, int width, int height) => ResizeByArea(source, width, height);

    private static Task PrepareCore(byte[] bytes) => Prepare(bytes, pinned: false);

    /// <summary>Decodes the pictures bundled into the app's own file system, which no download step ever sees,
    /// and keeps them: the moon, the debug grid, and the textures of the models in the bundled packs.</summary>
    public static async Task PrepareBundledAsync(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                if (IsPicture(file))
                {
                    await Prepare(await File.ReadAllBytesAsync(file), pinned: true);
                }
                else if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var picture in ManaModelPictures.InZipModels(file))
                    {
                        await Prepare(picture, pinned: true);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JSException)
            {
                ManaLog.WriteDiagnostic($"[Images] could not decode '{Path.GetFileName(file)}' ahead: {exception.Message}");
            }
        }
    }

    private static async Task Prepare(byte[] bytes, bool pinned)
    {
        if (TryReadRaw(bytes) != null)
        {
            return;
        }

        Touch();
        var key = Key(bytes);
        if (Decoded.TryGetValue(key, out var entry))
        {
            if (pinned && !entry.Pinned)
            {
                Decoded[key] = (entry.Image, true);
            }
            return;
        }

        if (!Decoding.TryGetValue(key, out var task))
        {
            Decoding[key] = task = DecodeAndStore(bytes, key, pinned);
        }
        await task;
    }

    private static async Task DecodeAndStore(byte[] bytes, string key, bool pinned)
    {
        try
        {
            using var result = await DecodeInBrowser(bytes);
            var width = result.GetPropertyAsInt32("width");
            var height = result.GetPropertyAsInt32("height");
            var pixels = result.GetPropertyAsByteArray("pixels");
            if (width > 0 && height > 0 && pixels != null)
            {
                Decoded[key] = (new ManaImage(width, height, pixels), pinned);
            }
        }
        catch (JSException exception)
        {
            // Not a picture the browser reads: the synchronous load reports it as unreadable.
            ManaLog.WriteDiagnostic($"[Images] the browser could not decode a {bytes.Length}-byte picture: {exception.Message}");
        }
        finally
        {
            Decoding.Remove(key);
            Touch();
        }
    }

    private static ManaImage? TryReadRaw(byte[] bytes)
    {
        if (bytes.Length < RawHeader || !bytes.AsSpan(0, 8).SequenceEqual(RawMagic))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
        var height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        if (width <= 0 || height <= 0 || (long)width * height * 4 != bytes.Length - RawHeader)
        {
            throw new InvalidDataException("A truncated raw picture.");
        }
        return new ManaImage(width, height, bytes.AsSpan(RawHeader).ToArray());
    }

    // Hashing whole textures is slow in wasm; the length and both ends tell any two real pictures apart.
    private static string Key(byte[] bytes)
    {
        const int Edge = 64 * 1024;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(bytes.LongLength));
        if (bytes.Length <= Edge * 2)
        {
            hash.AppendData(bytes);
        }
        else
        {
            hash.AppendData(bytes, 0, Edge);
            hash.AppendData(bytes, bytes.Length - Edge, Edge);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Touch()
    {
        _lastActivity = Environment.TickCount64;
        if (!_trimScheduled)
        {
            _trimScheduled = true;
            _ = TrimWhenIdle();
        }
    }

    private static async Task TrimWhenIdle()
    {
        while (Environment.TickCount64 - _lastActivity < IdleBeforeTrim.TotalMilliseconds)
        {
            await Task.Delay(IdleBeforeTrim);
        }

        foreach (var key in Decoded.Where(pair => !pair.Value.Pinned).Select(pair => pair.Key).ToList())
        {
            Decoded.Remove(key);
        }
        _trimScheduled = false;
    }
}
#endif
