using ManaUI;
using ManaVoxel.Code.Tools;

using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The voxel tool palette, rebuilt in ManaUI and driven by the real
    /// <see cref="VoxelEditorViewModel"/> -- the same instance the ImGui palette uses, so the two stay in
    /// step and either can drive the editor.
    /// <para>
    /// The view model exposes its state as public mutable fields with no change notification, which is
    /// typical of code that grew up against an immediate-mode UI. Property-path bindings cannot touch a
    /// field, so this reads through polled bindings and writes back through the controls' own events. That
    /// pairing is the intended way to drive an existing model without rewriting it, and it is why polled
    /// bindings exist at all -- the alternative would have been converting the whole view model before a
    /// single panel could be ported.
    /// </para>
    /// </summary>
    public static class VoxelToolPaletteManaView
    {
        public static UiElement Build(UiTheme theme, VoxelEditorViewModel vm)
        {
            return new StackPanel()
                .Vertical(theme.Spacing)
                .Padding(12.0f)
                .Children(
                    BuildToolSection(theme, vm),
                    Divider(theme),
                    BuildColorSection(theme, vm),
                    Divider(theme),
                    BuildStrokeSection(theme, vm),
                    BuildTextSection(theme, vm));
        }

        // ----- tools ----------------------------------------------------------------------------------

        private static UiElement BuildToolSection(UiTheme theme, VoxelEditorViewModel vm)
        {
            // Select is a mode rather than a tool, so it stays a separate toggle exactly as it is in the
            // ImGui palette.
            var select = new CheckBox()
                .Label("Select mode")
                .OnChanged(value => vm.IsSelectToolActive = value)
                .BindPolled(c => c.IsChecked, () => vm.IsSelectToolActive);

            var tool = new ComboBox()
                .Options(VoxelToolCatalog.ToolNames)
                .OnSelected(index => vm.SelectedToolIndex = index)
                .BindPolled(c => c.SelectedIndex, () => vm.SelectedToolIndex);

            var plane = new ComboBox()
                .Options(VoxelToolCatalog.PlaneNames)
                .OnSelected(index => vm.SelectedWorkPlaneIndex = index)
                .BindPolled(c => c.SelectedIndex, () => vm.SelectedWorkPlaneIndex);

            return new StackPanel().Vertical(theme.Spacing).Children(
                Caption("Tool", theme),
                select,
                LabelledRow(theme, "Tool", tool),
                LabelledRow(theme, "Work plane", plane));
        }

        // ----- paint colour ---------------------------------------------------------------------------

        private static UiElement BuildColorSection(UiTheme theme, VoxelEditorViewModel vm)
        {
            // The swatch opens a real picker now that there is one. Voxel colours are opaque, so the alpha
            // strip is hidden rather than offering a control that would do nothing.
            var field = new ColorField
            {
                Color = vm.PaintVoxelColor,
                ShowAlpha = false,
                Height = 28.0f
            };

            field.ColorChanged += (_, color) =>
                vm.PaintVoxelColor = new Vector4(color.X, color.Y, color.Z, vm.PaintVoxelColor.W);

            // Polled as well as written: the palette shares its colour with the ImGui palette and the
            // eyedropper, and neither announces a change.
            field.BindPolled(f => f.Color, () => vm.PaintVoxelColor);

            // The sliders stay. A picker is for choosing a colour; a slider is for nudging one channel of
            // the colour already chosen, and the palette does both.
            return new StackPanel().Vertical(theme.Spacing).Children(
                Caption("Paint colour", theme),
                field,
                ChannelSlider(theme, "R", () => vm.PaintVoxelColor.X, v => vm.PaintVoxelColor.X = v),
                ChannelSlider(theme, "G", () => vm.PaintVoxelColor.Y, v => vm.PaintVoxelColor.Y = v),
                ChannelSlider(theme, "B", () => vm.PaintVoxelColor.Z, v => vm.PaintVoxelColor.Z = v));
        }

        private static UiElement ChannelSlider(
            UiTheme theme,
            string label,
            Func<float> read,
            Action<float> write)
        {
            var slider = new Slider()
                .Range(0.0f, 1.0f)
                .OnChanged(write)
                .BindPolled(s => s.Value, read);

            var value = new TextBlock()
                .Foreground(theme.TextMuted)
                .FontSize(theme.FontSizeSmall)
                .CenterV()
                .BindTextPolled(read, v => $"{v:F2}");

            return new Grid().Columns("18, *, 34", 6.0f).Rows("Auto").Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).CenterV().GridColumn(0),
                slider.GridColumn(1),
                value.GridColumn(2));
        }

        // ----- stroke and text --------------------------------------------------------------------------

        private static UiElement BuildStrokeSection(UiTheme theme, VoxelEditorViewModel vm)
        {
            var section = new StackPanel().Vertical(theme.Spacing).Children(
                Caption("Stroke", theme),
                IntSlider(theme, "Radius", 0, 12,
                    () => vm.StrokeRadiusVoxels,
                    v => vm.StrokeRadiusVoxels = v));

            // Shown only for the tools that use a radius, matching the ImGui palette. Polled because the
            // tool index it depends on is a field like everything else.
            return section.BindPolled(s => s.Visibility, () =>
                VoxelToolCatalog.GetToolKind(vm.SelectedToolIndex)
                    is VoxelToolKind.Stroke or VoxelToolKind.Line or VoxelToolKind.Spline
                    ? Visibility.Visible
                    : Visibility.Collapsed);
        }

        private static UiElement BuildTextSection(UiTheme theme, VoxelEditorViewModel vm)
        {
            var text = new TextBox()
                .Hint("Text to stamp")
                .Configure(box => box.MaxLength = 128)
                .OnCommitted(value => vm.ToolText = value)
                .BindPolled(b => b.Text, () => vm.ToolText);

            var section = new StackPanel().Vertical(theme.Spacing).Children(
                Divider(theme),
                Caption("Text tool", theme),
                text,
                IntSlider(theme, "Scale", 1, 8, () => vm.TextScale, v => vm.TextScale = v),
                IntSlider(theme, "Depth", 1, 16, () => vm.TextDepth, v => vm.TextDepth = v));

            return section.BindPolled(s => s.Visibility, () =>
                VoxelToolCatalog.GetToolKind(vm.SelectedToolIndex) == VoxelToolKind.Text
                    ? Visibility.Visible
                    : Visibility.Collapsed);
        }

        private static UiElement IntSlider(
            UiTheme theme,
            string label,
            int minimum,
            int maximum,
            Func<int> read,
            Action<int> write)
        {
            var slider = new Slider()
                .Range(minimum, maximum, 1.0f)
                .OnChanged(value => write((int)MathF.Round(value)))
                .BindPolled(s => s.Value, () => read());

            var value = new TextBlock()
                .Foreground(theme.TextMuted)
                .FontSize(theme.FontSizeSmall)
                .CenterV()
                .BindTextPolled(read, v => v.ToString());

            return new Grid().Columns("Auto, *, 28", 6.0f).Rows("Auto").Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).CenterV().GridColumn(0),
                slider.GridColumn(1),
                value.GridColumn(2));
        }

        // ----- helpers --------------------------------------------------------------------------------

        private static UiElement LabelledRow(UiTheme theme, string label, UiElement control)
        {
            return new Grid().Columns("80, *", 6.0f).Rows("Auto").Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).CenterV().GridColumn(0),
                control.GridColumn(1));
        }

        private static TextBlock Caption(string text, UiTheme theme)
        {
            return new TextBlock()
                .Text(text)
                .Foreground(theme.TextMuted)
                .FontSize(theme.FontSizeSmall);
        }

        private static UiElement Divider(UiTheme theme)
        {
            return new Border().Fill(theme.Divider).Height(1.0f).Margin(0.0f, 4.0f);
        }
    }
}
