using Mana;
using Mana.Code.GameModes;
using ManaCore.Graphics;
using ManaCore.Loading;
using ManaGraphics.Code.Diagnostics;
using ManaGraphics.Code.Graphics.Browser;

// M9's live bootstrap: the editor, not play mode. Sibling of ManaVoxel.Web/Program.cs, which this
// otherwise mirrors closely -- see that file for the loading-screen bridge and the reasoning
// behind each step's ordering. The one real difference is what gets constructed at the bottom:
// VoxelEditorGameMode instead of BrowserPlayGameMode, with nothing hardcoded ahead of it (no
// bundled level -- the editor starts the same empty scene ManaVoxel.exe does with no --level
// argument, since --level isn't a thing a browser URL carries yet).

Console.WriteLine("ManaVoxel.Editor.Web booting (browser-wasm)...");

await ManaBrowserInterop.InitializeAsync();
// Emoji come from the browser's own font rather than a 10 MB download.
ManaBrowserEmoji.Install();

// F5 is Play in the editor. Left alone the browser would also refresh the page, throwing the level away, so the
// page is told not to; the engine still receives the key.
ManaBrowserInterop.SuppressBrowserKeys("F5");
await ManaAudio.ManaAudioFactory.InitializeBrowserAsync();
Console.WriteLine("Browser interop module loaded.");

// Desktop's Program.cs does this before constructing anything too. Needs mana_interop.js already
// loaded (ManaUiDensity routes through ManaBrowserSettingsBridge on MANA_BROWSER, backed by it),
// which InitializeAsync above just did -- this couldn't run any earlier than here.
ManaUiDensity.RestorePreset();

ManaBrowserInterop.ReportLoadingProgress("Starting engine", 0.0f);

// The emoji the editor's panels use, drawn while the loading screen is up (it shows each one as it is made), so
// the first frame of the UI finds them ready. Fire and forget: it yields between emoji and the load carries on.
_ = ManaBrowserEmoji.WarmAsync(new[]
{
    0x1F30A, 0x1F480, 0x1F680, 0x1F3A8, 0x1F5D1, 0x1F6E2, 0x1F50C, 0x1F479, 0x1F4A8, 0x1F4A5, 0x1F600, 0x1F4E6, 0x1F527,
    0x1F9EA, 0x1F30D, 0x1F30B, 0x1F332, 0x1F525, 0x1F4A7, 0x1F528, 0x1F4DD, 0x1F195, 0x1F496, 0x1F5A4, 0x1F53D
}, 15.0f);

try
{
    await ManaCore.Browser.ManaBrowserAssets.InitializeAsync();
    var editor = new VoxelEditorGameMode(ManaGraphicsBackendKind.WebGpu);

    // The editor shows its OWN loading screen once ManaUi is up (BaseGameMode wraps any
    // EnableManaUi=true mode's content in ManaUiLoadingOverlay automatically -- see
    // WrapManaUiWithLoadingScreen), so unlike BrowserPlayGameMode this HTML overlay only has to
    // cover the gap before that exists: the wasm download and engine bring-up, not the frame
    // ManaUi's own screen is already there for. No "wait one more frame" precision needed for
    // that reason -- ManaUi's screen is the safety net under this one, not empty canvas.
    //
    // Still has to forward every intermediate status, not just completion -- confirmed live (a
    // real itch.io deployment sat on "Starting engine" for the whole load) that skipping this
    // half, unlike BrowserPlayGameMode.OnLoadingProgressChanged which forwards each one, leaves
    // the overlay frozen on whatever it said last rather than showing VoxelEditorGameMode.Load()'s
    // own "Loading environment reflection map"/"Generating procedural shapes"/etc. steps.
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

    // See ManaVoxel.Web/Program.cs's identical comment: browser wasm is single-threaded, so
    // VoxelEditorGameMode.Load()'s own background Task.Run body (grid planes, cursor, KML probe)
    // runs inline on this same thread regardless -- the loading screen's DOM writes above still
    // need one turn of the event loop to actually paint before that starts.
    await Task.Delay(32);

    editor.RunWebGpu(null);
    Console.WriteLine("GameSilkWebGpu.Run() returned (expected: browser bring-up is async and " +
        "non-blocking; watch the console above/after this line for how far it actually got).");
}
catch (Exception exception)
{
    Console.WriteLine($"Browser bring-up failed: {exception}");
    ManaBrowserInterop.ReportLoadingFailed(exception.Message);
    throw;
}
