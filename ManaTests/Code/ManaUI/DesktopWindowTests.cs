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
        Check("double-click transfers the original tab and content", native!.Item == item && content.Root == native.Root && item.Owner == native.Tabs);
        Check("source root releases keyboard focus", root.FocusManager.Focused == null && !root.FocusManager.Focus(content));
        Check("external panel counts as open", controller.FloatingItems.Single() == item && controller.FindDesktop(item.Id!) == native);
        Check("repeated pop-out focuses existing window", controller.PopOut(item) == native && native.FocusCount == 1);
        native.Root.UpdateLayout(new Vector2(800, 600));
        DoubleClick(native.Root, native.Tabs.TabRect(0).Center, 2);
        Check("dock back preserves edits and ownership", content.Text == "keep this edit" && content.Root == root && item.Owner != null);
        Check("dock back disposes native window", native.Disposed && controller.DesktopWindows.Count == 0);
        var floating = controller.BeginFloat(item, new Vector2(300, 150));
        controller.PopOut(item);
        Check("in-app float can become native", floating.Child == null && controller.FloatingWindows.Count == 0 && native!.Item == item);
        controller.CloseDesktopWindows();
        Check("rebuild closes windows without docking stale content", native!.Disposed && controller.DesktopWindows.Count == 0 && item.Owner == null);

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
            workspace.Content = ManaUiDockDemo.Attach(workspace, new ManaUiShowcaseViewModel(), layoutPath: path);
            var docks = ManaUiDockDemo.LastDragController!;
            var showcase = new DockItem("ManaUI", new TextBlock("test")) { Id = "showcase" };
            var external = (FakeWindow)docks.PopOut(showcase, 2)!;
            var menus = Descendants(workspace).OfType<MenuBar>().Single();
            Check("Pop out menu is removed", menus.Menus.All(menu => menu.Text != "Pop out"));
            menus.Menus.Single(menu => menu.Text == "Panels").Items.Single(menu => menu.Text == "ManaUI").Invoke();
            Check("Panels menu focuses an existing pop-out without duplication", external.FocusCount == 1 && docks.DesktopWindows.Count == 1);
            external.Position = new Vector2(-900, 80);
            external.Size = new Vector2(500, 700);
            external.NotifyBoundsChanged();
            var saved = ManaUiWorkspaceLayout.Load(path)!;
            var entry = saved.Floating.Single(panel => panel.Id == "showcase");
            Check("workspace saves native bounds and home", entry.Desktop && entry.X == -900 && entry.Width == 500 && entry.HomeHostIndex == 2);
            workspace.Content = ManaUiDockDemo.Attach(workspace, new ManaUiShowcaseViewModel(), layoutPath: path);
            Check("workspace rebuild disposes previous native window", external.Disposed);
            ManaUiDockDemo.LastDragController!.CloseDesktopWindows();
            var restored = new UiRoot(fonts) { CreateDesktopWindow = _ => new FakeWindow(fonts) };
            restored.Content = ManaUiDockDemo.Attach(restored, new ManaUiShowcaseViewModel(), layoutPath: path);
            var restoredController = ManaUiDockDemo.LastDragController!;
            Check("workspace restores native window", restoredController.FindDesktop("showcase")?.Position == new Vector2(-900, 80));
            restoredController.CloseDesktopWindows();
            var fallback = new UiRoot(fonts);
            fallback.Content = ManaUiDockDemo.Attach(fallback, new ManaUiShowcaseViewModel(), layoutPath: path);
            Check("unsupported platform restores native panel as in-app float", ManaUiDockDemo.LastDragController!.FindFloating("showcase") != null);
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

    private sealed class FakeWindow : IUiDesktopWindow
    {
        public FakeWindow(ManaFontManager fonts)
        {
            Root = new UiRoot(fonts) { Content = Tabs };
            Tabs.TabDoubleClicked += (_, _) => RequestDockBack();
        }
        public UiRoot Root { get; }
        public DockTabGroup Tabs { get; } = new();
        private DockItem? _item;
        public DockItem? Item
        {
            get => _item;
            set
            {
                if (_item != null) Tabs.Remove(_item);
                _item = value;
                if (value != null) Tabs.Add(value);
            }
        }
        public Vector2 Position { get; set; }
        public Vector2 Size { get; set; } = new(480, 640);
        public int FocusCount { get; private set; }
        public bool Disposed { get; private set; }
        public event Action? DockBack;
        public event Action? BoundsChanged;
        public void RequestDockBack() => DockBack?.Invoke();
        public void NotifyBoundsChanged() => BoundsChanged?.Invoke();
        public void Focus() => FocusCount++;
        public void Dispose() { Item = null; Disposed = true; }
    }
}
