using ManaUI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

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

            // Two columns now, left and right, each in its own resizable panel.
            var columns = FindAll<DockHost>(dockRoot);
            Check("the workspace has two columns", columns.Count == 2, $"{columns.Count} dock hosts");

            var left = columns[0];
            var right = columns[1];

            Check("docked layout builds", right.RootNode != null, right.RootNode?.GetType().Name ?? "null");
            Check("the default preset puts every panel in one column",
                right.AllItems().Count() == 3 && !left.AllItems().Any(),
                $"left [{string.Join(", ", left.AllItems().Select(i => i.Title))}] "
                + $"right [{string.Join(", ", right.AllItems().Select(i => i.Title))}]");

            Check("the right column is anchored to the right",
                MathF.Abs(right.Bounds.Right - 1920.0f) < 1.0f, right.Bounds.ToString());
            Check("the columns leave the viewport visible",
                right.Bounds.Width < 500.0f, $"{right.Bounds.Width}");

            // An empty column collapses rather than leaving a resize handle against nothing.
            var panels = FindAll<ResizablePanel>(dockRoot);
            Check("each column is resizable", panels.Count == 2, $"{panels.Count} resizable panels");
            Check("an empty column is collapsed",
                panels[0].Visibility == Visibility.Collapsed, panels[0].Visibility.ToString());

            // A column sits over the viewport, so every pixel of it has to be painted. The gutters are the
            // easy ones to miss: the handle strip and the seams between panes belong to the column but to
            // no panel's content, and leaving them unpainted made a solid column read as translucent.
            Check("the column paints a backdrop", panels[1].Background.W > 0.0f,
                $"alpha {panels[1].Background.W}");

            // Sampled off-centre on purpose. The handle's own 2px line sits exactly in the middle of the
            // grab strip, so a centre sample passes whether or not the rest of the gutter is painted --
            // which is what it did before this comment existed.
            var handleRect = panels[1].HandleRect;
            var gutter = new Vector2(handleRect.X + 1.0f, handleRect.Center.Y);
            var painted = false;

            foreach (var instance in dockRoot.Render().Instances)
            {
                if (gutter.X >= instance.Rect.X
                    && gutter.X <= instance.Rect.X + instance.Rect.Z
                    && gutter.Y >= instance.Rect.Y
                    && gutter.Y <= instance.Rect.Y + instance.Rect.W
                    && UiColor.Unpack(instance.FillColor).W >= 1.0f)
                {
                    painted = true;
                    break;
                }
            }

            Check("something opaque covers the handle gutter", painted, $"sampled at {gutter}");

            // Dragging the handle widens the column, which is the point of the whole exercise.
            var rightPanel = panels[1];
            var widthBefore = rightPanel.Size;
            var grab = rightPanel.HandleRect.Center;

            dockRoot.Input.InjectPointerMoved(grab);
            Check("the handle asks for a resize cursor", dockRoot.RequestedCursor == UiCursor.SizeWE,
                dockRoot.RequestedCursor.ToString());

            dockRoot.Input.InjectPointerPressed(grab, PointerButton.Left, 0.0);
            dockRoot.Input.InjectPointerMoved(grab - new Vector2(120.0f, 0.0f));
            dockRoot.UpdateLayout(new Vector2(1920, 1080));
            Check("dragging the handle widens the column",
                MathF.Abs(rightPanel.Size - (widthBefore + 120.0f)) < 1.0f,
                $"{widthBefore} -> {rightPanel.Size}");
            Check("the dock host follows the column",
                right.Bounds.Width > 300.0f, $"{right.Bounds.Width:F0}px");
            dockRoot.Input.InjectPointerReleased(grab - new Vector2(120.0f, 0.0f), PointerButton.Left);

            Check("docked layout renders", dockRoot.Render().InstanceCount > 50,
                $"{dockRoot.Render().InstanceCount} instances");

            // ---------------------------------------------------------------- presets
            // Driven through the real menu rather than a back door, so the wiring is covered too.
            var menu = FindFirst<MenuBar>(dockRoot, _ => true);
            var layoutMenu = menu?.Menus.FirstOrDefault(item => item.Text == "Layout");
            Check("the layout menu exists", layoutMenu != null,
                string.Join(", ", menu?.Menus.Select(i => i.Text) ?? Array.Empty<string>()));

            var unity = layoutMenu?.Items.FirstOrDefault(item => item.Text == "Unity");
            Check("Unity is offered as a preset", unity != null,
                string.Join(", ", layoutMenu?.Items.Select(i => i.Text) ?? Array.Empty<string>()));

            unity?.Invoked?.Invoke();
            dockRoot.UpdateLayout(new Vector2(1920, 1080));

            Check("choosing a preset moves panels between columns",
                left.AllItems().Any() && right.AllItems().Any(),
                $"left [{string.Join(", ", left.AllItems().Select(i => i.Title))}] "
                + $"right [{string.Join(", ", right.AllItems().Select(i => i.Title))}]");
            Check("no panel is lost in the move",
                left.AllItems().Count() + right.AllItems().Count() == 3,
                $"{left.AllItems().Count() + right.AllItems().Count()} panels");
            Check("the now-filled column shows itself",
                panels[0].Visibility == Visibility.Visible, panels[0].Visibility.ToString());
            Check("the chosen preset is ticked", unity!.IsCheckedProvider?.Invoke() == true, "Unity checked");

            var rightColumnPreset = layoutMenu!.Items.First(item => item.Text == "Right column");
            rightColumnPreset.Invoked?.Invoke();
            dockRoot.UpdateLayout(new Vector2(1920, 1080));
            Check("switching back empties the left column again", !left.AllItems().Any(),
                string.Join(", ", left.AllItems().Select(i => i.Title)));

            Check("the workspace still renders after a preset change",
                dockRoot.Render().InstanceCount > 50, $"{dockRoot.Render().InstanceCount} instances");
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

    /// <summary>Every element of a type beneath a root, in visual order.</summary>
    private static List<T> FindAll<T>(UiElement element) where T : UiElement
    {
        var found = new List<T>();
        Collect(element, found);
        return found;
    }

    private static void Collect<T>(UiElement element, List<T> found) where T : UiElement
    {
        if (element is T typed)
        {
            found.Add(typed);
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            Collect(element.GetVisualChild(i), found);
        }
    }
}
