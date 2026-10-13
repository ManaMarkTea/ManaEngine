using ManaUI;
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// Swaps the whole editor for the game while playing: panels, docks and menu bar collapse away, and a
    /// single controls hint takes their place.
    /// <para>
    /// The editor is collapsed rather than merely hidden behind something, which is the same trick
    /// ManaUiLoadingOverlay uses and for a stronger reason here: a collapsed subtree is skipped for layout
    /// and for polled bindings, so while you are playing, the outliner is not walking the scene graph and
    /// the inspector is not reading a character's transform sixty times a second for a picture nobody can
    /// see. It also settles the input question outright -- there is no invisible panel left under the
    /// cursor to swallow a click meant for the game.
    /// </para>
    /// </summary>
    public sealed class ManaUiPlayOverlay : Panel
    {
        private readonly UiElement _editor;
        private readonly UiElement _hud;

        private readonly UiElement? _banner;

        public ManaUiPlayOverlay(UiElement editor, UiElement hud, Func<bool> isPlaying, UiElement? ejectedBanner = null)
        {
            _editor = editor;
            _hud = hud;
            _banner = ejectedBanner;

            Children.Add(editor);
            Children.Add(hud);
            if (ejectedBanner != null)
            {
                Children.Add(ejectedBanner);
            }

            editor.BindPolled(el => el.Visibility, () => isPlaying() ? Visibility.Collapsed : Visibility.Visible);
            hud.BindPolled(el => el.Visibility, () => isPlaying() ? Visibility.Visible : Visibility.Collapsed);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _editor.Measure(availableSize);
            _hud.Measure(availableSize);
            _banner?.Measure(availableSize);
            return availableSize;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var bounds = new UiRect(Vector2.Zero, finalSize);
            _editor.Arrange(bounds);
            _hud.Arrange(bounds);
            _banner?.Arrange(bounds);
            return finalSize;
        }

        /// <summary>
        /// A small Eject button in the top-right corner while playing, faint until the pointer comes near it: steps out
        /// of the match into the editor to inspect it (F8 does the same).
        /// </summary>
        public static UiElement EjectButton(UiTheme theme, Action eject)
        {
            var button = new Button("Inspect (F8)").Icon(new TextBlock().Text("\u23CF")).Variant(ButtonVariant.Subtle).OnClick(eject);
            button.Tooltip = "Step out of the game into the editor, with the match frozen. F8 again resumes it.";
            var corner = new Border().Rounded(8.0f).PaddingOf(4.0f).Right().Top().Margin(0.0f, 140.0f, 8.0f, 0.0f).Content(button);
            corner.Background = theme.Surface with { W = 0.0f };
            // Nearly invisible over the game; there when the pointer is near.
            corner.BindPolled(el => el.Opacity, () => button.IsPointerOver ? 1.0f : 0.12f);
            return corner;
        }

        /// <summary>While ejected: a bar over the editor saying the match is frozen, with Resume and Stop.</summary>
        public static UiElement EjectedBanner(UiTheme theme, Func<bool> isEjected, Action resume, Action stop)
        {
            var bar = new Border().Fill(theme.Accent with { W = 0.92f }).Rounded(8.0f).PaddingOf(12.0f, 6.0f)
                .CenterH().Bottom().Margin(0.0f, 0.0f, 0.0f, 40.0f)
                .Content(new StackPanel().Horizontal(10.0f).Children(
                    new TextBlock().Text("Game paused for inspection").Foreground(theme.TextOnAccent).CenterV(),
                    new Button("Resume (F8)").Icon(new TextBlock().Text("\u25B6")).OnClick(resume),
                    new Button("Stop").Icon(new TextBlock().Text("\u23F9")).OnClick(stop)));
            bar.BindPolled(el => el.Visibility, () => isEjected() ? Visibility.Visible : Visibility.Collapsed);
            return bar;
        }
    }

    /// <summary>
    /// What is still on screen while playing: what the keys do and how to get back out, plus -- when a
    /// gameplay line is given -- the score, health and clock along the top.
    /// </summary>
    public static class ManaUiPlayHud
    {
        public static UiElement Build(UiTheme theme, Func<string> status, Func<string>? gameplay = null, UiElement? results = null, UiElement? death = null)
        {
            // Wrapped and centred, inside side margins: a phone held upright is narrower than either line.
            var text = new TextBlock()
                .FontSize(theme.FontSize)
                .Foreground(new Vector4(0.94f, 0.96f, 1.0f, 0.92f))
                .Wrap()
                .Align(TextAlignment.Center)
                .CenterH();

            text.BindPolled(t => t.Text, status);

            var pill = new Border()
                .Fill(new Vector4(0.05f, 0.06f, 0.09f, 0.62f))
                .Rounded(10.0f)
                .Outline(new Vector4(1.0f, 1.0f, 1.0f, 0.10f))
                .PaddingOf(14.0f, 8.0f)
                .Content(text)
                .Margin(12.0f, 0.0f, 12.0f, 22.0f)
                .CenterH()
                .Bottom();

            // Nothing here may eat a click meant for the game behind it.
            pill.IsHitTestVisible = false;
            text.IsHitTestVisible = false;

            if (gameplay == null)
            {
                return pill;
            }

            var scoreText = new TextBlock()
                .FontSize(theme.FontSize + 3.0f)
                .Foreground(new Vector4(1.0f, 0.97f, 0.82f, 0.96f))
                .Wrap()
                .Align(TextAlignment.Center)
                .CenterH();
            scoreText.BindPolled(t => t.Text, gameplay);

            var scorePill = new Border()
                .Fill(new Vector4(0.05f, 0.06f, 0.09f, 0.62f))
                .Rounded(10.0f)
                .Outline(new Vector4(1.0f, 1.0f, 1.0f, 0.10f))
                .PaddingOf(16.0f, 8.0f)
                .Content(scoreText)
                .Margin(12.0f, 18.0f, 12.0f, 0.0f)
                .CenterH()
                .Top();
            scorePill.IsHitTestVisible = false;
            scoreText.IsHitTestVisible = false;
            // Nothing to report (a debug camera has no coins or clock): no empty box either.
            scorePill.BindPolled(el => el.Visibility, () => string.IsNullOrEmpty(gameplay()) ? Visibility.Collapsed : Visibility.Visible);

            // A grid with no tracks is a single cell, so both pills overlay it and each aligns itself.
            var overlay = new Grid();
            overlay.Children.Add(pill);
            overlay.Children.Add(scorePill);

            // Under the results, over the score: a death while the level-complete panel is up is impossible,
            // but the order says which one wins if it ever happens.
            if (death != null)
            {
                overlay.Children.Add(death);
            }

            // Left hit-testable: the grid has no background so it never catches a click itself, but
            // switching it off would take the results screen's buttons with it.
            if (results != null)
            {
                overlay.Children.Add(results);
            }

            return overlay;
        }
    }
}
