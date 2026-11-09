using ManaCore.Imaging;
using ManaUI;
using ManaEditor.Code.Levels;
using Button = ManaUI.Button;

namespace ManaEditor.Code.UI.ManaUi
{
    /// <summary>
    /// The start screen and pause menu of a play-only app (the phone): a column of levels to pick from, each with its
    /// picture, and in play a pause button that offers Resume, Restart and Choose Level. Shown by polling, so the game
    /// mode only keeps two flags.
    /// </summary>
    public static class ManaUiPlayMenus
    {
        private static readonly Vector4 Backdrop = new(0.03f, 0.04f, 0.07f, 0.94f);
        private static readonly Vector4 CardFill = new(0.10f, 0.12f, 0.17f, 1.0f);

        public static UiElement LevelSelect(UiTheme theme, IReadOnlyList<ManaPlayableLevel> levels, Action<ManaPlayableLevel> pick, Func<bool> visible,
            Func<Thickness>? safeArea = null)
        {
            var column = new StackPanel().Vertical(12.0f);
            column.Children.Add(new TextBlock().Text("Mana").FontSize(theme.FontSize + 22.0f)
                .Foreground(new Vector4(1.0f, 0.93f, 0.6f, 1.0f)).CenterH().Margin(0.0f, 12.0f, 0.0f, 0.0f));
            column.Children.Add(new TextBlock().Text("Choose a level").FontSize(theme.FontSize + 2.0f)
                .Foreground(new Vector4(0.8f, 0.84f, 0.92f, 1.0f)).CenterH().Margin(0.0f, 0.0f, 0.0f, 8.0f));
            if (levels.Count == 0)
            {
                column.Children.Add(new TextBlock().Text("No levels are published to play (scene Inspector > Publishing).").Wrap().CenterH());
            }
            foreach (var level in levels)
            {
                column.Children.Add(Card(theme, level, pick));
            }

            // Stretched across the screen with a margin, scrolling when the list is taller than it.
            var list = column.Margin(16.0f, 24.0f, 16.0f, 24.0f).Scrollable().Scrolls(vertical: ScrollBarVisibility.Auto, horizontal: ScrollBarVisibility.Disabled);
            // The backdrop runs to the screen's edges; the list keeps out of a notch and the home bar.
            if (safeArea != null)
            {
                list.BindPolled(el => el.Margin, safeArea);
            }
            var screen = new Border().Fill(Backdrop).Content(list);
            screen.BindPolled(el => el.Visibility, () => visible() ? Visibility.Visible : Visibility.Collapsed);
            return screen;
        }

        private static UiElement Card(UiTheme theme, ManaPlayableLevel level, Action<ManaPlayableLevel> pick)
        {
            var text = new StackPanel().Vertical(4.0f).CenterV();
            text.Children.Add(new TextBlock().Text(level.Name).FontSize(theme.FontSize + 4.0f).Foreground(new Vector4(1.0f, 1.0f, 1.0f, 1.0f)).Wrap());
            if (!string.IsNullOrWhiteSpace(level.Description))
            {
                text.Children.Add(new TextBlock().Text(level.Description).Foreground(new Vector4(0.75f, 0.79f, 0.87f, 1.0f)).Wrap());
            }

            var row = new Grid().Columns($"{PictureWidth}, *", 14.0f).Rows("Auto");
            row.Children.Add(Picture(level).GridColumn(0));
            row.Children.Add(text.GridColumn(1));

            var card = new Button { Variant = ButtonVariant.Subtle, HorizontalAlignment = HorizontalAlignment.Stretch }
                .Icon(new Border().Fill(CardFill).Rounded(12.0f).PaddingOf(10.0f).Content(row))
                .OnClick(() => pick(level));
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            return card;
        }

        private const float PictureWidth = 132.0f;
        private const float PictureHeight = 74.0f;

        /// <summary>The level's picture, scaled down once and kept in the UI atlas (re-added if the atlas is rebuilt).</summary>
        private static UiElement Picture(ManaPlayableLevel level)
        {
            var image = new UiImage { Fit = UiImageFit.Cover, Width = PictureWidth, Height = PictureHeight, Corners = new CornerRadius(8.0f) };
            if (level.Picture == null)
            {
                return image;
            }

            ManaImage? small = null;
            var generation = -1;
            image.BindPolled(el => el.Visibility, () =>
            {
                if (image.Root is not { } root)
                {
                    return Visibility.Visible;
                }
                if (generation != root.Images.Generation)
                {
                    try
                    {
                        small ??= Shrink(ManaImageCodec.Load(level.Picture), 288);
                        if (root.Images.Add(small.Pixels, small.Width, small.Height) is { } uv)
                        {
                            image.SourceUv = uv;
                            image.SourceSize = new Vector2(small.Width, small.Height);
                        }
                    }
                    catch (Exception)
                    {
                        // No picture is no reason not to offer the level.
                    }
                    generation = root.Images.Generation;
                }
                return Visibility.Visible;
            });
            return image;
        }

        /// <summary>Averages the picture down to <paramref name="width"/> pixels across: a 1080p shot is far more than a card shows.</summary>
        private static ManaImage Shrink(ManaImage source, int width)
        {
            if (source.Width <= width)
            {
                return source;
            }
            var height = Math.Max(1, source.Height * width / source.Width);
            var result = new ManaImage(width, height);
            for (var y = 0; y < height; y++)
            {
                var y0 = y * source.Height / height;
                var y1 = Math.Max(y0 + 1, (y + 1) * source.Height / height);
                for (var x = 0; x < width; x++)
                {
                    var x0 = x * source.Width / width;
                    var x1 = Math.Max(x0 + 1, (x + 1) * source.Width / width);
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (var sy = y0; sy < y1; sy++)
                    {
                        for (var sx = x0; sx < x1; sx++)
                        {
                            var i = (sy * source.Width + sx) * 4;
                            r += source.Pixels[i];
                            g += source.Pixels[i + 1];
                            b += source.Pixels[i + 2];
                            a += source.Pixels[i + 3];
                            n++;
                        }
                    }
                    var o = (y * width + x) * 4;
                    result.Pixels[o] = (byte)(r / n);
                    result.Pixels[o + 1] = (byte)(g / n);
                    result.Pixels[o + 2] = (byte)(b / n);
                    result.Pixels[o + 3] = (byte)(a / n);
                }
            }
            return result;
        }

        /// <summary>The pause button (top left, in play) and the panel it opens.</summary>
        public static UiElement Pause(UiTheme theme, Func<bool> playing, Func<bool> paused, Action pause, Action resume, Action restart, Action chooseLevel)
        {
            var button = new Button { Padding = new Thickness(14.0f, 6.0f, 14.0f, 6.0f) }.Label("II").Variant(ButtonVariant.Subtle).OnClick(pause);
            var corner = new Border().Fill(new Vector4(0.05f, 0.06f, 0.09f, 0.62f)).Rounded(10.0f).PaddingOf(2.0f)
                .Content(button).Left().Top().Margin(14.0f, 76.0f, 0.0f, 0.0f);
            corner.BindPolled(el => el.Visibility, () => playing() && !paused() ? Visibility.Visible : Visibility.Collapsed);

            var buttons = new StackPanel().Vertical(12.0f);
            buttons.Children.Add(new TextBlock().Text("Paused").FontSize(theme.FontSize + 10.0f).Foreground(new Vector4(1.0f, 0.93f, 0.6f, 1.0f)).CenterH());
            buttons.Children.Add(MenuButton("Resume", ButtonVariant.Primary, resume));
            buttons.Children.Add(MenuButton("Restart", ButtonVariant.Standard, restart));
            buttons.Children.Add(MenuButton("Choose Level", ButtonVariant.Standard, chooseLevel));
            var panel = new Border().Fill(new Vector4(0.05f, 0.06f, 0.09f, 0.9f)).Rounded(14.0f).Outline(new Vector4(1.0f, 1.0f, 1.0f, 0.14f))
                .PaddingOf(30.0f, 24.0f).Content(buttons).Center();
            panel.BindPolled(el => el.Visibility, () => playing() && paused() ? Visibility.Visible : Visibility.Collapsed);

            var layer = new Grid();
            layer.Children.Add(corner);
            layer.Children.Add(panel);
            return layer;
        }

        /// <summary>
        /// The strip along the bottom a phone's on-screen controls sit on (the platform draws the stick and buttons
        /// over it): the game's picture ends at its top edge, so thumbs never cover the action.
        /// </summary>
        public static UiElement ControlsBar(UiTheme theme, Func<float> height)
        {
            var edge = new Border().Fill(new Vector4(1.0f, 1.0f, 1.0f, 0.2f)).Top();
            edge.Height = 1.5f;
            var glow = new Border().Fill(new Vector4(0.2f, 0.19f, 0.34f, 0.55f)).Top();
            glow.Height = 18.0f;
            var bar = new Grid();
            bar.Children.Add(new Border().Fill(new Vector4(0.08f, 0.07f, 0.14f, 1.0f)));
            bar.Children.Add(glow);
            bar.Children.Add(edge);
            bar.VerticalAlignment = VerticalAlignment.Bottom;
            bar.IsHitTestVisible = false;
            bar.BindPolled(el => el.Height, height);
            bar.BindPolled(el => el.Visibility, () => height() > 0.0f ? Visibility.Visible : Visibility.Collapsed);
            return bar;
        }

        private static Button MenuButton(string label, ButtonVariant variant, Action click)
        {
            var button = new Button { Width = 220.0f, HorizontalAlignment = HorizontalAlignment.Center }.Label(label).Variant(variant).OnClick(click);
            return button;
        }
    }
}
