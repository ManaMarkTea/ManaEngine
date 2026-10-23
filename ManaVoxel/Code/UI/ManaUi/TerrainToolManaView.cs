using Mana;
using Mana.Code.Nodes;
using ManaUI;
using ManaVoxel.Code.Terrain;
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi;

/// <summary>
/// The Terrain window, up while a terrain is selected: Sculpt on or off, the brush, its size, strength and falloff,
/// and the layer Paint lays down.
/// </summary>
public static class TerrainToolManaView
{
    public const string PanelId = "terrain";

    public static UiElement Build(UiTheme theme, ManaWorld world)
    {
        ManaTerrainNode? Selected() => world.Selection.SelectedNode as ManaTerrainNode;

        var sculpt = new Button().Label("Sculpt").OnClick(() =>
        {
            if (Selected() is not { } terrain)
            {
                return;
            }

            if (TerrainEditing.IsSculpting(terrain))
            {
                TerrainEditing.Stop();
            }
            else
            {
                TerrainEditing.Begin(terrain);
            }
        });
        sculpt.WithTooltip("Drag over the terrain to sculpt it. Off, clicks select and move things as usual. Esc turns it off.");
        sculpt.BindPolled(b => b.Variant, () =>
        {
            if (Selected() is { } terrain && TerrainEditing.IsSculpting(terrain))
            {
                return ButtonVariant.Primary;
            }
            return ButtonVariant.Standard;
        });

        var brushes = new WrapPanel { Spacing = 4.0f };
        foreach (var (brush, label, tip) in new[]
        {
            (ManaTerrainBrush.Raise, "Raise", "Builds the ground up. Hold Ctrl to lower instead."),
            (ManaTerrainBrush.Lower, "Lower", "Digs the ground down. Hold Ctrl to raise instead."),
            (ManaTerrainBrush.Smooth, "Smooth", "Evens out bumps. Hold Shift with any brush to smooth."),
            (ManaTerrainBrush.Flatten, "Flatten", "Levels the ground to the height where the stroke began -- for paths and building plots."),
            (ManaTerrainBrush.Noise, "Noise", "Roughens the ground with small bumps."),
            (ManaTerrainBrush.Paint, "Paint", "Paints the layer chosen below: grass, dirt, rock or sand."),
        })
        {
            brushes.Children.Add(Chip(label, tip, () => TerrainEditing.Brush == brush, () =>
            {
                TerrainEditing.Brush = brush;
                if (Selected() is { } terrain)
                {
                    TerrainEditing.Begin(terrain);
                }
            }));
        }

        var layers = new WrapPanel { Spacing = 4.0f };
        for (var i = 0; i < TerrainEditing.LayerNames.Length; i++)
        {
            var layer = i;
            layers.Children.Add(Chip(TerrainEditing.LayerNames[i], $"Paint lays down {TerrainEditing.LayerNames[i].ToLowerInvariant()}.",
                () => TerrainEditing.Layer == layer, () => TerrainEditing.Layer = layer));
        }
        layers.BindPolled(l => l.Visibility, () =>
        {
            if (TerrainEditing.Brush == ManaTerrainBrush.Paint)
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        });

        var body = new StackPanel().Vertical(8.0f).Margin(theme.Spacing).Children(
            sculpt,
            PreferencesManaView.Heading(theme, "Brush"),
            brushes,
            layers,
            Setting(theme, "Size", 0.5f, 32.0f, () => TerrainEditing.Size, v => TerrainEditing.Size = v, "{0:0.#} m"),
            Setting(theme, "Strength", 0.05f, 1.0f, () => TerrainEditing.Strength, v => TerrainEditing.Strength = v, "{0:0.00}"),
            Setting(theme, "Falloff", 0.0f, 1.0f, () => TerrainEditing.Falloff, v => TerrainEditing.Falloff = v, "{0:0.00}"),
            new TextBlock().Text("Drag to sculpt; each stroke is one undo. Ctrl swaps raise and lower, Shift smooths.")
                .FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Wrap());

        return body.Scrollable().Scrolls(vertical: ScrollBarVisibility.Auto, horizontal: ScrollBarVisibility.Disabled);
    }

    private static UiElement Setting(UiTheme theme, string label, float minimum, float maximum, Func<float> read, Action<float> write, string format)
    {
        var slider = new Slider()
            .Range(minimum, maximum, (maximum - minimum) / 100.0f)
            .OnChanged(write)
            .BindPolled(s => s.Value, read);
        var value = new TextBlock().Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall).CenterV()
            .BindPolled(t => t.Text, () => string.Format(format, read()));
        return new Grid().Columns("64, *, 52", 6.0f).Rows("Auto").Children(
            new TextBlock().Text(label).CenterV().GridColumn(0),
            slider.CenterV().GridColumn(1),
            value.GridColumn(2));
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
        chip.BindPolled(b => b.Variant, () =>
        {
            if (on())
            {
                return ButtonVariant.Primary;
            }
            return ButtonVariant.Subtle;
        });
        return chip;
    }
}
