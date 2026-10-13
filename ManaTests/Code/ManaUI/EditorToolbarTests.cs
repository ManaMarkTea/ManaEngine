using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaUI;
using ManaVoxel.Code.Tools;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// The editor toolbar and the status bar, which are one subject rather than two: the toolbar used to carry
/// two readouts -- what level is open and what just happened -- that cost it roughly 380px of a row that
/// did not fit in a 1920-wide window, while the left two thirds of the status bar sat empty.
/// <para>
/// What is actually checked here is fit. A toolbar that overruns does not report an error, it silently
/// clips whatever is furthest right, which is the part somebody is using.
/// </para>
/// <para>
/// The voxel tools themselves moved off the strip into the Voxel Tools window, which is checked here too:
/// it comes up with a voxel tool or a voxel node and goes with them.
/// </para>
/// </summary>
public static class EditorToolbarChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var editor = new VoxelEditorViewModel();
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };

        var scene = new ManaScene(context) { Name = "Little Train Yard" };
        var node = new ManaSceneNode(context, Matrix4x4.Identity) { Name = "Station Visitor" };
        scene.Children.Add(node);
        world.Scenes.Add(scene);

        root.Content = ManaUiDockDemo.Attach(
            root, new ManaUiShowcaseViewModel(), null, persist: false, editor: editor, world: world);

        // ---------------------------------------------------------------- the strip fits
        // Measured against the toolbar's own scroller rather than the window, so the check keeps meaning
        // something if the strip is ever moved or re-sized.
        ScrollViewer? Toolbar() => FindAll<ScrollViewer>(root.Content!)
            .FirstOrDefault(viewer => viewer.Bounds.Y < 80.0f
                && viewer.HorizontalScrollBar != ScrollBarVisibility.Disabled);

        foreach (var kind in new[] { VoxelToolKind.Stroke, VoxelToolKind.Circle, VoxelToolKind.Text })
        {
            editor.IsSelectToolActive = false;
            editor.SelectedToolIndex = VoxelToolCatalog.GetToolIndex(kind);

            // Twice: the groups a tool shows and hides change what the row measures to, and a polled
            // visibility binding is read during layout, so the first pass is the one that sees the change.
            root.UpdateLayout(new Vector2(1920, 1080));
            root.UpdateLayout(new Vector2(1920, 1080));

            var toolbar = Toolbar();
            Check($"the toolbar fits in a 1920-wide window with the {kind} tool active",
                toolbar != null && !toolbar.CanScrollHorizontally,
                toolbar == null ? "no toolbar scroller" : $"overruns by {toolbar.ScrollableSize.X:0}px");
        }

        // ---------------------------------------------------------------- and stays reachable when it does not
        root.UpdateLayout(new Vector2(420, 700));
        root.UpdateLayout(new Vector2(420, 700));

        var narrow = Toolbar();
        Check("a window too narrow for the row scrolls it rather than clipping it away",
            narrow is { CanScrollHorizontally: true },
            $"{narrow?.ScrollableSize.X:0}px of overflow");

        Check("and the bar itself is hidden, so it costs the strip no height",
            narrow?.HorizontalScrollBar == ScrollBarVisibility.Hidden,
            $"{narrow?.HorizontalScrollBar}");

        root.UpdateLayout(new Vector2(1920, 1080));
        root.UpdateLayout(new Vector2(1920, 1080));

        // ---------------------------------------------------------------- the readouts moved
        // Found by its shape rather than by a Y threshold: scrolled content keeps its real coordinates, so
        // every row of the Import panel below the fold also sits past the status bar's top edge.
        Border StatusBar() => FindAll<Border>(root.Content!)
            .First(border => MathF.Abs(border.Bounds.Bottom - root.Size.Y) < 0.5f
                && MathF.Abs(border.Bounds.Width - root.Size.X) < 0.5f
                && border.Bounds.Height < 40.0f);

        List<string> StatusBarText() => FindAll<TextBlock>(StatusBar())
            .Select(text => text.Text)
            .ToList();

        List<string> ToolbarText() => FindAll<TextBlock>(root.Content!)
            .Where(text => text.Bounds.Y < 80.0f)
            .Select(text => text.Text)
            .ToList();

        editor.Levels.SetStatus("Opened Little Train Yard (60 objects)");
        root.UpdateLayout(new Vector2(1920, 1080));

        Check("the status bar says what just happened",
            StatusBarText().Any(text => text.Contains("Little Train Yard", StringComparison.Ordinal)),
            string.Join(" | ", StatusBarText()));

        Check("and the toolbar no longer repeats it",
            !ToolbarText().Any(text => text.Contains("Little Train Yard", StringComparison.Ordinal)),
            string.Join(" | ", ToolbarText()));

        // ---------------------------------------------------------------- the selection is always readable
        Check("with nothing selected the status bar says so",
            StatusBarText().Any(text => text.Contains("Nothing selected", StringComparison.Ordinal)),
            string.Join(" | ", StatusBarText()));

        world.Selection.SelectedNode = node;
        root.UpdateLayout(new Vector2(1920, 1080));

        Check("and names what is selected once something is",
            StatusBarText().Any(text => text.Contains("Station Visitor", StringComparison.Ordinal)),
            string.Join(" | ", StatusBarText()));

        // ---------------------------------------------------------------- the transform modes need a target
        // Three icon buttons where three word buttons used to be. They are found by being the only 24x24
        // buttons in the strip that turn on and off with the selection.
        world.Selection.SelectedNode = null;
        root.UpdateLayout(new Vector2(1920, 1080));

        var disabled = IconButtons(root).Count(button => !button.IsEnabled);
        Check("the transform mode buttons are disabled with nothing selected", disabled >= 3, $"{disabled}");

        world.Selection.SelectedNode = node;
        root.UpdateLayout(new Vector2(1920, 1080));

        var stillDisabled = IconButtons(root).Count(button => !button.IsEnabled);
        Check("and enabled again once something is selected",
            stillDisabled < disabled, $"{stillDisabled} still disabled, was {disabled}");

        // ---------------------------------------------------------------- one gap scale down the row
        // The strip is assembled from two files and five groups, and each had grown its own spacing: icon
        // buttons at 2px next to identical icon buttons at 4px, captions 4px from one control and 6px from
        // the next, and a "group boundary" that measured 13px, 20px or 21px depending on which of the two
        // divider implementations drew it. Nothing here is about a specific number -- only that there are
        // exactly two of them.
        var withinGroup = root.Theme.Spacing;

        // Stack gap, divider margin, the divider's own hairline, margin, stack gap.
        var betweenGroups = root.Theme.Spacing * 4.0f + 1.0f;

        var row = Row(root);
        Check("the toolbar has a row of controls to measure", row.Count > 8, $"{row.Count} controls");

        var odd = new List<string>();
        for (var i = 1; i < row.Count; i++)
        {
            var gap = row[i].Bounds.X - row[i - 1].Bounds.Right;

            // Only gaps inside the scrolling group matter; the work plane is pinned past the row's end.
            if (gap > betweenGroups + 1.0f)
            {
                continue;
            }

            if (MathF.Abs(gap - withinGroup) > 0.6f && MathF.Abs(gap - betweenGroups) > 0.6f)
            {
                odd.Add($"{gap:0.0}px before {Describe(row[i])}");
            }
        }

        Check("every gap in the row is either the within-group or the between-group one",
            odd.Count == 0, odd.Count == 0
                ? $"{withinGroup:0} within, {betweenGroups:0} between"
                : string.Join("; ", odd));

        // The three full-width bars stack directly on top of each other, so their content has to start and
        // end at the same place or the window reads as slightly crooked.
        var toolbarStrip = FindAll<ScrollViewer>(root.Content!)
            .First(viewer => viewer.Bounds.Y < 80.0f && viewer.HorizontalScrollBar != ScrollBarVisibility.Disabled);

        // The row no longer runs to the right edge -- nothing is pinned there since the work plane moved into
        // the Voxel Tools window -- so only its start is held to the bars' inset.
        var leftInset = row[0].Bounds.X;
        Check("the row starts at the inset the menu and status bars use",
            MathF.Abs(leftInset - 8.0f) < 0.6f, $"{leftInset:0.0}px");

        RunVoxelToolsWindowChecks(root, editor, world, context, Check);
    }

    private static void RunVoxelToolsWindowChecks(
        UiRoot root, VoxelEditorViewModel editor, ManaWorld world, ManaContext context, Action<string, bool, string> check)
    {
        UiWindow? Window() => ManaUiDockDemo.LastDragController?.FindFloating(ManaUiDockDemo.VoxelToolsPanelId);
        void Frame()
        {
            root.UpdateLayout(new Vector2(1920, 1080));
            root.UpdateLayout(new Vector2(1920, 1080));
        }

        world.Selection.SelectedNode = null;
        editor.IsSelectToolActive = true;
        Frame();
        check("the voxel tools window is not up while selecting", Window() == null, "");
        check("and the toolbar carries no voxel tool buttons, only the one that brings them up",
            // The seven it always had, plus the measuring tool's ruler.
            IconButtons(root).Count(button => button.Bounds.X > 0.0f) <= 8, $"{IconButtons(root).Count} icon buttons");

        editor.IsSelectToolActive = false;
        Frame();
        var window = Window();
        check("taking up a voxel tool floats the voxel tools window", window != null, "");
        check("with the tools in it", window != null && FindAll<Button>(window).Count(button => MathF.Abs(button.Bounds.Width - ToolbarIcons.ButtonSize) < 0.5f) >= 10,
            window == null ? "" : $"{FindAll<Button>(window).Count} buttons");

        editor.IsSelectToolActive = true;
        Frame();
        check("and going back to selecting closes it", Window() == null, "");

        var scene = world.Scenes[0];
        var voxels = new ManaVoxelsNode(context, Matrix4x4.Identity) { Name = "Loaded Voxels" };
        scene.Children.Add(voxels);
        world.Selection.SelectedNode = voxels;
        Frame();
        check("selecting a voxel node brings the window up", Window() != null, "");
        check("and makes that node the one the tools draw into, though the editor did not make it",
            ReferenceEquals(editor.VoxelNode, voxels), editor.VoxelNode?.Name ?? "none");

        editor.IsSelectToolActive = false;
        Window()?.Close();
        Frame();
        check("closing the window puts the tool down", editor.IsSelectToolActive, "");
        check("and it stays closed while the same node is selected", Window() == null, "");

        world.Selection.SelectedNode = null;
        Frame();
        world.Selection.SelectedNode = voxels;
        Frame();
        check("selecting the node again brings it back", Window() != null, "");
        world.Selection.SelectedNode = null;
        Frame();
    }

    /// <summary>
    /// The toolbar's controls, left to right. Label blocks inside a button are skipped -- they are the
    /// button's own content, not something spaced against its neighbours.
    /// </summary>
    private static List<UiElement> Row(UiRoot root)
    {
        return FindAll<UiElement>(root.Content!)
            .Where(e => e.Bounds.Y >= 28.0f && e.Bounds.Bottom <= 70.0f && e.Bounds.Width > 0.5f)
            .Where(e => e is Button or ComboBox or NumericBox or TextBox or Slider or ColorField or TextBlock)
            .Where(e => !HasAncestor<Button>(e))
            .OrderBy(e => e.Bounds.X)
            .ToList();
    }

    private static string Describe(UiElement element) =>
        element is TextBlock text ? $"\"{text.Text}\"" : element.GetType().Name;

    private static bool HasAncestor<T>(UiElement element) where T : UiElement
    {
        for (var parent = element.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is T)
            {
                return true;
            }
        }

        return false;
    }

    private static List<Button> IconButtons(UiRoot root) => FindAll<Button>(root.Content!)
        .Where(button => button.Bounds.Y < 80.0f && MathF.Abs(button.Bounds.Width - ToolbarIcons.ButtonSize) < 0.5f)
        .ToList();

    private static List<T> FindAll<T>(UiElement element) where T : UiElement
    {
        var found = new List<T>();

        void Recurse(UiElement current)
        {
            if (current is T typed)
            {
                found.Add(typed);
            }

            foreach (var child in current.VisualChildren)
            {
                Recurse(child);
            }
        }

        Recurse(element);
        return found;
    }
}
