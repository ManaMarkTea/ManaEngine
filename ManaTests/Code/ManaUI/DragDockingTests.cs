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

        // Counted against the same frame with the overlay hidden, not against a flat number. The old check
        // was "more than five instances", which the docked content alone satisfies -- so it passed for as
        // long as the overlay drew nothing whatsoever, which it did: it reported a zero desired size and
        // then tried to claim the root back during arrange, and Arrange clamps that to the desired size.
        // Empty bounds, and UiElement.Render skips those. The drop preview never appeared on screen once.
        root.UpdateLayout(new Vector2(1000, 600));
        var withoutOverlay = root.Render().InstanceCount;

        overlay.Show(DockDropResolver.Resolve(host, host.Bounds.Center));
        root.UpdateLayout(new Vector2(1000, 600));
        var drawn = root.Render();

        Check("the overlay has real bounds to paint into", !overlay.Bounds.IsEmpty, overlay.Bounds.ToString());
        Check("overlay renders a preview",
            overlay.Visibility == Visibility.Visible && drawn.InstanceCount > withoutOverlay,
            $"{withoutOverlay} without -> {drawn.InstanceCount} with");
        overlay.Hide();

        Check("everything still one draw call", root.Render().Batches.Count == 1);

        AcrossColumns(fonts, check);
    }

    /// <summary>
    /// Dragging a panel from one column into another. A controller per host cannot do this -- each only
    /// resolves drops inside its own tree, so a tab torn out of the left column had nowhere to land but
    /// the left column, which is what "it would only dock into the left" looks like.
    /// </summary>
    public static void AcrossColumns(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        static DockItem Item(string title) => new(title, new TextBlock().Text(title)) { Id = title };

        var root = new UiRoot(fonts);

        var left = new DockHost();
        var right = new DockHost();

        var stats = Item("Stats");
        left.Dock(Item("Tools"), DockSide.Center);
        left.Dock(stats, DockSide.Center, DockHost.FindFirstGroup(left.RootNode)!);
        right.Dock(Item("Outliner"), DockSide.Center);

        // Side by side, the way the workspace lays its columns out.
        root.Content = new Grid().Columns("*, *").Rows("*").Children(
            left.GridColumn(0),
            right.GridColumn(1));

        root.UpdateLayout(new Vector2(1200, 700));

        var controller = new DockDragController(root, new[] { left, right });

        Check("the left column starts with both panels",
            left.AllItems().Count() == 2 && right.AllItems().Count() == 1,
            $"left {left.AllItems().Count()}, right {right.AllItems().Count()}");

        var group = DockHost.FindFirstGroup(left.RootNode)!;
        var tab = group.TabRect(group.Items.ToList().IndexOf(stats));

        root.Input.InjectPointerMoved(tab.Center);
        root.Input.InjectPointerPressed(tab.Center, PointerButton.Left, 0.0);
        root.Input.InjectPointerMoved(tab.Center + new Vector2(0.0f, 140.0f));
        root.UpdateLayout(new Vector2(1200, 700));

        Check("tearing it out starts a drag", controller.IsDragging, $"{controller.IsDragging}");

        // Carry it over the other column and drop it there.
        var overRight = DockHost.FindFirstGroup(right.RootNode)!.Bounds.Center;
        root.Input.InjectPointerMoved(overRight);
        root.UpdateLayout(new Vector2(1200, 700));

        Check("the other column offers a drop target",
            DockDropResolver.Resolve(right, overRight).IsValid, "resolved across hosts");

        root.Input.InjectPointerReleased(overRight, PointerButton.Left);
        root.UpdateLayout(new Vector2(1200, 700));

        var leftTitles = left.AllItems().Select(i => i.Title).ToList();
        var rightTitles = right.AllItems().Select(i => i.Title).ToList();

        Check("the panel lands in the column it was dropped on", rightTitles.Contains("Stats"),
            string.Join(", ", rightTitles));

        Check("and leaves the one it came from", !leftTitles.Contains("Stats"),
            string.Join(", ", leftTitles));

        Check("without losing anything else",
            leftTitles.Count + rightTitles.Count == 3, $"{leftTitles.Count} + {rightTitles.Count}");

        // And back again, since a one-way trip would be its own kind of broken.
        var backGroup = DockHost.FindFirstGroup(right.RootNode)!;
        var backTab = backGroup.TabRect(backGroup.Items.ToList().FindIndex(i => i.Title == "Stats"));

        root.Input.InjectPointerMoved(backTab.Center);
        root.Input.InjectPointerPressed(backTab.Center, PointerButton.Left, 1.0);
        root.Input.InjectPointerMoved(backTab.Center + new Vector2(0.0f, 140.0f));
        root.UpdateLayout(new Vector2(1200, 700));

        var overLeft = DockHost.FindFirstGroup(left.RootNode)!.Bounds.Center;
        root.Input.InjectPointerMoved(overLeft);
        root.Input.InjectPointerReleased(overLeft, PointerButton.Left);
        root.UpdateLayout(new Vector2(1200, 700));

        Check("and it can be dragged back",
            left.AllItems().Any(i => i.Title == "Stats"),
            string.Join(", ", left.AllItems().Select(i => i.Title)));

        // ---------------------------------------------------------------- an emptied column
        // A column that hides once empty takes no space, so only its zone along the window edge can take a drop.
        var mainRoot = new UiRoot(fonts);
        var main = new DockHost();
        var side = new DockHost { Visibility = Visibility.Collapsed };
        var zone = new UiRect(1160.0f, 0.0f, 40.0f, 700.0f);
        side.EmptyDropZone = () => zone;
        side.EmptyDropPreview = () => new UiRect(900.0f, 0.0f, 300.0f, 700.0f);
        var details = Item("Details");
        main.Dock(Item("Scene"), DockSide.Center);
        main.Dock(details, DockSide.Center, DockHost.FindFirstGroup(main.RootNode)!);
        mainRoot.Content = new Grid().Columns("*, Auto").Rows("*").Children(main.GridColumn(0), side.GridColumn(1));
        mainRoot.UpdateLayout(new Vector2(1200, 700));
        var mainController = new DockDragController(mainRoot, new[] { main, side });

        var inZone = new Vector2(1180.0f, 350.0f);
        var zoneDrop = DockDropResolver.Resolve(side, inZone);
        Check("an empty hidden column takes a drop in its zone", zoneDrop.IsValid && zoneDrop.Side == DockSide.Center,
            zoneDrop.Preview.ToString());
        Check("previewed at the size it will open at", zoneDrop.Preview == new UiRect(900.0f, 0.0f, 300.0f, 700.0f),
            zoneDrop.Preview.ToString());
        Check("but not outside it", !DockDropResolver.Resolve(side, new Vector2(1100.0f, 350.0f)).IsValid);

        var mainGroup = DockHost.FindFirstGroup(main.RootNode)!;
        var detailsTab = mainGroup.TabRect(mainGroup.Items.ToList().IndexOf(details));
        mainRoot.Input.InjectPointerMoved(detailsTab.Center);
        mainRoot.Input.InjectPointerPressed(detailsTab.Center, PointerButton.Left, 2.0);
        mainRoot.Input.InjectPointerMoved(detailsTab.Center + new Vector2(0.0f, 140.0f));
        mainRoot.UpdateLayout(new Vector2(1200, 700));
        mainRoot.Input.InjectPointerMoved(inZone);
        mainRoot.UpdateLayout(new Vector2(1200, 700));
        mainRoot.Input.InjectPointerReleased(inZone, PointerButton.Left);
        mainRoot.UpdateLayout(new Vector2(1200, 700));

        Check("a panel dropped in the zone docks into the empty column",
            side.AllItems().Any(i => i.Title == "Details"),
            $"side: {string.Join(", ", side.AllItems().Select(i => i.Title))}; dragging {mainController.IsDragging}");
    }
}
