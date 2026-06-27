using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;
using Mana.Code.Diagnostics;

namespace Mana.Code.Graphics
{
    public sealed class GameSilkWebGpu
    {
        private readonly string _title;
        private readonly string? _requestedBackend;
        private IWindow _window = null!;
        private ManaWebGpuContext? _webGpu;

        public GameSilkWebGpu(string title, string? requestedBackend)
        {
            _title = title;
            _requestedBackend = requestedBackend;
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
                _webGpu = new ManaWebGpuContext(_window, _requestedBackend);
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
            ManaTime.GlobalTime.UpdateTime(deltaTime);
            var t = (float)ManaTime.GlobalTime.GameTime.TotalSeconds;
            var clearColor = new Color
            {
                R = 0.025 + Math.Sin(t * 0.55) * 0.015,
                G = 0.035,
                B = 0.085 + Math.Cos(t * 0.37) * 0.018,
                A = 1.0
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
        }
    }
}
