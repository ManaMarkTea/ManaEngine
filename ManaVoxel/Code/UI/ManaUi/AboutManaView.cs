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
            var rows = new StackPanel().Vertical(1.0f);

            rows.Children.Add(Row(
                theme,
                "Property",
                section.ValueHeading,
                hasComparison ? section.ComparisonHeading : null,
                heading: true,
                scope: ManaGraphicsDiagnosticScope.Common));

            foreach (var entry in section.Entries)
            {
                rows.Children.Add(Row(
                    theme,
                    entry.Label,
                    entry.Value,
                    hasComparison ? entry.ComparisonValue ?? "—" : null,
                    heading: false,
                    entry.Scope));
            }

            return new Expander().Header($"{section.Name} ({section.Entries.Count})").Content(rows);
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
            ManaGraphicsDiagnosticScope scope)
        {
            var columns = comparison == null ? "*, 160" : "*, 160, 160";

            var foreground = heading ? theme.TextMuted
                : scope switch
                {
                    ManaGraphicsDiagnosticScope.OpenGl => theme.Success,
                    ManaGraphicsDiagnosticScope.WebGpu => theme.Accent,
                    _ => theme.Text
                };

            var grid = new Grid().Columns(columns, theme.Spacing).Rows("Auto").Children(
                new TextBlock()
                    .Text(label)
                    .Foreground(heading ? theme.TextMuted : theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .Ellipsis()
                    .WithTooltip(label)
                    .GridColumn(0),

                new TextBlock()
                    .Text(value)
                    .Foreground(foreground)
                    .FontFamily(heading ? theme.FontFamily : theme.FontFamilyMono)
                    .FontSize(theme.FontSizeSmall)
                    .Ellipsis()
                    .WithTooltip(value)
                    .GridColumn(1));

            if (comparison != null)
            {
                grid.Children.Add(new TextBlock()
                    .Text(comparison)
                    .Foreground(foreground)
                    .FontFamily(heading ? theme.FontFamily : theme.FontFamilyMono)
                    .FontSize(theme.FontSizeSmall)
                    .Ellipsis()
                    .WithTooltip(comparison)
                    .GridColumn(2));
            }

            return grid;
        }
    }
}
