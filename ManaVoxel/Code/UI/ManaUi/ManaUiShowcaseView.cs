using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// A standing demo of what ManaUI can currently draw, built with the same fluent API real panels will
    /// use. It exists to make regressions in layout, text and the SDF primitive obvious at a glance while
    /// the framework is still being built out.
    /// </summary>
    public static class ManaUiShowcaseView
    {
        public static UiElement Build(UiTheme theme)
        {
            // Anchored to the top-left of a full-bleed canvas rather than filling the window, so the 3D
            // viewport underneath stays visible.
            var canvas = new Canvas();

            var panel = BuildPanel(theme);
            panel.EnsureLayoutSlot().CanvasLeft = 24.0f;
            panel.EnsureLayoutSlot().CanvasTop = 80.0f;
            canvas.Children.Add(panel);

            return canvas;
        }

        private static UiElement BuildPanel(UiTheme theme)
        {
            var body = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = theme.Spacing
            };

            body.Children.Add(new TextBlock("ManaUI")
            {
                FontSize = theme.FontSizeHeading,
                FontFamily = theme.FontFamilyMedium
            });

            body.Children.Add(new TextBlock("Retained-mode widgets, WPF-style layout, one draw call.")
            {
                Foreground = theme.TextMuted,
                FontSize = theme.FontSizeSmall
            });

            body.Children.Add(Divider(theme));

            // Rounded corners, borders and shadows all come from the same instanced primitive -- these
            // swatches are the visual proof that the SDF path is correct.
            body.Children.Add(new TextBlock("Primitives") { Foreground = theme.TextMuted, FontSize = theme.FontSizeSmall });
            body.Children.Add(SwatchRow(theme));

            body.Children.Add(Divider(theme));

            // Grid star sizing, exercised where a regression would be visible immediately.
            body.Children.Add(new TextBlock("Grid: Auto | * | 2*") { Foreground = theme.TextMuted, FontSize = theme.FontSizeSmall });
            body.Children.Add(GridSample(theme));

            body.Children.Add(Divider(theme));

            body.Children.Add(new TextBlock("Text wrapping and ellipsis") { Foreground = theme.TextMuted, FontSize = theme.FontSizeSmall });
            body.Children.Add(new TextBlock(
                "Two-pass layout means an Auto row containing wrapped text sizes correctly, because the "
                + "text is measured at its final width rather than guessed at.")
            {
                TextWrapping = TextWrapping.Wrap,
                Width = 320.0f
            });

            body.Children.Add(new Border
            {
                Background = theme.SurfaceAlt,
                CornerRadius = theme.CornerRadius,
                Padding = theme.ControlPadding,
                Width = 320.0f,
                Child = new TextBlock("This single line is far too long for the box it lives in, so it trims.")
                {
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = theme.TextMuted
                }
            });

            return new Border
            {
                Background = theme.Surface,
                BorderColor = theme.Border,
                BorderThickness = theme.BorderThickness,
                CornerRadius = theme.CornerRadiusLarge,
                Padding = new Thickness(16.0f),
                ShadowBlur = 18.0f,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = body
            };
        }

        private static UiElement Divider(UiTheme theme)
        {
            return new Border
            {
                Height = 1.0f,
                Background = theme.Divider,
                Margin = new Thickness(0.0f, 4.0f)
            };
        }

        private static UiElement SwatchRow(UiTheme theme)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = theme.Spacing
            };

            row.Children.Add(Swatch(theme.Accent, new CornerRadius(4.0f), 0.0f, theme));
            row.Children.Add(Swatch(theme.Success, new CornerRadius(16.0f), 0.0f, theme));
            row.Children.Add(Swatch(theme.Warning, new CornerRadius(16.0f, 4.0f, 16.0f, 4.0f), 0.0f, theme));
            row.Children.Add(Swatch(UiColor.Transparent, new CornerRadius(8.0f), 2.0f, theme));
            row.Children.Add(Swatch(theme.Danger, new CornerRadius(999.0f), 0.0f, theme));

            return row;
        }

        private static UiElement Swatch(Vector4 fill, CornerRadius corners, float borderThickness, UiTheme theme)
        {
            return new Border
            {
                Width = 48.0f,
                Height = 32.0f,
                Background = fill,
                CornerRadius = corners,
                BorderThickness = borderThickness,
                BorderColor = theme.BorderStrong
            };
        }

        private static UiElement GridSample(UiTheme theme)
        {
            var grid = new Grid { ColumnSpacing = 4.0f, Width = 320.0f };
            grid.SetColumns("64, *, 2*");
            grid.SetRows("Auto");

            grid.Children.Add(Cell("64px", theme, 0));
            grid.Children.Add(Cell("1 share", theme, 1));
            grid.Children.Add(Cell("2 shares", theme, 2));

            return grid;
        }

        private static UiElement Cell(string label, UiTheme theme, int column)
        {
            var cell = new Border
            {
                Background = theme.SurfaceAlt,
                CornerRadius = theme.CornerRadius,
                Padding = new Thickness(6.0f, 4.0f),
                Child = new TextBlock(label)
                {
                    Foreground = theme.TextMuted,
                    FontSize = theme.FontSizeSmall,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };

            cell.EnsureLayoutSlot().GridColumn = column;
            return cell;
        }
    }
}
