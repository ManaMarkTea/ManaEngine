using System.Globalization;
using ManaUI;

// ManaEditor has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaEditor.Code.UI.ManaUi
{
    /// <summary>
    /// The editor's preferences panel, opened from Edit &gt; Preferences: grid snapping for placing models and
    /// for the move, rotate and scale gizmos. Each value is saved as it changes (see
    /// <see cref="EditorSnapSettings"/>), so there is nothing to apply or cancel. The viewport's Tools drawer
    /// shows the same rows, from <see cref="SnapRows"/>.
    /// </summary>
    public static class PreferencesManaView
    {
        public static UiElement Build(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            return new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                Heading(theme, "Grid snapping"),
                SnapRows(theme, viewModel.Snap)).Scrollable();
        }

        /// <summary>A section heading, small and muted, as a panel's sub-headings are.</summary>
        public static TextBlock Heading(UiTheme theme, string text) =>
            new TextBlock().Text(text).FontFamily(theme.FontFamilyMedium).Foreground(theme.Text);

        /// <summary>
        /// Snap on or off, then a row each for move, rotate and scale: the step, its unit, and a few common steps
        /// a click away. Picking a step turns snapping on -- choosing one is asking for it.
        /// </summary>
        public static UiElement SnapRows(UiTheme theme, EditorSnapSettings snap)
        {
            var enabled = new CheckBox("Snap to grid") { IsChecked = snap.Enabled };
            enabled.CheckedChanged += (_, value) =>
            {
                if (!enabled.IsApplyingBinding)
                {
                    snap.Enabled = value;
                }
            };
            enabled.BindPolled(c => c.IsChecked, () => snap.Enabled);
            enabled.WithTooltip("Snaps placing models and the move, rotate and scale gizmos.");

            return new StackPanel().Vertical(6.0f).Children(
                enabled,
                Row(theme, snap, "Move", "m", 0.25f, EditorSnapSettings.MinimumTranslateStep, 100.0f, 2,
                    [0.25f, 0.5f, 1.0f, 2.0f], () => snap.TranslateStep, value => snap.TranslateStep = value),
                Row(theme, snap, "Rotate", "°", 5.0f, EditorSnapSettings.MinimumRotationStepDegrees, 180.0f, 1,
                    [5.0f, 15.0f, 45.0f, 90.0f], () => snap.RotationStepDegrees, value => snap.RotationStepDegrees = value),
                Row(theme, snap, "Scale", "×", 0.05f, EditorSnapSettings.MinimumScaleStep, 10.0f, 2,
                    [0.1f, 0.25f, 0.5f], () => snap.ScaleStep, value => snap.ScaleStep = value));
        }

        private static UiElement Row(
            UiTheme theme,
            EditorSnapSettings snap,
            string label,
            string unit,
            float step,
            float minimum,
            float maximum,
            int places,
            float[] presets,
            Func<float> read,
            Action<float> write)
        {
            var box = new NumericBox()
                .Step(step)
                .Range(minimum, maximum)
                .Places(places)
                .Value(read());
            box.ValueChanged += (_, value) =>
            {
                if (!box.IsApplyingBinding)
                {
                    write(value);
                }
            };
            box.BindPolled(b => b.Value, read);

            var chips = new StackPanel().Horizontal(2.0f);
            foreach (var preset in presets)
            {
                var value = preset;
                var chip = new Button
                {
                    IgnoreThemePadding = true,
                    Padding = new Thickness(5.0f, 0.0f, 5.0f, 0.0f),
                    Height = 22.0f
                }
                    .Label(value.ToString("0.##", CultureInfo.InvariantCulture))
                    .OnClick(() =>
                    {
                        write(value);
                        snap.Enabled = true;
                    });
                chip.BindPolled(b => b.Variant, () => snap.Enabled && MathF.Abs(read() - value) < 1e-4f
                    ? ButtonVariant.Primary
                    : ButtonVariant.Subtle);
                chips.Children.Add(chip);
            }

            return new Grid().Columns("52, 64, 16, *", 4.0f).Rows("Auto").Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).CenterV().GridColumn(0),
                box.GridColumn(1),
                new TextBlock().Text(unit).Foreground(theme.TextMuted).CenterV().GridColumn(2),
                chips.CenterV().GridColumn(3));
        }
    }
}
