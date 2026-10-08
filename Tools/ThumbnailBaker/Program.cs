// Pre-renders a preview image for every model in a published asset-catalog.json into a committed cache
// (Assets/Generated/Thumbnails), using the same headless CPU rasterizer MeshThumbnailCache falls back to at runtime
// (MeshThumbnailCache.GenerateViaCpuStandalone), so a pre-built preview and an on-demand one render alike.
// index.json maps each model to the content hash it was rendered from; only models whose file changed are
// rendered again, and previews of models that are gone are dropped. publish_assets.py attaches the cached
// previews to the catalog at publish time, so CI never renders any.
//
// Run offline, through Tools/WebAssets/bake_thumbnails.py, which publishes the catalog it reads first.
//
//   dotnet run --project Tools/ThumbnailBaker -- --app-bundle <published output> --cache Assets/Generated/Thumbnails

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mana;
using Mana.Code;
using ManaVoxel.Code.Assets;
using ManaCore.Imaging;

if (args.Length != 4 || args[0] != "--app-bundle" || args[2] != "--cache")
{
    Console.Error.WriteLine("Usage: dotnet run --project Tools/ThumbnailBaker -- --app-bundle <published output> --cache <Assets/Generated/Thumbnails>");
    return 1;
}

var appBundle = Path.GetFullPath(args[1]);
var cacheDirectory = Path.GetFullPath(args[3]);
var catalog = JsonNode.Parse(File.ReadAllText(Path.Combine(appBundle, "asset-catalog.json")))!.AsObject();

var filesByPath = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
foreach (var file in catalog["files"]!.AsArray())
{
    filesByPath[file!["path"]!.GetValue<string>()] = file.AsObject();
}

// A renderer change (version bump) or a new size invalidates every cached preview.
var indexFile = Path.Combine(cacheDirectory, "index.json");
var oldEntries = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
if (File.Exists(indexFile))
{
    var oldIndex = JsonNode.Parse(File.ReadAllText(indexFile))!.AsObject();
    if (oldIndex["version"]?.GetValue<int>() == MeshThumbnailCache.CacheFormatVersion
        && oldIndex["size"]?.GetValue<int>() == MeshThumbnailCache.ThumbnailSize)
    {
        foreach (var (label, entryNode) in oldIndex["thumbnails"]!.AsObject())
        {
            oldEntries[label] = entryNode!.AsObject();
        }
    }
}

// A throwaway world for a throwaway context -- the same minimal recipe MeshThumbnailTests.cs uses to
// exercise the CPU rasterizer headlessly, with no window, GPU or Initialize() call needed for it.
var context = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };

Directory.CreateDirectory(cacheDirectory);
var entries = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
var baked = 0;
var reused = 0;
var skipped = 0;

foreach (var modelNode in catalog["models"]!.AsArray())
{
    var model = modelNode!.AsObject();
    var path = model["path"]!.GetValue<string>();
    var entry = model.TryGetPropertyValue("entry", out var entryNode) ? entryNode!.GetValue<string>() : null;
    var label = entry == null ? path : $"{path}!{entry.Replace('\\', '/')}";

    if (!filesByPath.TryGetValue(path, out var fileEntry))
    {
        Console.WriteLine($"[ThumbnailBaker] '{label}': not found in the catalog's own file list, skipped.");
        skipped++;
        continue;
    }

    var source = fileEntry["sha256"]!.GetValue<string>();
    if (oldEntries.TryGetValue(label, out var old) && old["source"]?.GetValue<string>() == source
        && File.Exists(Path.Combine(cacheDirectory, old["png"]!.GetValue<string>())))
    {
        entries[label] = new JsonObject { ["source"] = source, ["png"] = old["png"]!.GetValue<string>() };
        reused++;
        continue;
    }

    var localFile = Path.Combine(appBundle, fileEntry["url"]!.GetValue<string>());

    byte[]? pixels;
    try
    {
        pixels = entry == null
            ? MeshThumbnailCache.GenerateViaCpuStandalone(context, localFile, BuildSiblingResolver(path))
            : MeshThumbnailCache.GenerateViaCpuStandalone(context, MeshAssetLibrary.EncodeZip(localFile, entry));
    }
    catch (Exception ex)
    {
        // One unrenderable model (an unsupported extension the loader could not substitute a sibling
        // for, a malformed export) must not take the whole catalog's bake down with it.
        Console.WriteLine($"[ThumbnailBaker] '{label}': {ex.Message}");
        skipped++;
        continue;
    }

    if (pixels == null)
    {
        Console.WriteLine($"[ThumbnailBaker] '{label}': produced no geometry, skipped.");
        skipped++;
        continue;
    }

    var pngBytes = ManaImageCodec.EncodePng(new ManaImage(MeshThumbnailCache.ThumbnailSize, MeshThumbnailCache.ThumbnailSize, pixels));
    var png = Convert.ToHexString(SHA256.HashData(pngBytes)).ToLowerInvariant() + ".png";

    var destination = Path.Combine(cacheDirectory, png);
    if (!File.Exists(destination))
    {
        File.WriteAllBytes(destination, pngBytes);
    }

    entries[label] = new JsonObject { ["source"] = source, ["png"] = png };
    baked++;
}

// Previews no model points at any more.
var referenced = entries.Values.Select(e => e["png"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
var removed = 0;
foreach (var file in Directory.EnumerateFiles(cacheDirectory, "*.png"))
{
    if (!referenced.Contains(Path.GetFileName(file)))
    {
        File.Delete(file);
        removed++;
    }
}

var index = new JsonObject
{
    ["version"] = MeshThumbnailCache.CacheFormatVersion,
    ["size"] = MeshThumbnailCache.ThumbnailSize,
    ["thumbnails"] = new JsonObject(entries.Select(e => KeyValuePair.Create(e.Key, (JsonNode?)e.Value)))
};
File.WriteAllText(indexFile, index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"[ThumbnailBaker] {baked} rendered, {reused} unchanged, {removed} removed, {skipped} skipped.");
return 0;

// A loose model's sibling buffers/textures live in AssetLibrary/ renamed to their content hash and
// flattened into one directory, so the relative URI SharpGLTF asks for ("FlightHelmet.bin") no longer
// resolves against anything on disk -- unlike a real checkout, or the browser's own downloaded copy, both
// of which keep the original directory structure. This resolver reverses publish_assets.py's own
// dependency_paths (model's own directory + the requested URI, normalized) to find which catalog file that
// URI actually meant, mirroring the resolver a zip-packed asset already needs for the same reason.
Func<string, byte[]?> BuildSiblingResolver(string modelCatalogPath)
{
    var modelDirectory = modelCatalogPath.Contains('/')
        ? modelCatalogPath[..modelCatalogPath.LastIndexOf('/')]
        : string.Empty;

    return assetName =>
    {
        var candidate = NormalizePosixPath($"{modelDirectory}/{Uri.UnescapeDataString(assetName)}");
        if (!filesByPath.TryGetValue(candidate, out var dependency))
        {
            return null;
        }

        return File.ReadAllBytes(Path.Combine(appBundle, dependency["url"]!.GetValue<string>()));
    };
}

static string NormalizePosixPath(string path)
{
    var segments = new List<string>();
    foreach (var segment in path.Split('/'))
    {
        switch (segment)
        {
            case "" or ".":
                continue;
            case "..":
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            default:
                segments.Add(segment);
                break;
        }
    }

    return string.Join('/', segments);
}
