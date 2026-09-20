using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The editor's preferences panel, opened from Edit &gt; Preferences. Today that is grid snapping: how
    /// big a grid cell is for moving, and the step for rotating and scaling. Each value is saved as it
    /// changes (see <see cref="EditorSnapSettings"/>), so there is nothing to apply or cancel.
    /// </summary>
    public static class PreferencesManaView
    {
        public static UiElement Build(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            var snap = viewModel.Snap;

            var enabled = new CheckBox("Snap to grid") { IsChecked = snap.Enabled };
            enabled.CheckedChanged += (_, value) =>
            {
                if (!enabled.IsApplyingBinding)
                {
                    snap.Enabled = value;
                }
            };
            enabled.BindPolled(c => c.IsChecked, () => snap.Enabled);

            return new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                new TextBlock()
                    .Text("Grid snapping")
                    .FontFamily(theme.FontFamilyMedium)
                    .Foreground(theme.Text),

                new TextBlock()
                    .Text("Applies to placing models and to the move, rotate and scale gizmos.")
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .Wrap(),

                enabled,

                Row(theme, "Move grid", "units",
                    Step(snap.TranslateStep, 0.25f, EditorSnapSettings.MinimumTranslateStep, 100.0f, 2,
                        () => snap.TranslateStep, value => snap.TranslateStep = value)),

                Row(theme, "Rotation step", "degrees",
                    Step(snap.RotationStepDegrees, 5.0f, EditorSnapSettings.MinimumRotationStepDegrees, 180.0f, 1,
                        () => snap.RotationStepDegrees, value => snap.RotationStepDegrees = value)),

                Row(theme, "Scale step", "×",
                    Step(snap.ScaleStep, 0.05f, EditorSnapSettings.MinimumScaleStep, 10.0f, 2,
                        () => snap.ScaleStep, value => snap.ScaleStep = value))).Scrollable();
        }

        private static UiElement Row(UiTheme theme, string label, string unit, UiElement editor)
        {
            return new Grid().Columns("110, 80, *", theme.Spacing).Rows("Auto").Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).Ellipsis().CenterV().GridColumn(0),
                editor.GridColumn(1),
                new TextBlock().Text(unit).Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall).CenterV().GridColumn(2));
        }

        private static NumericBox Step(
            float initial,
            float step,
            float minimum,
            float maximum,
            int places,
            Func<float> read,
            Action<float> write)
        {
            var box = new NumericBox()
                .Step(step)
                .Range(minimum, maximum)
                .Places(places)
                .Value(initial);

            box.ValueChanged += (_, value) =>
            {
                if (!box.IsApplyingBinding)
                {
                    write(value);
                }
            };

            box.BindPolled(b => b.Value, read);
            return box;
        }
    }
}
