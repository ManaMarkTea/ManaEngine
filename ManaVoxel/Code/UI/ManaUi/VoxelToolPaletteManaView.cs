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
            var row = new StackPanel().Horizontal(theme.Spacing * 2.0f).Padding(10.0f, 6.0f).Children(
                BuildToolGroup(theme, vm),
                Divider(theme),
                BuildColorGroup(theme, vm),
                BuildStrokeGroup(theme, vm),
                BuildTextGroup(theme, vm));

            // Horizontal rather than vertical: this lives in the toolbar strip under the menu now, not a
            // side column, so a window too narrow for every group scrolls sideways rather than wrapping.
            return row.Scrollable().Scrolls(vertical: ScrollBarVisibility.Disabled, horizontal: ScrollBarVisibility.Auto);
        }

        // ----- tools ----------------------------------------------------------------------------------

        private static UiElement BuildToolGroup(UiTheme theme, VoxelEditorViewModel vm)
        {
            // Select is a mode rather than a tool, so it stays a separate toggle exactly as it is in the
            // ImGui palette.
            var select = new CheckBox()
                .Label("Select")
                .CenterV()
                .OnChanged(value => vm.IsSelectToolActive = value)
                .BindPolled(c => c.IsChecked, () => vm.IsSelectToolActive);

            var tool = new ComboBox()
                .Options(VoxelToolCatalog.ToolNames)
                .Width(150.0f)
                .OnSelected(index => vm.SelectedToolIndex = index)
                .BindPolled(c => c.SelectedIndex, () => vm.SelectedToolIndex);

            var plane = new ComboBox()
                .Options(VoxelToolCatalog.PlaneNames)
                .Width(110.0f)
                .OnSelected(index => vm.SelectedWorkPlaneIndex = index)
                .BindPolled(c => c.SelectedIndex, () => vm.SelectedWorkPlaneIndex);

            return new StackPanel().Horizontal(theme.Spacing).Children(
                select,
                Group(theme, "Tool", tool),
                Group(theme, "Work plane", plane));
        }

        // ----- paint colour ---------------------------------------------------------------------------

        private static UiElement BuildColorGroup(UiTheme theme, VoxelEditorViewModel vm)
        {
            // The swatch opens a real picker now that there is one -- channel sliders included -- so the
            // toolbar only needs room for the swatch itself, not a copy of the picker beside it.
            var field = new ColorField
            {
                Color = vm.PaintVoxelColor,
                ShowAlpha = false,
                Width = 32.0f,
                Height = 32.0f
            };

            field.ColorChanged += (_, color) =>
                vm.PaintVoxelColor = new Vector4(color.X, color.Y, color.Z, vm.PaintVoxelColor.W);

            // Polled as well as written: the palette shares its colour with the ImGui palette and the
            // eyedropper, and neither announces a change.
            field.BindPolled(f => f.Color, () => vm.PaintVoxelColor);

            return Group(theme, "Paint colour", field);
        }

        // ----- stroke and text --------------------------------------------------------------------------

        private static UiElement BuildStrokeGroup(UiTheme theme, VoxelEditorViewModel vm)
        {
            var row = new StackPanel().Horizontal(theme.Spacing).Children(
                Divider(theme),
                IntSlider(theme, "Radius", 0, 12,
                    () => vm.StrokeRadiusVoxels,
                    v => vm.StrokeRadiusVoxels = v,
                    width: 150.0f));

            // Shown only for the tools that use a radius, matching the ImGui palette. The divider is part
            // of this group rather than a fixed fixture, so hiding the group does not leave it stranded
            // with nothing to its right.
            return row.BindPolled(s => s.Visibility, () =>
                VoxelToolCatalog.GetToolKind(vm.SelectedToolIndex)
                    is VoxelToolKind.Stroke or VoxelToolKind.Line or VoxelToolKind.Spline
                    ? Visibility.Visible
                    : Visibility.Collapsed);
        }

        private static UiElement BuildTextGroup(UiTheme theme, VoxelEditorViewModel vm)
        {
            var text = new TextBox()
                .Hint("Text to stamp")
                .Configure(box => box.MaxLength = 128)
                .Width(160.0f)
                .OnCommitted(value => vm.ToolText = value)
                .BindPolled(b => b.Text, () => vm.ToolText);

            var row = new StackPanel().Horizontal(theme.Spacing).Children(
                Divider(theme),
                Group(theme, "Text", text),
                IntSlider(theme, "Scale", 1, 8, () => vm.TextScale, v => vm.TextScale = v, width: 130.0f),
                IntSlider(theme, "Depth", 1, 16, () => vm.TextDepth, v => vm.TextDepth = v, width: 130.0f));

            return row.BindPolled(s => s.Visibility, () =>
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
            Action<int> write,
            float width = 170.0f)
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

            return new Grid().Columns("Auto, *, 28", 6.0f).Rows("Auto").Width(width).Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).CenterV().GridColumn(0),
                slider.GridColumn(1).CenterV(),
                value.GridColumn(2));
        }

        // ----- helpers --------------------------------------------------------------------------------

        /// <summary>A small caption above a control, stacked to keep a toolbar group readable without a row of its own.</summary>
        private static UiElement Group(UiTheme theme, string caption, UiElement control)
        {
            return new StackPanel().Vertical(2.0f).Children(Caption(caption, theme), control);
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
            return new Border().Fill(theme.Divider).Width(1.0f).Margin(2.0f, 4.0f).StretchV();
        }
    }
}
