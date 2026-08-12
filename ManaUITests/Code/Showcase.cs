using ManaUI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaUITests;

/// <summary>
/// Drives the real showcase view headlessly. This is the substitute for a screenshot: it proves the actual
/// production view constructs, binds, lays out and records draw commands, rather than testing a
/// hand-written copy of it that could drift.
/// </summary>
public static class ShowcaseChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var vm = new ManaUiShowcaseViewModel();
        var frame = 0L;

        UiElement? content = null;
        try
        {
            content = ManaUiShowcaseView.Build(root.Theme, vm, () => $"render frame {frame:N0}");
            Check("showcase constructs", true);
        }
        catch (Exception ex)
        {
            Check("showcase constructs", false, ex.Message);
            return;
        }

        // The docked layout is what the editor actually shows, so it is exercised too. Built against a
        // separate root so its persistence hooks cannot touch the ones under test above.
        try
        {
            var dockRoot = new UiRoot(fonts);
            var dockVm = new ManaUiShowcaseViewModel();
            // persist: false so the test never reads or writes the user's real layout file.
            var dockHostElement = ManaUiDockDemo.Attach(dockRoot, dockVm, () => "frame 0", persist: false);

            dockRoot.Content = dockHostElement;
            dockRoot.UpdateLayout(new Vector2(1920, 1080));

            var docked = FindFirst<DockHost>(dockRoot, _ => true);
            Check("docked layout builds", docked?.RootNode != null, docked?.RootNode?.GetType().Name ?? "null");
            Check("default layout has all panels", docked!.AllItems().Count() == 3,
                string.Join(", ", docked.AllItems().Select(i => i.Title)));
            Check("dock host anchored to the right",
                MathF.Abs(docked.Bounds.Right - 1920.0f) < 1.0f, docked.Bounds.ToString());
            Check("dock host leaves the viewport visible",
                docked.Bounds.Width < 500.0f, $"{docked.Bounds.Width}");
            Check("docked layout renders", dockRoot.Render().InstanceCount > 50,
                $"{dockRoot.Render().InstanceCount} instances");
        }
        catch (Exception ex)
        {
            Check("docked layout builds", false, ex.Message);
        }

        root.Content = content;
        root.UpdateLayout(new Vector2(1920, 1080));

        var panel = FindFirst<Border>(root, b => b.ShadowBlur > 0.0f);
        Check("panel laid out", panel != null && panel.Bounds.Width > 200 && panel.Bounds.Height > 300,
            panel?.Bounds.ToString() ?? "not found");

        var drawList = root.Render();
        Check("showcase records draws", drawList.InstanceCount > 100, $"{drawList.InstanceCount} instances");
        Check("still one draw call", drawList.Batches.Count == 1, $"{drawList.Batches.Count} batches");

        // Commands routed through the real view model.
        var before = vm.ClickCount;
        vm.BumpCommand.Execute(null);
        var counter = FindFirst<TextBlock>(root, t => t.Text.StartsWith("Clicked "));
        Check("command updates bound label", counter != null && counter.Text == $"Clicked {before + 1} time"
            + (before + 1 == 1 ? string.Empty : "s"), counter?.Text ?? "not found");

        // Two-way slider binding, driven from the view model side.
        vm.BrushRadius = 9.5f;
        var slider = FindFirst<Slider>(root, _ => true);
        Check("slider follows view model", slider != null && MathF.Abs(slider.Value - 9.5f) < 0.01f,
            $"{slider?.Value}");

        var summary = FindFirst<TextBlock>(root, t => t.Text.StartsWith("Brush radius"));
        Check("derived property updates", summary != null && summary.Text.Contains("9.5"),
            summary?.Text ?? "not found");

        // And from the control side, which is the direction that needs the write-back.
        slider!.Value = 3.0f;
        Check("slider writes back to view model", MathF.Abs(vm.BrushRadius - 3.0f) < 0.01f, $"{vm.BrushRadius}");

        // ObservableCollection driving the ItemsControl inside the real view.
        var list = FindFirst<ItemsControl>(root, _ => true);
        var initialRows = list!.Children.Count;
        Check("layer rows built", initialRows == vm.Layers.Count, $"{initialRows} rows / {vm.Layers.Count} layers");

        vm.AddLayerCommand.Execute(null);
        Check("add layer adds a row", list.Children.Count == initialRows + 1, $"{list.Children.Count}");

        vm.RemoveLayerCommand.Execute(null);
        Check("remove layer drops a row", list.Children.Count == initialRows, $"{list.Children.Count}");

        while (vm.Layers.Count > 0)
        {
            vm.RemoveLayerCommand.Execute(null);
        }

        Check("empty placeholder appears", list.Children.Count == 1, $"{list.Children.Count}");
        Check("remove disabled when empty", !vm.RemoveLayerCommand.CanExecute(null));

        // Polled binding against engine-style state with no change notification.
        var stat = FindFirst<TextBlock>(root, t => t.Text.StartsWith("render frame"));
        Check("polled stat bound", stat != null, stat?.Text ?? "not found");

        frame = 1234;
        root.UpdateLayout(new Vector2(1920, 1080));
        Check("polled stat updates on tick", stat!.Text == "render frame 1,234", stat.Text);

        // The whole tree must survive a relayout at a different size without throwing.
        try
        {
            root.UpdateLayout(new Vector2(800, 600));
            root.Render();
            root.UpdateLayout(new Vector2(2560, 1440));
            root.Render();
            Check("survives resize", true);
        }
        catch (Exception ex)
        {
            Check("survives resize", false, ex.Message);
        }

        // A DPI change drops the glyph atlas; text must re-raster rather than render blank.
        root.SetScale(2.0f);
        root.UpdateLayout(new Vector2(1920, 1080));
        var scaled = root.Render();
        Check("survives DPI change", scaled.InstanceCount > 100, $"{scaled.InstanceCount} instances at 2x");
    }

    private static T? FindFirst<T>(UiElement element, Func<T, bool> predicate) where T : UiElement
    {
        if (element is T typed && predicate(typed))
        {
            return typed;
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            var found = FindFirst(element.GetVisualChild(i), predicate);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
