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
    // the wasm loader resolves a relative JS path. InitializeAsync() must be awaited once before any other member here is
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

        [JSImport("getDevicePixelRatio", ModuleName)]
        internal static partial double GetDevicePixelRatio();

        [JSImport("pollGamepads", ModuleName)]
        [return: JSMarshalAs<JSType.Array<JSType.Number>>]
        internal static partial double[] PollGamepads();

        [JSImport("gamepadName", ModuleName)]
        internal static partial string GamepadName(int slot);

        /// <summary>The GPU the page got, as the browser describes it (mana_webgpu.c records it when the adapter arrives).</summary>
        [JSImport("gpuAdapter", ModuleName)]
        internal static partial string GpuAdapter();

        [JSImport("isMobileRendering", ModuleName)]
        internal static partial bool IsMobileRendering();

        [JSImport("getSkyMode", ModuleName)]
        internal static partial int GetSkyMode();

        [JSImport("getGpuParticleBudget", ModuleName)]
        internal static partial int GetGpuParticleBudget();

        [JSImport("getTimeMs", ModuleName)]
        internal static partial double GetTimeMs();

        [JSImport("observeResize", ModuleName)]
        internal static partial void ObserveResize();

        [JSImport("startRenderLoop", ModuleName)]
        internal static partial void StartRenderLoop();

        [JSImport("installInput", ModuleName)]
        internal static partial void InstallInput();

        [JSImport("suppressKeys", ModuleName)]
        private static partial void SuppressKeys(string codes);

        /// <summary>
        /// Stops the browser acting on these keys (<c>KeyboardEvent.code</c> values such as "F5") while still
        /// delivering them to the engine -- for a key the page gives a job of its own that the browser would
        /// otherwise also act on, like F5 refreshing the page it is meant to start Play in. Ctrl+F5 and Shift+F5
        /// are the same key and are covered too. Call once, after <see cref="InitializeAsync"/>.
        /// </summary>
        public static void SuppressBrowserKeys(params string[] codes)
        {
            SuppressKeys(string.Join(",", codes));
        }

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

        // --- Loading screen ----------------------------------------------------------------
        // The screen itself is HTML, in index.html, and is already up before the runtime exists --
        // it has to be, because the 45 MB of wasm plus the bundled assets are downloaded before a
        // single line of C# runs, and that is the longest part of the wait by far. These three
        // hand the engine's own load the same screen, so one bar runs from the first byte off the
        // network to the first rendered frame instead of a progress bar handing over to a black
        // canvas.
        //
        // Every call is swallowed on failure and then given up on. A loading screen is presentation:
        // it is never worth taking the game down for, and the one place these are called from is a
        // load path where an exception would surface as "loading failed" and hide the actual cause.

        private static bool _loadingBridgeBroken;

        [JSImport("setLoadingProgress", ModuleName)]
        private static partial void SetLoadingProgressCore(string status, double fraction);

        [JSImport("failLoading", ModuleName)]
        private static partial void FailLoadingCore(string message);

        [JSImport("hideLoadingOverlay", ModuleName)]
        private static partial void HideLoadingOverlayCore();

        /// <summary>
        /// Updates the loading screen. <paramref name="fraction"/> is 0..1 across the engine's own
        /// load; the screen maps it into whatever is left of the bar after the download.
        /// </summary>
        public static void ReportLoadingProgress(string status, float fraction)
        {
            Invoke(() => SetLoadingProgressCore(status, fraction));
        }

        /// <summary>
        /// Leaves the loading screen up showing why nothing started. Without this a failed load is
        /// a blank canvas and a stack trace in a console nobody had open.
        /// </summary>
        public static void ReportLoadingFailed(string message)
        {
            Invoke(() => FailLoadingCore(message));
        }

        /// <summary>
        /// Fades the loading screen out and removes it. Call after a real frame has been presented,
        /// not when loading merely finishes -- the two are a frame or more apart, and the gap shows
        /// as a flash of empty canvas.
        /// </summary>
        public static void HideLoadingScreen()
        {
            Invoke(HideLoadingOverlayCore);
        }

        private static void Invoke(Action call)
        {
            if (_loadingBridgeBroken)
            {
                return;
            }

            try
            {
                call();
            }
            catch (Exception exception)
            {
                // Once, not per frame: ReportLoadingProgress runs on every progress change, and a
                // broken bridge would otherwise fill the console with the same message.
                _loadingBridgeBroken = true;
                Console.WriteLine($"[ManaLoading] JS bridge unavailable; continuing without a loading screen. {exception.Message}");
            }
        }

        // JS -> C#. JS calls these directly via the assembly-exports object main.js stashes on
        // globalThis (see main.js) rather than passing .NET delegates into the JSImport calls
        // above, which keeps the marshaling on both sides to plain primitives.
        [JSExport]
        internal static void OnFrame(double deltaTimeSeconds)
        {
            // Pads have no events in the browser: read them fresh before the frame that uses them.
            ManaBrowserInputContext.Current?.PollGamepads();
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
        internal static void OnResize(int logicalWidth, int logicalHeight, int framebufferWidth, int framebufferHeight)
        {
            ManaBrowserWindow.Current?.RaiseResize(logicalWidth, logicalHeight, framebufferWidth, framebufferHeight);
        }
    }
}
#endif
