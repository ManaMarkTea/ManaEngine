using Mana;
using Mana.Code.UI.ManaUi;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// Picking a picture for a sprite: a slot showing the one chosen, which opens a searchable list of every picture
    /// the project has -- its Content folder, the pictures inside the asset packs, and the material textures. Stores
    /// what a sprite stores: a repo path, or a "zip:" path into a pack.
    /// </summary>
    public static class PicturePicker
    {
        private const int MaxShown = 80;

        // Pack pictures are copied out of their zip to be shown, which costs a zip open each; a few a frame keeps a
        // freshly filtered list from stalling while its thumbnails fill in.
        private const int ExtractionsPerFrame = 4;
        private static long _budgetFrame;
        private static int _budgetUsed;

        /// <summary>One picture: its thumbnail and name, a click away from the list.</summary>
        public static UiElement Slot(UiTheme theme, string label, Func<string> read, Action<string> write)
        {
            var size = theme.ControlHeight;
            var thumb = new UiImage { Fit = UiImageFit.Contain }.Size(size - 4.0f, size - 4.0f);
            BindThumbnail(thumb, read);

            var name = new TextBlock().Ellipsis().CenterV();
            name.BindPolled(t => t.Text, () => read() is { Length: > 0 } picture ? Path.GetFileNameWithoutExtension(picture) : "empty");
            name.BindPolled(t => t.Foreground, () => read().Length > 0 ? theme.Text : theme.TextMuted);

            var button = new Button().Variant(ButtonVariant.Subtle);
            button.Child = new Grid().Columns($"{size}, *", theme.Spacing).Rows("Auto").Children(
                new Border().Fill(theme.SurfaceAlt).Outline(theme.BorderStrong, 1.0f).Rounded(theme.CornerRadiusCompact)
                    .Size(size, size).Content(thumb.Center()).GridColumn(0),
                name.GridColumn(1));
            button.BindPolled(b => b.Tooltip, () => read() is { Length: > 0 } picture ? picture : $"{label}: click to choose a picture");
            button.OnClick(() => Open(theme, button, label, write));
            return button;
        }

        /// <summary>One picture as a square tile, thumbnail only -- for a grid of them, like a 9-slice's pieces.</summary>
        public static UiElement Tile(UiTheme theme, string label, float size, Func<string> read, Action<string> write)
        {
            var thumb = new UiImage { Fit = UiImageFit.Contain }.Size(size - 6.0f, size - 6.0f);
            BindThumbnail(thumb, read);
            var empty = new TextBlock().Text("+").Foreground(theme.TextMuted).Center();
            empty.BindPolled(t => t.Visibility, () => read().Length == 0 ? Visibility.Visible : Visibility.Collapsed);

            var button = new Button().Variant(ButtonVariant.Subtle).Size(size, size);
            button.Padding = new Thickness(0.0f);
            button.IgnoreThemePadding = true;
            button.Child = new Border().Fill(theme.SurfaceAlt).Outline(theme.BorderStrong, 1.0f).Rounded(theme.CornerRadiusCompact).Size(size, size)
                .Content(new Grid().Rows("*").Columns("*").Children(empty, thumb.Center()));
            button.BindPolled(b => b.Tooltip, () => read() is { Length: > 0 } picture ? $"{label}: {picture}" : $"{label}: click to choose a picture");
            button.OnClick(() => Open(theme, button, label, write));
            return button;
        }

        /// <summary>A list of pictures -- a sprite's frames: a slot each, removable, and one to add another.</summary>
        public static UiElement List(UiTheme theme, string label, Func<string[]> read, Action<string[]> write)
        {
            var list = new StackPanel().Vertical(3.0f);
            var shown = -1;

            void Rebuild()
            {
                var pictures = read();
                shown = pictures.Length;
                list.Children.Clear();
                for (var i = 0; i < pictures.Length; i++)
                {
                    var index = i;
                    string Current() => read() is { } now && index < now.Length ? now[index] : string.Empty;
                    void Set(string picture)
                    {
                        var now = (string[])read().Clone();
                        if (index < now.Length)
                        {
                            now[index] = picture;
                            write(now);
                        }
                    }

                    var remove = new Button().Label("x").Variant(ButtonVariant.Subtle).WithTooltip("Remove this frame")
                        .OnClick(() => write(read().Where((_, at) => at != index).ToArray()));
                    list.Children.Add(new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").Children(
                        Slot(theme, $"{label} {index + 1}", Current, Set).GridColumn(0),
                        remove.GridColumn(1)));
                }

                // A new frame starts as a copy of the last, so a walk cycle's next step is one click on a neighbour.
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

                var matches = candidates.Where(c => filter.Length == 0 || c.Reference.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var candidate in matches.Take(MaxShown))
                {
                    var chosen = candidate;
                    list.Children.Add(Option(theme, chosen, () =>
                    {
                        write(chosen.Reference);
                        popup?.Close();
                    }));
                }
                count.Text = matches.Count > MaxShown
                    ? $"{label}: {MaxShown} of {matches.Count} -- type to narrow"
                    : $"{label}: {matches.Count} pictures";
            }

            var filterBox = new TextBox().Hint("Filter: name, folder or pack (e.g. tiles/default/coin)");
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

        private readonly record struct Candidate(string Reference, string Name, string Folder);

        // Content first (what this project made), then the packs, then the material textures.
        private static List<Candidate> Candidates()
        {
            var found = new List<Candidate>();
            var content = ManaVoxel.Code.Assets.ManaContentLibrary.Root;
            if (Directory.Exists(content))
            {
                foreach (var file in Directory.EnumerateFiles(content, "*.png", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var relative = "Content/" + Path.GetRelativePath(content, file).Replace('\\', '/');
                    // Model previews baked for the web catalog, not pictures to draw.
                    if (relative.StartsWith("Content/Thumbnails/", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    found.Add(new Candidate(relative, Path.GetFileName(file), Path.GetFileName(Path.GetDirectoryName(file) ?? "")));
                }
            }
            found.AddRange(ManaSpritePictures.PackPictures().Select(p => new Candidate(p.Reference, p.Name, p.Folder)));
            found.AddRange(ManaUiTextureLibrary.Assets.Select(a => new Candidate(a.RelativePath, a.Name, a.Folder)));
            return found;
        }

        private static UiElement Option(UiTheme theme, Candidate candidate, Action choose)
        {
            var thumb = new UiImage { Fit = UiImageFit.Contain }.Size(30.0f, 30.0f);
            BindThumbnail(thumb, () => candidate.Reference);

            // The folder under the name is the pack or the folder it is in; the tooltip has the whole path.
            var folder = candidate.Reference.StartsWith("zip:", StringComparison.OrdinalIgnoreCase)
                ? $"{candidate.Folder} / {Path.GetFileName(Path.GetDirectoryName(candidate.Reference.Replace('/', Path.DirectorySeparatorChar)) ?? "")}"
                : candidate.Folder;
            var button = new Button().Variant(ButtonVariant.Subtle).WithTooltip(candidate.Reference).OnClick(choose);
            button.Child = new Grid().Columns("30, *", theme.Spacing).Rows("Auto").Children(
                new Border().Fill(theme.SurfaceAlt).Rounded(2.0f).Size(30.0f, 30.0f).Content(thumb).GridColumn(0),
                new StackPanel().Vertical(0.0f).CenterV().GridColumn(1).Children(
                    new TextBlock().Text(Path.GetFileNameWithoutExtension(candidate.Name)).Ellipsis().FontSize(theme.FontSizeSmall),
                    new TextBlock().Text(folder).Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall).Ellipsis()));
            return button;
        }

        // Polled: the picture can change underneath, and a pack picture may still be waiting its turn to be copied out.
        private static void BindThumbnail(UiImage image, Func<string> reference)
        {
            image.BindPolled(i => i.SourceUv, () => Preview(image, reference()).Uv);
            image.BindPolled(i => i.SourceSize, () => Preview(image, reference()).Size);
        }

        private static ManaUiTexturePreview Preview(UiImage image, string reference)
        {
            var atlas = image.Root?.Images;
            if (atlas == null || reference.Length == 0)
            {
                return default;
            }

            var file = ManaSpritePictures.TryGetResolved(reference);
            if (file == null)
            {
                var frame = Environment.TickCount64 / 16;
                if (frame != _budgetFrame)
                {
                    _budgetFrame = frame;
                    _budgetUsed = 0;
                }
                if (_budgetUsed >= ExtractionsPerFrame)
                {
                    return default;
                }
                _budgetUsed++;
                file = ManaSpritePictures.Resolve(reference);
            }

            return file != null && ManaUiTexturePreviews.TryGetForFile(atlas, file, out var preview) ? preview : default;
        }
    }
}
