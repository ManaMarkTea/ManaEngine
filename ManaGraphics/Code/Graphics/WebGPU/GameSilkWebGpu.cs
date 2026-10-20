using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Input;
using Silk.NET.Windowing;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
#if MANA_BROWSER
using ManaGraphics.Code.Graphics.Browser;
#endif

namespace ManaGraphics.Code.Graphics
{
    public sealed class GameSilkWebGpu
    {
        private readonly string _title;
        private readonly string? _requestedBackend;
        private readonly Func<ManaRenderScene>? _renderSceneProvider;
        private readonly Action<IWindow, IInputContext>? _inputReady;
        private readonly Action<double>? _beforeRender;
        private readonly Action<double>? _onRender;
        private readonly Action<ManaWebGpuContext, IInputContext>? _webGpuReady;
        private readonly string? _iconPath;
        private readonly Action? _onClosing;
        private bool _closed;
        private bool _rendering;
        private bool _closeRequested;
        private double _elapsedSeconds;
        private IWindow _window = null!;
        private IInputContext? _input;
        private ManaWebGpuContext? _webGpu;

        public GameSilkWebGpu(
            string title,
            string? requestedBackend,
            Func<ManaRenderScene>? renderSceneProvider = null,
            Action<IWindow, IInputContext>? inputReady = null,
            Action<double>? beforeRender = null,
            Action<double>? onRender = null,
            Action<ManaWebGpuContext, IInputContext>? webGpuReady = null,
            Vector2D<int>? windowSize = null,
            string? iconPath = null,
            Action? onClosing = null)
        {
            _title = title;
            _windowSize = windowSize ?? new Vector2D<int>(1920, 1080);
            _requestedBackend = requestedBackend;
            _renderSceneProvider = renderSceneProvider;
            _inputReady = inputReady;
            _beforeRender = beforeRender;
            _onRender = onRender;
            _webGpuReady = webGpuReady;
            _iconPath = iconPath;
            _onClosing = onClosing;
        }

        private readonly Vector2D<int> _windowSize;

        public void Run()
        {
#if MANA_BROWSER
            RunBrowser();
#else
            if (ManaWebGpuNativeHost.Current is { } host)
            {
                // A phone's view: its Run fires Load and returns, and the platform's display link drives the frames.
                ManaConsole.WriteDiagnostic("Running WebGPU on the platform's own view.");
                _window = host.Window;
                _window.Load += Load;
                _window.Render += Render;
                _window.FramebufferResize += Resize;
                _window.Closing += Closing;
                _window.Run();
                return;
            }

            ManaConsole.WriteDiagnostic("Creating WebGPU Silk window.");
            var options = WindowOptions.Default;
            options.API = new GraphicsAPI(ContextAPI.None, new APIVersion(0, 0));
            options.Title = _title;
            options.Size = _windowSize;
            options.WindowState = WindowState.Normal;
            options.FramesPerSecond = 90;
            options.UpdatesPerSecond = 90;
            options.ShouldSwapAutomatically = false;

            _window = Window.Create(options);
            _window.Load += Load;
            _window.Render += Render;
            _window.FramebufferResize += Resize;
            _window.Closing += Closing;
            ManaConsole.WriteDiagnostic("Running WebGPU Silk window.");
            _window.Run();
#endif
        }

#if MANA_BROWSER
        // Browser entry point, exercised by both web hosts in a real WebGPU browser.
        private async void RunBrowser()
        {
            try
            {
                Console.WriteLine("[RunBrowser] Creating browser WebGPU window.");
                var browserWindow = new ManaBrowserWindow("#manaCanvas");
                _window = browserWindow;
                _window.Closing += Closing;
                _window.FramebufferResize += Resize;
                browserWindow.Initialize();
                Console.WriteLine($"[RunBrowser] Window initialized, framebuffer size = {_window.FramebufferSize}.");

                _input = new ManaBrowserInputContext();
                _inputReady?.Invoke(_window, _input);

                Console.WriteLine("[RunBrowser] Constructing ManaWebGpuContext (sync part: instance + surface)...");
                var browserWebGpu = new ManaWebGpuContext(_window, _input, _requestedBackend, _renderSceneProvider);
                _webGpu = browserWebGpu;
                Console.WriteLine("[RunBrowser] Sync part done. Awaiting CompleteBrowserInitializationAsync (adapter/device request)...");
                await browserWebGpu.CompleteBrowserInitializationAsync();
                Console.WriteLine("[RunBrowser] CompleteBrowserInitializationAsync returned.");
                if (_closed) { return; }

                _webGpuReady?.Invoke(_webGpu, _input);

                browserWindow.Render += Render;
                browserWindow.StartRenderLoop();
                Console.WriteLine("[RunBrowser] Render loop started.");
            }
            catch (Exception ex)
            {
                var wasClosing = _closeRequested;
                Closing();
                if (wasClosing) { return; }
                Console.WriteLine("[RunBrowser] Browser WebGPU bring-up failed.");
                Console.WriteLine(ex);

                // Not rethrown: this is async void, so a throw kills the wasm runtime (exit code 1) and the
                // loading overlay just sits at its last percentage with nothing saying why.
                ManaBrowserInterop.ReportLoadingFailed(
                    "WebGPU is unavailable in this browser (no graphics adapter was found).\n" +
                    "Try a current desktop Chrome or Edge with hardware acceleration on, or open the editor in its own tab.\n\n" +
                    ex.Message);
            }
        }
#endif

        private void Load()
        {
            try
            {
                ManaConsole.WriteDiagnosticElapsed("WebGPU window Load started.");
                ManaWindowIcon.Apply(_window, _iconPath);
#if MANA_BROWSER
                _input = _window.CreateInput();
#else
                _input = ManaWebGpuNativeHost.Current?.Input ?? _window.CreateInput();
#endif

                // The game mode's Load runs here, and the background loading it starts races the rest of this method.
                _inputReady?.Invoke(_window, _input);
                ManaConsole.WriteDiagnosticElapsed("Game mode load dispatched; background loading may already be under way.");

                // Nothing can present until the device request and eager pipelines here finish.
                _webGpu = new ManaWebGpuContext(_window, _input, _requestedBackend, _renderSceneProvider);
                ManaConsole.WriteDiagnosticElapsed("WebGPU device and surface ready.");

                _webGpuReady?.Invoke(_webGpu, _input);
                ManaConsole.WriteDiagnosticElapsed("WebGPU context loaded; entering the frame loop next.");
            }
            catch (Exception ex)
            {
                ManaConsole.WriteDiagnostic("WebGPU Load failed.");
                ManaConsole.WriteDiagnostic(ex);
                throw;
            }
        }

        private bool _loggedFirstRender;

        private void Render(double deltaTime)
        {
            if (_closeRequested)
            {
                return;
            }
            _rendering = true;
            try
            {
                RenderFrame(deltaTime);
            }
            finally
            {
                _rendering = false;
                if (_closeRequested)
                {
                    Closing();
                }
            }
        }

        private void RenderFrame(double deltaTime)
        {
            if (!_loggedFirstRender)
            {
                _loggedFirstRender = true;

                // The first frame tick: compare with "WebGPU context loaded" to tell bring-up delay from frame cost.
                ManaConsole.WriteDiagnosticElapsed("First frame loop tick.");
            }

            _elapsedSeconds += deltaTime;
            _beforeRender?.Invoke(deltaTime);
            if (_closeRequested)
            {
                return;
            }
            _onRender?.Invoke(deltaTime);
            if (_closeRequested)
            {
                return;
            }

            var t = (float)_elapsedSeconds;
            var clearColor = new Color
            {
                R = 100 / 255.0f,
                G = 149 / 255.0f,
                B = 237 / 255.0f,
                A = 255 / 255.0f
            };

            _webGpu?.ClearFrame(clearColor);
        }

        private void Resize(Vector2D<int> size)
        {
            _webGpu?.Resize(size);
        }

        private void Closing()
        {
            _closeRequested = true;
            // A command can close the window inside update/render; let that callback unwind first.
            if (_rendering || _closed)
            {
                return;
            }
            _closed = true;
            try
            {
                // Retire application work before releasing the device it may still target.
                _onClosing?.Invoke();
            }
            finally
            {
                _webGpu?.Dispose();
                _webGpu = null;
                _input?.Dispose();
                _input = null;
            }
        }
    }
}
