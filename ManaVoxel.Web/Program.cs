using Mana;
using ManaCore.Graphics;
using ManaGraphics.Code.Graphics;
#if MANA_BROWSER
using ManaGraphics.Code.Graphics.Browser;
using ManaVoxel.Web;
#endif

Console.WriteLine("ManaVoxel.Web booting (browser-wasm)...");

// Quick boot smoke test first (cheap, and gives useful diagnostic output even if the heavier
// WebGPU bring-up below fails): proves the full ManaCore + Mana + ManaGraphics reference graph
// loads and runs real engine code under Mono's wasm runtime.
var world = new ManaWorld();
Console.WriteLine($"ManaWorld constructed OK ({world.Meshes.Count} meshes, {world.Scenes.Count} scenes at boot).");
Console.WriteLine($"ManaGraphicsBackendKind values: {string.Join(", ", Enum.GetNames<ManaGraphicsBackendKind>())}");

#if MANA_BROWSER
// The real M1/M2 attempt: a live browser IWindow (canvas-backed), the real WebGPU shim, and
// GameSilkWebGpu's actual render loop -- the same code path VoxelPlayGameMode's own bring-up
// uses on desktop, unmodified beyond the #if MANA_BROWSER branches already wired into it. Its
// default per-frame behavior clears the canvas to a solid color, matching M0's original
// "canvas clears to a color" verification target -- but this time through the real engine, not
// a throwaway spike. NOT yet confirmed working end to end against a real browser; the whole
// point of shipping this now is to find out from an actual browser console what breaks first.
await ManaBrowserInterop.InitializeAsync();
Console.WriteLine("Browser interop module loaded.");

// The loading screen (index.html's overlay) has been up since the page loaded, showing the wasm
// download. From here it belongs to the engine; see BrowserPlayGameMode's constructor for the
// bridge that keeps it fed.
ManaBrowserInterop.ReportLoadingProgress("Starting engine", 0.0f);

try
{
    // The real game: ground, the bundled level, a pawn controller and a follow camera -- the same
    // engine lifecycle the desktop play mode drives, minus the editor. See BrowserPlayGameMode.
    var playMode = new BrowserPlayGameMode("Levels/Little Train Yard.manalevel", ManaGraphicsBackendKind.WebGpu);

    // Not padding. Browser wasm is single-threaded, so BrowserPlayGameMode.Load runs inline on the
    // main thread and holds it for seconds -- decoding a 5 MB environment map and unzipping meshes.
    // The DOM writes queued above cannot paint until that lets go, so without a yield here the
    // screen still reads "Downloading" for the whole load and then jumps straight to the game. One
    // turn of the event loop lets the browser paint the status the user is about to be waiting on.
    // (The screen's own animations are CSS, on the compositor, so those keep moving through the
    // block regardless -- see index.html. This is only about the text and the bar position.)
    await Task.Delay(32);

    playMode.RunWebGpu();
    Console.WriteLine("GameSilkWebGpu.Run() returned (expected: browser bring-up is async and non-blocking; " +
        "watch the console above/after this line for how far it actually got).");
}
catch (Exception exception)
{
    // Anything thrown out of bring-up before the game mode's own loading coordinator exists would
    // otherwise leave the overlay spinning forever with no clue why.
    Console.WriteLine($"Browser bring-up failed: {exception}");
    ManaBrowserInterop.ReportLoadingFailed(exception.Message);
    throw;
}
#else
Console.WriteLine("Not a browser-wasm build; skipping the WebGPU bring-up attempt.");
#endif
