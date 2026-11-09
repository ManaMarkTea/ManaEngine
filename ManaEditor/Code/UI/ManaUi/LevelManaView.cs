using ManaUI;
using ManaEditor.Code.Levels;

// ManaEditor has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaEditor.Code.UI.ManaUi
{
    /// <summary>
    /// The Level panel: what is open, what is on disk, where the player starts, and Play.
    /// <para>
    /// A panel rather than only menu rows because the two things that need room -- typing a name to save
    /// under, and picking from the levels already saved -- are exactly the two a menu is worst at. The menu
    /// keeps the fixed verbs (New, Save, Play), which are the ones worth a keyboard shortcut.
    /// </para>
    /// </summary>
    public static class LevelManaView
    {
        public static UiElement Build(UiTheme theme, LevelViewModel levels)
        {
            var name = new TextBox { Text = levels.LevelName };
            name.TextChanged += (_, _) =>
            {
                if (!name.IsApplyingBinding)
                {
                    levels.LevelName = name.Text;
                }
            };

            // Polled back as well as pushed: saving and opening both rename the level, and a box still
            // showing what you typed before you opened something else is a trap -- the next Save would
            // silently write to the old name.
            name.BindPolled(box => box.Text, () => levels.LevelName);

            var play = new Button().Variant(ButtonVariant.Primary).OnClick(levels.TogglePlay);
            play.BindPolled(b => b.Text, () => levels.PlayButtonText);
            play.BindPolled(b => b.IsEnabled, () => levels.CanPlay);

            var status = new TextBlock()
                .FontSize(theme.FontSizeSmall)
                .Foreground(theme.TextMuted)
                .Wrap();
            status.BindPolled(t => t.Text, () => levels.Status);

            var current = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted);
            current.BindPolled(t => t.Text, () => $"Open: {levels.CurrentLevelLabel}");

            var startHint = new TextBlock().FontSize(theme.FontSizeSmall);
            startHint.BindPolled(t => t.Text, () => levels.HasPlayerStart
                ? "Player Start placed."
                : "No Player Start -- Play starts at the origin.");
            startHint.BindPolled(
                t => t.Foreground,
                () => levels.HasPlayerStart ? theme.TextMuted : theme.Warning);

            return new StackPanel().Vertical(theme.Spacing * 2.0f).Padding(8.0f).Children(
                Heading(theme, "Play"),
                play,
                new StackPanel().Horizontal(theme.Spacing).Children(
                    new Button().Label("From Camera").OnClick(levels.PlayFromCamera),
                    new Button().Label("New Window").OnClick(levels.PlayInNewWindow)),
                startHint,
                new Button().Label("Place Player Start Here").OnClick(levels.PlacePlayerStart),

                Heading(theme, "Level"),
                current,
                name,
                new StackPanel().Horizontal(theme.Spacing).Children(
                    new Button().Variant(ButtonVariant.Primary).Label("Save").OnClick(levels.Save),
                    new Button().Label("New").OnClick(levels.New),
                    new Button().Label("Reload").OnClick(levels.Reload),
                    new Button().Label("Refresh").OnClick(levels.Refresh)),
                status,

                Heading(theme, "Saved Levels"),
                BuildLevelList(theme, levels));
        }

        /// <summary>
        /// Rebuilt when the file list changes, not every frame -- a folder scan only moves when something is
        /// saved, and a row of buttons torn down and remade sixty times a second is a lot of work for a
        /// picture that never changes. <see cref="LevelViewModel.Refresh"/> hands back a new list instance,
        /// which is exactly the reference change DynamicContent watches for.
        /// </summary>
        private static UiElement BuildLevelList(UiTheme theme, LevelViewModel levels)
        {
            return new DynamicContent()
                .Showing<IReadOnlyList<string>>(
                    () => levels.Levels,
                    list => list.Count == 0 ? null : BuildRows(theme, levels, list))
                .Empty(new TextBlock()
                    .Text($"No levels yet. Save one and it appears here, in the {ManaLevelLibrary.FolderName} folder.")
                    .FontSize(theme.FontSizeSmall)
                    .Foreground(theme.TextMuted)
                    .Wrap());
        }

        private static UiElement BuildRows(UiTheme theme, LevelViewModel levels, IReadOnlyList<string> paths)
        {
            var stack = new StackPanel().Vertical(theme.Spacing);
            foreach (var path in paths)
            {
                var captured = path;
                stack.Children.Add(new Button()
                    .Label(Path.ChangeExtension(ManaLevelLibrary.RelativePath(captured, ManaLevelLibrary.ResolveFolder()) ?? Path.GetFileName(captured), null))
                    .WithTooltip(captured)
                    .OnClick(() => levels.Open(captured)));
            }

            return stack;
        }

        private static UiElement Heading(UiTheme theme, string text) =>
            new TextBlock()
                .Text(text)
                .FontSize(theme.FontSizeSmall)
                .Foreground(theme.Accent);
    }
}
