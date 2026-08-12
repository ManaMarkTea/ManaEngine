using ManaUI;

using Button = ManaUI.Button;

namespace ManaTests;

/// <summary>
/// The test runner's UI, built with ManaUI. Dogfooding is the point: if ItemsControl, ScrollViewer,
/// binding and text input cannot comfortably present a few hundred rows, that is worth discovering here
/// rather than in an editor panel.
/// </summary>
public static class TestRunnerView
{
    public static UiElement Build(UiTheme theme, TestRunnerViewModel vm)
    {
        return new DockPanel().Children(
            BuildToolbar(theme, vm).DockTo(Dock.Top),
            BuildStatusBar(theme, vm).DockTo(Dock.Bottom),
            BuildResults(theme, vm));
    }

    private static UiElement BuildToolbar(UiTheme theme, TestRunnerViewModel vm)
    {
        return new Border()
            .Fill(theme.Surface)
            .Content(new StackPanel()
                .Horizontal(theme.Spacing)
                .Padding(10.0f, 8.0f)
                .Children(
                    new Button()
                        .Label("Run tests")
                        .Variant(ButtonVariant.Primary)
                        .Command(vm.RunTestsCommand),

                    new TextBox()
                        .Hint("Filter checks…")
                        .Width(280.0f)
                        .CenterV()
                        .BindText(vm, v => v.Search),

                    new CheckBox()
                        .Label("Failures only")
                        .CenterV()
                        .BindIsChecked(vm, v => v.FailuresOnly)));
    }

    private static UiElement BuildStatusBar(UiTheme theme, TestRunnerViewModel vm)
    {
        return new Border()
            .Fill(theme.SurfaceAlt)
            .Content(new Grid()
                .Columns("Auto, *, Auto", theme.Spacing)
                .Rows("Auto")
                .Padding(10.0f, 6.0f)
                .Children(
                    // The pass/fail pill is the one thing you should be able to read across the room.
                    new Border()
                        .Rounded(3.0f)
                        .PaddingOf(8.0f, 2.0f)
                        .CenterV()
                        .GridColumn(0)
                        .Configure(border => border
                            .Bind(b => b.Background, vm, v => v.AllPassed,
                                passed => passed ? theme.Success : theme.Danger))
                        .Content(new TextBlock()
                            .Foreground(theme.TextOnAccent)
                            .Bind(t => t.Text, vm, v => v.AllPassed, passed => passed ? "PASS" : "FAIL")),

                    new TextBlock()
                        .BindText(vm, v => v.Summary)
                        .CenterV()
                        .GridColumn(1),

                    new TextBlock()
                        .BindText(vm, v => v.Timing)
                        .Foreground(theme.TextMuted)
                        .FontSize(theme.FontSizeSmall)
                        .CenterV()
                        .GridColumn(2)));
    }

    private static UiElement BuildResults(UiTheme theme, TestRunnerViewModel vm)
    {
        return new ItemsControl()
            .Items(vm.Sections, section => BuildSection(theme, section))
            .Spacing(2.0f)
            .Configure(items => items.Padding = new Thickness(10.0f))
            .WhenEmpty(new TextBlock()
                .Text("No results. Press Run tests.")
                .Foreground(theme.TextMuted)
                .Margin(12.0f))
            .Scrollable();
    }

    private static UiElement BuildSection(UiTheme theme, TestSectionViewModel section)
    {
        var rows = new StackPanel().Vertical(1.0f);

        rows.Children.Add(new Border()
            .Fill(section.HasFailures ? theme.Danger.WithAlpha(0.15f) : theme.SurfaceAlt)
            .Rounded(theme.CornerRadius)
            .PaddingOf(8.0f, 5.0f)
            .Content(new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").Children(
                new TextBlock()
                    .BindText(section, s => s.Header)
                    .FontFamily(theme.FontFamilyMedium)
                    .Ellipsis()
                    .GridColumn(0),
                new TextBlock()
                    .BindText(section, s => s.Summary)
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .GridColumn(1))));

        foreach (var check in section.Checks)
        {
            rows.Children.Add(BuildCheckRow(theme, check));
        }

        return new StackPanel().Vertical(1.0f).Children(rows).Margin(0.0f, 0.0f, 0.0f, 8.0f);
    }

    private static UiElement BuildCheckRow(UiTheme theme, TestCheckViewModel check)
    {
        // A coloured bar rather than a glyph: it survives any font, scans vertically, and costs one
        // instance per row, which matters at a few hundred rows.
        return new Grid()
            .Columns("3, *", 8.0f)
            .Rows("Auto")
            .Children(
                new Border()
                    .Fill(check.Passed ? theme.Success : theme.Danger)
                    .Rounded(1.5f)
                    .GridColumn(0),

                new TextBlock()
                    .Text(check.Label)
                    .Foreground(check.Passed ? theme.TextMuted : theme.Text)
                    .FontSize(theme.FontSizeSmall)
                    .Ellipsis()
                    .CenterV()
                    .Margin(0.0f, 2.0f)
                    .GridColumn(1));
    }
}
