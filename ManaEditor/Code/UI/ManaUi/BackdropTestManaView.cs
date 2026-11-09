using ManaUI;

namespace ManaEditor.Code.UI.ManaUi
{
    /// <summary>
    /// A test card for the backdrop blur, because the effect is invisible to every headless check there is.
    /// <para>
    /// Sampler state, texture copies and render-pass ordering all live on the GPU, and all three fail the
    /// same silent way: the blur simply does not appear. This panel is built to tell those apart by eye. It
    /// sets its own blur radii rather than reading the theme, so whether the menu is wired up is not one of
    /// the variables.
    /// </para>
    /// <para>
    /// What each row means is written on the panel rather than here, because the person reading it is
    /// looking at the screen and not at this file.
    /// </para>
    /// </summary>
    public static class BackdropTestManaView
    {
        private static readonly float[] Radii = { 0.0f, 4.0f, 8.0f, 16.0f, 32.0f };

        public static UiElement Build(UiTheme theme)
        {
            return new Grid().Rows("Auto, Auto, *, Auto, Auto", theme.Spacing).Columns("*").Padding(8.0f)
                .Children(
                    new TextBlock()
                        .Text("Drag this panel over the scene, or float it, so there is something behind it "
                            + "to blur.")
                        .Foreground(theme.TextMuted)
                        .FontSize(theme.FontSizeSmall)
                        .Wrap()
                        .GridRow(0),

                    // The control. Transparent and unblurred: if this does not show the scene, the problem is
                    // compositing or draw order, and the blur never had a chance.
                    Labelled(theme, "No blur, fully transparent — should show the sharp scene",
                        new Border().Fill(UiColor.Transparent).Height(56.0f)).GridRow(1),

                    // The one that matters. Transparent and blurred as hard as the chain goes.
                    Labelled(theme, "Maximum blur, fully transparent — should show the scene, unrecognisably soft",
                        new Border()
                            .Fill(UiColor.Transparent)
                            .Blur(ManaUiDrawList.MaxBackdropBlur)
                            .StretchV()).GridRow(2),

                    Steps(theme).GridRow(3),

                    new StackPanel().Vertical(2.0f).GridRow(4).Children(
                        new TextBlock()
                            .Text("If every tile matches the first, nothing is being captured. If they all "
                                + "look the same but slightly soft, the blur is being clamped to one level. "
                                + "If they step from sharp to soft, it works.")
                            .Foreground(theme.TextMuted)
                            .FontSize(theme.FontSizeSmall)
                            .Wrap(),

                        // What the backend says it did, which narrows it further than the tiles can: a
                        // capture count that never moves means the request is not reaching the renderer,
                        // and one that climbs while the tiles stay sharp means it is, and the sampling is
                        // the problem.
                        new TextBlock()
                            .BindTextPolled(() => UiBackdropStats.Summary)
                            .FontFamily(theme.FontFamilyMono)
                            .FontSize(theme.FontSizeSmall)
                            .Foreground(theme.TextMuted)
                            .Wrap()));
        }

        /// <summary>
        /// Every radius side by side. A ladder rather than a single sample: which rung stops changing says
        /// where it is being clamped, where one tile can only say that something is wrong.
        /// </summary>
        private static UiElement Steps(UiTheme theme)
        {
            var row = new Grid()
                .Rows("Auto, Auto", 2.0f)
                .Columns(string.Join(", ", Radii.Select(_ => "*")), theme.Spacing);

            for (var i = 0; i < Radii.Length; i++)
            {
                row.Children.Add(new Border()
                    .Fill(UiColor.Transparent)
                    .Blur(Radii[i])
                    .Outline(theme.Border, 1.0f)
                    .Height(64.0f)
                    .GridColumn(i)
                    .GridRow(0));

                row.Children.Add(new TextBlock()
                    .Text(Radii[i] <= 0.0f ? "sharp" : $"{Radii[i]:0}px")
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .CenterH()
                    .GridColumn(i)
                    .GridRow(1));
            }

            return row;
        }

        private static UiElement Labelled(UiTheme theme, string label, UiElement sample)
        {
            return new Grid().Rows("Auto, *", 2.0f).Columns("*").Children(
                new TextBlock()
                    .Text(label)
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .Wrap()
                    .GridRow(0),

                sample.GridRow(1));
        }
    }
}
