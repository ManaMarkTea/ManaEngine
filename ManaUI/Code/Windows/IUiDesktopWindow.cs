namespace ManaUI
{
    public interface IUiWindowRenderer : IDisposable
    {
        void Render(UiRoot root);
    }

    /// <summary>A platform window. Dispose detaches content without invoking DockBack.</summary>
    public interface IUiDesktopWindow : IDisposable
    {
        DockItem? Item { get; set; }
        Vector2 Position { get; set; }
        Vector2 Size { get; set; }
        event Action? DockBack;
        event Action? BoundsChanged;
        void Focus();
    }
}
