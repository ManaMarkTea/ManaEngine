#if MANA_BROWSER
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using ManaCore.Assets;

namespace ManaCore.Browser;

[SupportedOSPlatform("browser")]
public static partial class ManaBrowserAssets
{
    [JSImport("loadCatalog", "mana_assets")]
    private static partial Task<string> LoadCatalog();

    [JSImport("downloadAsset", "mana_assets")]
    private static partial Task<JSObject> DownloadCore(string url, string hash, double size);

    [JSImport("readBytes", "mana_assets")]
    private static partial byte[] ReadBytes(JSObject result);

    private static async Task<byte[]> Download(string url, string hash, double size)
    {
        using var result = await DownloadCore(url, hash, size);
        return ReadBytes(result);
    }

    /// <summary>
    /// Where the itch.io build's catalog points its blobs, now that the library itself is too large for
    /// itch's browser-embed size cap (see Tools/WebAssets/split_for_itch.py). A locally published build's
    /// catalog still uses relative "AssetLibrary/..." URLs and is unaffected -- this only widens what the
    /// catalog is *allowed* to say, it does not change what a given build's catalog actually says.
    /// </summary>
    private const string ExternalAssetBaseUrl = "https://manamarktea.github.io/ManaRenderer/web-assets/AssetLibrary/";

    public static async Task InitializeAsync()
    {
        // Imports resolve against _framework/dotnet.runtime.js, not the document.
        await JSHost.ImportAsync("mana_assets", $"../asset-downloads.js?v={Guid.NewGuid():N}");
        ManaAssetCatalog.Current = new ManaAssetCatalog(await LoadCatalog(), AppContext.BaseDirectory,
            (url, hash, size) => Download(url, hash, size), ExternalAssetBaseUrl);
    }
}
#endif
