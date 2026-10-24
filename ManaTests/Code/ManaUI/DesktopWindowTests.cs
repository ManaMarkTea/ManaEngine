using ManaUI;
using Mana.Code.UI.ManaUi;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

public static class DesktopWindowChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string name, bool passed) => check(name, passed, "");
        var root = new UiRoot(fonts);
        var home = new DockHost();
        root.Content = home;
        var content = new TextBox { Text = "keep this edit" };
        var item = new DockItem("Inspector", content) { Id = "inspector" };
        home.Dock(item, DockSide.Center);
        root.UpdateLayout(new Vector2(800, 600));
        var controller = new DockDragController(root, home);
        Check("unsupported pop-out leaves panel docked", controller.PopOut(item) == null && item.Owner != null);
        root.CreateDesktopWindow = _ => throw new InvalidOperationException("creation failed");
        try { controller.PopOut(item); } catch (InvalidOperationException) { }
        Check("native creation failure preserves panel", item.Owner != null && content.Root == root);

        FakeWindow? native = null;
        root.CreateDesktopWindow = _ => native = new FakeWindow(fonts);
        root.FocusManager.Focus(content);
        var tabPoint = item.Owner!.TabRect(0).Center;
        root.Input.InjectPointerPressed(tabPoint, PointerButton.Left, 1);
        root.Input.InjectPointerReleased(tabPoint, PointerButton.Left);
        Check("a single tab click does not pop out", native == null && content.Root == root);
        root.Input.InjectPointerPressed(tabPoint, PointerButton.Left, 1.1);
        root.Input.InjectPointerReleased(tabPoint, PointerButton.Left);
        Check("double-click transfers the original tab and content", native!.Frame.Dock.AllItems().Single() == item && content.Root == native.Root && item.Owner == native.Frame.Dock.RootNode);
        Check("source root releases keyboard focus", root.FocusManager.Focused == null && !root.FocusManager.Focus(content));
        Check("external panel counts as open", controller.FloatingItems.Single() == item && controller.FindDesktop(item.Id!) == native);
        Check("repeated pop-out focuses existing window", controller.PopOut(item) == native && native.FocusCount == 1);
        native.Root.UpdateLayout(new Vector2(800, 600));
        DoubleClick(native.Root, ((DockTabGroup)native.Frame.Dock.RootNode!).TabRect(0).Center, 2);
        Check("dock back preserves edits and ownership", content.Text == "keep this edit" && content.Root == root && item.Owner != null);
        Check("dock back disposes native window", native.Disposed && controller.DesktopWindows.Count == 0);
        var floating = controller.BeginFloat(item, new Vector2(300, 150));
        controller.PopOut(item);
        Check("in-app float can become native", floating.Child == null && controller.FloatingWindows.Count == 0 && native!.Frame.Dock.AllItems().Contains(item));
        controller.CloseDesktopWindows();
        Check("rebuild closes windows without docking stale content", native!.Disposed && controller.DesktopWindows.Count == 0 && item.Owner == null);

        RunDockingChecks(fonts, Check);
        RunPromoteChecks(fonts, check);

        var atlas = new ManaImageAtlas(32, 32);
        var revision = atlas.Revision;
        atlas.ClearDirty();
        Check("consuming dirty region preserves revision for other windows", atlas.Revision == revision);
        atlas.Reset();
        Check("atlas reset changes revision", atlas.Revision > revision);

        var path = Path.Combine(Path.GetTempPath(), $"mana-popout-{Guid.NewGuid():N}.json");
        var workspace = new UiRoot(fonts);
        workspace.CreateDesktopWindow = _ => new FakeWindow(fonts);
        try
        {
            workspace.Content = EditorWorkspace.Attach(workspace, new ManaUiShowcaseViewModel(), layoutPath: path);
            var docks = EditorWorkspace.For(workspace).DragController!;
            var showcase = new DockItem("ManaUI", new TextBlock("test")) { Id = "showcase" };
            var external = (FakeWindow)docks.PopOut(showcase, 2)!;
            Check("native panels use their owner's asset services",
                ReferenceEquals(EditorWorkspace.For(external.Root).Assets, EditorWorkspace.For(workspace).Assets));
            var menus = Descendants(workspace).OfType<MenuBar>().Single();
            Check("Pop out menu is removed", menus.Menus.All(menu => menu.Text != "Pop out"));
            menus.Menus.Single(menu => menu.Text == "Panels").Items.Single(menu => menu.Text == "ManaUI").Invoke();
            Check("Panels menu focuses an existing pop-out without duplication", external.FocusCount == 1 && docks.DesktopWindows.Count == 1);
            var blur = new DockItem("Blur Test", new TextBlock("blur")) { Id = "blurtest" };
            external.Frame.Dock.Dock(blur, DockSide.Center, DockHost.FindFirstGroup(external.Frame.Dock.RootNode));
            external.Position = new Vector2(-900, 80);
            external.Size = new Vector2(500, 700);
            external.NotifyBoundsChanged();
            var saved = ManaUiWorkspaceLayout.Load(path)!;
            var entry = saved.Floating.Single(panel => panel.Id == "showcase");
            Check("workspace saves native bounds and home", entry.Desktop && entry.X == -900 && entry.Width == 500 && entry.HomeHostIndex == 2);
            Check("workspace saves every panel in a native window", entry.Layout?.Items?.Contains("blurtest") == true && entry.ItemHomes?.ContainsKey("blurtest") == true);
            workspace.Content = EditorWorkspace.Attach(workspace, new ManaUiShowcaseViewModel(), layoutPath: path);
            Check("workspace rebuild disposes previous native window", external.Disposed);
            EditorWorkspace.For(workspace).DragController!.CloseDesktopWindows();
            var restored = new UiRoot(fonts) { CreateDesktopWindow = _ => new FakeWindow(fonts) };
            restored.Content = EditorWorkspace.Attach(restored, new ManaUiShowcaseViewModel(), layoutPath: path);
            var restoredController = EditorWorkspace.For(restored).DragController!;
            Check("workspace restores native window", restoredController.FindDesktop("showcase")?.Position == new Vector2(-900, 80));
            Check("workspace restores panels sharing a native window",
                restoredController.FindDesktop("blurtest") is { } shared && shared == restoredController.FindDesktop("showcase"));
            restoredController.CloseDesktopWindows();
            var fallback = new UiRoot(fonts);
            fallback.Content = EditorWorkspace.Attach(fallback, new ManaUiShowcaseViewModel(), layoutPath: path);
            Check("unsupported platform restores native panel as in-app float", EditorWorkspace.For(fallback).DragController!.FindFloating("showcase") != null);
        }
        finally { File.Delete(path); }

        var small = new UiRoot(fonts);
        var large = new UiRoot(fonts);
        small.Theme.FontSize = 12;
        large.Theme.FontSize = 32;
        var text = new TextBlock("Inherited font metrics");
        var subtree = new StackPanel();
        subtree.Children.Add(text);
        small.Content = subtree;
        small.UpdateLayout(new Vector2(800, 600));
        var oldWidth = text.DesiredSize.X;
        small.Content = null;
        large.Content = subtree;
        large.UpdateLayout(new Vector2(800, 600));
        Check("cross-root transfer remeasures nested controls", text.DesiredSize.X > oldWidth * 2);
    }

    private static void RunDockingChecks(ManaFontManager fonts, Action<string, bool> Check)
    {
        var root = new UiRoot(fonts);
        var host = new DockHost();
        root.Content = host;
        var a = new DockItem("A", new TextBlock("a")) { Id = "a" };
        var b = new DockItem("B", new TextBlock("b")) { Id = "b" };
        var c = new DockItem("C", new TextBlock("c")) { Id = "c" };
        host.Dock(a, DockSide.Center);
        host.Dock(b, DockSide.Center);
        host.Dock(c, DockSide.Center);

        var windows = new List<FakeWindow>();
        root.CreateDesktopWindow = _ =>
        {
            var created = new FakeWindow(fonts);
            windows.Add(created);
            return created;
        };
        root.UpdateLayout(new Vector2(800, 600));
        var controller = new DockDragController(root, host);

        var first = (FakeWindow)controller.PopOut(a)!;
        first.MoveTo(new Vector2(1000, 50));
        first.Size = new Vector2(800, 600);
        first.Root.UpdateLayout(new Vector2(800, 600));

        // A panel dragged out of the editor lands in the popped-out window, wherever that is on screen.
        var tab = b.Owner!.TabRect(b.Owner.Items.ToList().IndexOf(b));
        root.Input.InjectPointerMoved(tab.Center);
        root.Input.InjectPointerPressed(tab.Center, PointerButton.Left, 10.0);
        root.Input.InjectPointerMoved(tab.Center + new Vector2(0, 120));
        Check("dragging a docked tab out floats it", controller.FloatingWindows.Count == 1);
        var over = new Vector2(1400, 350);
        root.Input.InjectPointerMoved(over);
        Check("hovering a popped-out window shows its drop preview", controller.CurrentTarget.IsValid);
        root.Input.InjectPointerReleased(over, PointerButton.Left);
        Check("dropping on a popped-out window docks into it",
            first.Frame.Dock.AllItems().Contains(b) && first.Frame.Dock.AllItems().Contains(a)
            && controller.FloatingWindows.Count == 0);

        // Pulling one of its two tabs out makes a second window that follows the pointer.
        first.Root.UpdateLayout(new Vector2(800, 600));
        var group = (DockTabGroup)first.Frame.Dock.RootNode!;
        Check("two panels tab together", group.Items.Count == 2 && !group.TabsMoveWindow);
        var native = group.TabRect(group.Items.ToList().IndexOf(b));
        first.Root.Input.InjectPointerMoved(native.Center);
        first.Root.Input.InjectPointerPressed(native.Center, PointerButton.Left, 20.0);
        first.Root.Input.InjectPointerMoved(native.Center + new Vector2(0, 120));
        Check("tearing a tab out of a popped-out window opens another", controller.DesktopWindows.Count == 2 && windows.Count == 2);
        var second = windows[1];
        var away = new Vector2(-6000, 300);
        first.Root.Input.InjectPointerMoved(away);
        first.Root.Input.InjectPointerReleased(away, PointerButton.Left);
        var expected = first.Position + away - new Vector2(MathF.Min(first.Size.X * 0.5f, 120.0f), 12.0f);
        Check("the new window follows the pointer", second.Position == expected);
        Check("the source keeps its other panel", first.Frame.Dock.AllItems().Single() == a);

        // A window that is one panel moves by its tab strip, and merges where it is dropped.
        second.Root.UpdateLayout(new Vector2(800, 600));
        var strip = ((DockTabGroup)second.Frame.Dock.RootNode!).TabStripRect;
        var grab = new Vector2(strip.Right - 100, strip.Center.Y);
        second.Root.Input.InjectPointerMoved(grab);
        second.Root.Input.InjectPointerPressed(grab, PointerButton.Left, 30.0);
        second.Root.Input.InjectPointerReleased(grab, PointerButton.Left);
        Check("clicking a strip does not dock or close the window", controller.DesktopWindows.Count == 2);

        var before = second.Position;
        second.Root.Input.InjectPointerPressed(grab, PointerButton.Left, 40.0);
        second.Root.Input.InjectPointerMoved(grab + new Vector2(30, 0));
        Check("dragging the strip moves the window", second.Position == before + new Vector2(30, 0));

        Vector2 Client(Vector2 screen) => screen - second.Position;
        var target = new Vector2(1400, 350);
        second.Root.Input.InjectPointerMoved(Client(target));
        Check("a window over another shows a drop preview", controller.CurrentTarget.IsValid);
        Check("and steps out of the way of it", second.Position.X < -10000);
        second.Root.Input.InjectPointerReleased(Client(target), PointerButton.Left);
        Check("dropping merges the panel and closes its window",
            first.Frame.Dock.AllItems().Contains(b) && second.Disposed && controller.DesktopWindows.Count == 1);

        // Let go of a panel over nothing and it becomes a window of its own.
        var lone = c.Owner!.TabRect(0);
        root.Input.InjectPointerMoved(lone.Center);
        root.Input.InjectPointerPressed(lone.Center, PointerButton.Left, 50.0);
        root.Input.InjectPointerMoved(lone.Center + new Vector2(0, 120));
        var nowhere = new Vector2(-6000, 300);
        root.Input.InjectPointerMoved(nowhere);
        root.Input.InjectPointerReleased(nowhere, PointerButton.Left);
        Check("a panel dropped on nothing pops out",
            controller.FloatingWindows.Count == 0 && controller.FindDesktop("c") != null);

        controller.CloseDesktopWindows();
        Check("closing windows leaves nothing registered", controller.DesktopWindows.Count == 0);
    }

    private static IEnumerable<UiElement> Descendants(UiElement element)
    {
        yield return element;
        for (var i = 0; i < element.VisualChildCount; i++)
            foreach (var child in Descendants(element.GetVisualChild(i))) yield return child;
    }

    private static void DoubleClick(UiRoot root, Vector2 point, double time)
    {
        root.Input.InjectPointerPressed(point, PointerButton.Left, time);
        root.Input.InjectPointerReleased(point, PointerButton.Left);
        root.Input.InjectPointerPressed(point, PointerButton.Left, time + 0.1);
        root.Input.InjectPointerReleased(point, PointerButton.Left);
    }

    // A float dragged past the editor window's edge becomes a native window there and then, and the drag carries on
    // moving that window; let go away from every dock target, it stays a window.
    private static void RunPromoteChecks(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var root = new UiRoot(fonts);
        var home = new DockHost();
        root.Content = home;
        var item = new DockItem("Terrain", new TextBlock().Text("brushes")) { Id = "terrain" };
        home.Dock(item, DockSide.Center);
        var size = new Vector2(800, 600);
        root.UpdateLayout(size);
        var controller = new DockDragController(root, home);
        FakeWindow? native = null;
        root.CreateDesktopWindow = _ => native = new FakeWindow(fonts);

        var floating = controller.BeginFloat(item, new Vector2(400, 300));
        root.UpdateLayout(size);
        var title = new Vector2(floating.Bounds.X + floating.Bounds.Width * 0.5f, floating.Bounds.Y + 12.0f);
        root.Input.InjectPointerMoved(title);
        root.Input.InjectPointerPressed(title, PointerButton.Left, 100.0);
        var inside = title + new Vector2(-150, 0);
        root.Input.InjectPointerMoved(inside);
        root.UpdateLayout(size);
        check("dragged about inside the window, it is still a float", native == null && controller.FloatingWindows.Count == 1, "");

        var grab = inside - floating.Position;
        var outside = new Vector2(-40, inside.Y);
        root.Input.InjectPointerMoved(outside);
        root.UpdateLayout(size);
        check("past the window's edge it becomes a window of its own", native != null && native.Frame.Dock.AllItems().Contains(item)
            && controller.FloatingWindows.Count == 0, $"{native != null}, {controller.FloatingWindows.Count} floats");
        check("held where it was grabbed", native != null && Vector2.Distance(native.Position, outside - grab) < 1.0f,
            $"{native?.Position} vs {outside - grab}");

        var further = outside + new Vector2(-200, 80);
        root.Input.InjectPointerMoved(further);
        check("and the same drag carries on moving it", native != null && Vector2.Distance(native.Position, further - grab) < 1.0f,
            $"{native?.Position} vs {further - grab}");

        root.Input.InjectPointerReleased(further, PointerButton.Left);
        root.UpdateLayout(size);
        check("let go out there, it stays a window", native is { Disposed: false } && controller.FindDesktop("terrain") == native, "");
        check("and the editor's pointer is free again", root.Input.PointerCaptureTarget == null, $"{root.Input.PointerCaptureTarget}");
    }

    private sealed class FakeWindow : IUiDesktopWindow
    {
        public FakeWindow(ManaFontManager fonts)
        {
            Root = new UiRoot(fonts) { Content = Frame };
            Frame.ScreenOrigin = () => Position;
        }
        public UiRoot Root { get; }
        public DesktopWindowFrame Frame { get; } = new();
        public Vector2 Position { get; set; }
        public Vector2 Size { get; set; } = new(480, 640);
        public int FocusCount { get; private set; }
        public bool Disposed { get; private set; }
        public event Action? DockBack;
        public event Action? BoundsChanged;
        public void RequestDockBack() => DockBack?.Invoke();
        public void NotifyBoundsChanged() => BoundsChanged?.Invoke();
        public void MoveTo(Vector2 screenOrigin) => Position = screenOrigin;
        public void Focus() => FocusCount++;
        public void Dispose()
        {
            foreach (var item in Frame.Dock.AllItems().ToList())
            {
                item.Owner?.Remove(item);
            }

            Disposed = true;
        }
    }
}
