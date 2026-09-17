using ManaUI;

namespace ManaTests;

/// <summary>
/// The aim-able drop buttons shown while a panel is dragged.
/// <para>
/// The thing being tested is that a button beats the proportional rule underneath it. Without that, the
/// buttons would be decoration: they would show you five choices and then hand you whichever one the
/// pointer's position happened to imply, which is what made docking feel like a guess in the first place.
/// </para>
/// </summary>
public static class DockDropIndicatorChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- geometry
        var pane = new UiRect(100.0f, 100.0f, 600.0f, 400.0f);
        var cluster = DockDropIndicators.ForGroup(pane);

        Check("a roomy pane offers all five drops", cluster.Count == 5, $"{cluster.Count}");
        Check("one of them per side, plus the centre",
            cluster.Select(i => i.Side).OrderBy(s => s).SequenceEqual(
                new[] { DockSide.Left, DockSide.Right, DockSide.Top, DockSide.Bottom, DockSide.Center }
                    .OrderBy(s => s)),
            string.Join(", ", cluster.Select(i => i.Side)));

        var escaped = cluster.Count(i =>
            i.Box.X < pane.X || i.Box.Right > pane.Right || i.Box.Y < pane.Y || i.Box.Bottom > pane.Bottom);
        Check("every button sits inside the pane it refers to", escaped == 0, $"{escaped} outside");

        var overlapping = 0;
        for (var i = 0; i < cluster.Count; i++)
        {
            for (var j = i + 1; j < cluster.Count; j++)
            {
                var a = cluster[i].Box;
                var b = cluster[j].Box;
                if (a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom)
                {
                    overlapping++;
                }
            }
        }

        Check("and no two of them overlap", overlapping == 0, $"{overlapping} pairs");

        // A pane too small for the cluster gets the centre alone rather than buttons hanging outside it.
        var tiny = DockDropIndicators.ForGroup(new UiRect(0.0f, 0.0f, 60.0f, 40.0f));
        Check("a pane too small for the cluster offers only the centre",
            tiny.Count == 1 && tiny[0].Side == DockSide.Center, $"{tiny.Count}");

        // ---------------------------------------------------------------- aiming beats proportion
        var root = new UiRoot(fonts);
        var host = new DockHost();
        root.Content = host;

        host.Dock(new DockItem("Scene", new Border().Fill(UiColor.White)) { Id = "scene" }, DockSide.Center);
        root.UpdateLayout(new Vector2(1000.0f, 700.0f));

        var group = host.RootNode as DockTabGroup;
        Check("the host has a pane to aim at", group != null);
        if (group == null)
        {
            return;
        }

        var buttons = DockDropIndicators.ForGroup(group.Bounds);
        foreach (var button in buttons)
        {
            var resolved = DockDropResolver.Resolve(host, button.Box.Center);
            Check($"aiming at the {button.Side} button docks {button.Side}",
                resolved.Side == button.Side && !resolved.IsRootEdge,
                $"{resolved.Side}, rootEdge {resolved.IsRootEdge}");
        }

        // The left button sits well inside the pane's middle third, which the proportional fallback calls
        // Center -- so this only passes because the button was consulted first.
        var left = buttons.First(b => b.Side == DockSide.Left).Box.Center;
        var fraction = (left.X - group.Bounds.X) / group.Bounds.Width;
        Check("and the left button really is in the pane's middle third",
            fraction > 0.3f && fraction < 0.5f, $"at {fraction:P0} across");

        // ---------------------------------------------------------------- the layout's own edges
        foreach (var edge in DockDropIndicators.ForHost(host.Bounds))
        {
            var resolved = DockDropResolver.Resolve(host, edge.Box.Center);
            Check($"aiming at the outer {edge.Side} button splits the whole layout",
                resolved.Side == edge.Side && resolved.IsRootEdge,
                $"{resolved.Side}, rootEdge {resolved.IsRootEdge}");
        }

        // ---------------------------------------------------------------- a miss still behaves as before
        var middle = DockDropResolver.Resolve(host, group.Bounds.Center + new Vector2(0.0f, 120.0f));
        Check("a drop that touches no button falls back to the old proportional rule",
            middle.Side == DockSide.Center, middle.Side.ToString());

        // ---------------------------------------------------------------- and the target carries the frames
        Check("the target reports the pane the buttons were laid out over",
            middle.GroupBounds == group.Bounds, middle.GroupBounds.ToString());
        Check("and the host, for the outer four", middle.HostBounds == host.Bounds,
            middle.HostBounds.ToString());

        // ---------------------------------------------------------------- the overlay paints them
        var overlay = new DockDropOverlay();
        root.OverlayLayer.Children.Add(overlay);

        overlay.Hide();
        root.UpdateLayout(new Vector2(1000.0f, 700.0f));
        var idle = root.Render().InstanceCount;

        overlay.Show(DockDropResolver.Resolve(host, group.Bounds.Center));
        root.UpdateLayout(new Vector2(1000.0f, 700.0f));
        var dragging = root.Render().InstanceCount;

        // Nine buttons (five over the pane, four at the edges), several instances each, plus the preview.
        Check("the overlay draws the buttons while a drag is live",
            dragging > idle + 20, $"{idle} idle -> {dragging} dragging, bounds {overlay.Bounds}");
    }
}
