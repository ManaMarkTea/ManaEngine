namespace ManaTests;

using ManaUI;

/// <summary>
/// Paint order of a panel's children. It is asked for once per child on every frame, so it has to be cheap
/// when nothing has a ZIndex (the overwhelmingly common case) -- a cache that treated "nothing to sort" as
/// "not computed" rescanned every child on every call and made drawing a panel quadratic in its children,
/// which was the single biggest cost in the editor's frame -- and still correct when something does.
/// </summary>
public static class PaintOrderChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var panel = new StackPanel();
        var first = new Border();
        var second = new Border();
        var third = new Border();
        panel.Children.Add(first);
        panel.Children.Add(second);
        panel.Children.Add(third);

        Check("with no ZIndex anywhere, paint order is declaration order",
            ReferenceEquals(panel.GetVisualChild(0), first) && ReferenceEquals(panel.GetVisualChild(2), third));
        Check("and asking again gives the very same answer",
            ReferenceEquals(panel.Children.PaintOrdered, panel.Children.PaintOrdered));

        second.ZIndex(5);
        first.ZIndex(9);
        Check("a raised ZIndex paints later", ReferenceEquals(panel.GetVisualChild(2), first) && ReferenceEquals(panel.GetVisualChild(1), second)
            && ReferenceEquals(panel.GetVisualChild(0), third));

        first.ZIndex(0);
        second.ZIndex(0);
        Check("and putting it back restores declaration order",
            ReferenceEquals(panel.GetVisualChild(0), first) && ReferenceEquals(panel.GetVisualChild(1), second));

        var extra = new Border();
        panel.Children.Add(extra);
        Check("a child added later is painted last", ReferenceEquals(panel.GetVisualChild(3), extra));

        panel.Children.Remove(first);
        Check("and one removed is gone from the order", panel.VisualChildCount == 3 && ReferenceEquals(panel.GetVisualChild(0), second));

        // ---------------------------------------------------------------- Root and IsEffectivelyEnabled are remembered, and must stay right
        var rootA = new UiRoot(fonts);
        var rootB = new UiRoot(fonts);
        var outer = new StackPanel();
        var inner = new Border();
        var label = new TextBlock().Text("hello");
        outer.Children.Add(inner);
        inner.Content(label);

        Check("a detached element has no root", label.Root == null && inner.Root == null);
        rootA.Content = outer;
        Check("attaching the tree gives every element its root", ReferenceEquals(label.Root, rootA) && ReferenceEquals(inner.Root, rootA));
        rootA.Content = null;
        Check("detaching it takes the root away again, however deep", label.Root == null && inner.Root == null && outer.Root == null);
        rootB.Content = outer;
        Check("and moving the tree to another root is noticed", ReferenceEquals(label.Root, rootB), $"{label.Root == rootA}");

        Check("everything starts enabled", label.IsEffectivelyEnabled && inner.IsEffectivelyEnabled);
        outer.IsEnabled = false;
        Check("disabling a container disables everything under it", !label.IsEffectivelyEnabled && !inner.IsEffectivelyEnabled && !outer.IsEffectivelyEnabled);
        outer.IsEnabled = true;
        Check("and enabling it brings them back", label.IsEffectivelyEnabled && inner.IsEffectivelyEnabled);
        label.IsEnabled = false;
        Check("a disabled child does not disable its parents", !label.IsEffectivelyEnabled && inner.IsEffectivelyEnabled && outer.IsEffectivelyEnabled);
        label.IsEnabled = true;

        var spare = new StackPanel();
        outer.Children.Remove(inner);
        spare.IsEnabled = false;
        spare.Children.Add(inner);
        Check("a subtree moved under a disabled parent is disabled", !label.IsEffectivelyEnabled);
        spare.Children.Remove(inner);
        Check("and is enabled again when moved out", label.IsEffectivelyEnabled);

        var sized = new TextBlock().Text("size");
        var sizedRoot = new UiRoot(fonts);
        sizedRoot.Content = sized;
        sizedRoot.UpdateLayout(new Vector2(200, 100));
        var smallFont = sized.Font;
        sized.FontSize(30.0f);
        Check("a text element picks up a new font size, not the font it remembered", sized.Font != null && !ReferenceEquals(sized.Font, smallFont));

        var many = new StackPanel();
        for (var i = 0; i < 4000; i++)
        {
            many.Children.Add(new Border());
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var pass = 0; pass < 20; pass++)
        {
            for (var i = 0; i < many.VisualChildCount; i++)
            {
                _ = many.GetVisualChild(i);
            }
        }

        // 80,000 lookups. Linear, it is a millisecond or two; the quadratic version rescanned 4,000 children each time.
        Check("walking a big panel's children stays cheap", clock.ElapsedMilliseconds < 200, $"{clock.ElapsedMilliseconds}ms for 80,000 lookups");
    }
}
