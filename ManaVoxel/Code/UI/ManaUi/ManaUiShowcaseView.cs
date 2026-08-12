using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button. ManaUI's widget names are
// unprefixed on purpose (the fluent syntax would be unreadable otherwise), so files that touch both
// namespaces disambiguate here.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// A standing demo of ManaUI, written in the fluent style real panels use and driven entirely by
    /// <see cref="ManaUiShowcaseViewModel"/>. It doubles as the visual regression check for layout, text,
    /// the SDF primitive, input and binding -- if any of those break, it shows.
    /// </summary>
    public static class ManaUiShowcaseView
    {
        /// <summary>
        /// <paramref name="liveStat"/> is any engine value worth watching; it is read every frame through a
        /// polled binding, which is how the UI shows state that will never implement INotifyPropertyChanged.
        /// </summary>
        public static UiElement Build(UiTheme theme, ManaUiShowcaseViewModel vm, Func<string>? liveStat = null)
        {
            // Anchored rather than filling the window, so the 3D viewport underneath stays visible.
            return new Canvas().Children(
                BuildPanel(theme, vm, liveStat).At(24.0f, 80.0f));
        }

        private static UiElement BuildPanel(UiTheme theme, ManaUiShowcaseViewModel vm, Func<string>? liveStat)
        {
            return new Border()
                .Fill(theme.Surface)
                .Outline(theme.Border, theme.BorderThickness)
                .Rounded(theme.CornerRadiusLarge)
                .Shadow(18.0f)
                .PaddingOf(16.0f)
                .Left().Top()
                .Content(BuildContent(theme, vm, liveStat));
        }

        /// <summary>
        /// The panel's contents without the floating chrome, for hosting inside a dock group -- the group
        /// supplies its own frame and title.
        /// </summary>
        public static UiElement BuildContent(UiTheme theme, ManaUiShowcaseViewModel vm, Func<string>? liveStat)
        {
            return new StackPanel().Vertical(theme.Spacing).Padding(12.0f).Children(
                    new TextBlock()
                        .BindText(vm, v => v.Title)
                        .FontSize(theme.FontSizeHeading)
                        .FontFamily(theme.FontFamilyMedium),

                    Caption("Retained-mode widgets, WPF-style layout, one draw call.", theme),

                    Divider(theme),

                    Caption("Primitives", theme),
                    Swatches(theme),

                    Divider(theme),

                    Caption("Grid: Auto | * | 2*", theme),
                    GridSample(theme),

                    Divider(theme),

                    Caption("Two-way binding", theme),
                    BindingSample(theme, vm),

                    Divider(theme),

                    Caption("Inspector editors (drag to scrub, click to type)", theme),
                    EditorSample(theme, vm),

                    Divider(theme),

                    Caption("ItemsControl over an ObservableCollection", theme),
                    LayerList(theme, vm),

                    Divider(theme),

                    Caption("Polled binding (engine state, no INotifyPropertyChanged)", theme),
                    new TextBlock()
                        .Configure(text =>
                        {
                            if (liveStat != null)
                            {
                                text.BindTextPolled(liveStat);
                            }
                            else
                            {
                                text.Text = "no live stat supplied";
                            }
                        })
                        .Foreground(theme.TextMuted),

                    Divider(theme),

                    Caption("Text wrapping and ellipsis", theme),
                    new TextBlock()
                        .Text("Two-pass layout means an Auto row containing wrapped text sizes correctly, "
                            + "because the text is measured at its final width rather than guessed at.")
                        .Wrap()
                        .Width(320.0f),

                    new Border()
                        .Fill(theme.SurfaceAlt)
                        .Rounded(theme.CornerRadius)
                        .PaddingOf(8.0f, 4.0f)
                        .Width(320.0f)
                        .Content(new TextBlock()
                            .Text("This single line is far too long for the box it lives in, so it trims.")
                            .Ellipsis()
                            .Foreground(theme.TextMuted)));
        }

        // ----- sections ---------------------------------------------------------------------------------

        private static UiElement BindingSample(UiTheme theme, ManaUiShowcaseViewModel vm)
        {
            return new StackPanel().Vertical(theme.Spacing).Children(
                new StackPanel().Horizontal(theme.Spacing).Children(
                    new Button().Label("Primary").Variant(ButtonVariant.Primary).Command(vm.BumpCommand),
                    new Button().Label("Standard").Command(vm.BumpCommand),
                    new Button().Label("Subtle").Variant(ButtonVariant.Subtle).Command(vm.BumpCommand),
                    new Button().Label("Disabled").Enabled(false)),

                new TextBlock()
                    .BindText(vm, v => v.ClickCount, n => $"Clicked {n} time{(n == 1 ? string.Empty : "s")}")
                    .Foreground(theme.TextMuted),

                // Slider and label share one source, so the label proves the write-back actually happened
                // rather than the slider merely repainting itself.
                new Slider().Range(0.0f, 16.0f, 0.5f).Width(320.0f).BindValue(vm, v => v.BrushRadius),
                new TextBlock().BindText(vm, v => v.BrushSummary).Foreground(theme.TextMuted),

                new CheckBox().Label("Show wireframe").BindIsChecked(vm, v => v.ShowWireframe),

                // The dropdown and the label share a source, so the label proves the selection committed
                // rather than the combo merely repainting itself.
                new ComboBox()
                    .Options(vm.Tools)
                    .Width(320.0f)
                    .BindSelectedIndex(vm, v => v.SelectedToolIndex),

                new TextBlock()
                    .BindText(vm, v => v.SelectedToolName, name => $"Tool: {name}")
                    .Foreground(theme.TextMuted),

                // Bound to the same property as the panel heading, so editing here retitles the panel --
                // the clearest possible demonstration that write-back works.
                new TextBox()
                    .Hint("Panel title")
                    .Width(320.0f)
                    .BindText(vm, v => v.Title));
        }

        /// <summary>
        /// The three controls the inspector is made of. Both editors write to the same view model as the
        /// readouts beneath them, so a drag that moves the label is proof the write-back happened rather
        /// than the field repainting itself.
        /// </summary>
        private static UiElement EditorSample(UiTheme theme, ManaUiShowcaseViewModel vm)
        {
            return new Expander()
                .Header("Brush")
                .Width(320.0f)
                .Section(theme.Spacing,
                    new Grid().Columns("70, *", 8.0f).Rows("Auto").Children(
                        Caption("Origin", theme).CenterV().GridColumn(0),
                        new VectorBox().Step(0.25f).Places(2)
                            .BindValue(vm, v => v.BrushOrigin).GridColumn(1)),

                    new Grid().Columns("70, *", 8.0f).Rows("Auto").Children(
                        Caption("Density", theme).CenterV().GridColumn(0),
                        new NumericBox().Step(0.01f).Range(0.0f, 1.0f).Places(2)
                            .BindValue(vm, v => v.Density).GridColumn(1)),

                    new TextBlock().BindText(vm, v => v.BrushOriginSummary).Foreground(theme.TextMuted));
        }

        private static UiElement LayerList(UiTheme theme, ManaUiShowcaseViewModel vm)
        {
            return new StackPanel().Vertical(theme.Spacing).Children(
                new StackPanel().Horizontal(theme.Spacing).Children(
                    new Button().Label("Add layer").Command(vm.AddLayerCommand),
                    new Button().Label("Remove").Command(vm.RemoveLayerCommand)),

                // Capped in height so adding layers scrolls rather than growing the panel forever.
                new ItemsControl()
                    .Items(vm.Layers, layer => LayerRow(theme, layer))
                    .Spacing(2.0f)
                    .WhenEmpty(Caption("No layers.", theme))
                    .Scrollable()
                    .Size(320.0f, 92.0f));
        }

        private static UiElement LayerRow(UiTheme theme, ShowcaseLayerViewModel layer)
        {
            return new Border()
                .Fill(theme.SurfaceAlt)
                .Rounded(theme.CornerRadius)
                .PaddingOf(8.0f, 4.0f)
                .Content(new Grid().Columns("*, Auto", 8.0f).Rows("Auto").Children(
                    new TextBlock().BindText(layer, l => l.Name).Ellipsis().GridColumn(0),
                    new TextBlock()
                        .BindText(layer, l => l.VoxelCount, n => $"{n:N0}")
                        .Foreground(theme.TextMuted)
                        .GridColumn(1)));
        }

        private static UiElement Swatches(UiTheme theme)
        {
            return new StackPanel().Horizontal(theme.Spacing).Children(
                Swatch(theme.Accent, new CornerRadius(4.0f)),
                Swatch(theme.Success, new CornerRadius(16.0f)),
                Swatch(theme.Warning, new CornerRadius(16.0f, 4.0f, 16.0f, 4.0f)),
                new Border().Rounded(8.0f).Outline(theme.BorderStrong, 2.0f).Size(48.0f, 32.0f),
                Swatch(theme.Danger, new CornerRadius(999.0f)));
        }

        private static UiElement Swatch(Vector4 fill, CornerRadius corners)
        {
            return new Border().Fill(fill).Rounded(corners).Size(48.0f, 32.0f);
        }

        private static UiElement GridSample(UiTheme theme)
        {
            return new Grid().Columns("64, *, 2*", 4.0f).Rows("Auto").Width(320.0f).Children(
                Cell("64px", theme).GridColumn(0),
                Cell("1 share", theme).GridColumn(1),
                Cell("2 shares", theme).GridColumn(2));
        }

        private static UiElement Cell(string label, UiTheme theme)
        {
            return new Border()
                .Fill(theme.SurfaceAlt)
                .Rounded(theme.CornerRadius)
                .PaddingOf(6.0f, 4.0f)
                .Content(Caption(label, theme).Ellipsis());
        }

        private static TextBlock Caption(string text, UiTheme theme)
        {
            return new TextBlock().Text(text).Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall);
        }

        private static UiElement Divider(UiTheme theme)
        {
            return new Border().Fill(theme.Divider).Height(1.0f).Margin(0.0f, 4.0f);
        }
    }
}
