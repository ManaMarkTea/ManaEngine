using ManaUI;

namespace ManaTests;

public static class WindowChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var time = 0.0;

        var window = new UiWindow("Inspector") { Width = 300.0f, Height = 200.0f };
        window.Child = new TextBlock().Text("content");
        root.ShowWindow(window, new Vector2(100.0f, 100.0f));
        root.UpdateLayout(new Vector2(1280, 720));

        Check("window placed", window.Bounds == new UiRect(100, 100, 300, 200), window.Bounds.ToString());
        Check("window lives in the floating layer", root.FloatingLayer.Children.Contains(window));

        var draws = root.Render();
        Check("window renders chrome", draws.InstanceCount > 5, $"{draws.InstanceCount} instances");

        // ---------------------------------------------------------------- dragging
        var titleBarPoint = new Vector2(200.0f, 108.0f);
        root.Input.InjectPointerMoved(titleBarPoint);
        Check("title bar wants a move cursor or arrow",
            root.RequestedCursor is UiCursor.Arrow or UiCursor.SizeAll, root.RequestedCursor.ToString());

        root.Input.InjectPointerPressed(titleBarPoint, PointerButton.Left, time);
        Check("title drag captures", root.PointerCaptureTarget == window);

        root.Input.InjectPointerMoved(titleBarPoint + new Vector2(50.0f, 30.0f));
        root.UpdateLayout(new Vector2(1280, 720));
        Check("drag moves the window", window.Bounds.Position == new Vector2(150.0f, 130.0f),
            window.Bounds.Position.ToString());
        Check("drag does not resize", window.Bounds.Size == new Vector2(300.0f, 200.0f),
            window.Bounds.Size.ToString());

        root.Input.InjectPointerReleased(titleBarPoint + new Vector2(50.0f, 30.0f), PointerButton.Left);
        Check("drag release frees capture", root.PointerCaptureTarget == null);

        // Content must not initiate a drag.
        window.Position = new Vector2(100.0f, 100.0f);
        root.UpdateLayout(new Vector2(1280, 720));
        var contentPoint = new Vector2(200.0f, 180.0f);
        root.Input.InjectPointerMoved(contentPoint);
        root.Input.InjectPointerPressed(contentPoint, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerMoved(contentPoint + new Vector2(40.0f, 40.0f));
        root.UpdateLayout(new Vector2(1280, 720));
        Check("dragging content does not move the window",
            window.Bounds.Position == new Vector2(100.0f, 100.0f), window.Bounds.Position.ToString());
        root.Input.InjectPointerReleased(contentPoint, PointerButton.Left);

        // ---------------------------------------------------------------- resizing
        root.UpdateLayout(new Vector2(1280, 720));
        var rightEdge = new Vector2(window.Bounds.Right - 2.0f, window.Bounds.Center.Y);
        root.Input.InjectPointerMoved(rightEdge);
        Check("right edge wants a horizontal cursor", root.RequestedCursor == UiCursor.SizeWE,
            root.RequestedCursor.ToString());

        root.Input.InjectPointerPressed(rightEdge, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerMoved(rightEdge + new Vector2(60.0f, 0.0f));
        root.UpdateLayout(new Vector2(1280, 720));
        Check("right drag widens", MathF.Abs(window.Bounds.Width - 360.0f) < 0.01f, $"{window.Bounds.Width}");
        Check("right drag keeps the origin", window.Bounds.X == 100.0f, $"{window.Bounds.X}");
        root.Input.InjectPointerReleased(rightEdge, PointerButton.Left);

        // A left-edge drag must move the origin and keep the right edge pinned.
        root.UpdateLayout(new Vector2(1280, 720));
        var rightBefore = window.Bounds.Right;
        var leftEdge = new Vector2(window.Bounds.Left + 2.0f, window.Bounds.Center.Y);
        root.Input.InjectPointerMoved(leftEdge);
        root.Input.InjectPointerPressed(leftEdge, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerMoved(leftEdge + new Vector2(40.0f, 0.0f));
        root.UpdateLayout(new Vector2(1280, 720));
        Check("left drag moves the origin", MathF.Abs(window.Bounds.X - 140.0f) < 0.01f, $"{window.Bounds.X}");
        Check("left drag pins the right edge", MathF.Abs(window.Bounds.Right - rightBefore) < 0.01f,
            $"{window.Bounds.Right} vs {rightBefore}");

        // Pulling well past the minimum must not creep the pinned edge -- the classic resize bug.
        root.Input.InjectPointerMoved(leftEdge + new Vector2(5000.0f, 0.0f));
        root.UpdateLayout(new Vector2(1280, 720));
        Check("min width respected", MathF.Abs(window.Bounds.Width - window.MinWidth) < 0.01f,
            $"{window.Bounds.Width} vs min {window.MinWidth}");
        Check("over-drag does not move the pinned edge",
            MathF.Abs(window.Bounds.Right - rightBefore) < 0.01f, $"{window.Bounds.Right} vs {rightBefore}");
        root.Input.InjectPointerReleased(leftEdge, PointerButton.Left);

        // Corner
        root.UpdateLayout(new Vector2(1280, 720));
        var corner = new Vector2(window.Bounds.Right - 2.0f, window.Bounds.Bottom - 2.0f);
        root.Input.InjectPointerMoved(corner);
        Check("corner wants a diagonal cursor", root.RequestedCursor == UiCursor.SizeNWSE,
            root.RequestedCursor.ToString());

        // ---------------------------------------------------------------- z-order
        var second = new UiWindow("Second") { Width = 200.0f, Height = 150.0f };
        root.ShowWindow(second, new Vector2(150.0f, 150.0f));
        root.UpdateLayout(new Vector2(1280, 720));

        Check("new window is on top", second.ZIndex > window.ZIndex, $"{second.ZIndex} vs {window.ZIndex}");

        var overlap = new Vector2(200.0f, 200.0f);
        Check("topmost window wins the hit test",
            second.IsAncestorOf(root.HitTest(overlap)), root.HitTest(overlap)?.ToString() ?? "null");

        // Clicking the lower window must raise it above the other.
        window.Position = new Vector2(400.0f, 400.0f);
        root.UpdateLayout(new Vector2(1280, 720));
        var lowerPoint = window.Bounds.Center;
        root.Input.InjectPointerMoved(lowerPoint);
        root.Input.InjectPointerPressed(lowerPoint, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(lowerPoint, PointerButton.Left);
        Check("clicking raises a window", window.ZIndex > second.ZIndex, $"{window.ZIndex} vs {second.ZIndex}");

        // ---------------------------------------------------------------- closing
        var closed = 0;
        second.Closed += (_, _) => closed++;
        root.UpdateLayout(new Vector2(1280, 720));

        var closeButton = new Vector2(second.Bounds.Right - 12.0f, second.Bounds.Y + 14.0f);
        root.Input.InjectPointerMoved(closeButton);
        root.Input.InjectPointerPressed(closeButton, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(closeButton, PointerButton.Left);

        Check("close button closes", closed == 1, $"{closed}");
        Check("closed window leaves the layer", !root.FloatingLayer.Children.Contains(second));

        // ---------------------------------------------------------------- flags
        var fixedWindow = new UiWindow("Fixed") { CanResize = false, CanMove = false, CanClose = false };
        root.ShowWindow(fixedWindow, new Vector2(600.0f, 100.0f));
        root.UpdateLayout(new Vector2(1280, 720));

        var fixedEdge = new Vector2(fixedWindow.Bounds.Right - 2.0f, fixedWindow.Bounds.Center.Y);
        root.Input.InjectPointerMoved(fixedEdge);
        Check("CanResize=false gives no resize cursor", root.RequestedCursor == UiCursor.Arrow,
            root.RequestedCursor.ToString());

        var fixedTitle = new Vector2(fixedWindow.Bounds.Center.X, fixedWindow.Bounds.Y + 8.0f);
        root.Input.InjectPointerPressed(fixedTitle, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerMoved(fixedTitle + new Vector2(50.0f, 50.0f));
        root.UpdateLayout(new Vector2(1280, 720));
        Check("CanMove=false stays put", fixedWindow.Bounds.Position == new Vector2(600.0f, 100.0f),
            fixedWindow.Bounds.Position.ToString());
        root.Input.InjectPointerReleased(fixedTitle, PointerButton.Left);

        // ---------------------------------------------------------------- content
        var scrolled = new UiWindow("Scrolled") { Width = 200.0f, Height = 120.0f };
        var longStack = new StackPanel().Vertical();
        for (var i = 0; i < 30; i++)
        {
            longStack.Children.Add(new Border().Fill(UiColor.White).Height(20.0f));
        }

        scrolled.Child = longStack.Scrollable();
        root.ShowWindow(scrolled, new Vector2(800.0f, 300.0f));
        root.UpdateLayout(new Vector2(1280, 720));

        var viewer = scrolled.Child as ScrollViewer;
        Check("window content scrolls", viewer != null && viewer.CanScrollVertically,
            $"{viewer?.ScrollableSize.Y}");
        Check("window clips its content",
            longStack.Bounds.Bottom > scrolled.Bounds.Bottom, "content taller than window");

        var windowed = root.Render();
        Check("windows render together", windowed.InstanceCount > 20, $"{windowed.InstanceCount} instances");
        Check("still one draw call", windowed.Batches.Count == 1, $"{windowed.Batches.Count} batches");
    }
}
