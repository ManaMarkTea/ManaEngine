using Mana;
using ManaCore.Audio;
using ManaCore.Diagnostics;
using ManaUI;

// ManaEditor has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// Picking a sound for a <c>[SoundPath]</c> property: a slot showing the one chosen with a play button, which opens a
    /// searchable list of every sound the project has (the engine's Assets/Audio and the Content folder), each playable
    /// before it is chosen. Stores the path the audio system loads.
    /// </summary>
    public static class SoundPicker
    {
        private const int MaxShown = 80;
        private static readonly string[] Extensions = [".wav", ".ogg", ".mp3", ".flac"];

        public static UiElement Slot(UiTheme theme, string label, ManaContext? engine, Func<string> read, Action<string> write)
        {
            var name = new TextBlock().Ellipsis().CenterV();
            name.BindPolled(t => t.Text, () => read() is { Length: > 0 } sound ? Path.GetFileNameWithoutExtension(sound) : "none");
            name.BindPolled(t => t.Foreground, () => read().Length > 0 ? theme.Text : theme.TextMuted);

            var choose = new Button().Variant(ButtonVariant.Subtle);
            choose.Child = new StackPanel().Horizontal(theme.Spacing).Children(new TextBlock().Text("\U0001F50A").CenterV(), name);
            choose.BindPolled(b => b.Tooltip, () => read() is { Length: > 0 } sound ? sound : $"{label}: click to choose a sound");
            choose.OnClick(() => Open(theme, choose, label, engine, write));

            return new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").Children(
                choose.GridColumn(0),
                PlayButton(engine, read).GridColumn(1));
        }

        private static Button PlayButton(ManaContext? engine, Func<string> sound)
        {
            var play = new Button().Label("▶").Variant(ButtonVariant.Subtle).WithTooltip("Play it");
            play.BindPolled(b => b.IsEnabled, () => engine?.Audio.IsAvailable == true && sound().Length > 0);
            play.OnClick(() => Preview(engine, sound()));
            return play;
        }

        private static async void Preview(ManaContext? engine, string path)
        {
            if (engine == null || path.Length == 0)
            {
                return;
            }
            try
            {
                var clip = await engine.Audio.LoadClipAsync(path);
                engine.Audio.Play(clip, new ManaAudioPlayOptions());
            }
            catch (Exception exception)
            {
                ManaLog.WriteDiagnostic($"[Sound picker] Could not play '{path}': {exception.Message}");
            }
        }

        private static void Open(UiTheme theme, UiElement anchor, string label, ManaContext? engine, Action<string> write)
        {
            var root = anchor.Root;
            if (root == null)
            {
                return;
            }

            UiPopup? popup = null;
            var candidates = Candidates();
            var list = new StackPanel().Vertical(3.0f);
            var count = new TextBlock().Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall);

            void Show(string filter)
            {
                list.Children.Clear();
                list.Children.Add(new Button().Label("None").Variant(ButtonVariant.Subtle).OnClick(() =>
                {
                    write(string.Empty);
                    popup?.Close();
                }));

                var matches = candidates.Where(path => filter.Length == 0 || path.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var path in matches.Take(MaxShown))
                {
                    var chosen = path;
                    var pick = new Button().Variant(ButtonVariant.Subtle).WithTooltip(chosen).OnClick(() =>
                    {
                        write(chosen);
                        popup?.Close();
                    });
                    pick.Child = new StackPanel().Vertical(0.0f).Children(
                        new TextBlock().Text(Path.GetFileNameWithoutExtension(chosen)).Ellipsis().FontSize(theme.FontSizeSmall),
                        new TextBlock().Text(Path.GetDirectoryName(chosen)?.Replace('\\', '/') ?? "").Foreground(theme.TextMuted)
                            .FontSize(theme.FontSizeSmall).Ellipsis());
                    list.Children.Add(new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").Children(
                        pick.GridColumn(0),
                        PlayButton(engine, () => chosen).GridColumn(1)));
                }
                count.Text = matches.Count > MaxShown
                    ? $"{label}: {MaxShown} of {matches.Count} -- type to narrow"
                    : $"{label}: {matches.Count} sounds";
            }

            var filterBox = new TextBox().Hint("Filter: name or folder (e.g. jump, Footsteps)");
            filterBox.TextChanged += (_, text) => Show(text);
            Show(string.Empty);

            popup = new UiPopup
            {
                Placement = PopupPlacement.Bottom,
                Child = new Border().Fill(theme.SurfaceRaised).Outline(theme.BorderStrong, 1.0f).Rounded(theme.CornerRadius).Shadow(16.0f)
                    .Width(360.0f)
                    .Content(new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                        filterBox,
                        count,
                        list.Scrollable().Configure(viewer => viewer.MaxHeight = 380.0f)))
            };
            root.Popups.Show(popup, anchor);
        }

        // The engine's sounds load by their path under the app (Assets/Audio/...); the project's from Content/.
        private static List<string> Candidates()
        {
            var found = new List<string>();
            AddFrom(found, AppContext.BaseDirectory, "Assets/Audio");
            AddFrom(found, Path.GetDirectoryName(ManaEditor.Code.Assets.ManaContentLibrary.Root) ?? "", "Content");
            return found;
        }

        private static void AddFrom(List<string> found, string baseFolder, string folder)
        {
            var full = Path.Combine(baseFolder, folder);
            if (!Directory.Exists(full))
            {
                return;
            }
            found.AddRange(Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .Select(file => Path.GetRelativePath(baseFolder, file).Replace('\\', '/'))
                .Order(StringComparer.OrdinalIgnoreCase));
        }
    }
}
