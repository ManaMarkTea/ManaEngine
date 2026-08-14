using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Input;
using Silk.NET.Windowing;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;

namespace ManaGraphics.Code.Graphics
{
    public sealed class GameSilkWebGpu
    {
        private readonly string _title;
        private readonly string? _requestedBackend;
        private readonly Func<ManaRenderScene>? _renderSceneProvider;
        private readonly Action<IWindow, IInputContext>? _inputReady;
        private readonly Action<double>? _beforeRender;
        private readonly Action<double>? _onRenderImGui;
        private readonly Action<ManaWebGpuContext, IInputContext>? _webGpuReady;
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
            Action<double>? onRenderImGui = null,
            Action<ManaWebGpuContext, IInputContext>? webGpuReady = null,
            Vector2D<int>? windowSize = null)
        {
            _title = title;
            _windowSize = windowSize ?? new Vector2D<int>(1920, 1080);
            _requestedBackend = requestedBackend;
            _renderSceneProvider = renderSceneProvider;
            _inputReady = inputReady;
            _beforeRender = beforeRender;
            _onRenderImGui = onRenderImGui;
            _webGpuReady = webGpuReady;
        }

        private readonly Vector2D<int> _windowSize;

        public void Run()
        {
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
        }

        private void Load()
        {
            try
            {
                ManaConsole.WriteDiagnosticElapsed("WebGPU window Load started.");
                _input = _window.CreateInput();

                // Everything up to and including the game mode's own Load() runs here, on this thread,
                // before this method returns -- including dispatching whatever background loading task the
                // game mode kicks off. That task starts racing the rest of this method immediately.
                _inputReady?.Invoke(_window, _input);
                ManaConsole.WriteDiagnosticElapsed("Game mode load dispatched; background loading may already be under way.");

                // The adapter/device request and any eagerly-built pipelines inside this constructor are
                // the leading suspect for "the loading screen does not appear for a while": nothing can be
                // presented to the window before this returns, no matter how cheap a frame would otherwise
                // be, because there is no frame loop running yet to present one.
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
            if (!_loggedFirstRender)
            {
                _loggedFirstRender = true;

                // The frame loop's first tick. Everything from here on is a real frame that gets a chance
                // to present -- if the gap between this and "WebGPU context loaded" above is small, the
                // delay was in bring-up; if the gap between this and the loading screen actually appearing
                // is the large one instead, the delay is per-frame cost after the loop already started.
                ManaConsole.WriteDiagnosticElapsed("First frame loop tick.");
            }

            _elapsedSeconds += deltaTime;
            _beforeRender?.Invoke(deltaTime);
            _onRenderImGui?.Invoke(deltaTime);

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
            _webGpu?.Dispose();
            _webGpu = null;
            _input?.Dispose();
            _input = null;
        }
    }
}
