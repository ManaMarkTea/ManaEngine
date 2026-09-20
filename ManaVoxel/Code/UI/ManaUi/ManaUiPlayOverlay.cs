using ManaUI;

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

        public ManaUiPlayOverlay(UiElement editor, UiElement hud, Func<bool> isPlaying)
        {
            _editor = editor;
            _hud = hud;

            Children.Add(editor);
            Children.Add(hud);

            editor.BindPolled(el => el.Visibility, () => isPlaying() ? Visibility.Collapsed : Visibility.Visible);
            hud.BindPolled(el => el.Visibility, () => isPlaying() ? Visibility.Visible : Visibility.Collapsed);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            _editor.Measure(availableSize);
            _hud.Measure(availableSize);
            return availableSize;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var bounds = new UiRect(Vector2.Zero, finalSize);
            _editor.Arrange(bounds);
            _hud.Arrange(bounds);
            return finalSize;
        }
    }

    /// <summary>
    /// What is still on screen while playing: what the keys do and how to get back out, plus -- when a
    /// gameplay line is given -- the score, health and clock along the top.
    /// </summary>
    public static class ManaUiPlayHud
    {
        public static UiElement Build(UiTheme theme, Func<string> status, Func<string>? gameplay = null, UiElement? results = null)
        {
            var text = new TextBlock()
                .FontSize(theme.FontSize)
                .Foreground(new Vector4(0.94f, 0.96f, 1.0f, 0.92f))
                .CenterH();

            text.BindPolled(t => t.Text, status);

            var pill = new Border()
                .Fill(new Vector4(0.05f, 0.06f, 0.09f, 0.62f))
                .Rounded(10.0f)
                .Outline(new Vector4(1.0f, 1.0f, 1.0f, 0.10f))
                .PaddingOf(14.0f, 8.0f)
                .Content(text)
                .Margin(0.0f, 0.0f, 0.0f, 22.0f)
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
                .CenterH();
            scoreText.BindPolled(t => t.Text, gameplay);

            var scorePill = new Border()
                .Fill(new Vector4(0.05f, 0.06f, 0.09f, 0.62f))
                .Rounded(10.0f)
                .Outline(new Vector4(1.0f, 1.0f, 1.0f, 0.10f))
                .PaddingOf(16.0f, 8.0f)
                .Content(scoreText)
                .Margin(0.0f, 18.0f, 0.0f, 0.0f)
                .CenterH()
                .Top();
            scorePill.IsHitTestVisible = false;
            scoreText.IsHitTestVisible = false;

            // A grid with no tracks is a single cell, so both pills overlay it and each aligns itself.
            var overlay = new Grid();
            overlay.Children.Add(pill);
            overlay.Children.Add(scorePill);

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
