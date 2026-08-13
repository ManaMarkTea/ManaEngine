using ManaUI;

using Button = ManaUI.Button;

namespace ManaTests;

/// <summary>
/// The test runner's UI, built with ManaUI. Dogfooding is the point: if ItemsControl, ScrollViewer,
/// Expander, binding and text input cannot comfortably present nearly a thousand rows, that is worth
/// discovering here rather than in an editor panel.
/// <para>
/// Laid out tall and narrow, because that is the shape of the data. A list of check names wants a column,
/// not a field -- a wide window just left most of every row empty and pushed the failures further apart.
/// </para>
/// </summary>
public static class TestRunnerView
{
    /// <summary>Width of the status column. Sized for a check mark, which is what sets it.</summary>
    private const float IconColumnWidth = 20.0f;

    public static UiElement Build(UiTheme theme, TestRunnerViewModel vm)
    {
        return new DockPanel().Children(
            BuildHeader(theme, vm).DockTo(Dock.Top),
            BuildToolbar(theme, vm).DockTo(Dock.Top),
            BuildStatusBar(theme, vm).DockTo(Dock.Bottom),
            BuildResults(theme, vm));
    }

    /// <summary>
    /// The banner: one emoji and one sentence, sized to be read without looking directly at it. Everything
    /// else on screen is detail you go hunting for; this is the answer to the only question being asked.
    /// </summary>
    private static UiElement BuildHeader(UiTheme theme, TestRunnerViewModel vm)
    {
        return new Border()
            .Configure(border => border.Bind(
                b => b.Background,
                vm,
                v => v.AllPassed,
                passed => passed ? theme.Success.WithAlpha(0.16f) : theme.Danger.WithAlpha(0.20f)))
            .Content(new Grid()
                .Columns("Auto, *, Auto", 12.0f)
                .Rows("Auto")
                .Padding(14.0f, 12.0f)
                .Children(
                    new TextBlock()
                        .BindText(vm, v => v.StatusIcon)
                        .FontSize(26.0f)
                        .CenterV()
                        .GridColumn(0),

                    new StackPanel().Vertical(2.0f).CenterV().GridColumn(1).Children(
                        new TextBlock()
                            .BindText(vm, v => v.Headline)
                            .FontSize(theme.FontSizeHeading)
                            .FontFamily(theme.FontFamilyMedium)
                            .Ellipsis(),

                        new TextBlock()
                            .BindText(vm, v => v.Summary)
                            .Foreground(theme.TextMuted)
                            .FontSize(theme.FontSizeSmall)
                            .Ellipsis()),

                    // Counts rather than a pill: the numbers are what you compare between runs.
                    new StackPanel().Horizontal(10.0f).CenterV().GridColumn(2).Children(
                        Tally(theme, vm, "✅", v => v.PassedCount, theme.Success),
                        Tally(theme, vm, "❌", v => v.FailedCount, theme.Danger))));
    }

    private static UiElement Tally(
        UiTheme theme,
        TestRunnerViewModel vm,
        string icon,
        System.Linq.Expressions.Expression<Func<TestRunnerViewModel, int>> count,
        Vector4 color)
    {
        return new StackPanel().Horizontal(4.0f).Children(
            new TextBlock().Text(icon).FontSize(theme.FontSizeSmall).CenterV(),
            new TextBlock()
                .Bind(t => t.Text, vm, count, n => n.ToString("N0"))
                .Foreground(color)
                .FontFamily(theme.FontFamilyMedium)
                .CenterV());
    }

    private static UiElement BuildToolbar(UiTheme theme, TestRunnerViewModel vm)
    {
        return new Border()
            .Fill(theme.Surface)
            .Content(new Grid()
                .Columns("Auto, *, Auto", theme.Spacing)
                .Rows("Auto")
                .Padding(10.0f, 8.0f)
                .Children(
                    new Button()
                        .Label("\U0001F504  Run")
                        .Variant(ButtonVariant.Primary)
                        .Command(vm.RunTestsCommand)
                        .GridColumn(0),

                    // Star column rather than a fixed width, so the field uses whatever the window has --
                    // the search box was the reason the old layout wanted to be wide.
                    new TextBox()
                        .Hint("\U0001F50D  Filter checks…")
                        .CenterV()
                        .BindText(vm, v => v.Search)
                        .GridColumn(1),

                    new CheckBox()
                        .Label("Failures only")
                        .CenterV()
                        .BindIsChecked(vm, v => v.FailuresOnly)
                        .GridColumn(2)));
    }

    private static UiElement BuildStatusBar(UiTheme theme, TestRunnerViewModel vm)
    {
        return new Border()
            .Fill(theme.SurfaceAlt)
            .Content(new Grid()
                .Columns("*, Auto", theme.Spacing)
                .Rows("Auto")
                .Padding(10.0f, 5.0f)
                .Children(
                    new TextBlock()
                        .Text("Sections open automatically when something in them fails.")
                        .Foreground(theme.TextMuted)
                        .FontSize(theme.FontSizeSmall)
                        .Ellipsis()
                        .CenterV()
                        .GridColumn(0),

                    new TextBlock()
                        .BindText(vm, v => v.Timing, t => $"⏱  {t}")
                        .Foreground(theme.TextMuted)
                        .FontSize(theme.FontSizeSmall)
                        .CenterV()
                        .GridColumn(1)));
    }

    private static UiElement BuildResults(UiTheme theme, TestRunnerViewModel vm)
    {
        return new ItemsControl()
            .Items(vm.Sections, section => BuildSection(theme, section))
            .Spacing(3.0f)
            .Configure(items => items.Padding = new Thickness(10.0f, 10.0f, 4.0f, 10.0f))
            .WhenEmpty(new TextBlock()
                .Text("Nothing to show. Press Run, or clear the filter.")
                .Foreground(theme.TextMuted)
                .Margin(12.0f))
            .Scrollable();
    }

    /// <summary>
    /// One suite, collapsible. Passing suites come up closed and failing ones open, which is the whole
    /// point at this many checks: the failures are on screen without scrolling and the eight hundred
    /// passes are one line each until asked for.
    /// </summary>
    private static UiElement BuildSection(UiTheme theme, TestSectionViewModel section)
    {
        var rows = new StackPanel().Vertical(1.0f).Padding(2.0f, 4.0f, 0.0f, 4.0f);

        foreach (var check in section.Checks)
        {
            rows.Children.Add(BuildCheckRow(theme, check));
        }

        return new Expander()
            .Header(section.Header)
            .Expanded(section.StartsExpanded)
            .Accessory(new StackPanel().Horizontal(8.0f).CenterV().Children(
                new TextBlock()
                    .BindText(section, s => s.Summary)
                    .Foreground(section.HasFailures ? theme.Danger : theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .CenterV(),

                new TextBlock()
                    .BindText(section, s => s.Icon)
                    .FontSize(theme.FontSizeSmall)
                    .CenterV()))
            .Content(rows);
    }

    private static UiElement BuildCheckRow(UiTheme theme, TestCheckViewModel check)
    {
        var row = new Grid()
            .Columns($"{IconColumnWidth}, *", 6.0f)
            .Rows("Auto")
            .Children(
                new TextBlock()
                    .Text(check.Icon)
                    .FontSize(theme.FontSizeSmall)
                    .CenterV()
                    .GridColumn(0),

                new TextBlock()
                    .Text(check.Label)
                    .Foreground(check.Passed ? theme.TextMuted : theme.Text)
                    .FontSize(theme.FontSizeSmall)
                    .Ellipsis()
                    .CenterV()
                    .GridColumn(1));

        if (check.Passed)
        {
            return row;
        }

        // A failure gets a tinted band as well as its emoji. Colour is what the eye finds while scrolling;
        // the emoji is what identifies it once found.
        return new Border()
            .Fill(theme.Danger.WithAlpha(0.14f))
            .Rounded(3.0f)
            .PaddingOf(4.0f, 2.0f)
            .Content(row);
    }
}
