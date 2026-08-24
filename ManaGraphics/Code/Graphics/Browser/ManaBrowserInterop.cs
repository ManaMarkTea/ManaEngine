#if MANA_BROWSER
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace ManaGraphics.Code.Graphics.Browser
{
    // JS glue for the browser IWindow/IInputContext: canvas size query, a
    // requestAnimationFrame-driven render loop, a ResizeObserver, and real keyboard/mouse input
    // (DOM listeners live in mana_interop.js's installInput; ManaBrowserInputContext holds the
    // state the engine polls). Pointer lock is exposed for mouse-look but must be requested from
    // inside a user gesture -- see RequestPointerLock.
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
            // The ?v= token is not decoration. This module is fetched by a dynamic import at
            // runtime, not as a subresource of the document, and Chrome will happily serve that
            // from its HTTP cache even through a hard reload -- which means a freshly built .wasm
            // can end up calling into a stale copy of this JS. That surfaces as
            // "installInput must be a Function but was undefined": new C# calling a function the
            // cached module predates. Confirmed live, twice, including with no-store response
            // headers already in place. Making the URL itself differ per process start sidesteps
            // every layer of caching, and the file is a few KB.
            var cacheBuster = Guid.NewGuid().ToString("N");
            await JSHost.ImportAsync(ModuleName, $"../mana_interop.js?v={cacheBuster}");
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

        [JSImport("installInput", ModuleName)]
        internal static partial void InstallInput();

        /// <summary>
        /// Browsers only grant pointer lock from inside a user-gesture handler, so this has to be
        /// called from a click/keypress the user actually made -- calling it on startup is silently
        /// ignored.
        /// </summary>
        [JSImport("requestPointerLock", ModuleName)]
        internal static partial void RequestPointerLock();

        [JSImport("exitPointerLock", ModuleName)]
        internal static partial void ExitPointerLock();

        [JSImport("isPointerLocked", ModuleName)]
        internal static partial bool IsPointerLocked();

        // JS -> C#. JS calls these directly via the assembly-exports object main.js stashes on
        // globalThis (see main.js) rather than passing .NET delegates into the JSImport calls
        // above, which keeps the marshaling on both sides to plain primitives.
        [JSExport]
        internal static void OnFrame(double deltaTimeSeconds)
        {
            ManaBrowserWindow.Current?.RaiseFrame(deltaTimeSeconds);

            // After the frame, not before: scroll is a one-frame delta in Silk.NET's model, so the
            // frame that just ran had to be the one that got to see it.
            ManaBrowserInputContext.Current?.EndFrame();
        }

        [JSExport]
        internal static void OnKeyDown(string code, bool isRepeat)
        {
            ManaBrowserInputContext.Current?.Keyboard.HandleKeyDown(ManaBrowserKeyMap.FromDomCode(code), isRepeat);
        }

        [JSExport]
        internal static void OnKeyUp(string code)
        {
            ManaBrowserInputContext.Current?.Keyboard.HandleKeyUp(ManaBrowserKeyMap.FromDomCode(code));
        }

        [JSExport]
        internal static void OnKeyChar(string character)
        {
            if (!string.IsNullOrEmpty(character))
            {
                ManaBrowserInputContext.Current?.Keyboard.HandleKeyChar(character[0]);
            }
        }

        [JSExport]
        internal static void OnFocusLost()
        {
            ManaBrowserInputContext.Current?.ReleaseAll();
        }

        [JSExport]
        internal static void OnMouseMove(double x, double y)
        {
            ManaBrowserInputContext.Current?.Mouse.HandleMove((float)x, (float)y);
        }

        [JSExport]
        internal static void OnMouseDelta(double deltaX, double deltaY)
        {
            ManaBrowserInputContext.Current?.Mouse.HandleDelta((float)deltaX, (float)deltaY);
        }

        [JSExport]
        internal static void OnMouseDown(int button)
        {
            ManaBrowserInputContext.Current?.Mouse.HandleButtonDown(button);
        }

        [JSExport]
        internal static void OnMouseUp(int button)
        {
            ManaBrowserInputContext.Current?.Mouse.HandleButtonUp(button);
        }

        [JSExport]
        internal static void OnMouseWheel(double x, double y)
        {
            ManaBrowserInputContext.Current?.Mouse.HandleWheel((float)x, (float)y);
        }

        [JSExport]
        internal static void OnResize(int width, int height)
        {
            ManaBrowserWindow.Current?.RaiseFramebufferResize(width, height);
        }
    }
}
#endif
