using ManaUI;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// The editor's main menu, ported from ImGui. The point of the port is that nothing about the editor
/// changes: the same view model drives it, the same rows appear, and a tick still means what it meant.
/// <para>
/// An immediate-mode menu gets its live state for free -- it is rebuilt every frame from whatever the
/// view model says. A retained one has to arrange that, and getting it wrong gives a menu that is right
/// the first time it opens and stale afterwards. Most of what is checked here is that.
/// </para>
/// </summary>
public static class PortedMainMenuChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var vm = new VoxelEditorViewModel();

        MenuBar? bar = null;
        try
        {
            // No game mode: the rows needing one are the ImGui-era font and theme settings, which are the
            // rows due to disappear with ImGui anyway.
            bar = VoxelEditorMainMenuManaView.Build(vm);
            Check("the ported menu constructs without a game mode", true);
        }
        catch (Exception ex)
        {
            Check("the ported menu constructs without a game mode", false, ex.Message);
            return;
        }

        root.Content = new Grid().Rows("Auto, *").Columns("*").Children(bar.GridRow(0));
        root.UpdateLayout(new Vector2(1280, 720));

        var headings = bar.Menus.Select(m => m.Text).ToList();
        Check("it has the same top-level menus", headings.SequenceEqual(new[] { "File", "Character", "View" }),
            string.Join(", ", headings));

        Check("the bar is one row tall", bar.Bounds.Height <= root.Theme.ControlHeight + 8.0f,
            $"{bar.Bounds.Height:F0}px");

        // ---------------------------------------------------------------- File
        var file = bar.Menus[0];
        Check("File offers baking and locking",
            file.Items.Any(i => i.Text == "Bake Voxels To Physics")
                && file.Items.Any(i => i.Text == "Lock Voxels For Editing"),
            string.Join(", ", file.Items.Select(i => i.Text)));

        // Enablement is a lambda over the view model, not a value read when the menu was built. This is
        // the thing a retained menu has to get right and an immediate one cannot get wrong.
        var bake = file.Items.First(i => i.Text == "Bake Voxels To Physics");
        var lockVoxels = file.Items.First(i => i.Text == "Lock Voxels For Editing");

        Check("with nothing baked, baking is offered and locking is not",
            bake.ResolveEnabled() && !lockVoxels.ResolveEnabled(),
            $"bake {bake.ResolveEnabled()}, lock {lockVoxels.ResolveEnabled()}");

        // ---------------------------------------------------------------- View toggles
        var view = bar.Menus[2];
        var toggles = new (string Row, Func<bool> Read)[]
        {
            ("Show Lightning Ball", () => vm.ShowLightningBall),
            ("Show Liquid Ball", () => vm.ShowLiquidBall),
            ("Show Water", () => vm.ShowWater),
            ("Show Wireframe", () => vm.ShowWireframe),
            ("Show Vertex Normals", () => vm.ShowVertexNormals),
            ("Shade By Normal", () => vm.ShowNormals)
        };

        foreach (var (row, read) in toggles)
        {
            var item = view.Items.FirstOrDefault(i => i.Text == row);
            if (item == null)
            {
                Check($"View has {row}", false, string.Join(", ", view.Items.Select(i => i.Text)));
                continue;
            }

            var before = read();
            Check($"{row} reports the editor's state", item.ResolveChecked() == before, $"{before}");

            item.Invoke();
            Check($"and choosing it flips that state", read() != before, $"{read()}");

            // Reported live, not captured: a tick read once at build time would still say the old value.
            Check($"and the tick follows", item.ResolveChecked() == read(), $"{item.ResolveChecked()}");

            item.Invoke();
        }

        // ---------------------------------------------------------------- the hosted slider
        // A menu is not only a list of commands. Normal Length is a value you nudge while watching the
        // result, so it lives in the menu rather than sending you to a panel.
        var normalLength = view.Items.FirstOrDefault(i => i.Text == "Normal Length");
        Check("Normal Length is a hosted control", normalLength?.HasContent == true,
            normalLength == null ? "missing" : $"content {normalLength.HasContent}");

        if (normalLength?.Content is SliderBox slider)
        {
            root.Content = null;
            root.Content = new Canvas().Children(slider.At(10.0f, 10.0f));
            root.UpdateLayout(new Vector2(1280, 720));

            Check("it shows the current length",
                MathF.Abs(slider.Value - vm.VertexNormalLength) < 0.001f, $"{slider.Value}");

            slider.Value = 4.0f;
            Check("and dragging it writes to the editor",
                MathF.Abs(vm.VertexNormalLength - 4.0f) < 0.001f, $"{vm.VertexNormalLength}");

            // Only meaningful while the normals are shown, and it says so rather than pretending.
            vm.ShowVertexNormals = false;
            root.UpdateLayout(new Vector2(1280, 720));
            Check("and it is disabled when the normals are hidden", !slider.IsEnabled, $"{slider.IsEnabled}");

            vm.ShowVertexNormals = true;
            root.UpdateLayout(new Vector2(1280, 720));
            Check("and enabled again when they are shown", slider.IsEnabled, $"{slider.IsEnabled}");
        }

        // ---------------------------------------------------------------- labels and hints
        var character = bar.Menus[1];
        var summary = character.Items.FirstOrDefault(i => i.IsLabel);
        Check("the character count is a label, not a command",
            summary != null && summary.Invoked == null, summary?.Text ?? "missing");

        var airspace = view.Items.FirstOrDefault(i => i.Text == "Show Airspace (KML)");
        Check("the airspace row reports loading in its shortcut column",
            airspace?.ResolveShortcut() == string.Empty, airspace?.ResolveShortcut() ?? "missing");

        // The row stays enabled while loading so it can be switched back off mid-load; it is disabled only
        // when the file is not on this machine at all.
        Check("and is disabled only when the file is absent",
            airspace?.ResolveEnabled() == vm.KmlAirspaceAvailable,
            $"available {vm.KmlAirspaceAvailable}, enabled {airspace?.ResolveEnabled()}");

        var physics = character.Items.FirstOrDefault(i => i.Text == "Physics Capsule (new spawns)");
        Check("the physics row explains itself on hover",
            physics?.Tooltip?.Contains("Bepu capsule", StringComparison.Ordinal) == true,
            physics?.Tooltip ?? "no tooltip");

        // ---------------------------------------------------------------- opening it for real
        // Rebuilt rather than re-parented: the framework refuses to move an element that still has a
        // parent, which is the right call and exactly what a stale reference would do here.
        bar = VoxelEditorMainMenuManaView.Build(vm);

        root.Content = null;
        root.Content = new Grid().Rows("Auto, *").Columns("*").Children(bar.GridRow(0));
        root.UpdateLayout(new Vector2(1280, 720));

        var fileHeading = FindAll<UiElement>(bar).FirstOrDefault(e => e.GetType().Name == "MenuBarHeading");
        Check("the bar builds a heading per menu", fileHeading != null);

        if (fileHeading != null)
        {
            root.Input.InjectPointerMoved(fileHeading.Bounds.Center);
            root.Input.InjectPointerPressed(fileHeading.Bounds.Center, PointerButton.Left, 1.0);
            root.Input.InjectPointerReleased(fileHeading.Bounds.Center, PointerButton.Left);
            root.UpdateLayout(new Vector2(1280, 720));

            Check("clicking a heading opens its menu", root.Popups.HasOpenPopups && bar.IsAnyOpen,
                $"{root.Popups.Open.Count} popups");

            Check("and the whole thing still renders in one draw call",
                root.Render().Batches.Count == 1, $"{root.Render().Batches.Count} batches");

            root.Popups.CloseAll();
        }

        InWorkspace(fonts, check);
    }

    /// <summary>
    /// The menu as the editor actually gets it: dropped into the workspace bar beside the workspace's own
    /// chrome, rather than living in a second bar of its own.
    /// </summary>
    public static void InWorkspace(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var vm = new VoxelEditorViewModel();
        var showcase = new ManaUiShowcaseViewModel();

        // Not persisted: this must not write over the workspace the user actually has.
        var scratch = Path.Combine(Path.GetTempPath(), $"mana-menu-{Guid.NewGuid():N}.json");

        UiElement? workspace = null;
        try
        {
            workspace = ManaUiDockDemo.Attach(root, showcase, null, persist: false, editor: vm, layoutPath: scratch);
        }
        catch (Exception ex)
        {
            Check("the workspace builds with the editor menus in it", false, ex.Message);
            return;
        }

        root.Content = workspace;
        root.UpdateLayout(new Vector2(1600, 900));

        var bars = FindAll<MenuBar>(workspace);
        Check("the workspace has a menu bar", bars.Count == 1, $"{bars.Count} bars");

        if (bars.Count != 1)
        {
            return;
        }

        var headings = bars[0].Menus.Select(m => m.Text).ToList();

        Check("the editor's menus are in it",
            headings.Contains("File") && headings.Contains("Character") && headings.Contains("View"),
            string.Join(", ", headings));

        // The workspace's own chrome is still there: one bar, not two competing ones.
        Check("alongside the workspace's own",
            headings.Contains("Layout") && headings.Contains("Debug"), string.Join(", ", headings));

        Check("and the editor's come first", headings.IndexOf("File") < headings.IndexOf("Layout"),
            string.Join(", ", headings));

        // Two menus with one name is a coin toss for whoever is looking for something -- and merging two
        // bars is exactly how that happens.
        var duplicates = headings.GroupBy(h => h).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Check("no two menus share a name", duplicates.Count == 0, string.Join(", ", duplicates));

        // Live against the same view model the ImGui menu drives, through the real workspace.
        var view = bars[0].Menus.First(m => m.Text == "View");
        var water = view.Items.First(i => i.Text == "Show Water");
        var before = vm.ShowWater;

        water.Invoke();
        Check("choosing a row through the workspace reaches the editor", vm.ShowWater != before, $"{vm.ShowWater}");
        water.Invoke();

        Check("the workspace still renders in one draw call", root.Render().Batches.Count == 1,
            $"{root.Render().Batches.Count} batches");

        try
        {
            if (File.Exists(scratch))
            {
                File.Delete(scratch);
            }
        }
        catch (IOException)
        {
        }
    }

    private static List<T> FindAll<T>(UiElement? element) where T : UiElement
    {
        var found = new List<T>();
        Walk(element, found);
        return found;
    }

    private static void Walk<T>(UiElement? element, List<T> found) where T : UiElement
    {
        if (element == null)
        {
            return;
        }

        if (element is T match)
        {
            found.Add(match);
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            Walk(element.GetVisualChild(i), found);
        }
    }
}
