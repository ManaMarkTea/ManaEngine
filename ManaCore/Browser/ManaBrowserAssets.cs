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

    public static async Task InitializeAsync()
    {
        // Imports resolve against _framework/dotnet.runtime.js, not the document.
        await JSHost.ImportAsync("mana_assets", $"../asset-downloads.js?v={Guid.NewGuid():N}");
        ManaAssetCatalog.Current = new ManaAssetCatalog(await LoadCatalog(), AppContext.BaseDirectory,
            (url, hash, size) => Download(url, hash, size));
    }
}
#endif
