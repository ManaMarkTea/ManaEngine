using Mana;
using Mana.Code.GameModes;
using ManaCore.Graphics;
using ManaCore.Loading;
using ManaGraphics.Code.Diagnostics;
using ManaGraphics.Code.Graphics.Browser;
using ManaVoxel.Code.Levels;
using ManaVoxel.Web;

var editorMode = args.Length > 0 && args[0] == "--editor";
Console.WriteLine($"ManaVoxel.Web booting ({(editorMode ? "editor" : "play")}, browser-wasm)...");

await ManaBrowserInterop.InitializeAsync();
ManaBrowserEmoji.Install();
await ManaAudio.ManaAudioFactory.InitializeBrowserAsync();
ManaBrowserInterop.ReportLoadingProgress("Starting engine", 0.0f);

try
{
    await ManaCore.Browser.ManaBrowserAssets.InitializeAsync();
    if (editorMode)
    {
        ManaBrowserInterop.SuppressBrowserKeys("F5");
        ManaUiDensity.RestorePreset();
        _ = ManaBrowserEmoji.WarmAsync(new[]
        {
            0x1F30A, 0x1F480, 0x1F680, 0x1F3A8, 0x1F5D1, 0x1F6E2, 0x1F50C, 0x1F479,
            0x1F4A8, 0x1F4A5, 0x1F600, 0x1F4E6, 0x1F527, 0x1F9EA, 0x1F30D, 0x1F30B,
            0x1F332, 0x1F525, 0x1F4A7, 0x1F528, 0x1F4DD, 0x1F195, 0x1F496, 0x1F5A4, 0x1F53D
        }, 15.0f);
        var editor = new VoxelEditorGameMode(ManaGraphicsBackendKind.WebGpu);
        var overlayHidden = false;
        editor.Loading.PropertyChanged += (_, _) =>
        {
            if (overlayHidden)
            {
                return;
            }
            if (!editor.IsLoading)
            {
                overlayHidden = true;
                ManaBrowserInterop.HideLoadingScreen();
                return;
            }
            var snapshot = editor.Loading.Snapshot;
            if (editor.LoadingCoordinator.Stage == ManaLoadingStage.Failed)
            {
                ManaBrowserInterop.ReportLoadingFailed(snapshot.Status);
                return;
            }
            ManaBrowserInterop.ReportLoadingProgress(snapshot.Status, snapshot.Fraction);
        };
        // Yield so the browser paints the loading status before synchronous world construction.
        await Task.Delay(32);
        editor.RunWebGpu(null);
    }
    else
    {
        var levelPath = "Content/Levels/Sample Course.manalevel";
        if (args.Length > 0 && (args[0].StartsWith("Content/Levels/", StringComparison.Ordinal) || args[0].StartsWith("Games/", StringComparison.Ordinal))
            && !args[0].Contains("..") && args[0].EndsWith(".manalevel", StringComparison.Ordinal))
        {
            // A game's level moved into Games/<Name>/Content/Levels; a link from before that still finds it by name.
            var moved = Directory.Exists("Games")
                ? Directory.EnumerateDirectories("Games").Select(game => $"{game.Replace('\\', '/')}/Content/Levels/{Path.GetFileName(args[0])}").FirstOrDefault(File.Exists)
                : null;
            levelPath = File.Exists(args[0]) ? args[0] : moved ?? levelPath;
        }
        Console.WriteLine($"[BrowserPlay] Level: {levelPath}");
        await ManaVoxel.Code.Assets.ManaLevelAssetPreloader.EnsureAsync(ManaLevel.Load(levelPath));
        var play = new BrowserPlayGameMode(levelPath, ManaGraphicsBackendKind.WebGpu);
        await Task.Delay(32);
        play.RunWebGpu();
    }
}
catch (Exception exception)
{
    Console.WriteLine($"Browser bring-up failed: {exception}");
    ManaBrowserInterop.ReportLoadingFailed(exception.Message);
    throw;
}
