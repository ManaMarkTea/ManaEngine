namespace ManaUI
{
    public interface IUiWindowRenderer : IDisposable
    {
        void Render(UiRoot root);
    }

    /// <summary>
    /// A platform window holding its own dock layout. Dispose removes whatever is still docked in it
    /// without invoking DockBack.
    /// </summary>
    public interface IUiDesktopWindow : IDisposable
    {
        /// <summary>This window's own UI tree, separate from the editor's.</summary>
        UiRoot Root { get; }

        /// <summary>The window's content: a dock host plus the move and resize handling a borderless window lacks.</summary>
        DesktopWindowFrame Frame { get; }

        /// <summary>Screen position of the client area's top-left. Assigning is for restoring a saved position.</summary>
        Vector2 Position { get; set; }

        Vector2 Size { get; set; }

        /// <summary>Moves the window exactly where asked, unlike <see cref="Position"/> which refuses unreachable spots.</summary>
        void MoveTo(Vector2 screenOrigin);

        /// <summary>The user asked for the window to close; its panels belong back in the editor.</summary>
        event Action? DockBack;

        event Action? BoundsChanged;
        void Focus();
    }
}
