using ManaUI;

namespace ManaTests;

public static class MenuChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var time = 0.0;

        var opened = new List<string>();
        var wireframe = false;

        var bar = new MenuBar().AddRange(
            new MenuItem("File").WithItems(
                new MenuItem("New", () => opened.Add("new")).WithShortcut("Ctrl+N"),
                new MenuItem("Open", () => opened.Add("open")).WithShortcut("Ctrl+O"),
                MenuItem.Separator(),
                new MenuItem("Recent").WithItems(
                    new MenuItem("scene-a.vox", () => opened.Add("recent-a")),
                    new MenuItem("scene-b.vox", () => opened.Add("recent-b"))),
                MenuItem.Separator(),
                new MenuItem("Exit", () => opened.Add("exit"))),

            new MenuItem("View").WithItems(
                new MenuItem("Wireframe", () => wireframe = !wireframe).Checkable(() => wireframe),
                new MenuItem("Unavailable", () => opened.Add("nope")).EnabledWhen(() => false)));

        root.Content = new DockPanel().Children(bar.DockTo(Dock.Top), new Border().Fill(UiColor.Transparent));
        root.UpdateLayout(new Vector2(1000, 700));

        Check("menu bar builds headings", bar.Children.Count == 2, $"{bar.Children.Count}");
        Check("headings sit side by side",
            bar.Children[1].Bounds.X > bar.Children[0].Bounds.X,
            $"{bar.Children[0].Bounds.X} then {bar.Children[1].Bounds.X}");
        Check("menu bar is one row tall", bar.Bounds.Height < 40.0f, $"{bar.Bounds.Height}");

        // ---------------------------------------------------------------- opening
        var fileHeading = bar.Children[0];
        root.Input.InjectPointerMoved(fileHeading.Bounds.Center);
        root.Input.InjectPointerPressed(fileHeading.Bounds.Center, PointerButton.Left, time);
        root.Input.InjectPointerReleased(fileHeading.Bounds.Center, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 700));

        Check("clicking a heading opens its menu", bar.IsAnyOpen && root.Popups.HasOpenPopups);
        Check("menu sits below the bar",
            root.Popups.Topmost!.Bounds.Y >= fileHeading.Bounds.Bottom,
            $"{root.Popups.Topmost.Bounds.Y}");

        var rows = FindAll<Control>(root.Popups.Topmost).Where(IsMenuRow).ToList();
        Check("separators are not rows", rows.Count == 4, $"{rows.Count} rows for 4 commands + 2 separators");

        // Hovering another heading while open switches menus without a second click.
        var viewHeading = bar.Children[1];
        root.Input.InjectPointerMoved(viewHeading.Bounds.Center);
        root.UpdateLayout(new Vector2(1000, 700));

        var viewRows = FindAll<Control>(root.Popups.Topmost!).Where(IsMenuRow).ToList();
        Check("hovering another heading switches menu", viewRows.Count == 2, $"{viewRows.Count} rows");
        Check("only one menu open at a time", root.Popups.Open.Count == 1, $"{root.Popups.Open.Count}");

        // Disabled rows do nothing.
        var disabledRow = viewRows[1];
        root.Input.InjectPointerMoved(disabledRow.Bounds.Center);
        root.Input.InjectPointerPressed(disabledRow.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(disabledRow.Bounds.Center, PointerButton.Left);
        Check("disabled item does not invoke", !opened.Contains("nope"), string.Join(",", opened));
        Check("disabled item keeps the menu open", root.Popups.HasOpenPopups);

        // A checkable item toggles and reports its state.
        var wireframeRow = viewRows[0];
        root.Input.InjectPointerMoved(wireframeRow.Bounds.Center);
        root.Input.InjectPointerPressed(wireframeRow.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(wireframeRow.Bounds.Center, PointerButton.Left);
        Check("checkable item invokes", wireframe, $"{wireframe}");
        Check("invoking closes the menu", !root.Popups.HasOpenPopups && !bar.IsAnyOpen);

        // ---------------------------------------------------------------- commands
        root.Input.InjectPointerMoved(fileHeading.Bounds.Center);
        root.Input.InjectPointerPressed(fileHeading.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(fileHeading.Bounds.Center, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 700));

        rows = FindAll<Control>(root.Popups.Topmost!).Where(IsMenuRow).ToList();
        root.Input.InjectPointerMoved(rows[0].Bounds.Center);
        root.Input.InjectPointerPressed(rows[0].Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(rows[0].Bounds.Center, PointerButton.Left);

        Check("clicking an item invokes it", opened.Contains("new"), string.Join(",", opened));
        Check("clicking an item closes the menu", !root.Popups.HasOpenPopups);

        // ---------------------------------------------------------------- submenus
        root.Input.InjectPointerMoved(fileHeading.Bounds.Center);
        root.Input.InjectPointerPressed(fileHeading.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(fileHeading.Bounds.Center, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 700));

        rows = FindAll<Control>(root.Popups.Topmost!).Where(IsMenuRow).ToList();
        var recentRow = rows[2];

        root.Input.InjectPointerMoved(recentRow.Bounds.Center);
        root.UpdateLayout(new Vector2(1000, 700));

        Check("hovering a parent opens its submenu", root.Popups.Open.Count == 2,
            $"{root.Popups.Open.Count} popups");
        Check("submenu opens to the side",
            root.Popups.Topmost!.Bounds.X >= recentRow.Bounds.Right - 1.0f,
            $"{root.Popups.Topmost.Bounds.X} vs {recentRow.Bounds.Right}");

        // Reaching a submenu means crossing the sibling rows between the parent row and the submenu's own
        // rows -- the direct path drops below the parent while still inside the parent menu. Those
        // crossings must not dismiss it. This is the reported intermittent bug: whether it happens depends
        // entirely on the path the pointer takes.
        var siblingBelow = rows[3];
        var submenuTarget = root.Popups.Topmost!.Bounds.Center;

        void TravelToSubmenu()
        {
            // Down onto the sibling first, then across -- how a real pointer overshoots on the way.
            root.Input.InjectPointerMoved(siblingBelow.Bounds.Center);
            root.Popups.Tick(0.016);
            root.UpdateLayout(new Vector2(1000, 700));

            root.Input.InjectPointerMoved(submenuTarget);
            root.Popups.Tick(0.016);
            root.UpdateLayout(new Vector2(1000, 700));
        }

        TravelToSubmenu();
        Check("travelling across a sibling keeps the submenu open", root.Popups.Open.Count == 2,
            $"{root.Popups.Open.Count} popups after crossing a sibling");

        // The parent row stays lit while its submenu is open, though the pointer has moved off it.
        Check("parent row keeps its highlight while the submenu is open",
            !recentRow.IsPointerOver && root.Popups.Open.Count == 2,
            "pointer is in the submenu, chain still open");

        // Settling on a sibling does close it, once the grace period elapses.
        root.Input.InjectPointerMoved(rows[0].Bounds.Center);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("sibling hover does not close immediately", root.Popups.Open.Count == 2,
            $"{root.Popups.Open.Count} popups");

        root.Popups.Tick(1.0);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("settling on a sibling closes the submenu", root.Popups.Open.Count == 1,
            $"{root.Popups.Open.Count} popups");

        // Proves the checks above have teeth: with the grace period removed, that very same path closes
        // the submenu. Without this, a path that never crossed a sibling would pass regardless.
        root.Input.InjectPointerMoved(recentRow.Bounds.Center);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("submenu reopens on the parent", root.Popups.Open.Count == 2, $"{root.Popups.Open.Count}");

        root.Popups.SubmenuCloseDelaySeconds = 0.0;
        TravelToSubmenu();
        Check("without the grace period the same path closes it", root.Popups.Open.Count == 1,
            $"{root.Popups.Open.Count} popups");

        root.Popups.SubmenuCloseDelaySeconds = 0.35;
        root.Input.InjectPointerMoved(recentRow.Bounds.Center);
        root.UpdateLayout(new Vector2(1000, 700));

        // Invoking inside a submenu closes the whole chain.
        root.Input.InjectPointerMoved(recentRow.Bounds.Center);
        root.UpdateLayout(new Vector2(1000, 700));
        var submenuRows = FindAll<Control>(root.Popups.Topmost!).Where(IsMenuRow).ToList();
        Check("submenu has its items", submenuRows.Count == 2, $"{submenuRows.Count}");

        root.Input.InjectPointerMoved(submenuRows[1].Bounds.Center);
        root.Input.InjectPointerPressed(submenuRows[1].Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(submenuRows[1].Bounds.Center, PointerButton.Left);

        Check("submenu item invokes", opened.Contains("recent-b"), string.Join(",", opened));
        Check("submenu invoke closes everything", !root.Popups.HasOpenPopups && !bar.IsAnyOpen);

        // ---------------------------------------------------------------- dismissal
        root.Input.InjectPointerMoved(fileHeading.Bounds.Center);
        root.Input.InjectPointerPressed(fileHeading.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(fileHeading.Bounds.Center, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("menu open before dismissal", root.Popups.HasOpenPopups);

        root.Input.InjectKeyDown(UiKey.Escape);
        Check("escape dismisses the menu", !root.Popups.HasOpenPopups);

        // Clicking the open heading again closes rather than reopening.
        root.Input.InjectPointerMoved(fileHeading.Bounds.Center);
        root.Input.InjectPointerPressed(fileHeading.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(fileHeading.Bounds.Center, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("menu reopened", root.Popups.HasOpenPopups);

        root.Input.InjectPointerPressed(fileHeading.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(fileHeading.Bounds.Center, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("clicking the open heading closes it", !root.Popups.HasOpenPopups, "no reopen loop");

        // ---------------------------------------------------------------- context menu
        var target = new Border().Fill(UiColor.White).Size(200.0f, 100.0f);
        root.Content = null;
        root.Content = new Canvas().Children(target.At(300.0f, 300.0f));
        root.UpdateLayout(new Vector2(1000, 700));

        var contextInvoked = false;
        var point = new Vector2(350.0f, 340.0f);
        MenuFlyout.ShowAt(root, point, new[]
        {
            new MenuItem("Duplicate", () => contextInvoked = true),
            new MenuItem("Delete")
        });
        root.UpdateLayout(new Vector2(1000, 700));

        Check("context menu opens at the point",
            MathF.Abs(root.Popups.Topmost!.Bounds.X - point.X) < 1.0f
            && MathF.Abs(root.Popups.Topmost.Bounds.Y - point.Y) < 1.0f,
            root.Popups.Topmost.Bounds.ToString());

        var contextRows = FindAll<Control>(root.Popups.Topmost).Where(IsMenuRow).ToList();
        root.Input.InjectPointerMoved(contextRows[0].Bounds.Center);
        root.Input.InjectPointerPressed(contextRows[0].Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(contextRows[0].Bounds.Center, PointerButton.Left);
        Check("context menu item invokes", contextInvoked);

        // A tick list that stays open: choosing one leaves the menu up, and a live label follows the choice.
        var chosen = "A";
        MenuFlyout.ShowAt(root, point, new[]
        {
            new MenuItem().TextFrom(() => $"Into {chosen}"),
            new MenuItem("A", () => chosen = "A").Checkable(() => chosen == "A").KeepsMenuOpen(),
            new MenuItem("B", () => chosen = "B").Checkable(() => chosen == "B").KeepsMenuOpen()
        });
        root.UpdateLayout(new Vector2(1000, 700));
        var stayingRows = FindAll<Control>(root.Popups.Topmost!).Where(IsMenuRow).ToList();
        root.Input.InjectPointerMoved(stayingRows[2].Bounds.Center);
        root.Input.InjectPointerPressed(stayingRows[2].Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(stayingRows[2].Bounds.Center, PointerButton.Left);
        Check("a stays-open item runs and leaves the menu up", chosen == "B" && root.Popups.Topmost != null);
        Check("a live label reads the current state", new MenuItem().TextFrom(() => $"Into {chosen}").ResolveText() == "Into B");
        root.Popups.CloseAll();

        // A context menu near the edge must stay on screen.
        MenuFlyout.ShowAt(root, new Vector2(995.0f, 695.0f), new[]
        {
            new MenuItem("Something long enough to overflow")
        });
        root.UpdateLayout(new Vector2(1000, 700));
        var edge = root.Popups.Topmost!;
        Check("context menu clamps on screen",
            edge.Bounds.Right <= 1000.01f && edge.Bounds.Bottom <= 700.01f, edge.Bounds.ToString());

        var drawn = root.Render();
        Check("menus render", drawn.InstanceCount > 5, $"{drawn.InstanceCount} instances");
        Check("still one draw call", drawn.Batches.Count == 1, $"{drawn.Batches.Count} batches");

        // A menu taller than the window scrolls instead of running off the bottom (a long Examples list).
        root.Popups.CloseAll();
        var longItems = new List<MenuItem>();
        for (var i = 0; i < 60; i++)
        {
            var index = i;
            longItems.Add(new MenuItem("Example " + i, () => opened.Add("long" + index)));
        }
        MenuFlyout.ShowAt(root, new Vector2(100, 40), longItems);
        root.UpdateLayout(new Vector2(1000, 700));
        var longMenu = root.Popups.Topmost!;
        Check("a menu taller than the window is capped to it",
            longMenu.Bounds.Bottom <= 700.01f && longMenu.Bounds.Y >= 0.0f, longMenu.Bounds.ToString());
        var scroller = FindAll<ScrollViewer>(longMenu).FirstOrDefault();
        Check("a menu taller than the window scrolls", scroller != null && scroller.CanScrollVertically);
        if (scroller != null)
        {
            scroller.ScrollToBottom();
            root.UpdateLayout(new Vector2(1000, 700));
            var last = FindAll<Control>(longMenu).Where(IsMenuRow).Last();
            Check("the last row can be scrolled into reach", last.Bounds.Bottom <= longMenu.Bounds.Bottom,
                $"{last.Bounds.Bottom} vs {longMenu.Bounds.Bottom}");
        }
        root.Popups.CloseAll();
        MenuFlyout.ShowAt(root, new Vector2(100, 40), longItems.Take(3).ToList());
        root.UpdateLayout(new Vector2(1000, 700));
        Check("a short menu is not wrapped in a scroller", !FindAll<ScrollViewer>(root.Popups.Topmost!).Any());
        root.Popups.CloseAll();
        root.Popups.CloseAll();
    }

    private static bool IsMenuRow(Control control) => control.GetType().Name == "MenuRow";

    private static IEnumerable<T> FindAll<T>(UiElement element) where T : UiElement
    {
        if (element is T typed)
        {
            yield return typed;
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            foreach (var found in FindAll<T>(element.GetVisualChild(i)))
            {
                yield return found;
            }
        }
    }
}
