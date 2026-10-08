using ManaUI;
using ManaVoxel.Code.Gameplay.TrainYardWars.Rules;

namespace ManaVoxel.Code.Gameplay.TrainYardWars.View
{
    /// <summary>What the HUD reads each frame; the game mode keeps it current.</summary>
    internal sealed class TywHudState
    {
        public TywGame? Game { get; set; }
        public string Hint { get; set; } = "";
        public string Message { get; set; } = "";
        public float MessageUntil { get; set; }
        public bool Paused { get; set; }
        public float Speed { get; set; } = 1.0f;
        public bool Autoplay { get; set; }
    }

    /// <summary>
    /// Train Yard Wars' screen furniture: the scoreboard, the clock, a running log, what a click would do, and the
    /// winner. All of it is see-through to the mouse; the board under it is the game.
    /// </summary>
    internal static class TywHud
    {
        private static readonly Vector4 Pill = new(0.05f, 0.06f, 0.09f, 0.70f);
        private static readonly Vector4 TextColor = new(0.95f, 0.96f, 1.0f, 0.95f);
        private static readonly Vector4 Muted = new(0.75f, 0.78f, 0.85f, 0.85f);

        public static UiElement Build(UiTheme theme, TywHudState state)
        {
            var overlay = new Grid();
            overlay.Children.Add(Scoreboard(theme, state));
            overlay.Children.Add(Clock(theme, state));
            overlay.Children.Add(EventLog(theme, state));
            overlay.Children.Add(Hint(theme, state));
            overlay.Children.Add(Controls(theme));
            overlay.Children.Add(Results(theme, state));
            foreach (var element in Walk(overlay))
            {
                element.IsHitTestVisible = false;
            }

            return overlay;
        }

        private static IEnumerable<UiElement> Walk(UiElement element)
        {
            yield return element;
            var children = element switch
            {
                Panel panel => panel.Children.ToList(),
                Border { Child: { } child } => new List<UiElement> { child },
                _ => new List<UiElement>()
            };

            foreach (var child in children)
            {
                foreach (var inner in Walk(child))
                {
                    yield return inner;
                }
            }
        }

        private static Border Box(UiElement content) =>
            new Border().Fill(Pill).Rounded(10.0f).Outline(new Vector4(1, 1, 1, 0.1f)).PaddingOf(14.0f, 10.0f).Content(content);

        private static TextBlock Label(UiTheme theme, Func<string> text, float sizeDelta = 0.0f, Vector4? color = null)
        {
            var block = new TextBlock().FontSize(theme.FontSize + sizeDelta).Foreground(color ?? TextColor);
            block.BindPolled(t => t.Text, text);
            return block;
        }

        private static Border Swatch(Vector4 color, float size = 12.0f) =>
            new Border().Fill(color).Rounded(3.0f).Size(size, size).CenterV();

        private static UiElement Scoreboard(UiTheme theme, TywHudState state)
        {
            var rows = new StackPanel().Vertical(6.0f);
            var header = new StackPanel().Horizontal(0.0f);
            header.Children.Add(new TextBlock().Text("").Width(108.0f));
            foreach (var name in new[] { "Wood", "Stone", "Grain", "Coal" })
            {
                header.Children.Add(new TextBlock().Text(name).FontSize(theme.FontSizeSmall - 1.0f).Foreground(Muted).Width(46.0f));
            }

            header.Children.Add(new TextBlock().Text("Trains").FontSize(theme.FontSizeSmall - 1.0f).Foreground(Muted).Width(52.0f));
            header.Children.Add(new TextBlock().Text("VP").FontSize(theme.FontSizeSmall - 1.0f).Foreground(Muted).Width(40.0f));
            rows.Children.Add(header);

            for (var index = 0; index < TywGame.PlayerNames.Length; index++)
            {
                var p = index;
                var row = new StackPanel().Horizontal(0.0f);
                row.Children.Add(Swatch(TywBoardView.PlayerColors[p], 14.0f).Margin(0, 0, 8, 0));
                row.Children.Add(Label(theme, () =>
                {
                    var game = state.Game;
                    if (game == null || p >= game.Players.Count)
                    {
                        return "";
                    }

                    var player = game.Players[p];
                    return player.IsBot && !(p == 0 && state.Autoplay) ? $"{player.Name} (bot)" : $"{player.Name} (you)";
                }).Width(86.0f));
                for (var r = 0; r < 4; r++)
                {
                    var resource = r;
                    var cell = new StackPanel().Horizontal(4.0f).Width(46.0f);
                    cell.Children.Add(Swatch(TywBoardView.ResourceColors[resource], 10.0f));
                    cell.Children.Add(Label(theme, () =>
                    {
                        var game = state.Game;
                        return game == null || p >= game.Players.Count ? "" : game.Players[p].Bank[resource].ToString();
                    }));
                    row.Children.Add(cell);
                }

                row.Children.Add(Label(theme, () =>
                {
                    var game = state.Game;
                    return game == null || p >= game.Players.Count ? "" : game.TrainCount(p).ToString();
                }).Width(52.0f));
                row.Children.Add(Label(theme, () =>
                {
                    var game = state.Game;
                    return game == null || p >= game.Players.Count ? "" : game.VictoryPoints(p).ToString();
                }, 2.0f, new Vector4(1.0f, 0.92f, 0.6f, 1.0f)).Width(40.0f));
                row.BindPolled(el => el.Visibility, () =>
                    state.Game != null && p < state.Game.Players.Count ? Visibility.Visible : Visibility.Collapsed);
                rows.Children.Add(row);
            }

            rows.Children.Add(Label(theme, () =>
            {
                var game = state.Game;
                if (game == null)
                {
                    return "";
                }

                var network = game.NetworkHolder >= 0 ? game.Players[game.NetworkHolder].Name : "--";
                var fleet = game.FleetHolder >= 0 ? game.Players[game.FleetHolder].Name : "--";
                return $"Biggest Network: {network}    Biggest Fleet: {fleet}";
            }, -2.0f, Muted).Margin(0, 4, 0, 0));

            return Box(rows).Left().Top().Margin(16.0f);
        }

        private static UiElement Clock(UiTheme theme, TywHudState state)
        {
            var text = Label(theme, () =>
            {
                var game = state.Game;
                if (game == null)
                {
                    return "";
                }

                var left = TimeSpan.FromSeconds(MathF.Ceiling(game.TimeLeft));
                var extra = state.Paused ? "   PAUSED" : state.Speed != 1.0f ? $"   x{state.Speed:0}" : "";
                return $"{left:m\\:ss}   First to {game.VictoryTarget} VP{extra}";
            }, 4.0f);
            return Box(text).CenterH().Top().Margin(0, 16, 0, 0);
        }

        private static UiElement EventLog(UiTheme theme, TywHudState state)
        {
            var lines = new StackPanel().Vertical(3.0f);
            for (var i = 0; i < 7; i++)
            {
                var back = 6 - i;
                var line = Label(theme, () =>
                {
                    var log = state.Game?.Log;
                    if (log == null || log.Count <= back)
                    {
                        return "";
                    }

                    var entry = log[log.Count - 1 - back];
                    return $"{TimeSpan.FromSeconds(entry.Time):m\\:ss}  {entry.Text}";
                }, -2.0f);
                line.BindPolled(t => t.Foreground, () =>
                {
                    var log = state.Game?.Log;
                    if (log == null || log.Count <= back)
                    {
                        return (Vector4?)TextColor;
                    }

                    var color = TywBoardView.PlayerColors[log[log.Count - 1 - back].Player];
                    return (Vector4?)Vector4.Lerp(color, Vector4.One, 0.45f);
                });
                line.BindPolled(el => el.Visibility, () =>
                    state.Game?.Log is { } log && log.Count > back ? Visibility.Visible : Visibility.Collapsed);
                lines.Children.Add(line);
            }

            var box = Box(lines).Right().Top().Margin(16.0f).Width(380.0f);
            box.BindPolled(el => el.Visibility, () => state.Game is { Log.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed);
            return box;
        }

        private static UiElement Hint(UiTheme theme, TywHudState state)
        {
            var text = Label(theme, () =>
            {
                var game = state.Game;
                return game != null && game.Time < state.MessageUntil && state.Message.Length > 0 ? state.Message : state.Hint;
            }, 1.0f);
            var box = Box(text).CenterH().Bottom().Margin(0, 0, 0, 62.0f);
            box.BindPolled(el => el.Visibility, () =>
                state.Hint.Length > 0 || (state.Game != null && state.Game.Time < state.MessageUntil) ? Visibility.Visible : Visibility.Collapsed);
            return box;
        }

        private static UiElement Controls(UiTheme theme)
        {
            var text = new TextBlock()
                .Text("Drag or click: lay track    Click your hex: extractor / factory / station / repair    Click a rival's hex: sabotage    " +
                      "T: buy train    Space: pause    F: speed    B: bot plays for you    N: new map    Esc: quit")
                .FontSize(theme.FontSizeSmall)
                .Foreground(Muted);
            return Box(text).CenterH().Bottom().Margin(0, 0, 0, 14.0f);
        }

        private static UiElement Results(UiTheme theme, TywHudState state)
        {
            var title = Label(theme, () =>
            {
                var game = state.Game;
                return game is { IsOver: true } ? $"{game.Players[game.Winner].Name} wins!" : "";
            }, 18.0f);
            title.BindPolled(t => t.Foreground, () =>
                state.Game is { IsOver: true } game ? (Vector4?)TywBoardView.PlayerColors[game.Winner] : TextColor);
            var standings = Label(theme, () =>
            {
                var game = state.Game;
                if (game is not { IsOver: true })
                {
                    return "";
                }

                return string.Join("\n", game.Players
                    .OrderByDescending(p => game.VictoryPoints(p.Index))
                    .Select(p => $"{p.Name}: {game.VictoryPoints(p.Index)} VP  ({game.VictoryBreakdown(p.Index)})"));
            }, 0.0f);
            var again = new TextBlock().Text("N for a new map, Esc to quit").FontSize(theme.FontSizeSmall).Foreground(Muted);
            var panel = new StackPanel().Vertical(12.0f).Children(title.CenterH(), standings.CenterH(), again.CenterH());
            var box = Box(panel).Center();
            box.BindPolled(el => el.Visibility, () => state.Game is { IsOver: true } ? Visibility.Visible : Visibility.Collapsed);
            return box;
        }
    }
}
