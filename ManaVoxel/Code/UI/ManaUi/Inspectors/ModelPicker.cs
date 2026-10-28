using System.IO.Compression;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// Picking models for a <c>[ModelPath]</c> property (a Scatter's grass and trees): a slot naming the one chosen,
    /// which opens a searchable list of every model in the asset packs and the loose Assets/Models. Stores what
    /// a Mesh node stores: a "zip:" path into a pack, or a repo path.
    /// </summary>
    public static class ModelPicker
    {
        private const int MaxShown = 80;

        private readonly record struct Candidate(string Reference, string Name, string Folder);

        // Read once: opening every pack is a few hundred milliseconds, and packs do not change while the editor runs.
        private static readonly Lazy<List<Candidate>> All = new(Candidates);

        public static UiElement Slot(UiTheme theme, string label, Func<string> read, Action<string> write)
        {
            var name = new TextBlock().Ellipsis().CenterV();
            name.BindPolled(t => t.Text, () => read() is { Length: > 0 } model ? $"{Path.GetFileNameWithoutExtension(model)}  ({Pack(model)})" : "none");
            name.BindPolled(t => t.Foreground, () => read().Length > 0 ? theme.Text : theme.TextMuted);
            var button = new Button().Variant(ButtonVariant.Subtle);
            button.Child = name;
            button.BindPolled(b => b.Tooltip, () => read() is { Length: > 0 } model ? model : $"{label}: click to choose a model");
            button.OnClick(() => Open(theme, button, label, write));
            return button;
        }

        /// <summary>A list of models: a slot each, removable, and one to add another.</summary>
        public static UiElement List(UiTheme theme, string label, Func<string[]> read, Action<string[]> write)
        {
            var list = new StackPanel().Vertical(3.0f);
            var shown = -1;

            void Rebuild()
            {
                var models = read();
                shown = models.Length;
                list.Children.Clear();
                for (var i = 0; i < models.Length; i++)
                {
                    var index = i;
                    string Current() => read() is { } now && index < now.Length ? now[index] : string.Empty;
                    void Set(string model)
                    {
                        var now = (string[])read().Clone();
                        if (index < now.Length)
                        {
                            now[index] = model;
                            write(now);
                        }
                    }

                    var remove = new Button().Label("x").Variant(ButtonVariant.Subtle).WithTooltip("Remove this model")
                        .OnClick(() => write(read().Where((_, at) => at != index).ToArray()));
                    list.Children.Add(new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").Children(
                        Slot(theme, $"{label} {index + 1}", Current, Set).GridColumn(0),
                        remove.GridColumn(1)));
                }

                // A new one starts as a copy of the last: the next tree is usually a sibling in the same pack.
                list.Children.Add(new Button().Label("+ Add").Variant(ButtonVariant.Subtle)
                    .OnClick(() => write([.. read(), read().LastOrDefault() ?? string.Empty])));
            }

            // Polled, so an undo or a load that changes how many there are rebuilds the rows.
            var watcher = new TextBlock().Width(0.0f).Height(0.0f);
            watcher.BindPolled(t => t.Text, () =>
            {
                if (read().Length != shown)
                {
                    Rebuild();
                }
                return string.Empty;
            });

            Rebuild();
            return new StackPanel().Vertical(0.0f).Children(list, watcher);
        }

        private static void Open(UiTheme theme, UiElement anchor, string label, Action<string> write)
        {
            var root = anchor.Root;
            if (root == null)
            {
                return;
            }

            UiPopup? popup = null;
            var list = new StackPanel().Vertical(2.0f);
            var count = new TextBlock().Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall);

            void Show(string filter)
            {
                list.Children.Clear();
                list.Children.Add(new Button().Label("None").Variant(ButtonVariant.Subtle).OnClick(() =>
                {
                    write(string.Empty);
                    popup?.Close();
                }));

                var words = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var matches = All.Value.Where(c => words.All(word => c.Reference.Contains(word, StringComparison.OrdinalIgnoreCase))).ToList();
                foreach (var candidate in matches.Take(MaxShown))
                {
                    var chosen = candidate;
                    var option = new Button().Variant(ButtonVariant.Subtle).WithTooltip(chosen.Reference).OnClick(() =>
                    {
                        write(chosen.Reference);
                        popup?.Close();
                    });
                    option.Child = new StackPanel().Vertical(0.0f).Children(
                        new TextBlock().Text(chosen.Name).Ellipsis().FontSize(theme.FontSizeSmall),
                        new TextBlock().Text(chosen.Folder).Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall).Ellipsis());
                    list.Children.Add(option);
                }
                count.Text = matches.Count > MaxShown
                    ? $"{label}: {MaxShown} of {matches.Count} -- type to narrow"
                    : $"{label}: {matches.Count} models";
            }

            var filterBox = new TextBox().Hint("Filter: name or pack (e.g. survival tree)");
            filterBox.TextChanged += (_, text) => Show(text);
            Show(string.Empty);

            popup = new UiPopup
            {
                Placement = PopupPlacement.Bottom,
                Child = new Border().Fill(theme.SurfaceRaised).Outline(theme.BorderStrong, 1.0f).Rounded(theme.CornerRadius).Shadow(16.0f)
                    .Width(340.0f)
                    .Content(new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                        filterBox,
                        count,
                        list.Scrollable().Configure(viewer => viewer.MaxHeight = 380.0f)))
            };
            root.Popups.Show(popup, anchor);
        }

        // The pack a model is in, or its folder.
        private static string Pack(string reference)
        {
            if (reference.StartsWith("zip:", StringComparison.OrdinalIgnoreCase))
            {
                var slash = reference.IndexOf('/');
                if (slash > 4)
                {
                    return Path.GetFileNameWithoutExtension(reference[4..slash]);
                }
            }
            return Path.GetFileName(Path.GetDirectoryName(reference)) ?? string.Empty;
        }

        private static List<Candidate> Candidates()
        {
            var found = new List<Candidate>();
            if (!Directory.Exists("Assets"))
            {
                return found;
            }
            foreach (var zip in Directory.EnumerateFiles("Assets", "*.zip").Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(zip);
                    var pack = Path.GetFileName(zip);
                    foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
                    {
                        if (IsModel(entry.FullName))
                        {
                            found.Add(new Candidate($"zip:{pack}/{entry.FullName}", Path.GetFileNameWithoutExtension(entry.Name),
                                Path.GetFileNameWithoutExtension(pack)));
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                }
            }
            if (Directory.Exists("Assets/Models"))
            {
                foreach (var file in Directory.EnumerateFiles("Assets/Models", "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                {
                    if (IsModel(file))
                    {
                        var reference = file.Replace('\\', '/');
                        found.Add(new Candidate(reference, Path.GetFileNameWithoutExtension(file),
                            Path.GetFileName(Path.GetDirectoryName(file)) ?? string.Empty));
                    }
                }
            }
            return found;
        }

        // glTF only (what every platform loads), and the GLB copies when a Kenney pack ships a GLTF folder beside them.
        private static bool IsModel(string path)
        {
            var extension = Path.GetExtension(path);
            if (extension.Equals(".glb", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("GLTF format", StringComparison.OrdinalIgnoreCase);
        }
    }
}
