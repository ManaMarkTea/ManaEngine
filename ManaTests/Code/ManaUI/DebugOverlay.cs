using ManaUI;

namespace ManaTests;

/// <summary>
/// The wireframe overlay. Mostly it has to obey one rule: describe the UI without becoming part of what it
/// describes, and cost nothing when it is off.
/// </summary>
public static class DebugOverlayChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);

        var inner = new StackPanel().Vertical(4.0f).Children(
            new Border().Fill(UiColor.White).Height(20.0f),
            new Border().Fill(UiColor.White).Height(20.0f),
            new TextBlock().Text("Label"));

        root.Content = new Border().Fill(UiColor.Black).PaddingOf(8.0f).Content(inner);
        root.UpdateLayout(new Vector2(400, 300));

        // ---------------------------------------------------------------- off by default
        Check("the overlay starts off", root.Debug.Flags == UiDebugFlags.None && !root.Debug.IsEnabled,
            root.Debug.Flags.ToString());

        var plain = root.Render().InstanceCount;
        Check("the UI renders without it", plain > 0, $"{plain} instances");


        // ---------------------------------------------------------------- element bounds
        root.Debug.Flags = UiDebugFlags.ElementBounds;
        var outlined = root.Render().InstanceCount;

        Check("turning it on adds outlines", outlined > plain, $"{plain} -> {outlined}");
        Check("it walked the tree", root.Debug.ElementCount > 5, $"{root.Debug.ElementCount} elements");
        Check("it recorded the nesting depth", root.Debug.MaxDepth > 2, $"depth {root.Debug.MaxDepth}");

        // One outline per element with a non-empty rect. Exact rather than approximate, because a wireframe
        // that quietly skips elements is worse than none -- the missing box looks like a layout bug.
        var withBounds = CountNonEmpty(root);
        Check("one outline per laid-out element", outlined - plain == withBounds,
            $"{outlined - plain} outlines for {withBounds} elements");

        // ---------------------------------------------------------------- it does not measure itself
        // The readout describes the UI, so the overlay's own boxes must not be counted in it.
        root.Debug.Flags = UiDebugFlags.Stats;
        root.Render();
        var statsOnly = root.Render().InstanceCount;
        Check("stats add a panel of their own", statsOnly > plain, $"{plain} -> {statsOnly}");

        // Rendering twice must not drift: a counter that accumulated would climb every frame.
        var again = root.Render().InstanceCount;
        Check("repeated frames are stable", again == statsOnly, $"{statsOnly} then {again}");

        var walkedOnce = root.Debug.ElementCount;
        root.Render();
        Check("the element count does not accumulate", root.Debug.ElementCount == walkedOnce,
            $"{walkedOnce} then {root.Debug.ElementCount}");

        // ---------------------------------------------------------------- flags combine
        // Only elements that actually clip get a clip outline, so the flag needs something clipping before
        // it can show anything -- a Border does not, a ScrollViewer does.
        inner.Children.Add(new ScrollViewer
        {
            Height = 40.0f,
            Child = new Border().Fill(UiColor.White).Height(200.0f)
        });

        root.UpdateLayout(new Vector2(400, 300));

        // The tree changed, so the no-overlay baseline has to be retaken before it is compared against.
        root.Debug.Flags = UiDebugFlags.None;
        plain = root.Render().InstanceCount;

        root.Debug.Flags = UiDebugFlags.ElementBounds;
        var withClipper = root.Render().InstanceCount;

        root.Debug.Flags = UiDebugFlags.ElementBounds | UiDebugFlags.ClipRects;
        var withClips = root.Render().InstanceCount;
        Check("clip rects draw on top of bounds", withClips > withClipper,
            $"{withClipper} -> {withClips}");

        root.Debug.Flags = UiDebugFlags.All;
        Check("All includes every flag",
            (root.Debug.Flags & UiDebugFlags.ElementBounds) != 0
            && (root.Debug.Flags & UiDebugFlags.Stats) != 0
            && (root.Debug.Flags & UiDebugFlags.HoverPath) != 0,
            root.Debug.Flags.ToString());

        // Hovering something must add its ancestor chain without throwing.
        root.Input.InjectPointerMoved(inner.Children[0].Bounds.Center);
        Check("the hover path renders", root.Render().InstanceCount > withClips,
            $"{root.Render().InstanceCount} instances");

        // ---------------------------------------------------------------- off again
        root.Debug.Flags = UiDebugFlags.None;
        Check("turning it off costs nothing", root.Render().InstanceCount == plain,
            $"{root.Render().InstanceCount} vs {plain} without the overlay");

        // The overlay is drawn with the same primitive as everything else, so a frame with it on is still
        // one draw call -- which is also the claim the stats readout is there to let you verify.
        root.Debug.Flags = UiDebugFlags.All;
        var drawn = root.Render();
        Check("the overlay does not split the batch", drawn.Batches.Count == 1,
            $"{drawn.Batches.Count} batches");
    }

    private static int CountNonEmpty(UiElement element)
    {
        var count = element.Bounds.IsEmpty ? 0 : 1;
        var children = element.VisualChildCount;

        for (var i = 0; i < children; i++)
        {
            var child = element.GetVisualChild(i);
            if (child != null && child.Visibility != Visibility.Collapsed)
            {
                count += CountNonEmpty(child);
            }
        }

        return count;
    }
}
