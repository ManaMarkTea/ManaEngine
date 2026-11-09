using ManaUI;
using ManaEditor.Code.UI;
using ManaEditor.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// Grid snapping: the numbers it produces, that they are remembered, and that the Edit menu and the
/// preferences panel are two views of the one setting rather than two copies of it.
/// </summary>
public static class EditorSnapChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        static bool Near(float a, float b) => MathF.Abs(a - b) < 0.0001f;

        // ---------------------------------------------------------------- the numbers
        var snap = new EditorSnapSettings { TranslateStep = 0.5f, RotationStepDegrees = 15.0f, ScaleStep = 0.25f };

        Check("snapping is off until asked for", !snap.Enabled);
        Check("off, a position is left alone",
            snap.SnapPosition(new Vector3(1.23f, 4.56f, 7.89f)) == new Vector3(1.23f, 4.56f, 7.89f));
        Check("off, a rotation and a scale are left alone",
            Near(snap.SnapRotationDegrees(37.0f), 37.0f) && Near(snap.SnapScale(1.1f), 1.1f));

        snap.Enabled = true;
        var placed = snap.SnapPosition(new Vector3(1.23f, 4.6f, -0.3f));
        Check("on, each axis lands on the move grid", placed == new Vector3(1.0f, 4.5f, -0.5f), placed.ToString());
        Check("rotation lands on its own step",
            Near(snap.SnapRotationDegrees(37.0f), 30.0f) && Near(snap.SnapRotationDegrees(38.0f), 45.0f));
        Check("scale lands on its own step", Near(snap.SnapScale(1.1f), 1.0f));
        Check("a scale never snaps down to nothing", Near(snap.SnapScale(0.02f), 0.25f), snap.SnapScale(0.02f).ToString());

        snap.TranslateStep = 0.0f;
        Check("a zero or negative step is clamped, not divided by",
            Near(snap.TranslateStep, EditorSnapSettings.MinimumTranslateStep)
            && float.IsFinite(snap.SnapPosition(Vector3.One).X));
        snap.TranslateStep = float.NaN;
        Check("and so is NaN", Near(snap.TranslateStep, EditorSnapSettings.MinimumTranslateStep));

        // ---------------------------------------------------------------- dragging a move handle
        var grid = new EditorSnapSettings { Enabled = true, TranslateStep = 1.0f };
        var pivot = new Vector3(0.3f, 2.0f, 5.0f);

        // The pivot ends up ON a grid line, not a whole step from where it began.
        var along = grid.SnapDragDistance(pivot, Vector3.UnitX, 2.4f);
        Check("a world-aligned drag lands the object on the grid, not a step from where it started",
            Near(pivot.X + along, 3.0f), $"{pivot.X + along}");

        var down = grid.SnapDragDistance(pivot, -Vector3.UnitY, 1.4f);
        Check("and it does so for a handle pointing down an axis",
            Near(pivot.Y - down, 1.0f), $"{pivot.Y - down}");

        var turned = Vector3.Normalize(new Vector3(1.0f, 0.0f, 1.0f));
        Check("a turned handle has no grid line, so only the distance snaps",
            Near(grid.SnapDragDistance(pivot, turned, 2.4f), 2.0f));

        grid.Enabled = false;
        Check("off, a drag is exactly what the pointer asked for",
            Near(grid.SnapDragDistance(pivot, Vector3.UnitX, 2.4f), 2.4f));

        // ---------------------------------------------------------------- remembering it
        var file = Path.Combine(Path.GetTempPath(), $"mana-snap-{Guid.NewGuid():N}.json");

        try
        {
            var unsaved = new EditorSnapSettings { Enabled = true, TranslateStep = 4.0f };
            unsaved.Enabled = false;
            Check("an instance never told to remember writes nothing", !File.Exists(file));

            var first = new EditorSnapSettings();
            first.Restore(file);
            Check("with nothing saved it starts from the defaults",
                !first.Enabled
                && Near(first.TranslateStep, EditorSnapSettings.DefaultTranslateStep)
                && Near(first.RotationStepDegrees, EditorSnapSettings.DefaultRotationStepDegrees)
                && Near(first.ScaleStep, EditorSnapSettings.DefaultScaleStep));

            first.Enabled = true;
            first.TranslateStep = 2.5f;
            first.RotationStepDegrees = 45.0f;
            first.ScaleStep = 0.5f;
            first.GizmoSize = 1.75f;

            var second = new EditorSnapSettings();
            second.Restore(file);
            Check("every setting comes back the next run",
                second.Enabled && Near(second.TranslateStep, 2.5f) && Near(second.RotationStepDegrees, 45.0f)
                && Near(second.ScaleStep, 0.5f),
                $"{second.Enabled} {second.TranslateStep} {second.RotationStepDegrees} {second.ScaleStep}");
            Check("manual gizmo size is remembered", Near(second.GizmoSize, 1.75f));

            File.WriteAllText(file, "{ not json");
            var corrupt = new EditorSnapSettings();
            var threw = false;

            try
            {
                corrupt.Restore(file);
            }
            catch (Exception)
            {
                threw = true;
            }

            Check("a corrupt file falls back to defaults instead of stopping the editor",
                !threw && !corrupt.Enabled && Near(corrupt.TranslateStep, EditorSnapSettings.DefaultTranslateStep));

            File.WriteAllText(file, "{\"enabled\":true,\"translateStep\":-3,\"rotationStepDegrees\":0,\"scaleStep\":0}");
            var hostile = new EditorSnapSettings();
            hostile.Restore(file);
            Check("older preferences keep the original gizmo size", Near(hostile.GizmoSize, 1.0f));
            Check("and hand-edited nonsense is clamped on the way in",
                Near(hostile.TranslateStep, EditorSnapSettings.MinimumTranslateStep)
                && Near(hostile.RotationStepDegrees, EditorSnapSettings.MinimumRotationStepDegrees)
                && Near(hostile.ScaleStep, EditorSnapSettings.MinimumScaleStep));
        }
        finally
        {
            File.Delete(file);
        }

        // ---------------------------------------------------------------- the Edit menu
        var vm = new VoxelEditorViewModel();
        var opened = 0;
        var bar = VoxelEditorMainMenuManaView.Build(vm, null, () => opened++);
        var edit = bar.Menus.First(m => m.Text == "Edit");
        var snapRow = edit.Items.FirstOrDefault(i => i.Text == "Snap to Grid");
        var prefsRow = edit.Items.FirstOrDefault(i => i.Text == "Preferences...");

        Check("Edit offers Snap to Grid and Preferences", snapRow != null && prefsRow != null,
            string.Join(", ", edit.Items.Select(i => i.Text)));

        if (snapRow == null || prefsRow == null)
        {
            return;
        }

        Check("the tick follows the setting", !snapRow.ResolveChecked());
        snapRow.Invoke();
        Check("choosing it turns snapping on", vm.Snap.Enabled && snapRow.ResolveChecked());
        vm.Snap.Enabled = false;
        Check("and the tick follows a change made elsewhere", !snapRow.ResolveChecked());

        prefsRow.Invoke();
        Check("Preferences asks the workspace to open the panel", opened == 1);

        var bare = VoxelEditorMainMenuManaView.Build(new VoxelEditorViewModel());
        Check("with no workspace to open one in, the row is left out rather than shown dead",
            bare.Menus.First(m => m.Text == "Edit").Items.All(i => i.Text != "Preferences..."));

        // ---------------------------------------------------------------- the preferences panel
        var root = new UiRoot(fonts);
        var panelVm = new VoxelEditorViewModel();
        root.Content = PreferencesManaView.Build(root.Theme, panelVm);
        root.UpdateLayout(new Vector2(420, 360));

        var boxes = FindAll<NumericBox>(root.Content);
        var tick = FindAll<CheckBox>(root.Content).FirstOrDefault();

        Check("it has a box for each of move, rotation and scale, and the switch",
            boxes.Count == 3 && tick != null, $"{boxes.Count} boxes");

        if (boxes.Count != 3 || tick == null)
        {
            return;
        }

        Check("the boxes show the current steps",
            Near(boxes[0].Value, panelVm.Snap.TranslateStep)
            && Near(boxes[1].Value, panelVm.Snap.RotationStepDegrees)
            && Near(boxes[2].Value, panelVm.Snap.ScaleStep),
            $"{boxes[0].Value} / {boxes[1].Value} / {boxes[2].Value}");

        boxes[0].Value = 2.0f;
        boxes[1].Value = 30.0f;
        boxes[2].Value = 0.5f;
        Check("typing into a box changes the setting",
            Near(panelVm.Snap.TranslateStep, 2.0f)
            && Near(panelVm.Snap.RotationStepDegrees, 30.0f)
            && Near(panelVm.Snap.ScaleStep, 0.5f),
            $"{panelVm.Snap.TranslateStep} / {panelVm.Snap.RotationStepDegrees} / {panelVm.Snap.ScaleStep}");

        panelVm.Snap.TranslateStep = 8.0f;
        panelVm.Snap.Enabled = true;
        root.UpdateLayout(new Vector2(420, 360));
        Check("and a change from elsewhere shows up in the panel",
            Near(boxes[0].Value, 8.0f) && tick.IsChecked, $"{boxes[0].Value}, ticked {tick.IsChecked}");

        tick.IsChecked = false;
        Check("the panel's own switch is the same one the menu drives", !panelVm.Snap.Enabled);

        var options = ViewportToolbarManaView.SnapOptions(root.Theme, panelVm);
        root.Content = options;
        root.UpdateLayout(new Vector2(360, 350));
        var sizeSlider = FindAll<SliderBox>(options).Single();
        sizeSlider.Value = 2.0f;
        root.UpdateLayout(new Vector2(360, 350));
        Check("the viewport slider updates gizmo size", Near(panelVm.Snap.GizmoSize, 2));
        panelVm.Snap.GizmoSize = float.NaN;
        root.UpdateLayout(new Vector2(360, 350));
        Check("invalid size recovers and is reflected in the slider", Near(sizeSlider.Value, 1));
        panelVm.Snap.Enabled = false;
        FindAll<CheckBox>(options).Single().IsChecked = true;
        Check("viewport snapping uses the same setting as Preferences", panelVm.Snap.Enabled);
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
