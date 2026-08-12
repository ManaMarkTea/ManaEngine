using ManaUI;

namespace ManaTests;

public static class DragDockingChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        static DockItem Item(string title) =>
            new(title, new Border().Fill(UiColor.White)) { Id = title };

        var root = new UiRoot(fonts);
        var host = new DockHost();
        root.Content = host;

        var scene = Item("Scene");
        var group = host.Dock(scene, DockSide.Center);
        root.UpdateLayout(new Vector2(1000, 600));

        // ---------------------------------------------------------------- drop resolution
        var centre = DockDropResolver.Resolve(host, group.Bounds.Center);
        Check("centre of a pane docks as a tab", centre.Side == DockSide.Center, centre.Side.ToString());
        Check("centre preview covers the pane", centre.Preview == group.Bounds, centre.Preview.ToString());
        Check("centre target names the group", ReferenceEquals(centre.Group, group));

        // Inside the pane but off-centre: split that way, not tab.
        var innerLeft = new Vector2(group.Bounds.X + group.Bounds.Width * 0.12f, group.Bounds.Center.Y);
        var leftDrop = DockDropResolver.Resolve(host, innerLeft);
        Check("pane edge docks to that side", leftDrop.Side == DockSide.Left, leftDrop.Side.ToString());
        Check("side preview is half the pane",
            MathF.Abs(leftDrop.Preview.Width - group.Bounds.Width * 0.5f) < 0.01f,
            leftDrop.Preview.ToString());
        Check("side drop is not a root edge", !leftDrop.IsRootEdge);

        var innerBottom = new Vector2(group.Bounds.Center.X, group.Bounds.Bottom - group.Bounds.Height * 0.1f);
        Check("bottom of a pane docks below",
            DockDropResolver.Resolve(host, innerBottom).Side == DockSide.Bottom,
            DockDropResolver.Resolve(host, innerBottom).Side.ToString());

        // The outer band docks against the whole layout, not the pane under the cursor.
        var outerRight = new Vector2(host.Bounds.Right - 8.0f, host.Bounds.Center.Y);
        var rootDrop = DockDropResolver.Resolve(host, outerRight);
        Check("outer edge is a root edge", rootDrop.IsRootEdge, $"{rootDrop.IsRootEdge}");
        Check("root edge has no group", rootDrop.Group == null);
        Check("root edge preview is half the host",
            MathF.Abs(rootDrop.Preview.Width - host.Bounds.Width * 0.5f) < 0.01f, rootDrop.Preview.ToString());

        Check("outside the host is not a target",
            !DockDropResolver.Resolve(host, new Vector2(-50.0f, -50.0f)).IsValid);

        // ---------------------------------------------------------------- tear out
        var controller = new DockDragController(root, host);
        var outliner = Item("Outliner");
        host.Dock(outliner, DockSide.Center, group);
        root.UpdateLayout(new Vector2(1000, 600));

        Check("two tabs before tear-out", group.Items.Count == 2, $"{group.Items.Count}");

        var tabRect = group.TabRect(1);
        root.Input.InjectPointerMoved(tabRect.Center);
        root.Input.InjectPointerPressed(tabRect.Center, PointerButton.Left, 0.0);

        // Pulling down off the strip should tear the tab out into a floating window.
        var pulled = tabRect.Center + new Vector2(0.0f, 120.0f);
        root.Input.InjectPointerMoved(pulled);
        root.UpdateLayout(new Vector2(1000, 600));

        Check("tear-out starts a drag", controller.IsDragging);
        Check("torn tab left the group", group.Items.Count == 1, $"{group.Items.Count}");
        Check("a floating window appeared", root.FloatingLayer.Children.Count == 1,
            $"{root.FloatingLayer.Children.Count}");

        var floating = root.FloatingLayer.Children[0] as UiWindow;
        Check("floating window is titled", floating?.Title == "Outliner", floating?.Title ?? "null");
        Check("content moved into the window", ReferenceEquals(floating?.Child, outliner.Content));
        Check("floating window has the drag", root.PointerCaptureTarget == floating);

        // Dragging over the layout should resolve a target.
        root.Input.InjectPointerMoved(group.Bounds.Center);
        Check("drag over a pane resolves a target", controller.CurrentTarget.IsValid,
            controller.CurrentTarget.Side.ToString());
        Check("target is the pane underneath", ReferenceEquals(controller.CurrentTarget.Group, group));

        // ---------------------------------------------------------------- drop back in
        var dropPoint = new Vector2(group.Bounds.Right - group.Bounds.Width * 0.1f, group.Bounds.Center.Y);
        root.Input.InjectPointerMoved(dropPoint);
        root.Input.InjectPointerReleased(dropPoint, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 600));

        Check("drop ends the drag", !controller.IsDragging);
        Check("floating window removed", root.FloatingLayer.Children.Count == 0,
            $"{root.FloatingLayer.Children.Count}");
        Check("item is docked again", host.AllItems().Any(i => i.Title == "Outliner"));
        Check("dropping on the right split the layout", host.RootNode is DockSplit,
            host.RootNode?.GetType().Name ?? "null");

        var split = (DockSplit)host.RootNode!;
        Check("docked to the right side",
            (split.Children[1] as DockTabGroup)?.Items[0].Title == "Outliner",
            (split.Children[1] as DockTabGroup)?.Items[0].Title ?? "?");
        Check("no items lost", host.AllItems().Count() == 2, $"{host.AllItems().Count()}");
        Check("content re-parented into the tree", outliner.Content.Parent != null);

        root.UpdateLayout(new Vector2(1000, 600));
        Check("dropped panel has real bounds", outliner.Content.Bounds.Width > 0.0f,
            outliner.Content.Bounds.ToString());

        // ---------------------------------------------------------------- drop outside stays floating
        var timeline = Item("Timeline");
        host.Dock(timeline, DockSide.Center, split.Children[0] as DockNode);
        root.UpdateLayout(new Vector2(1000, 600));

        var hostGroup = timeline.Owner!;
        var timelineTab = hostGroup.TabRect(hostGroup.Items.ToList().IndexOf(timeline));
        root.Input.InjectPointerMoved(timelineTab.Center);
        root.Input.InjectPointerPressed(timelineTab.Center, PointerButton.Left, 1.0);
        root.Input.InjectPointerMoved(timelineTab.Center + new Vector2(0.0f, 120.0f));
        root.UpdateLayout(new Vector2(1000, 600));

        Check("second tear-out floats", root.FloatingLayer.Children.Count == 1,
            $"{root.FloatingLayer.Children.Count}");

        // Release well outside the dock host: it should stay floating rather than snapping back.
        var outside = new Vector2(-200.0f, -200.0f);
        root.Input.InjectPointerMoved(outside);
        root.Input.InjectPointerReleased(outside, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 600));

        Check("drop outside keeps it floating", root.FloatingLayer.Children.Count == 1,
            $"{root.FloatingLayer.Children.Count}");
        Check("floated item is out of the tree", !host.AllItems().Any(i => i.Title == "Timeline"));
        Check("drag state cleared", !controller.IsDragging);

        // ---------------------------------------------------------------- re-docking a settled window
        // The reported bug: a window torn out, released, and dragged back later never docked, because the
        // drag handlers were removed when the tear-out gesture ended. Registration must outlive the
        // gesture, so this uses a completely separate drag from the one that created the window.
        var settled = root.FloatingLayer.Children[0] as UiWindow;
        Check("a settled floating window exists", settled != null, settled?.Title ?? "null");
        Check("settled window is still dockable",
            controller.FloatingWindows.Contains(settled!), $"{controller.FloatingWindows.Count} registered");

        // Move it somewhere harmless first, so the new drag starts from a clean position.
        settled!.Position = new Vector2(500.0f, 500.0f);
        root.UpdateLayout(new Vector2(1000, 600));

        var titleBar = new Vector2(settled.Bounds.Center.X, settled.Bounds.Y + 10.0f);
        root.Input.InjectPointerMoved(titleBar);
        root.Input.InjectPointerPressed(titleBar, PointerButton.Left, 5.0);
        Check("second drag starts", root.PointerCaptureTarget == settled);

        var backOver = host.Bounds.Center;
        root.Input.InjectPointerMoved(backOver);
        Check("second drag resolves a target", controller.CurrentTarget.IsValid,
            controller.CurrentTarget.Side.ToString());

        var itemsBefore = host.AllItems().Count();
        root.Input.InjectPointerReleased(backOver, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 600));

        Check("second drag docks the window", host.AllItems().Count() == itemsBefore + 1,
            $"{itemsBefore} -> {host.AllItems().Count()}");
        Check("re-docked panel is back in the tree", host.AllItems().Any(i => i.Title == "Timeline"),
            string.Join(", ", host.AllItems().Select(i => i.Title)));
        Check("window removed after re-docking", root.FloatingLayer.Children.Count == 0,
            $"{root.FloatingLayer.Children.Count}");
        Check("no longer registered as floating", controller.FloatingWindows.Count == 0,
            $"{controller.FloatingWindows.Count}");

        root.UpdateLayout(new Vector2(1000, 600));
        var reDocked = host.AllItems().First(i => i.Title == "Timeline");
        Check("re-docked content has real bounds", reDocked.Content.Bounds.Width > 0.0f,
            reDocked.Content.Bounds.ToString());

        // ---------------------------------------------------------------- overlay
        var overlay = root.OverlayLayer.Children.OfType<DockDropOverlay>().FirstOrDefault();
        Check("drop overlay exists", overlay != null);
        Check("overlay hidden when idle", overlay!.Visibility == Visibility.Collapsed,
            overlay.Visibility.ToString());
        Check("overlay never blocks input", !overlay.IsHitTestVisible);

        overlay.Show(DockDropResolver.Resolve(host, host.Bounds.Center));
        root.UpdateLayout(new Vector2(1000, 600));
        var drawn = root.Render();
        Check("overlay renders a preview", overlay.Visibility == Visibility.Visible && drawn.InstanceCount > 5,
            $"{drawn.InstanceCount} instances");
        overlay.Hide();

        Check("everything still one draw call", root.Render().Batches.Count == 1);
    }
}
