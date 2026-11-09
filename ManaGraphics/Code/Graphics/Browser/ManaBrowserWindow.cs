#if MANA_BROWSER
using System;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace ManaGraphics.Code.Graphics.Browser
{
    // Minimal custom IWindow for browser-wasm (M1). Silk.NET has no shipped browser windowing
    // backend, so Window.Create(...) can't be used at all here.
    //
    // Scope is deliberately narrow: ManaWebGpuContext.cs only ever reads window.FramebufferSize
    // (confirmed by grepping every _window. call site in that file before writing this), so that's
    // the one property this class actually has to get right. Most of the rest of IWindow's large
    // interface surface (child windows, monitor info, window icons, Vulkan/OpenGL context sources)
    // has no browser equivalent and throws NotSupportedException -- if something downstream needs
    // one of these, that throw is the signal to come back and give it a real implementation rather
    // than a silent wrong answer.
    //
    // Run(Action) deliberately does NOT block the calling thread the way GLFW's real
    // implementation does: a browser tab has exactly one JS thread, and blocking it would freeze
    // the requestAnimationFrame callbacks this class's own frame loop depends on. It fires Load
    // once and returns immediately; the actual per-frame Update/Render calls happen later, driven
    // by ManaBrowserInterop.OnFrame (JS calling back in on each animation frame).
    public sealed class ManaBrowserWindow : IWindow
    {
        public static ManaBrowserWindow? Current { get; private set; }

        public string CanvasSelector { get; }

        public ManaBrowserWindow(string canvasSelector)
        {
            CanvasSelector = canvasSelector;
            Current = this;
        }

        // Size is CSS pixels and FramebufferSize device pixels, so their ratio is devicePixelRatio --
        // the same shape GLFW gives a Retina window, which is what ManaUI and the camera scale from.
        private Vector2D<int> _size = new(1, 1);
        private Vector2D<int> _framebufferSize = new(1, 1);
        private bool _loaded;

        public void Initialize()
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            if (!ManaBrowserInterop.SelectCanvas(CanvasSelector))
            {
                throw new InvalidOperationException($"No canvas found for selector '{CanvasSelector}'.");
            }

            RefreshSizeFromCanvas();
            ManaBrowserInterop.ObserveResize();
            ManaBrowserInterop.InstallInput();
            Load?.Invoke();
        }

        private void RefreshSizeFromCanvas()
        {
            // The canvas reports CSS pixels; the ResizeObserver's first callback replaces the
            // estimated framebuffer with the exact device-pixel size.
            var width = Math.Max(1, ManaBrowserInterop.GetCanvasWidth());
            var height = Math.Max(1, ManaBrowserInterop.GetCanvasHeight());
            var dpr = Math.Max(1.0, ManaBrowserInterop.GetDevicePixelRatio());
            _size = new Vector2D<int>(width, height);
            _framebufferSize = new Vector2D<int>((int)Math.Round(width * dpr), (int)Math.Round(height * dpr));
        }

        internal void RaiseResize(int logicalWidth, int logicalHeight, int framebufferWidth, int framebufferHeight)
        {
            _size = new Vector2D<int>(Math.Max(1, logicalWidth), Math.Max(1, logicalHeight));
            _framebufferSize = new Vector2D<int>(Math.Max(1, framebufferWidth), Math.Max(1, framebufferHeight));
            Resize?.Invoke(_size);
            FramebufferResize?.Invoke(_framebufferSize);
        }

        internal void RaiseFrame(double deltaTimeSeconds)
        {
            if (IsClosing)
            {
                return;
            }

            Update?.Invoke(deltaTimeSeconds);
            Render?.Invoke(deltaTimeSeconds);
        }

        public void StartRenderLoop()
        {
            ManaBrowserInterop.StartRenderLoop();
        }

        // ----- IView -----
        public nint Handle => 0;
        public bool IsClosing { get; set; }
        public double Time => ManaBrowserInterop.GetTimeMs() / 1000.0;
        public Vector2D<int> FramebufferSize => _framebufferSize;
        public bool IsInitialized => _loaded;

        public event Action<Vector2D<int>>? Resize;
        public event Action<Vector2D<int>>? FramebufferResize;
        public event Action? Closing;
        public event Action<bool>? FocusChanged { add { } remove { } }
        public event Action? Load;
        public event Action<double>? Update;
        public event Action<double>? Render;

        public void DoRender()
        {
        }

        public void DoUpdate()
        {
        }

        public void DoEvents()
        {
        }

        public void ContinueEvents()
        {
        }

        public void Reset()
        {
        }

        public void Focus()
        {
        }

        public void Close()
        {
            if (IsClosing)
            {
                return;
            }

            IsClosing = true;
            Closing?.Invoke();
        }

        public Vector2D<int> PointToClient(Vector2D<int> point) => point;
        public Vector2D<int> PointToScreen(Vector2D<int> point) => point;
        public Vector2D<int> PointToFramebuffer(Vector2D<int> point) => point;

        public object Invoke(Delegate d, params object[] args) => d.DynamicInvoke(args)!;

        public void Run(Action onFrame)
        {
            // Ignored; see the class-level remarks. The real per-frame loop is
            // ManaBrowserInterop's requestAnimationFrame callback, started explicitly by
            // GameSilkWebGpu's browser branch via StartRenderLoop() after Load has run.
            Initialize();
        }

        // ----- IWindowProperties / IViewProperties -----
        public bool IsVisible { get; set; } = true;
        public Vector2D<int> Position { get; set; }
        public Vector2D<int> Size { get => _size; set { } }
        public string Title { get; set; } = string.Empty;
        public WindowState WindowState { get; set; } = WindowState.Normal;
        public WindowBorder WindowBorder { get; set; } = WindowBorder.Fixed;
        public bool TransparentFramebuffer => false;
        public bool TopMost { get; set; }
        public IGLContext? SharedContext => null;
        public string WindowClass => "ManaEditor.Web";

        public bool ShouldSwapAutomatically { get; set; }
        public bool IsEventDriven { get; set; }
        public bool IsContextControlDisabled { get; set; }
        public double FramesPerSecond { get; set; }
        public double UpdatesPerSecond { get; set; }
        public GraphicsAPI API { get; set; } = new(ContextAPI.None, new APIVersion(0, 0));
        public bool VSync { get; set; } = true;
        public VideoMode VideoMode => VideoMode.Default;
        public int? PreferredDepthBufferBits => null;
        public int? PreferredStencilBufferBits => null;
        public Vector4D<int>? PreferredBitDepth => null;
        public int? Samples => null;

        // ----- IWindow -----
        public IWindowHost? Parent => null;
        public IMonitor? Monitor { get; set; }
        public Rectangle<int> BorderSize => new(0, 0, 0, 0);

        // A canvas never moves, changes state or takes dropped files the way a desktop window does.
        public event Action<Vector2D<int>>? Move { add { } remove { } }
        public event Action<WindowState>? StateChanged { add { } remove { } }
        public event Action<string[]>? FileDrop { add { } remove { } }

        public void SetWindowIcon(ReadOnlySpan<Silk.NET.Core.RawImage> icons)
        {
        }

        public IWindow CreateWindow(WindowOptions opts) =>
            throw new NotSupportedException("Browser windows cannot host child windows.");

        // ----- Not applicable in-browser: WebGPU-only, no GL/Vulkan context -----
        public IGLContext? GLContext => null;
        public IVkSurface? VkSurface => null;
        public INativeWindow? Native =>
            throw new NotSupportedException(
                "No native window handle in-browser; ManaBrowserWebGpuSurface.CreateFromCanvas " +
                "uses the canvas selector directly instead.");

        public void Dispose()
        {
            if (ReferenceEquals(Current, this))
            {
                Current = null;
            }
        }
    }
}
#endif
