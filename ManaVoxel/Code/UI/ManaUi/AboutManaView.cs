using ManaCore.Graphics;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The graphics diagnostics snapshot, ported from ImGui.
    /// <para>
    /// A snapshot rather than a live readout, and it says when it was taken: these are the values the
    /// backend reported at start-up, and a panel that quietly re-read them would hide the difference
    /// between what was negotiated and what is true now.
    /// </para>
    /// </summary>
    public static class AboutManaView
    {
        public static UiElement Build(UiTheme theme, ManaGraphicsDiagnosticsSnapshot? snapshot, string backend)
        {
            var body = new StackPanel().Vertical(theme.Spacing).Padding(10.0f).Children(
                new TextBlock().Text("Mana Renderer").FontFamily(theme.FontFamilyMedium).FontSize(theme.FontSizeHeading),
                new TextBlock()
                    .Text($"{backend} diagnostics and runtime capabilities")
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .Wrap());

            if (snapshot == null)
            {
                body.Children.Add(new TextBlock()
                    .Text("No diagnostics have been captured yet.")
                    .Foreground(theme.TextMuted)
                    .Wrap()
                    .Margin(0.0f, 8.0f, 0.0f, 0.0f));

                return body.Scrollable();
            }

            body.Children.Add(new TextBlock()
                .Text($"Captured {snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss zzz} during {snapshot.CapturePhase}")
                .Foreground(theme.TextMuted)
                .FontSize(theme.FontSizeSmall)
                .Wrap());

            foreach (var section in snapshot.Sections)
            {
                body.Children.Add(Section(theme, section));
            }

            return body.Scrollable();
        }

        private static UiElement Section(UiTheme theme, ManaGraphicsDiagnosticSection section)
        {
            var hasComparison = !string.IsNullOrEmpty(section.ComparisonHeading);
            var rows = new StackPanel().Vertical(0.0f);

            rows.Children.Add(Row(
                theme,
                "Property",
                section.ValueHeading,
                hasComparison ? section.ComparisonHeading : null,
                heading: true,
                striped: false,
                scope: ManaGraphicsDiagnosticScope.Common));

            for (var i = 0; i < section.Entries.Count; i++)
            {
                var entry = section.Entries[i];

                rows.Children.Add(Row(
                    theme,
                    entry.Label,
                    entry.Value,
                    hasComparison ? entry.ComparisonValue ?? "—" : null,
                    heading: false,
                    // Alternating bands rather than a grid of lines. A table this dense is read across a
                    // row, and a band carries the eye further than a rule between every cell does.
                    striped: i % 2 == 1,
                    entry.Scope));
            }

            return new Expander()
                .Header($"{section.Name} ({section.Entries.Count})")
                .Content(new Border()
                    .Outline(theme.Border, 1.0f)
                    .Rounded(theme.CornerRadius)
                    .Content(rows));
        }

        /// <summary>
        /// One row. Backend-specific values are tinted rather than filtered out: which of them came from
        /// OpenGL and which from WebGPU is most of what makes a comparison worth reading.
        /// </summary>
        private static UiElement Row(
            UiTheme theme,
            string label,
            string value,
            string? comparison,
            bool heading,
            bool striped,
            ManaGraphicsDiagnosticScope scope)
        {
            // The column rules are 1px columns of their own. Border carries a uniform thickness, and a
            // rule on one edge is what separates columns without boxing every cell in.
            var columns = comparison == null ? "220, 1, *" : "220, 1, *, 1, *";

            var foreground = heading ? theme.Text
                : scope switch
                {
                    ManaGraphicsDiagnosticScope.OpenGl => theme.Success,
                    ManaGraphicsDiagnosticScope.WebGpu => theme.Accent,
                    _ => theme.Text
                };

            var grid = new Grid().Columns(columns, 0.0f).Rows("Auto").Children(
                Cell(theme, label, heading ? theme.Text : theme.TextMuted, heading).GridColumn(0),
                Rule(theme).GridColumn(1),
                Cell(theme, value, foreground, heading).GridColumn(2));

            if (comparison != null)
            {
                grid.Children.Add(Rule(theme).GridColumn(3));
                grid.Children.Add(Cell(theme, comparison, foreground, heading).GridColumn(4));
            }

            // The heading gets the stronger fill, so a long section still says what its columns are when
            // the top of it has scrolled away.
            var background = heading ? theme.SurfaceAlt
                : striped ? theme.SurfaceRaised
                : UiColor.Transparent;

            return new Border().Fill(background).Content(grid);
        }

        private static UiElement Rule(UiTheme theme) => new Border().Fill(theme.Divider).Width(1.0f);

        private static UiElement Cell(UiTheme theme, string text, Vector4 foreground, bool heading)
        {
            return new TextBlock()
                .Text(text)
                .Foreground(foreground)
                .FontFamily(heading ? theme.FontFamilyMedium : theme.FontFamilyMono)
                .FontSize(theme.FontSizeSmall)
                .Ellipsis()
                .WithTooltip(text)
                .Margin(6.0f, 3.0f, 6.0f, 3.0f);
        }
    }
}
