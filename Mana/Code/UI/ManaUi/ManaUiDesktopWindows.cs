#if !MANA_BROWSER
using ManaUI;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>Pumps secondary windows on the editor thread; no second game loop or scene.</summary>
    internal sealed class ManaUiDesktopWindows : IDisposable
    {
        private readonly IWindow _main;
        private readonly UiRoot _root;
        private readonly Func<IWindow, IUiWindowRenderer?> _createRenderer;
        private readonly string? _iconPath;
        private readonly List<DesktopWindow> _windows = new();
        private bool _disposed;

        public ManaUiDesktopWindows(IWindow main, UiRoot root, Func<IWindow, IUiWindowRenderer?> createRenderer, string? iconPath)
        {
            _main = main;
            _root = root;
            _createRenderer = createRenderer;
            _iconPath = iconPath;
            root.CreateDesktopWindow = Create;
            main.Closing += Dispose;
        }

        private IUiDesktopWindow Create(string title)
        {
            var window = new DesktopWindow(this, title);
            _windows.Add(window);
            return window;
        }

        public void Render(double delta)
        {
            foreach (var window in _windows.ToArray())
            {
                if (window.IsDisposed)
                {
                    window.Destroy();
                    _windows.Remove(window);
                    continue;
                }
                window.Render(delta);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _main.Closing -= Dispose;
            _root.CreateDesktopWindow = null;
            foreach (var window in _windows.ToArray()) { window.Dispose(); window.Destroy(); }
            _windows.Clear();
        }

        private sealed class DesktopWindow : IUiDesktopWindow
        {
            private readonly ManaUiDesktopWindows _owner;
            private readonly IWindow _window;
            private IInputContext? _input;
            private ManaUiHost? _host;
            private IUiWindowRenderer? _surface;
            private readonly DesktopWindowFrame _frame = new();
            private bool _rendering;
            private bool _destroyed;
            private bool _closeRequested;
            private bool _boundsDirty;
            private double _saveDelay;
            public bool IsDisposed { get; private set; }

            public DesktopWindow(ManaUiDesktopWindows owner, string title)
            {
                _owner = owner;
                var options = WindowOptions.Default;
                options.API = new GraphicsAPI(ContextAPI.None, new APIVersion(0, 0));
                options.Title = title + " — ManaUI";
                options.Size = new Vector2D<int>(480, 640);
                options.Position = owner._main.Position + new Vector2D<int>(60, 60);
                options.ShouldSwapAutomatically = false;
                options.IsVisible = false;

                // No OS title bar: the tab strip is the handle, and the frame supplies the edges.
                options.WindowBorder = WindowBorder.Hidden;
                _window = Window.Create(options);
                try
                {
                    _window.Initialize();
                    ManaGraphics.Code.Graphics.ManaWindowIcon.Apply(_window, owner._iconPath);
                    _input = _window.CreateInput();
                    _host = new ManaUiHost(_window, _input, owner._root.Theme, owner._root.Images);
                    _frame.ScreenOrigin = () => new Vector2(_window.Position.X, _window.Position.Y);
                    _frame.Dock.LayoutChanged += (_, _) => OnLayoutChanged();
                    _host.Root.Background = owner._root.Theme.Background;
                    _host.Root.Content = _frame;
                    _window.Closing += () => _closeRequested = true;
                    _window.Move += _ => MarkBoundsDirty();
                    _window.Resize += _ => { MarkBoundsDirty(); DrawFrame(0); };
                    // Windows runs a modal event loop during a native resize, pausing the editor's
                    // frame loop. These callbacks still run on the UI thread during that loop.
                    _window.FramebufferResize += _ => DrawFrame(0);
                }
                catch { Dispose(); Destroy(); throw; }
            }

            public UiRoot Root => _host!.Root;
            public DesktopWindowFrame Frame => _frame;

            private void OnLayoutChanged()
            {
                if (IsDisposed || _frame.Dock.RootNode == null)
                {
                    return;
                }

                _window.Title = (_frame.Dock.AllItems().FirstOrDefault()?.Title ?? string.Empty) + " — ManaUI";
                _window.IsVisible = true;
                _host?.Root.InvalidateAll();
                _host?.Root.FocusManager.ValidateFocus();
                _host?.Root.Input.ClearKeyboardState();
                _host?.Root.Input.ReleasePointerCapture();
            }

            public Vector2 Position
            {
                get => new(_window.Position.X, _window.Position.Y);
                set
                {
                    if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)) return;
                    var position = new Vector2D<int>((int)value.X, (int)value.Y);
                    // Restore only where the tab strip can still be reached on an attached monitor.
                    var reachable = Silk.NET.Windowing.Monitor.GetMonitors(_window).Any(m =>
                        position.X + 80 > m.Bounds.Origin.X && position.X < m.Bounds.Origin.X + m.Bounds.Size.X &&
                        position.Y >= m.Bounds.Origin.Y && position.Y + 40 < m.Bounds.Origin.Y + m.Bounds.Size.Y);
                    _window.Position = reachable ? position : _owner._main.Position + new Vector2D<int>(60, 60);
                }
            }
            public void MoveTo(Vector2 screenOrigin)
            {
                if (float.IsFinite(screenOrigin.X) && float.IsFinite(screenOrigin.Y))
                {
                    _window.Position = new Vector2D<int>((int)screenOrigin.X, (int)screenOrigin.Y);
                }
            }

            public Vector2 Size
            {
                get => new(_window.Size.X, _window.Size.Y);
                set
                {
                    if (float.IsFinite(value.X) && float.IsFinite(value.Y))
                        _window.Size = new Vector2D<int>((int)Math.Clamp(value.X, 200, 7680), (int)Math.Clamp(value.Y, 120, 4320));
                }
            }
            public event Action? DockBack;
            public event Action? BoundsChanged;
            public void Focus() { _window.WindowState = WindowState.Normal; _window.Focus(); }
            private void MarkBoundsDirty() { _boundsDirty = true; _saveDelay = 0.3; }

            public void Render(double delta)
            {
                if (IsDisposed) return;
                // Events are dispatched by the main GLFW pump. DoUpdate keeps this input context current.
                _window.DoUpdate();
                if (IsDisposed) return;
                if (_closeRequested || _window.IsClosing) { DockBack?.Invoke(); return; }
                if (_boundsDirty && (_saveDelay -= delta) <= 0)
                {
                    _boundsDirty = false;
                    BoundsChanged?.Invoke();
                }
                DrawFrame(delta);
            }

            private void DrawFrame(double delta)
            {
                if (IsDisposed || _rendering || _closeRequested || _frame.Dock.RootNode == null || _host == null) return;
                _rendering = true;
                try
                {
                    _surface ??= _owner._createRenderer(_window);
                    if (_surface == null) return;
                    _host!.Render(delta);
                    _surface.Render(_host.Root);
                }
                catch (Exception ex)
                {
                    ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Panel window failed; docking it back: {ex}");
                    // Move content outside the native resize callback, on the next normal tick.
                    _closeRequested = true;
                }
                finally { _rendering = false; }
            }

            public void Dispose()
            {
                if (IsDisposed) return;
                IsDisposed = true;

                // Whatever is still docked here is released without being docked anywhere: callers that want
                // the panels kept move them first.
                foreach (var item in _frame.Dock.AllItems().ToList())
                {
                    item.Owner?.Remove(item);
                }

                _host?.Root.Input.ClearKeyboardState();
                _host?.Root.Input.ReleasePointerCapture();
                _window.IsVisible = false;
            }

            // Deferred until outside input callbacks, so a menu action cannot destroy its event source.
            public void Destroy()
            {
                if (_destroyed) return;
                _destroyed = true;
                _surface?.Dispose();
                _host?.Dispose();
                _input?.Dispose();
                _window.Dispose();
            }
        }
    }
}
#endif
