using ManaUI;
using ManaVoxel.Code.Measure;
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi;

/// <summary>
/// The Measure tool window, up while the tool is on: what a click measures, what points snap to, units, and the
/// measurements taken (each can be removed).
/// </summary>
public static class MeasureToolManaView
{
    public const string PanelId = "measure";

    public static UiElement Build(UiTheme theme, VoxelEditorViewModel editor)
    {
        var tool = editor.Measure;

        var modes = new StackPanel().Horizontal(4.0f);
        foreach (var (mode, label, tip) in new[]
        {
            (MeasureMode.Distance, "Distance", "Click two points."),
            (MeasureMode.Radius, "Radius", "Click a centre, then a point on the rim. The circle lies in the centre's surface."),
            (MeasureMode.Path, "Path", "Click point after point; double-click or press Enter to finish."),
            (MeasureMode.Ray, "Ray", "Click a spot: a ray goes from it to the next surface -- floor to ceiling, one side of a cabinet to the other."),
        })
        {
            modes.Children.Add(Chip(label, tip, () => tool.Mode == mode, () => tool.Mode = mode));
        }

        var directions = new StackPanel().Horizontal(4.0f);
        foreach (var (direction, label, tip) in new[]
        {
            (MeasureRayDirection.Out, "Out", "Straight out of the surface clicked: up from a floor, down from a ceiling, across from a wall."),
            (MeasureRayDirection.Up, "Up", "Straight up, wherever you click."),
            (MeasureRayDirection.Down, "Down", "Straight down, wherever you click."),
        })
        {
            directions.Children.Add(Chip(label, tip, () => tool.RayDirection == direction, () => tool.RayDirection = direction));
        }
        directions.BindPolled(d => d.Visibility, () => tool.Mode == MeasureMode.Ray ? Visibility.Visible : Visibility.Collapsed);

        var snaps = new WrapPanel { Spacing = 4.0f };
        foreach (var (target, label, tip) in new[]
        {
            (MeasureSnapTargets.Vertex, "Vertex", "Corners of the mesh."),
            (MeasureSnapTargets.Midpoint, "Midpoint", "The middle of an edge."),
            (MeasureSnapTargets.Edge, "Edge", "Anywhere along an edge."),
            (MeasureSnapTargets.Face, "Face", "Anywhere on a surface."),
            (MeasureSnapTargets.Grid, "Grid", "The ground's snap grid where there is nothing else (when Snap is on)."),
        })
        {
            snaps.Children.Add(Chip(label, tip, () => tool.Targets.HasFlag(target), () => tool.Targets ^= target));
        }

        var stay = new CheckBox("Stay on the first surface") { IsChecked = tool.StayOnFirstSurface };
        stay.CheckedChanged += (_, value) =>
        {
            if (!stay.IsApplyingBinding)
            {
                tool.StayOnFirstSurface = value;
            }
        };
        stay.BindPolled(c => c.IsChecked, () => tool.StayOnFirstSurface);
        stay.WithTooltip("Later points drop onto the first point's surface: start on the floor and every point is "
            + "measured along the floor, wherever the pointer is. Hold Shift for the same, just while it is held.");

        var units = new StackPanel().Horizontal(4.0f).Children(
            Chip("Metres", "Metres and centimetres.", () => tool.Units == MeasureUnits.Metric, () => tool.Units = MeasureUnits.Metric),
            Chip("Feet", "Feet and inches.", () => tool.Units == MeasureUnits.Imperial, () => tool.Units = MeasureUnits.Imperial));

        var list = new DynamicContent().Showing<object>(Key(() => (tool.Version, tool.Units, tool.Measurements.Count)), _ =>
        {
            if (tool.Measurements.Count == 0)
            {
                return new TextBlock().Text("None yet.").Foreground(theme.TextMuted);
            }
            var rows = new StackPanel().Vertical(4.0f);
            for (var i = 0; i < tool.Measurements.Count; i++)
            {
                var measurement = tool.Measurements[i];
                var remove = new Button
                {
                    IgnoreThemePadding = true,
                    Padding = new Thickness(6.0f, 0.0f, 6.0f, 0.0f),
                    Height = 22.0f
                }.Label("×").Variant(ButtonVariant.Subtle).OnClick(() => tool.Remove(measurement));
                remove.WithTooltip("Remove this measurement.");
                rows.Children.Add(new Grid().Columns("Auto, *, Auto", 6.0f).Rows("Auto").Children(
                    new TextBlock().Text($"{i + 1}. {measurement.Mode}").Foreground(theme.TextMuted).CenterV().GridColumn(0),
                    new TextBlock().Text(tool.Headline(measurement)).FontFamily(theme.FontFamilyMedium).CenterV().GridColumn(1),
                    remove.CenterV().GridColumn(2)));
            }
            return rows;
        });

        var clear = new Button().Label("Clear all").OnClick(tool.Clear);
        clear.BindPolled(b => b.IsEnabled, () => tool.Measurements.Count > 0 || tool.Current != null);

        var body = new StackPanel().Vertical(8.0f).Margin(theme.Spacing).Children(
            PreferencesManaView.Heading(theme, "Measure"),
            modes,
            directions,
            PreferencesManaView.Heading(theme, "Snap to"),
            snaps,
            stay,
            PreferencesManaView.Heading(theme, "Units"),
            units,
            PreferencesManaView.Heading(theme, "Measurements"),
            list,
            clear,
            new TextBlock().Text("Esc lets go of the one in progress, then puts the tool away. M turns it on and off.")
                .FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Wrap());

        return body.Scrollable().Scrolls(vertical: ScrollBarVisibility.Auto, horizontal: ScrollBarVisibility.Disabled);
    }

    private static Button Chip(string label, string tooltip, Func<bool> on, Action click)
    {
        var chip = new Button
        {
            IgnoreThemePadding = true,
            Padding = new Thickness(8.0f, 0.0f, 8.0f, 0.0f),
            Height = 24.0f
        }.Label(label).OnClick(click);
        chip.WithTooltip(tooltip);
        chip.BindPolled(b => b.Variant, () => on() ? ButtonVariant.Primary : ButtonVariant.Subtle);
        return chip;
    }

    // A fresh object whenever the parts change, so the list rebuilds then and only then.
    private static Func<object> Key<T>(Func<T> parts)
    {
        object? key = null;
        T last = default!;
        return () =>
        {
            var now = parts();
            if (key == null || !EqualityComparer<T>.Default.Equals(now, last))
            {
                last = now;
                key = new object();
            }
            return key;
        };
    }
}
