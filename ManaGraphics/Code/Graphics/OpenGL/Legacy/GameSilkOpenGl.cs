using Mana.Code;
using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Runtime.InteropServices;
using WindowVideoMode = Silk.NET.Windowing.VideoMode;

namespace Mana.Code.Graphics
{
    public sealed class GameSilkOpenGl
    {
        private readonly BaseGameMode _gameMode;
        private readonly string _title;
        private readonly GraphicsAPI _graphicsApi;
        private IWindow _window = null!;
        private GL? _gl;
        private IInputContext? _input;

        public GameSilkOpenGl(BaseGameMode gameMode, string title, GraphicsAPI graphicsApi)
        {
            _gameMode = gameMode;
            _title = title;
            _graphicsApi = graphicsApi;
        }

        public void Run()
        {
            ConfigureOpenGlWindowHints();

            var options = WindowOptions.Default;
            options.IsVisible = true;
            options.Position = new Vector2D<int>(50, 50);
            options.Size = CalculateInitialWindowSize();
            options.FramesPerSecond = 90;
            options.UpdatesPerSecond = 90;
            options.API = _graphicsApi;
            options.Title = _title;
            options.WindowState = WindowState.Normal;
            options.WindowBorder = WindowBorder.Resizable;
            options.VSync = true;
            options.ShouldSwapAutomatically = true;
            options.VideoMode = new WindowVideoMode(90);
            options.PreferredDepthBufferBits = 32;
            options.PreferredStencilBufferBits = 8;

            _window = Window.Create(options);
            _window.Load += Load;
            _window.FramebufferResize += Resize;
            _window.Update += _gameMode.update;
            _window.Render += _gameMode.Render;
            _window.Closing += Closing;
            _window.Run();
        }

        private void Load()
        {
            _gl = _window.CreateOpenGL();
            _input = _window.CreateInput();
            _gameMode.LoadForOpenGlBackend(_window, _gl, _input);
        }

        private void Resize(Vector2D<int> size)
        {
            _gl?.Viewport(size);
        }

        private void Closing()
        {
            _gameMode.Closing();
            _input = null;
            _gl = null;
        }

        private static void ConfigureOpenGlWindowHints()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return;
            }

            var glfw = GlfwProvider.GLFW.Value;
            glfw.WindowHint(WindowHintBool.OpenGLForwardCompat, true);
            glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
        }

        private static Vector2D<int> CalculateInitialWindowSize()
        {
            const int fallbackWidth = 1280;
            const int fallbackHeight = 720;

            try
            {
                var platform = Window.GetWindowPlatform(false);
                var mainMonitor = platform?.GetMainMonitor();
                var screenSize = mainMonitor?.VideoMode.Resolution;
                if (!screenSize.HasValue)
                {
                    return new Vector2D<int>(fallbackWidth, fallbackHeight);
                }

                var resolution = screenSize.Value;
                if (resolution.X <= 0 || resolution.Y <= 0)
                {
                    return new Vector2D<int>(fallbackWidth, fallbackHeight);
                }

                var initialWindowHeight = Math.Max(480, (int)(resolution.Y * 0.92f));
                var targetWidthFromHeight = (int)(initialWindowHeight * (16.0f / 10.0f));
                var maxUsableWidth = (int)(resolution.X * 0.92f);
                var initialWindowWidth = Math.Max(640, Math.Min(targetWidthFromHeight, maxUsableWidth));
                return new Vector2D<int>(initialWindowWidth, initialWindowHeight);
            }
            catch
            {
                return new Vector2D<int>(fallbackWidth, fallbackHeight);
            }
        }
    }
}
