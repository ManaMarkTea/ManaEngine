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

        // Moving back to a sibling collapses the submenu rather than leaving two open.
        root.Input.InjectPointerMoved(rows[0].Bounds.Center);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("moving to a sibling closes the submenu", root.Popups.Open.Count == 1,
            $"{root.Popups.Open.Count} popups");

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
