#if MANA_BROWSER
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ManaGraphics.Code.Graphics.Browser
{
    // JS glue for the browser IWindow/IInputContext (M1). Deliberately minimal: canvas size query,
    // a requestAnimationFrame-driven render loop, and a ResizeObserver -- exactly what
    // ManaBrowserWindow needs (see WebPort-Plan.md's earlier finding that ManaWebGpuContext only
    // ever reads window.FramebufferSize). Real keyboard/mouse input is not wired yet; play mode's
    // camera controls won't respond to input until that's added.
    //
    // Uses the explicit JSHost.ImportAsync registration pattern rather than a bare
    // [JSImport("func", "mana_interop.js")] module-name string, to avoid depending on exactly how
    // the wasm loader resolves a relative JS path (not confirmed against a live browser yet --
    // see WebPort-Plan.md). InitializeAsync() must be awaited once before any other member here is
    // used; Program.cs does this before constructing ManaBrowserWindow.
    [SupportedOSPlatform("browser")]
    public static partial class ManaBrowserInterop
    {
        private const string ModuleName = "mana_interop";

        public static async System.Threading.Tasks.Task InitializeAsync()
        {
            // "../" because JSHost.ImportAsync resolves a relative path against the calling JS
            // module's own location (dotnet.runtime.js, inside _framework/), not the app root --
            // confirmed by an actual ERR_MODULE_NOT_FOUND for "_framework/mana_interop.js" (where
            // it's deployed one level up, alongside index.html/main.js), not assumed.
            await JSHost.ImportAsync(ModuleName, "../mana_interop.js");
        }

        [JSImport("selectCanvas", ModuleName)]
        internal static partial bool SelectCanvas(string selector);

        [JSImport("getCanvasWidth", ModuleName)]
        internal static partial int GetCanvasWidth();

        [JSImport("getCanvasHeight", ModuleName)]
        internal static partial int GetCanvasHeight();

        [JSImport("getTimeMs", ModuleName)]
        internal static partial double GetTimeMs();

        [JSImport("observeResize", ModuleName)]
        internal static partial void ObserveResize();

        [JSImport("startRenderLoop", ModuleName)]
        internal static partial void StartRenderLoop();

        // JS -> C#. JS calls these directly via the assembly-exports object main.js stashes on
        // globalThis (see main.js) rather than passing .NET delegates into the JSImport calls
        // above, which keeps the marshaling on both sides to plain primitives.
        [JSExport]
        internal static void OnFrame(double deltaTimeSeconds)
        {
            ManaBrowserWindow.Current?.RaiseFrame(deltaTimeSeconds);
        }

        [JSExport]
        internal static void OnResize(int width, int height)
        {
            ManaBrowserWindow.Current?.RaiseFramebufferResize(width, height);
        }
    }
}
#endif
