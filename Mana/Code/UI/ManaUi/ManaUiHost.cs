using System.Runtime.InteropServices;
using ManaUI;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// Owns the ManaUI tree for a window and drives it once per frame.
    /// <para>
    /// The engine layer holds the root rather than ManaGraphics, because layout and rendering are
    /// application concerns and must not run inside the GPU present path. The backend is handed only the
    /// finished draw list.
    /// </para>
    /// <para>
    /// This runs alongside the existing ImGui controller by design -- both draw every frame, and panels
    /// migrate one at a time rather than in a single breaking change.
    /// </para>
    /// </summary>
    public sealed class ManaUiHost : IDisposable
    {
        private readonly IWindow _window;
        private bool _disposed;

        public ManaUiHost(IWindow window, IInputContext? input = null, UiTheme? theme = null)
        {
            ArgumentNullException.ThrowIfNull(window);

            _window = window;

            Fonts = new ManaFontManager();
            foreach (var directory in GetFontDirectories())
            {
                Fonts.AddSearchDirectory(directory);
            }

            Root = new UiRoot(Fonts, theme ?? UiTheme.Dark());
            Root.SetScale(GetFramebufferScale());

            if (input != null)
            {
                InputBridge = new ManaUiInputBridge(Root, input);
            }
        }

        public UiRoot Root { get; }
        public ManaFontManager Fonts { get; }

        /// <summary>Null when the host was created without an input context (headless use).</summary>
        public ManaUiInputBridge? InputBridge { get; }

        /// <summary>
        /// True when the pointer is over ManaUI. Game modes must check this before acting on a click so a
        /// button press does not also reach the scene behind it.
        /// </summary>
        public bool IsPointerOverUi => IsEnabled && Root.Input.IsPointerOverUi;

        /// <summary>True when a focused control is consuming keystrokes, so editor shortcuts should stand down.</summary>
        public bool IsKeyboardCaptured => IsEnabled && Root.Input.IsKeyboardCaptured;

        /// <summary>Set false to skip layout and rendering entirely, e.g. behind a feature flag.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>Logical UI size, which tracks the window's logical (not framebuffer) size.</summary>
        public Vector2 LogicalSize => new Vector2(_window.Size.X, _window.Size.Y);

        /// <summary>
        /// Lays out and records the frame. Call during the render phase, before the backend submits.
        /// <paramref name="deltaSeconds"/> drives keyboard auto-repeat.
        /// </summary>
        public void Render(double deltaSeconds = 0.0)
        {
            if (_disposed || !IsEnabled)
            {
                return;
            }

            // Before layout: a repeat that edits text should be reflected in this frame, not the next.
            Root.Input.TickKeyRepeat(deltaSeconds);

            Root.SetScale(GetFramebufferScale());
            Root.UpdateLayout(LogicalSize);
            Root.Render();

            // After layout, so resize handles report against this frame's geometry.
            InputBridge?.ApplyCursor();
        }

        /// <summary>
        /// Framebuffer pixels per logical pixel. Falls back to 1 on a zero-sized (minimised) window rather
        /// than producing a divide-by-zero that would reset the glyph atlas every frame.
        /// </summary>
        private float GetFramebufferScale()
        {
            if (_window.Size.X <= 0 || _window.Size.Y <= 0)
            {
                return Root?.Scale ?? 1.0f;
            }

            return (float)_window.FramebufferSize.X / _window.Size.X;
        }

        /// <summary>
        /// Bundled fonts first, then the platform's own -- same ordering the ImGui font loader uses, so both
        /// UI stacks resolve a given family to the same file.
        /// </summary>
        private static IEnumerable<string> GetFontDirectories()
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
            if (Directory.Exists(bundled))
            {
                yield return bundled;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                yield return Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                yield return "/System/Library/Fonts";
                yield return "/System/Library/Fonts/Supplemental";
                yield return "/Library/Fonts";
            }
            else
            {
                yield return "/usr/share/fonts/truetype";
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            InputBridge?.Dispose();
            Fonts.Dispose();
        }
    }
}
