// Pre-renders a preview image for every model in a published asset-catalog.json, using the same headless
// CPU rasterizer MeshThumbnailCache falls back to at runtime (MeshThumbnailCache.GenerateViaCpuStandalone),
// so a pre-built preview and an on-demand one render identically. Staged into the same AssetLibrary/ the
// catalog already ships beside, and referenced from each model entry by a "thumbnail" catalog path -- an
// ordinary file as far as the rest of the pipeline (ManaAssetCatalog, tools/WebAssets/split_for_itch.py) is
// concerned, verified and served exactly like a model's own blob.
//
// Run after `dotnet publish` (which populates AppBundle/AssetLibrary) and before split_for_itch.py (which
// moves AssetLibrary out to GitHub Pages) -- this needs the real model bytes on disk to render from, and
// whatever it adds to AssetLibrary/ rides along with everything else once split_for_itch.py runs.
//
//   dotnet run --project tools/ThumbnailBaker -- --app-bundle <path to AppBundle>

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Mana;
using Mana.Code;
using ManaVoxel.Code.Assets;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

if (args.Length != 2 || args[0] != "--app-bundle")
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/ThumbnailBaker -- --app-bundle <AppBundle directory>");
    return 1;
}

var appBundle = Path.GetFullPath(args[1]);
var catalogFile = Path.Combine(appBundle, "asset-catalog.json");
var catalog = JsonNode.Parse(File.ReadAllText(catalogFile))!.AsObject();
var files = catalog["files"]!.AsArray();

var filesByPath = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
foreach (var file in files)
{
    filesByPath[file!["path"]!.GetValue<string>()] = file.AsObject();
}

// A throwaway world for a throwaway context -- the same minimal recipe MeshThumbnailTests.cs uses to
// exercise the CPU rasterizer headlessly, with no window, GPU or Initialize() call needed for it.
var context = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };

var libraryDirectory = Path.Combine(appBundle, "AssetLibrary");
var baked = 0;
var skipped = 0;

foreach (var modelNode in catalog["models"]!.AsArray())
{
    var model = modelNode!.AsObject();
    var path = model["path"]!.GetValue<string>();
    var entry = model.TryGetPropertyValue("entry", out var entryNode) ? entryNode!.GetValue<string>() : null;
    var label = entry == null ? path : $"{path}!{entry}";

    if (!filesByPath.TryGetValue(path, out var fileEntry))
    {
        Console.WriteLine($"[ThumbnailBaker] '{label}': not found in the catalog's own file list, skipped.");
        skipped++;
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

    using var image = Image.LoadPixelData<Rgba32>(pixels, MeshThumbnailCache.ThumbnailSize, MeshThumbnailCache.ThumbnailSize);
    using var pngStream = new MemoryStream();
    image.SaveAsPng(pngStream);
    var pngBytes = pngStream.ToArray();
    var hash = Convert.ToHexString(SHA256.HashData(pngBytes)).ToLowerInvariant();

    Directory.CreateDirectory(libraryDirectory);
    var destination = Path.Combine(libraryDirectory, $"{hash}.png");
    if (!File.Exists(destination))
    {
        File.WriteAllBytes(destination, pngBytes);
    }

    // "!" separates a zip's own path from the entry inside it -- never produced by publish_assets.py for a
    // real asset path, so this can't collide with one. Same file reused (not re-added) if two catalog runs
    // land on the same content, matching publish_assets.py's own hash-named dedup.
    var thumbnailPath = entry == null ? $"{path}.thumb.png" : $"{path}!{entry.Replace('\\', '/')}.thumb.png";
    if (!filesByPath.ContainsKey(thumbnailPath))
    {
        var thumbnailEntry = new JsonObject
        {
            ["path"] = thumbnailPath,
            ["url"] = $"AssetLibrary/{hash}.png",
            ["sha256"] = hash,
            ["size"] = pngBytes.LongLength,
            ["dependencies"] = new JsonArray()
        };
        files.Add(thumbnailEntry);
        filesByPath[thumbnailPath] = thumbnailEntry;
    }

    model["thumbnail"] = thumbnailPath;
    baked++;
}

File.WriteAllText(catalogFile, catalog.ToJsonString());
Console.WriteLine($"[ThumbnailBaker] {baked} pre-built preview(s), {skipped} skipped.");
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
