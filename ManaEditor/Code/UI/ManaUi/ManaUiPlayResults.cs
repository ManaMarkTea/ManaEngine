using Mana.Code;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi
{
    /// <summary>
    /// What is on screen when the level is won: how it went, and two ways forward. Shown and hidden by
    /// polling the game state, so nothing has to remember to raise it.
    /// </summary>
    public static class ManaUiPlayResults
    {
        public static UiElement Build(UiTheme theme, ManaGameState state, Action playAgain, Action exit, string exitLabel)
        {
            var title = new TextBlock()
                .FontSize(theme.FontSize + 12.0f)
                .Foreground(new Vector4(1.0f, 0.93f, 0.55f, 1.0f))
                .Text("Level Complete!")
                .CenterH();

            var lines = new StackPanel().Vertical(4.0f);
            lines.Children.Add(Line(theme, () => $"Time  {FormatTime(state.ElapsedSeconds)}"));
            lines.Children.Add(Line(theme, () => state.TotalCoins > 0 ? $"Coins  {state.Coins} / {state.TotalCoins}" : string.Empty));
            lines.Children.Add(Line(theme, () => state.TotalEnemies > 0 ? $"Enemies defeated  {state.EnemiesDefeated} / {state.TotalEnemies}" : string.Empty));
            lines.Children.Add(Line(theme, () => state.Deaths > 0 ? $"Deaths  {state.Deaths}" : string.Empty));
            lines.Children.Add(Line(theme, () => Verdict(state)));

            var buttons = new StackPanel().Horizontal(12.0f).CenterH();
            buttons.Children.Add(new ManaUI.Button().Label("Play Again").Variant(ButtonVariant.Primary).OnClick(playAgain));
            buttons.Children.Add(new ManaUI.Button().Label(exitLabel).Variant(ButtonVariant.Subtle).OnClick(exit));

            var column = new StackPanel().Vertical(16.0f);
            column.Children.Add(title);
            column.Children.Add(lines);
            column.Children.Add(buttons);

            var panel = new Border()
                .Fill(new Vector4(0.05f, 0.06f, 0.09f, 0.88f))
                .Rounded(14.0f)
                .Outline(new Vector4(1.0f, 1.0f, 1.0f, 0.14f))
                .PaddingOf(34.0f, 26.0f)
                .Content(column)
                .Center();

            panel.BindPolled(
                el => el.Visibility,
                () => state.Status == ManaGameStatus.Won ? Visibility.Visible : Visibility.Collapsed);
            return panel;
        }

        /// <summary>A line that takes no room while it has nothing to say, so a level without coins shows no coin row.</summary>
        private static UiElement Line(UiTheme theme, Func<string> text)
        {
            var block = new TextBlock()
                .FontSize(theme.FontSize + 2.0f)
                .Foreground(new Vector4(0.94f, 0.96f, 1.0f, 0.95f))
                .CenterH();
            block.BindPolled(t => t.Text, text);
            block.BindPolled(t => t.Visibility, () => text().Length > 0 ? Visibility.Visible : Visibility.Collapsed);
            return block;
        }

        public static string FormatTime(float seconds)
        {
            var minutes = (int)(seconds / 60.0f);
            var rest = seconds - (minutes * 60.0f);
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{minutes}:{rest:00.0}");
        }

        /// <summary>One line of praise, for the run that earned it.</summary>
        public static string Verdict(ManaGameState state)
        {
            var allCoins = state.TotalCoins == 0 || state.Coins >= state.TotalCoins;
            var allEnemies = state.TotalEnemies == 0 || state.EnemiesDefeated >= state.TotalEnemies;

            if (allCoins && allEnemies && state.Deaths == 0 && (state.TotalCoins > 0 || state.TotalEnemies > 0))
            {
                return "Flawless!";
            }

            return allCoins && state.TotalCoins > 0 ? "Every coin collected." : string.Empty;
        }
    }
}
