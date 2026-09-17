using System.Text;
using System.Text.Json;
using ManaCore.Assets;
using ManaUI;

namespace ManaTests;

public static class AssetCatalogChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check) =>
        RunAsync(check).GetAwaiter().GetResult();

    private static async Task RunAsync(Action<string, bool, string> check)
    {
        var directory = Directory.CreateTempSubdirectory("mana-asset-catalog-");
        try
        {
            var definitions = new[]
            {
                new { path = "Assets/model.gltf", url = "AssetLibrary/model.gltf", sha256 = "model", size = 3, dependencies = new[] { "Assets/model.bin", "Assets/texture.png" } },
                new { path = "Assets/model.bin", url = "AssetLibrary/model.bin", sha256 = "buffer", size = 3, dependencies = Array.Empty<string>() },
                new { path = "Assets/texture.png", url = "AssetLibrary/texture.png", sha256 = "texture", size = 3, dependencies = Array.Empty<string>() },
                new { path = "Assets/pack.zip", url = "AssetLibrary/pack.zip", sha256 = "pack", size = 3, dependencies = Array.Empty<string>() }
            };
            var json = JsonSerializer.Serialize(new
            {
                version = 1, files = definitions,
                models = new object[] { new { path = "Assets/model.gltf" }, new { path = "Assets/pack.zip", entry = "Models/ship.glb" } }
            });
            var requests = new List<string>();
            var gate = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var catalog = new ManaAssetCatalog(json, directory.FullName, async (url, hash, size) =>
            {
                lock (requests) requests.Add(url);
                return url.EndsWith("model.gltf") ? await gate.Task : new byte[] { 1, 2, 3 };
            });
            check("the catalog lists loose and archived models without downloading", catalog.Models.Count == 2 && requests.Count == 0, "");
            check("an unloaded model is not ready for thumbnails", !catalog.IsReady(catalog.Models[0]), "");
            var bytesBeforeFirstBatch = ManaAssetDownloadActivity.TotalBytesDownloaded;
            var first = catalog.EnsureAsync(catalog.Models[0]);
            var second = catalog.EnsureAsync("Assets/model.gltf");
            check("concurrent requests share the same download", requests.Count == 1, "");
            check("a download in flight shows up as downloading",
                ManaAssetDownloadActivity.IsDownloading && ManaAssetDownloadActivity.ActiveNames.Contains("model.gltf"),
                string.Join(",", ManaAssetDownloadActivity.ActiveNames));
            gate.SetResult(new byte[] { 1, 2, 3 });
            await Task.WhenAll(first, second);
            check("import fetches the buffer and texture as well as the model", requests.Count == 3 && catalog.IsReady(catalog.Models[0]), "");
            check("and clears once every download in the batch finishes", !ManaAssetDownloadActivity.IsDownloading, "");
            check("the session total grows by exactly what was downloaded",
                ManaAssetDownloadActivity.TotalBytesDownloaded - bytesBeforeFirstBatch == 9, // 3 files x 3 bytes
                $"+{ManaAssetDownloadActivity.TotalBytesDownloaded - bytesBeforeFirstBatch}");
            await catalog.EnsureAsync(catalog.Models[0]);
            check("staged assets are reused without another download", requests.Count == 3, "");
            var relativeZip = "zipasset-rel:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Assets/pack.zip"))
                + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Models/ship.glb"));
            await catalog.EnsureAsync(relativeZip);
            check("saved levels resolve relative zip references to the same downloaded pack", catalog.IsReady(catalog.Models[1]) && requests.Count == 4, "");
            await catalog.EnsureAsync("user-model.glb");
            check("unknown local files do not become network requests",
                catalog.IsReady("user-model.glb") && requests.Count == 4, "");

            File.Delete(Path.Combine(directory.FullName, "Assets", "pack.zip"));
            var attempt = 0;
            var retry = new ManaAssetCatalog(json, directory.FullName, (_, _, _) =>
                Task.FromResult(++attempt == 1 ? new byte[] { 1 } : new byte[] { 1, 2, 3 }));
            var bytesBeforeRetry = ManaAssetDownloadActivity.TotalBytesDownloaded;
            var rejected = false;
            try { await retry.EnsureAsync(relativeZip); }
            catch (InvalidDataException) { rejected = true; }
            check("an incomplete download is not installed", rejected && !retry.IsReady(relativeZip), "");
            check("a rejected download does not count toward the session total",
                ManaAssetDownloadActivity.TotalBytesDownloaded == bytesBeforeRetry, "");
            await retry.EnsureAsync(relativeZip);
            check("a failed download can be retried", attempt == 2 && retry.IsReady(relativeZip), "");
            check("and the retry that actually lands does count",
                ManaAssetDownloadActivity.TotalBytesDownloaded - bytesBeforeRetry == 3, "");

            rejected = false;
            try { _ = new ManaAssetCatalog(json.Replace("Assets/model.bin", "Assets/../escape.bin"), directory.FullName, (_, _, _) => Task.FromResult(Array.Empty<byte>())); }
            catch (InvalidDataException) { rejected = true; }
            check("catalog paths cannot escape the asset root", rejected, "");

            const string trustedHost = "https://example.github.io/repo/web-assets/AssetLibrary/";
            var externalJson = json.Replace("\"AssetLibrary/model.gltf\"", $"\"{trustedHost}model.gltf\"");
            _ = new ManaAssetCatalog(externalJson, directory.FullName, (_, _, _) => Task.FromResult(Array.Empty<byte>()),
                externalAssetBaseUrl: trustedHost);
            check("a blob url naming the caller's trusted external host is accepted", true, "");

            rejected = false;
            try
            {
                _ = new ManaAssetCatalog(externalJson, directory.FullName, (_, _, _) => Task.FromResult(Array.Empty<byte>()));
            }
            catch (InvalidDataException) { rejected = true; }
            check("but not when the caller named no trusted host at all", rejected, "");

            rejected = false;
            var elsewhere = json.Replace("\"AssetLibrary/model.gltf\"",
                "\"https://evil.example/repo/web-assets/AssetLibrary/model.gltf\"");
            try
            {
                _ = new ManaAssetCatalog(elsewhere, directory.FullName, (_, _, _) => Task.FromResult(Array.Empty<byte>()),
                    externalAssetBaseUrl: trustedHost);
            }
            catch (InvalidDataException) { rejected = true; }
            check("nor a url naming some other host entirely", rejected, "");
        }
        finally { directory.Delete(recursive: true); }
    }
}
