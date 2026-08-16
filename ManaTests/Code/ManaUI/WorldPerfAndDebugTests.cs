using Mana;
using ManaUI;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// The ManaUI ports of ManaWorld's ImGui "World Perf" window and VoxelEditorPanelsView's "Debug" tab --
/// the two panels ported so ImGui's per-frame timing dictionaries and picking-ray readout would not be
/// lost when ImGui comes out of the layout.
/// <para>
/// Neither of these can be exercised through a real game mode headlessly (that needs a window and a GPU),
/// so what is checked here is the part that can be: that the panels build and lay out without throwing
/// against a bare <see cref="ManaWorld"/> / <see cref="VoxelEditorViewModel"/>, and that the live values
/// pushed into them actually reach the rendered text.
/// </para>
/// </summary>
public static class WorldPerfAndDebugChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        RunWorldPerf(fonts, Check);
        RunDebug(fonts, Check);
    }

    private static void RunWorldPerf(ManaFontManager fonts, Action<string, bool, string> Check)
    {
        var world = new ManaWorld();
        var root = new UiRoot(fonts);
        var theme = root.Theme;

        world.RecordFrameStat("GPU: Draw Meshes", TimeSpan.FromMilliseconds(2.5));
        world.RecordFrameStatText("Planet patches resident", "42");

        UiElement view;
        try
        {
            view = WorldPerfManaView.Build(theme, world);
            Check("the world perf panel builds", true, string.Empty);
        }
        catch (Exception ex)
        {
            Check("the world perf panel builds", false, ex.Message);
            return;
        }

        root.Content = view;
        root.UpdateLayout(new Vector2(360, 600));

        var labels = FindAll<TextBlock>(view).Select(t => t.Text).ToList();

        Check("mesh count reaches the panel", labels.Any(t => t.Contains("Meshes rendered", StringComparison.Ordinal)),
            string.Join(" | ", labels));

        Check("a pushed frame stat reaches the panel",
            labels.Any(t => t.Contains("GPU: Draw Meshes", StringComparison.Ordinal) && t.Contains("2.50ms", StringComparison.Ordinal)),
            string.Join(" | ", labels));

        Check("a pushed streaming stat reaches the panel",
            labels.Any(t => t.Contains("Planet patches resident: 42", StringComparison.Ordinal)),
            string.Join(" | ", labels));

        // Overwriting the same name updates the row in place rather than adding a second one -- the
        // whole point of keying these by name instead of appending to a log.
        world.RecordFrameStat("GPU: Draw Meshes", TimeSpan.FromMilliseconds(9.0));
        root.UpdateLayout(new Vector2(360, 600));
        var updated = FindAll<TextBlock>(view).Select(t => t.Text).ToList();

        Check("an updated frame stat replaces the old value",
            updated.Any(t => t.Contains("9.00ms", StringComparison.Ordinal))
                && !updated.Any(t => t.Contains("2.50ms", StringComparison.Ordinal)),
            string.Join(" | ", updated));
    }

    private static void RunDebug(ManaFontManager fonts, Action<string, bool, string> Check)
    {
        var viewModel = new VoxelEditorViewModel();
        var root = new UiRoot(fonts);
        var theme = root.Theme;

        viewModel.DebugRayOrigin = new Vector3(1.0f, 2.0f, 3.0f);
        viewModel.DebugRayDirection = new Vector3(0.0f, -1.0f, 0.0f);

        UiElement view;
        try
        {
            view = DebugManaView.Build(theme, viewModel);
            Check("the debug panel builds", true, string.Empty);
        }
        catch (Exception ex)
        {
            Check("the debug panel builds", false, ex.Message);
            return;
        }

        root.Content = view;
        root.UpdateLayout(new Vector2(360, 600));

        var sparklines = FindAll<Sparkline>(view);
        Check("the panel offers a frame-time and a memory graph", sparklines.Count == 2, $"{sparklines.Count} sparklines");

        var labels = FindAll<TextBlock>(view).Select(t => t.Text).ToList();
        Check("the ray readout reflects the live origin", labels.Any(t => t.Contains("1, 2, 3", StringComparison.Ordinal)),
            string.Join(" | ", labels));

        Check("the ray readout reflects the live direction", labels.Any(t => t.Contains("0, -1, 0", StringComparison.Ordinal)),
            string.Join(" | ", labels));

        viewModel.DebugRayOrigin = new Vector3(5.0f, 5.0f, 5.0f);
        root.UpdateLayout(new Vector2(360, 600));
        var updated = FindAll<TextBlock>(view).Select(t => t.Text).ToList();
        Check("the ray readout follows the ray every frame rather than freezing at build time",
            updated.Any(t => t.Contains("5, 5, 5", StringComparison.Ordinal)),
            string.Join(" | ", updated));
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
