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

                    Caption("Colour emoji (CBDT bitmaps, same draw call as the text)", theme),
                    EmojiSample(theme),

                    Divider(theme),

                    Caption("Inspector editors (drag to scrub, click to type)", theme),
                    EditorSample(theme, vm),

                    Divider(theme),

                    Caption("ItemsControl over an ObservableCollection", theme),
                    LayerList(theme, vm),

                    Divider(theme),

                    Caption("Buttons: variants, tooltip, a menu", theme),
                    ButtonSample(theme),

                    Divider(theme),

                    Caption("Progress: set, indeterminate", theme),
                    ProgressSample(theme),

                    Divider(theme),

                    Caption("Disabled: check box, drop-down, text box, slider, number", theme),
                    DisabledSample(theme),

                    Divider(theme),

                    Caption("Slider box and colour field", theme),
                    SliderBoxSample(theme),

                    Divider(theme),

                    Caption("Tabs", theme),
                    TabSample(theme),

                    Divider(theme),

                    Caption("Tree", theme),
                    TreeSample(theme),

                    Divider(theme),

                    Caption("Indicator lights, slots and a glass panel", theme),
                    IndicatorSample(theme),

                    Divider(theme),

                    Caption("Sparkline and a card", theme),
                    SparklineSample(theme),

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

                // Rich text: bold and coloured words in a run that still wraps.
                new RichTextBlock("Torches make nearby mines work [good]**+50%**[/] faster; sabotage puts them [bad]**out**[/]. Hex colours too: [#ffcc44]amber[/].")
                    .Configure(r => r.MaxWidth = 380.0f),

                // Icons: any element before the text (an emoji here), or the whole button; and a highlighted one,
                // as a gamepad or keyboard selection shows without a pointer.
                new StackPanel().Horizontal(theme.Spacing).Children(
                    new Button().Label("Send").Icon(new TextBlock().Text("🚦")).Variant(ButtonVariant.Primary).Command(vm.BumpCommand),
                    new Button().Label("Buy a train").Icon(new TextBlock().Text("🚂")).Command(vm.BumpCommand),
                    new Button().Icon(new TextBlock().Text("⚙")).Variant(ButtonVariant.Subtle).Command(vm.BumpCommand),
                    new Button().Label("Highlighted").Icon(new Border().Fill(theme.Accent).Rounded(5.0f).Size(10.0f, 10.0f)).Highlighted()),

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
        /// <summary>
        /// Emoji drawn from the colour atlas, beside the text they share a line with.
        /// <para>
        /// The mixed rows are the interesting ones: an emoji is an image and a letter is coverage, drawn
        /// from two different atlases, and they still go out as one instanced draw call because both are
        /// bound at once. Turn on Debug > Stats and the batch count stays at one.
        /// </para>
        /// </summary>
        private static UiElement EmojiSample(UiTheme theme)
        {
            return new StackPanel().Vertical(theme.Spacing).Children(
                new TextBlock().Text("😀 🚀 🎨 📦 🔧 🧪")
                    .FontSize(theme.FontSizeHeading),

                new TextBlock().Text("🌍 🌋 🌲 🌊 🔥 💧")
                    .FontSize(theme.FontSizeHeading),

                // Mixed with text, which is the case worth proving.
                new TextBlock().Text("Build 🔨 · Paint 🎨 · Ship 🚀"),

                // A trailing U+FE0F asks for the emoji form of a character that also has a text form. The
                // selector itself is invisible; without that it drew a tofu box after every heart.
                new TextBlock().Text("Variation selectors: ❤️ ⭐️ ⚡️"),

                new TextBlock()
                    .Text("Digits stay text: 0123456789 -- an emoji font maps them as keycap bases.")
                    .Wrap()
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall));
        }

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

        private static UiElement ButtonSample(UiTheme theme)
        {
            Button? menuButton = null;
            menuButton = new Button().Label("Menu").OnClick(() =>
            {
                if (menuButton?.Root is { } root)
                {
                    MenuFlyout.ShowBelow(root, menuButton, new[]
                    {
                        new MenuItem("Open"),
                        new MenuItem("Save") { Shortcut = "Ctrl+S" },
                        MenuItem.Separator(),
                        new MenuItem("Autosave") { IsCheckable = true, IsChecked = true },
                        new MenuItem("Recent").WithItems(new MenuItem("One"), new MenuItem("Two"))
                    });
                }
            });

            var hinted = new Button().Label("Hover me").Variant(ButtonVariant.Primary);
            hinted.Tooltip = "A tooltip: the reason behind a control, which its label has no room for.";

            return new StackPanel().Horizontal(theme.Spacing).Children(
                hinted,
                new Button().Label("Danger").Variant(ButtonVariant.Danger),
                menuButton);
        }

        private static UiElement ProgressSample(UiTheme theme)
        {
            var sweeping = new ProgressBar { IsIndeterminate = true }.Width(320.0f);
            sweeping.BindPolled(bar => bar.ElapsedSeconds, () => (float)(Environment.TickCount64 / 1000.0));

            return new StackPanel().Vertical(theme.Spacing).Children(
                new ProgressBar { Fraction = 0.65f }.Width(320.0f),
                new ProgressBar { Fraction = 0.45f, Tone = UiTone.Danger }.Width(320.0f),
                new ProgressBar { Fraction = 0.7f, Tone = UiTone.Warning }.Width(320.0f),
                new ProgressBar { Fraction = 0.9f, Tone = UiTone.Success }.Width(320.0f),
                new ProgressBar { Fraction = 0.8f, Gloss = true, Thickness = 12.0f }.Width(320.0f),
                new ProgressBar { Fraction = 0.55f, Square = true, Tone = UiTone.Warning, Thickness = 12.0f }.Width(320.0f),
                new ProgressBar { Fraction = 0.4f, Square = true, Gloss = true, Tone = UiTone.Danger, Thickness = 12.0f }.Width(320.0f),
                new ProgressBar { Fraction = 0.75f, Gloss = true, Tone = UiTone.Success, Thickness = 16.0f }.Width(320.0f),
                new ProgressBar { Fraction = 0.6f, Segments = 6, Thickness = 16.0f }.Width(320.0f),
                new StackPanel().Horizontal(theme.Spacing * 2.0f).Children(
                    new ProgressBar { Fraction = 0.8f, Vertical = true, Thickness = 14.0f, Tone = UiTone.Success },
                    new ProgressBar { Fraction = 0.35f, Vertical = true, Thickness = 14.0f, Tone = UiTone.Danger },
                    new ProgressBar { Fraction = 0.6f, Vertical = true, Thickness = 14.0f }),
                sweeping);
        }

        private static UiElement DisabledSample(UiTheme theme)
        {
            return new StackPanel().Vertical(theme.Spacing).Children(
                new CheckBox().Label("Cannot be changed").Enabled(false),
                new ComboBox().Options(new[] { "Locked" }).Width(320.0f).Enabled(false),
                new TextBox().Hint("Read only").Width(320.0f).Enabled(false),
                new Slider().Range(0.0f, 1.0f, 0.1f).Width(320.0f).Enabled(false),
                new NumericBox().Range(0.0f, 10.0f).Places(1).Width(160.0f).Enabled(false));
        }

        private static UiElement SliderBoxSample(UiTheme theme)
        {
            return new StackPanel().Vertical(theme.Spacing).Children(
                new SliderBox { Minimum = 0.0f, Maximum = 100.0f, DecimalPlaces = 0, Value = 40.0f }.Width(320.0f),
                new ColorField { Color = new Vector4(0.12f, 0.63f, 0.86f, 1.0f) }.Width(160.0f));
        }

        private static UiElement TabSample(UiTheme theme)
        {
            return new TabView()
                .Add("Scene", new TextBlock().Text("Tab one: the scene's nodes.").Foreground(theme.TextMuted).Margin(8.0f))
                .Add("Assets", new TextBlock().Text("Tab two: packs and prefabs.").Foreground(theme.TextMuted).Margin(8.0f))
                .Add("Debug", new TextBlock().Text("Tab three: profiler scopes.").Foreground(theme.TextMuted).Margin(8.0f))
                .Size(320.0f, 90.0f);
        }

        private sealed record TreeSampleNode(string Name, TreeSampleNode[] Children);

        private static UiElement TreeSample(UiTheme theme)
        {
            var roots = new[]
            {
                new TreeSampleNode("World", new[]
                {
                    new TreeSampleNode("Terrain", Array.Empty<TreeSampleNode>()),
                    new TreeSampleNode("Props", new[]
                    {
                        new TreeSampleNode("Lantern", Array.Empty<TreeSampleNode>()),
                        new TreeSampleNode("Pine", Array.Empty<TreeSampleNode>())
                    })
                }),
                new TreeSampleNode("Camera", Array.Empty<TreeSampleNode>())
            };

            return new TreeView()
                .Tree(roots, node => node.Children, node => new TextBlock().Text(node.Name))
                .Scrollable()
                .Size(320.0f, 110.0f);
        }

        private static UiElement IndicatorSample(UiTheme theme)
        {
            UiElement Gate(string name, bool on, UiTone tone) => new StackPanel().Horizontal(theme.Spacing).Children(
                new StatusLight { IsOn = on, Tone = tone }.CenterV(),
                new TextBlock().Text(name));

            UiElement Slot(string letter) => new Border { SkinName = "octagon" }
                .Fill(theme.Surface)
                .Size(54.0f, 54.0f)
                .Content(new TextBlock().Text(letter).Foreground(theme.TextMuted).Center());

            return new StackPanel().Vertical(theme.Spacing).Children(
                new StackPanel().Horizontal(theme.Spacing * 2.0f).Children(
                    Gate("Gate A", true, UiTone.Danger),
                    Gate("Gate B", true, UiTone.Danger),
                    Gate("Gate C", false, UiTone.Success)),
                new StackPanel().Horizontal(theme.Spacing).Children(Slot("A"), Slot("B"), Slot("C")),
                new Border { SkinName = "glass" }
                    .Fill(theme.Surface.WithAlpha(0.8f))
                    .PaddingOf(10.0f)
                    .Width(320.0f)
                    .Content(new TextBlock().Text("A glass panel: translucent, for readouts over the scene.").Wrap()));
        }

        private static UiElement SparklineSample(UiTheme theme)
        {
            var samples = Enumerable.Range(0, 40).Select(i => 0.5f + 0.4f * MathF.Sin(i * 0.45f) * MathF.Cos(i * 0.11f)).ToArray();
            var card = new Border { SkinName = "panel" }
                .Fill(theme.Surface)
                .Outline(theme.Border, theme.BorderThickness)
                .Rounded(theme.CornerRadius)
                .PaddingOf(10.0f)
                .Width(320.0f)
                .Content(new TextBlock().Text("A card: a bordered panel that wears the skin's panel picture.").Wrap());

            return new StackPanel().Vertical(theme.Spacing).Children(
                new Sparkline { Samples = samples, Minimum = 0.0f, Maximum = 1.0f }.Size(320.0f, 36.0f),
                card);
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
