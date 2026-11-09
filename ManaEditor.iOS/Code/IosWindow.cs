using CoreAnimation;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace ManaEditor.iOS;

/// <summary>
/// The game's view as a Silk.NET window, as ManaBrowserWindow is for the web: the engine reads its sizes and
/// subscribes to Load/Update/Render. Run fires Load and returns -- UIKit owns the main loop -- and a CADisplayLink
/// drives the frames, at 60 Hz (ProMotion could do 120; the battery prefers not).
/// </summary>
public sealed class IosWindow : IWindow
{
    private readonly UIView _view;
    private readonly IosInput _input;
    private CADisplayLink? _link;
    private double _lastTimestamp;
    private Vector2D<int> _size = new(1, 1);
    private Vector2D<int> _framebufferSize = new(1, 1);
    private bool _loaded;
    private long _frames;

    public IosWindow(UIView view, IosInput input)
    {
        _view = view;
        _input = input;
        RefreshSize();
    }

    /// <summary>The view's size in points, and in pixels at the screen's native scale; the view calls it on layout.</summary>
    public void RefreshSize()
    {
        // At most 2x: a phone's 3x screen is over twice the pixels to shade, for detail nobody sees at arm's length.
        var scale = Math.Min(2.0, _view.Window?.Screen.NativeScale ?? UIScreen.MainScreen.NativeScale);
        var size = new Vector2D<int>(Math.Max(1, (int)Math.Round(_view.Bounds.Width)), Math.Max(1, (int)Math.Round(_view.Bounds.Height)));
        var framebuffer = new Vector2D<int>(Math.Max(1, (int)Math.Round(_view.Bounds.Width * scale)), Math.Max(1, (int)Math.Round(_view.Bounds.Height * scale)));
        if (size == _size && framebuffer == _framebufferSize)
        {
            return;
        }
        _size = size;
        _framebufferSize = framebuffer;
        if (_view.Layer is CAMetalLayer layer)
        {
            layer.ContentsScale = (System.Runtime.InteropServices.NFloat)scale;
            // Frames are sRGB, as on desktop; said so, iOS converts them for the P3 panel instead of showing them raw.
            layer.ColorSpace ??= CoreGraphics.CGColorSpace.CreateSrgb();
        }
        if (_loaded)
        {
            Resize?.Invoke(_size);
            FramebufferResize?.Invoke(_framebufferSize);
        }
    }

    private void Frame()
    {
        if (IsClosing || _link == null)
        {
            return;
        }
        var now = _link.Timestamp;
        // The first frame, and one after the app comes back from the background, count as a normal frame, not the gap.
        if (_frames++ == 0)
        {
            AppDelegate.Trace("First frame");
        }
        var delta = _lastTimestamp == 0 || now - _lastTimestamp > 0.25 ? 1.0 / 60.0 : now - _lastTimestamp;
        _lastTimestamp = now;
        Update?.Invoke(delta);
        Render?.Invoke(delta);
        _input.EndFrame();
    }

    /// <summary>Stops drawing while the app is in the background, where Metal may not be used.</summary>
    public void SetPaused(bool paused)
    {
        if (_link != null)
        {
            _link.Paused = paused;
            _lastTimestamp = 0;
        }
    }

    public void Run(Action onFrame)
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        Load?.Invoke();
        _link = CADisplayLink.Create(Frame);
        // 60, not ProMotion's 120: half the frames for the battery and heat, and the game reads smoothly at 60.
        _link.PreferredFrameRateRange = new CAFrameRateRange { Minimum = 30, Maximum = 60, Preferred = 60 };
        _link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
    }

    // ----- IView -----
    public nint Handle => _view.Handle;
    public bool IsClosing { get; set; }
    public double Time => CAAnimation.CurrentMediaTime();
    public Vector2D<int> FramebufferSize => _framebufferSize;
    public bool IsInitialized => _loaded;

    public event Action<Vector2D<int>>? Resize;
    public event Action<Vector2D<int>>? FramebufferResize;
    public event Action? Closing;
    public event Action<bool>? FocusChanged { add { } remove { } }
    public event Action? Load;
    public event Action<double>? Update;
    public event Action<double>? Render;

    public void Initialize() { }
    public void DoRender() { }
    public void DoUpdate() { }
    public void DoEvents() { }
    public void ContinueEvents() { }
    public void Reset() { }
    public void Focus() { }

    // An iOS app is never closed by itself; the game's Quit just stops the frames.
    public void Close()
    {
        if (IsClosing)
        {
            return;
        }
        AppDelegate.Trace($"The game closed its window: {Environment.StackTrace}");
        IsClosing = true;
        _link?.Invalidate();
        _link = null;
        Closing?.Invoke();
    }

    public Vector2D<int> PointToClient(Vector2D<int> point) => point;
    public Vector2D<int> PointToScreen(Vector2D<int> point) => point;
    public Vector2D<int> PointToFramebuffer(Vector2D<int> point) => point;
    public object Invoke(Delegate d, params object[] args) => d.DynamicInvoke(args)!;

    // ----- IWindowProperties / IViewProperties -----
    public bool IsVisible { get; set; } = true;
    public Vector2D<int> Position { get; set; }
    public Vector2D<int> Size { get => _size; set { } }
    public string Title { get; set; } = string.Empty;
    public WindowState WindowState { get; set; } = WindowState.Fullscreen;
    public WindowBorder WindowBorder { get; set; } = WindowBorder.Hidden;
    public bool TransparentFramebuffer => false;
    public bool TopMost { get; set; }
    public IGLContext? SharedContext => null;
    public string WindowClass => "ManaEditor.iOS";
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
    public event Action<Vector2D<int>>? Move { add { } remove { } }
    public event Action<WindowState>? StateChanged { add { } remove { } }
    public event Action<string[]>? FileDrop { add { } remove { } }
    public void SetWindowIcon(ReadOnlySpan<Silk.NET.Core.RawImage> icons) { }
    public IWindow CreateWindow(WindowOptions opts) => throw new NotSupportedException("An iOS app has one view.");
    public IGLContext? GLContext => null;
    public IVkSurface? VkSurface => null;
    public INativeWindow? Native => null;

    public void Dispose()
    {
        _link?.Invalidate();
        _link = null;
    }
}
