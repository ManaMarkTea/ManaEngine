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
        Check("it has the same top-level menus", headings.SequenceEqual(new[] { "File", "Play", "Character", "View" }),
            string.Join(", ", headings));

        Check("the bar is one row tall", bar.Bounds.Height <= root.Theme.ControlHeight + 8.0f,
            $"{bar.Bounds.Height:F0}px");

        // ---------------------------------------------------------------- File
        var file = bar.Menus.First(m => m.Text == "File");
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
        var view = bar.Menus.First(m => m.Text == "View");
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

        // ---------------------------------------------------------------- Play
        // Every row here drives LevelViewModel, which has no host until the game mode wires one up. The
        // point of these checks is that a menu built without one is inert rather than broken: a null host
        // must disable the rows and swallow the invokes, because that is exactly the state the menu is in
        // for the whole of startup, when it is on screen and clickable.
        var play = bar.Menus.First(m => m.Text == "Play");
        var playRow = play.Items.First(i => i.Text == "Play");
        var stopRow = play.Items.First(i => i.Text == "Stop");

        Check("Play offers play, play-from-camera, a new window and stop",
            play.Items.Count(i => !i.IsSeparator && !i.IsLabel) == 4,
            string.Join(", ", play.Items.Where(i => !i.IsSeparator).Select(i => i.Text)));

        Check("the shortcuts are on the rows, so F5 is discoverable",
            playRow.ResolveShortcut() == "F5" && stopRow.ResolveShortcut() == "Esc",
            $"{playRow.ResolveShortcut()} / {stopRow.ResolveShortcut()}");

        Check("with no host attached, playing is not offered",
            !playRow.ResolveEnabled() && !stopRow.ResolveEnabled());

        Check("and invoking one anyway does nothing rather than throwing",
            !Throws(() => playRow.Invoke()));

        // ---------------------------------------------------------------- File: levels
        var newLevel = file.Items.FirstOrDefault(i => i.Text == "New Level");
        var reload = file.Items.FirstOrDefault(i => i.Text == "Reload Level");
        Check("File offers the level verbs",
            newLevel != null && file.Items.Any(i => i.Text == "Save Level") && reload != null,
            string.Join(", ", file.Items.Select(i => i.Text)));

        Check("reload is offered only once a level has been opened or saved",
            reload?.ResolveEnabled() == false, $"{reload?.ResolveEnabled()}");

        Check("Save Level shows which level it would write",
            file.Items.First(i => i.Text == "Save Level").ResolveShortcut().Contains("unsaved", StringComparison.Ordinal),
            file.Items.First(i => i.Text == "Save Level").ResolveShortcut());

        // ---------------------------------------------------------------- labels and hints
        var character = bar.Menus.First(m => m.Text == "Character");
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

        var debugMenu = bars[0].Menus.First(m => m.Text == "Debug");
        var sceneDebug = debugMenu.Items.FirstOrDefault(item => item.Text == "Scene");
        Check("scene visualizations live in the Debug menu",
            sceneDebug?.Items.Any(item => item.Text == "Track visual bounds") == true
            && sceneDebug.Items.Any(item => item.Text == "Track pivots")
            && sceneDebug.Items.Any(item => item.Text == "Track snap points")
            && sceneDebug.Items.Any(item => item.Text == "Selected collider bounds"));
        Check("scene visualizations start off",
            !vm.ShowTrackBounds
            && !vm.ShowTrackPivots
            && !vm.ShowTrackSnapPoints
            && !vm.ShowSelectedColliderBounds);

        sceneDebug?.Items.First(item => item.Text == "Track visual bounds").Invoke();
        Check("a scene debug menu row reaches the editor", vm.ShowTrackBounds, $"{vm.ShowTrackBounds}");
        debugMenu.Items.First(item => item.Text == "All debug off").Invoke();
        Check("all debug off clears scene visualization too", !vm.ShowTrackBounds, $"{vm.ShowTrackBounds}");

        Check("the transform gizmo starts visible in move mode",
            vm.ShowTransformGizmo && vm.TransformGizmoMode == SceneTransformGizmoMode.Translate,
            $"{vm.ShowTransformGizmo}, {vm.TransformGizmoMode}");

        Check("the workspace still renders in one draw call", root.Render().Batches.Count == 1,
            $"{root.Render().Batches.Count} batches");

        // Each tool panel is its own dock item beside the outliner rather than a tab inside one window, so
        // any of them can be pulled out and put somewhere else.
        var docked = FindAll<DockHost>(workspace)
            .SelectMany(host => host.AllItems())
            .Select(item => item.Title)
            .ToList();

        foreach (var panel in new[] { "Placement", "Physics", "Voxel Stats", "About" })
        {
            Check($"{panel} is its own dock panel", docked.Contains(panel), string.Join(", ", docked));
        }

        // No world in this fixture, and a panel that needs one is skipped rather than docked empty -- the
        // same rule that keeps the voxel palette out of a session with no editor.
        Check("a panel needing a world is left out when there is none",
            !docked.Contains("Outliner") && !docked.Contains("Inspector"), string.Join(", ", docked));

        Check("and they arrive together rather than one preset knowing about them",
            docked.Count(d => d is "Placement" or "Physics" or "Voxel Stats" or "About") == 4,
            string.Join(", ", docked));

        // The merge list and the factory have to agree. When they were written separately they silently
        // stopped agreeing -- six panels existed in the factory and not in the list, so upgrading a saved
        // workspace found nothing new to add and they appeared only after a reset. Every id the workspace
        // docks must be one the merge knows about, or it goes missing for exactly the people who already
        // use the editor.
        var known = ManaUiDockDemo.PanelIds.ToHashSet(StringComparer.Ordinal);

        var missingFromList = docked
            .Select(TitleToId)
            .Where(id => id != null && !known.Contains(id!))
            .ToList();

        Check("every docked panel is one the upgrade merge knows about",
            missingFromList.Count == 0,
            missingFromList.Count == 0 ? string.Join(", ", known) : string.Join(", ", missingFromList));

        // A menu that runs off the edge of its container is a menu nobody can open. The bar was pinned to
        // the right column's width, so the last two -- Panels and Appearance, the ones added most recently
        // -- were cut off entirely and could not be reached at all.
        var barHeadings = FindAll<MenuBarHeading>(bars[0]);

        Check("every menu has a heading drawn for it",
            barHeadings.Count == bars[0].Menus.Count, $"{barHeadings.Count} of {bars[0].Menus.Count}");

        var offScreen = barHeadings
            .Where(h => h.Bounds.Right > root.Size.X + 0.5f || h.Bounds.Width <= 0.0f)
            .Select(h => h.Menu.Text)
            .ToList();

        Check("and none of them run off the screen",
            offScreen.Count == 0,
            offScreen.Count == 0
                ? $"bar ends at {barHeadings.Max(h => h.Bounds.Right):0} of {root.Size.X:0}"
                : string.Join(", ", offScreen));

        Check("Panels is reachable", headings.Contains("Panels"), string.Join(", ", headings));

        // The Panels menu is what makes a closed panel reachable again, and a newly added one findable
        // without waiting for a version merge. It draws from the same list, so a panel missing a title would
        // appear in it as a raw id.
        var untitled = ManaUiDockDemo.PanelIds
            .Where(id => !ManaUiDockDemo.PanelTitles.ContainsKey(id))
            .ToList();

        Check("every panel has a name for the Panels menu",
            untitled.Count == 0,
            untitled.Count == 0 ? $"{ManaUiDockDemo.PanelTitles.Count} named" : string.Join(", ", untitled));

        // And the other way, or the menu offers something the factory cannot build.
        var unknownTitles = ManaUiDockDemo.PanelTitles.Keys
            .Where(id => !known.Contains(id))
            .ToList();

        Check("and every named panel is one the workspace can build",
            unknownTitles.Count == 0,
            unknownTitles.Count == 0 ? "matched" : string.Join(", ", unknownTitles));

        // ---------------------------------------------------------------- presets
        // Every preset names the panels it arranges, so a newly added one is in none of them and choosing a
        // preset drops it. That is how Import, Search and the blur test card went missing for anyone who
        // reset their layout -- and it looks exactly like the panel was never added.
        var layout = bars[0].Menus.FirstOrDefault(m => m.Text == "Layout");
        Check("there is a Layout menu of presets", layout is { Items.Count: > 0 }, $"{layout?.Items.Count ?? 0}");

        if (layout != null)
        {
            // Compared against each other rather than against the full id list: some panels need a world,
            // which this suite has none of, so they cannot be built here at all. What must hold is that no
            // preset drops something another preset keeps -- which is precisely the bug, and needs no
            // knowledge of what is buildable.
            HashSet<string>? expected = null;
            var reference = string.Empty;

            foreach (var presetItem in layout.Items.Where(i => !i.IsSeparator).ToList())
            {
                presetItem.Invoke();
                root.UpdateLayout(new Vector2(1600, 900));

                var placed = FindAll<DockHost>(workspace)
                    .SelectMany(host => host.AllItems())
                    .Select(item => item.Id!)
                    .ToHashSet(StringComparer.Ordinal);

                if (expected == null)
                {
                    expected = placed;
                    reference = presetItem.Text;

                    // The panels the presets were missing. Every preset hand-listed its arrangement, so
                    // these three were in none of them and choosing any preset silently dropped them.
                    foreach (var id in new[] { "import", "search", "blurtest" })
                    {
                        Check($"a preset places {id}", placed.Contains(id), string.Join(", ", placed));
                    }

                    continue;
                }

                var dropped = expected.Where(id => !placed.Contains(id)).ToList();

                Check($"the {presetItem.Text} preset keeps what {reference} does",
                    dropped.Count == 0,
                    dropped.Count == 0 ? $"{placed.Count} panels" : $"dropped {string.Join(", ", dropped)}");
            }
        }

        // Not covered here: that a workspace saved before these panels existed picks them up on upgrade.
        // That path is a version mismatch inside Build, and every attempt to seed it from a file ended up
        // proving nothing -- an unparseable seed falls back to the preset, which places every panel and
        // makes the check pass whatever the version says. The bump to LayoutVersion is reasoned, not
        // verified; the generic merge behaviour it relies on is covered in the persistence suite.

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

    /// <summary>
    /// Panel titles are what a dock exposes; ids are what a layout stores. This is the one place the two
    /// are mapped, and only for checking that the merge list is complete.
    /// </summary>
    private static string? TitleToId(string title) => title switch
    {
        "Toolbar" => "tools",
        "Import" => "import",
        "Search" => "search",
        "Blur Test" => "blurtest",
        "Placement" => "placement",
        "Physics" => "physics",
        "Voxel Stats" => "voxelstats",
        "Lighting" => "lighting",
        "Outliner" => "outliner",
        "Inspector" => "inspector",
        "ManaUI" => "showcase",
        "About" => "about",
        "World Perf" => "worldperf",
        "Debug" => "debug",
        _ => null
    };

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

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch
        {
            return true;
        }
    }
}
