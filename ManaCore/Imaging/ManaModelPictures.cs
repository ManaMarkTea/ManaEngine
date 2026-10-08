using System.IO.Compression;
using System.Text.Json;

namespace ManaCore.Imaging;

/// <summary>
/// The encoded pictures a glTF model draws with -- separate files, zip entries beside it, or embedded in its
/// buffers -- read without loading the model, so the browser can decode them before the model loads.
/// </summary>
public static class ManaModelPictures
{
    /// <summary>A loose model file's pictures, or the file itself when it is a picture.</summary>
    public static IEnumerable<byte[]> OfFile(string path)
    {
        if (ManaImageCodec.IsPicture(path))
        {
            return [File.ReadAllBytes(path)];
        }
        if (!IsModel(path))
        {
            return [];
        }

        var directory = Path.GetDirectoryName(path) ?? "";
        return OfModel(File.ReadAllBytes(path), relative =>
        {
            var file = Path.Combine(directory, relative);
            return File.Exists(file) ? File.ReadAllBytes(file) : null;
        });
    }

    /// <summary>The pictures of a model inside a zip, or the entry itself when it is a picture.</summary>
    public static IEnumerable<byte[]> OfZipEntry(string zipPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return OfZipEntry(archive, entryName.Replace('\\', '/')).ToList();
    }

    /// <summary>The pictures of every model in a zip (not the pack's other pictures, such as its previews).</summary>
    public static IEnumerable<byte[]> InZipModels(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        return archive.Entries.Where(entry => IsModel(entry.FullName))
            .SelectMany(entry => OfZipEntry(archive, entry.FullName)).ToList();
    }

    private static IEnumerable<byte[]> OfZipEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        if (entry == null)
        {
            return [];
        }
        if (ManaImageCodec.IsPicture(entryName))
        {
            return [Read(entry)];
        }
        if (!IsModel(entryName))
        {
            return [];
        }

        var directory = entryName.Contains('/') ? entryName[..entryName.LastIndexOf('/')] : "";
        return OfModel(Read(entry), relative =>
            archive.GetEntry(Normalize(directory + "/" + relative)) is { } sibling ? Read(sibling) : null).ToList();
    }

    private static List<byte[]> OfModel(byte[] model, Func<string, byte[]?> readRelative)
    {
        var (json, binary) = Split(model);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var pictures = new List<byte[]>();
        if (!root.TryGetProperty("images", out var images))
        {
            return pictures;
        }

        var buffers = new Dictionary<int, byte[]?>();
        foreach (var image in images.EnumerateArray())
        {
            if (image.TryGetProperty("uri", out var uriElement) && uriElement.GetString() is { } uri)
            {
                if ((FromDataUri(uri) ?? readRelative(Uri.UnescapeDataString(uri))) is { } bytes)
                {
                    pictures.Add(bytes);
                }
                continue;
            }

            if (!image.TryGetProperty("bufferView", out var viewIndex) || !root.TryGetProperty("bufferViews", out var views))
            {
                continue;
            }
            var view = views[viewIndex.GetInt32()];
            var bufferIndex = view.GetProperty("buffer").GetInt32();
            if (!buffers.TryGetValue(bufferIndex, out var buffer))
            {
                var source = root.GetProperty("buffers")[bufferIndex];
                buffers[bufferIndex] = buffer = source.TryGetProperty("uri", out var bufferUri) && bufferUri.GetString() is { } u
                    ? FromDataUri(u) ?? readRelative(Uri.UnescapeDataString(u))
                    : binary;
            }

            var offset = view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
            var length = view.GetProperty("byteLength").GetInt32();
            if (buffer != null && offset >= 0 && length > 0 && offset + length <= buffer.Length)
            {
                pictures.Add(buffer.AsSpan(offset, length).ToArray());
            }
        }
        return pictures;
    }

    // A .gltf is its JSON; a .glb is a header, then a JSON chunk and an optional binary one.
    private static (ReadOnlyMemory<byte> Json, byte[]? Binary) Split(byte[] model)
    {
        if (model.Length < 20 || BitConverter.ToUInt32(model, 0) != 0x46546C67)
        {
            return (model, null);
        }

        var jsonLength = (int)BitConverter.ToUInt32(model, 12);
        if (20 + jsonLength > model.Length)
        {
            throw new InvalidDataException("A truncated GLB.");
        }
        var json = new ReadOnlyMemory<byte>(model, 20, jsonLength);
        var binaryStart = 20 + jsonLength;
        if (binaryStart + 8 > model.Length || BitConverter.ToUInt32(model, binaryStart + 4) != 0x004E4942)
        {
            return (json, null);
        }
        var binaryLength = (int)Math.Min(BitConverter.ToUInt32(model, binaryStart), (uint)(model.Length - binaryStart - 8));
        return (json, model.AsSpan(binaryStart + 8, binaryLength).ToArray());
    }

    private static byte[]? FromDataUri(string uri)
    {
        if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var comma = uri.IndexOf(',');
        return comma > 0 && uri[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(uri[(comma + 1)..])
            : null;
    }

    private static bool IsModel(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".glb" or ".gltf";

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream((int)Math.Max(0, entry.Length));
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Split('/'))
        {
            if (part is "" or ".")
            {
                continue;
            }
            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
                continue;
            }
            parts.Add(part);
        }
        return string.Join('/', parts);
    }
}
