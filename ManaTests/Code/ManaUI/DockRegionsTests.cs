using Mana;
using Mana.Code;
using ManaUI;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// The workspace's outer chrome: the full-width, unrounded menu bar; the top editor toolbar strip
/// gets to itself; the bottom strip Import gets to itself, spanning the left column and the viewport but
/// not the right column; the status bar (with the performance readout riding in it) along the very
/// bottom; the columns sitting flush under the toolbar; and the retirement of the Layers demo panel and
/// its menu.
/// </summary>
public static class DockRegionsChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var scratch = Path.Combine(Path.GetTempPath(), $"manaui-dockregions-{Guid.NewGuid():N}.json");

        try
        {
            // ---------------------------------------------------------------- chrome position
            var root = new UiRoot(fonts);
            var vm = new VoxelEditorViewModel();

            // A bare world too, so world-only panels (Outliner, Inspector) populate the right column under
            // Unity below rather than leaving it collapsed for want of anything to show.
            var world = new ManaWorld();
            _ = new ManaContext(null!, null!, world) { DeferGpuUploads = true };

            root.Content = ManaUiDockDemo.Attach(
                root, new ManaUiShowcaseViewModel(), null, persist: false, editor: vm, world: world);
            root.UpdateLayout(new Vector2(1920, 1080));

            var menuBar = FindAll<MenuBar>(root.Content!).Single(bar => bar.Menus.Any(menu => menu.Text == "File"));
            Check("the menu bar is present", menuBar != null);

            var menuBorder = FindAncestorBorder(menuBar);
            Check("the menu sits at the left edge, under the window's own title bar",
                menuBorder != null && menuBorder.Bounds.X < 1.0f && menuBorder.Bounds.Y < 1.0f,
                menuBorder?.Bounds.ToString() ?? "not found");
            Check("the menu spans the full width",
                menuBorder != null && menuBorder.Bounds.Width > 1900.0f, menuBorder?.Bounds.ToString() ?? "not found");
            Check("the menu is not rounded",
                menuBorder != null && menuBorder.CornerRadius.Equals(CornerRadius.Zero),
                menuBorder?.CornerRadius.ToString() ?? "not found");

            // HoverRevealer is the strip's always-visible frame; its own Sparklines only appear inside a
            // hover popup, positioned wherever the popup lands rather than at the strip's own anchor.
            var strip = FindFirst<HoverRevealer>(root.Content!);
            Check("the performance strip lives in the status bar along the bottom",
                strip != null && strip.Bounds.Bottom > 1050.0f && strip.Bounds.Right > 1900.0f,
                strip?.Bounds.ToString() ?? "not found");

            // ---------------------------------------------------------------- Layers is gone
            Check("layers is no longer a known panel", !ManaUiDockDemo.PanelIds.Contains("layers"), "removed");
            Check("stats is no longer a known panel", !ManaUiDockDemo.PanelIds.Contains("stats"), "removed");
            Check("the Layers menu is gone from the bar",
                !menuBar!.Menus.Any(m => m.Text == "Layers"),
                string.Join(", ", menuBar.Menus.Select(m => m.Text)));

            // ---------------------------------------------------------------- four dock strips
            // Walk order matches the Grid's own child order: top, left, right, bottom.
            var hosts = FindAll<DockHost>(root.Content!);
            Check("there are four dock hosts", hosts.Count == 4, $"{hosts.Count}");

            var topHost = hosts[0];
            var leftHost = hosts[1];
            var rightHost = hosts[2];
            var bottomHost = hosts[3];

            Check("voxel tools docks into the top strip by default",
                topHost.AllItems().Any(i => i.Id == "tools"),
                string.Join(", ", topHost.AllItems().Select(i => i.Id)));
            Check("import docks into the bottom strip by default",
                bottomHost.AllItems().Any(i => i.Id == "import"),
                string.Join(", ", bottomHost.AllItems().Select(i => i.Id)));

            Check("neither is also left in a column",
                leftHost.AllItems().Concat(rightHost.AllItems()).All(i => i.Id != "tools" && i.Id != "import"),
                string.Join(", ", leftHost.AllItems().Concat(rightHost.AllItems()).Select(i => i.Id)));

            // ---------------------------------------------------------------- columns flush under the toolbar
            // The default RightColumn preset leaves the left column empty (collapsed to 0x0, an
            // undefined position), so Unity is chosen first to give both columns real content to be flush
            // about.
            var menuForFlushCheck = FindAll<MenuBar>(root.Content!).Single(bar => bar.Menus.Any(menu => menu.Text == "File"));
            var layoutMenuForFlushCheck = menuForFlushCheck!.Menus.First(item => item.Text == "Layout");
            layoutMenuForFlushCheck.Items.First(item => item.Text == "Unity").Invoked?.Invoke();
            root.UpdateLayout(new Vector2(1920, 1080));

            Check("Unity puts the Hierarchy (outliner) on the left, as Unity does",
                leftHost.AllItems().Any(i => i.Id == "outliner"),
                string.Join(", ", leftHost.AllItems().Select(i => i.Id)));
            Check("and the Inspector on the right",
                rightHost.AllItems().Any(i => i.Id == "inspector"),
                string.Join(", ", rightHost.AllItems().Select(i => i.Id)));

            // All four docks are ResizablePanels -- the top toolbar strip's handle is switched off
            // (AllowResize = false, see ManaUiDockDemo's topPanel) rather than the panel itself being
            // dropped, since DockHost/DockTabGroup always measure to whatever height they are given rather
            // than to their own content.
            var panels = FindAll<ResizablePanel>(root.Content!);
            var topPanel = panels[0];
            var leftPanel = panels[1];
            var rightPanel = panels[2];
            var bottomPanel = panels[3];

            Check("all four docks have content to check flushness with", panels.Count == 4
                && leftPanel.Visibility == Visibility.Visible && rightPanel.Visibility == Visibility.Visible
                && bottomPanel.Visibility == Visibility.Visible,
                string.Join(" | ", panels.Select(p => $"{p.Edge}={p.Visibility}")));

            Check("the left column starts exactly where the toolbar strip ends",
                MathF.Abs(leftPanel.Bounds.Y - topPanel.Bounds.Bottom) < 0.5f,
                $"toolbar bottom {topPanel.Bounds.Bottom:0.0}, left column top {leftPanel.Bounds.Y:0.0}");
            Check("the right column starts exactly where the toolbar strip ends",
                MathF.Abs(rightPanel.Bounds.Y - topPanel.Bounds.Bottom) < 0.5f,
                $"toolbar bottom {topPanel.Bounds.Bottom:0.0}, right column top {rightPanel.Bounds.Y:0.0}");

            // ---------------------------------------------------------------- Unreal-shaped bottom strip
            // The right column (Outliner/Details) runs the full height down to the status bar, unbroken by
            // the Import strip -- which spans the left column and the viewport instead, exactly like
            // Unreal's own Content Browser sitting beside, not under, the World Outliner column.
            Check("the right column reaches all the way down to the status bar",
                MathF.Abs(rightPanel.Bounds.Bottom - leftPanel.Bounds.Bottom - bottomPanel.Bounds.Height) < 0.5f,
                $"right column bottom {rightPanel.Bounds.Bottom:0.0}, left+import bottom "
                + $"{leftPanel.Bounds.Bottom + bottomPanel.Bounds.Height:0.0}");
            Check("the Import strip stops before the right column, not under it",
                bottomPanel.Bounds.Right <= rightPanel.Bounds.X + 0.5f,
                $"import right edge {bottomPanel.Bounds.Right:0.0}, right column left edge {rightPanel.Bounds.X:0.0}");
            Check("and starts below the left column",
                MathF.Abs(bottomPanel.Bounds.Y - leftPanel.Bounds.Bottom) < 0.5f,
                $"import top {bottomPanel.Bounds.Y:0.0}, left column bottom {leftPanel.Bounds.Bottom:0.0}");

            // ---------------------------------------------------------------- migrating an older workspace
            // Simulates a workspace saved before the top/bottom strips existed: Voxel Tools and Import
            // docked in columns like any other panel, at an old version so the merge path runs on load.
            var oldLayout = new ManaUiWorkspaceLayout
            {
                Version = 1,
                Preset = WorkspacePreset.RightColumn,
                Left = new DockLayoutNode { Type = "tabs", Items = new List<string> { "tools" } },
                Right = new DockLayoutNode { Type = "tabs", Items = new List<string> { "import", "about" } }
            };
            File.WriteAllText(scratch, oldLayout.ToJson());

            var migratedRoot = new UiRoot(fonts);
            migratedRoot.Content = ManaUiDockDemo.Attach(
                migratedRoot, new ManaUiShowcaseViewModel(), null, persist: true,
                editor: new VoxelEditorViewModel(), layoutPath: scratch);
            migratedRoot.UpdateLayout(new Vector2(1920, 1080));

            var migratedHosts = FindAll<DockHost>(migratedRoot.Content!);
            var migratedTop = migratedHosts[0];
            var migratedLeft = migratedHosts[1];
            var migratedRight = migratedHosts[2];
            var migratedBottom = migratedHosts[3];

            Check("an old workspace's Voxel Tools moves to the top strip on load",
                migratedTop.AllItems().Any(i => i.Id == "tools"),
                string.Join(", ", migratedTop.AllItems().Select(i => i.Id)));
            Check("and is no longer left behind in the left column",
                migratedLeft.AllItems().All(i => i.Id != "tools"),
                string.Join(", ", migratedLeft.AllItems().Select(i => i.Id)));

            Check("an old workspace's Import moves to the bottom strip on load",
                migratedBottom.AllItems().Any(i => i.Id == "import"),
                string.Join(", ", migratedBottom.AllItems().Select(i => i.Id)));
            Check("and is no longer left behind in the right column",
                migratedRight.AllItems().All(i => i.Id != "import"),
                string.Join(", ", migratedRight.AllItems().Select(i => i.Id)));

            Check("about, which was tabbed with it, is undisturbed",
                migratedRight.AllItems().Any(i => i.Id == "about"),
                string.Join(", ", migratedRight.AllItems().Select(i => i.Id)));
        }
        finally
        {
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
    }

    /// <summary>The Border directly wrapping an element, for its own on-screen position.</summary>
    private static Border? FindAncestorBorder(UiElement? target)
    {
        if (target == null)
        {
            return null;
        }

        for (var current = target.Parent; current != null; current = current.Parent)
        {
            if (current is Border border)
            {
                return border;
            }
        }

        return null;
    }

    private static T? FindFirst<T>(UiElement element) where T : UiElement
    {
        if (element is T typed)
        {
            return typed;
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            var found = FindFirst<T>(element.GetVisualChild(i));
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

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
