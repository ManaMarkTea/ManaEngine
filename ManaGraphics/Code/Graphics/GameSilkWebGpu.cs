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
            Action<ManaWebGpuContext, IInputContext>? webGpuReady = null)
        {
            _title = title;
            _requestedBackend = requestedBackend;
            _renderSceneProvider = renderSceneProvider;
            _inputReady = inputReady;
            _beforeRender = beforeRender;
            _onRenderImGui = onRenderImGui;
            _webGpuReady = webGpuReady;
        }

        public void Run()
        {
            ManaConsole.WriteDiagnostic("Creating WebGPU Silk window.");
            var options = WindowOptions.Default;
            options.API = new GraphicsAPI(ContextAPI.None, new APIVersion(0, 0));
            options.Title = _title;
            options.Size = new Vector2D<int>(1280, 720);
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
                ManaConsole.WriteDiagnostic("WebGPU window Load.");
                _input = _window.CreateInput();
                _inputReady?.Invoke(_window, _input);
                _webGpu = new ManaWebGpuContext(_window, _requestedBackend, _renderSceneProvider);
                _webGpuReady?.Invoke(_webGpu, _input);
                ManaConsole.WriteDiagnostic("WebGPU context loaded.");
            }
            catch (Exception ex)
            {
                ManaConsole.WriteDiagnostic("WebGPU Load failed.");
                ManaConsole.WriteDiagnostic(ex);
                throw;
            }
        }

        private void Render(double deltaTime)
        {
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
