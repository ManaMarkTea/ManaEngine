using System.Runtime.CompilerServices;
using ManaCore.Imaging;
using ManaUI;
using ManaEditor.Code.Levels;
using Button = ManaUI.Button;

namespace ManaEditor.Code.UI.ManaUi;

/// <summary>A searchable shelf of saved scenes, using their authored level pictures.</summary>
public static class ScenesManaView
{
    private sealed class PreviewCache
    {
        public int Generation = -1;
        public readonly Dictionary<string, (Vector4 Uv, Vector2 Size)?> Entries = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly ConditionalWeakTable<ManaImageAtlas, PreviewCache> Previews = new();

    // The browser decodes pictures only asynchronously: each is decoded ahead, and its tile waits until then.
    private static readonly Dictionary<string, Task> Decoding = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Filter(IEnumerable<string> paths, string search)
    {
        var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return paths.Where(path => words.All(word => ManaLevelLibrary.DisplayName(path)
            .Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    public static string? PreviewPath(string levelPath)
    {
        var folder = Path.GetDirectoryName(levelPath);
        if (folder == null)
        {
            return null;
        }
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg" })
        {
            var path = Path.Combine(folder, "Pictures", Path.GetFileNameWithoutExtension(levelPath) + extension);
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    public static UiElement Build(UiTheme theme, LevelViewModel levels)
    {
        var search = new TextBox { Placeholder = "Search scenes" };
        var rootFolder = ManaLevelLibrary.ResolveFolder();
        IReadOnlyList<string>? folderSource = null;
        ManaLevelFolder? folders = null;
        var selectedFolder = "";
        string? previousFolder = null;
        ManaLevelFolder Folders()
        {
            if (!ReferenceEquals(folderSource, levels.Levels))
            {
                folderSource = levels.Levels;
                folders = ManaLevelFolder.Build(folderSource, rootFolder);
                if (folders.Find(selectedFolder) == null)
                {
                    selectedFolder = "";
                }
            }
            return folders!;
        }
        IReadOnlyList<string>? previous = null;
        string? previousSearch = null;
        IReadOnlyList<string> filtered = Array.Empty<string>();
        IReadOnlyList<string> Matches()
        {
            var folder = Folders().Find(selectedFolder)!;
            if (!ReferenceEquals(previous, levels.Levels) || previousSearch != search.Text || previousFolder != selectedFolder)
            {
                previous = levels.Levels;
                previousSearch = search.Text;
                previousFolder = selectedFolder;
                var contained = folder.DescendantLevels().ToHashSet(StringComparer.OrdinalIgnoreCase);
                filtered = Filter(previous.Where(contained.Contains), search.Text);
            }
            return filtered;
        }
        var list = new DynamicContent().Showing<IReadOnlyList<string>>(Matches, paths =>
        {
            if (paths.Count == 0)
            {
                return new TextBlock().Text(search.Text.Length == 0
                    ? "No saved scenes yet. Save a level to add it here." : "No scenes match your search.")
                    .Foreground(theme.TextMuted).Wrap();
            }
            return new VirtualWrapPanel
            {
                Spacing = theme.Spacing,
                Items = paths.Cast<object>().ToList(),
                ItemSize = () => new Vector2(192, 148),
                CreateItem = index => Tile(theme, levels, paths[index])
            };
        });
        var outliner = new DynamicContent().Showing<ManaLevelFolder>(Folders, root =>
        {
            var tree = new TreeView
            {
                ItemsSource = new[] { root },
                ChildrenSelector = item => ((ManaLevelFolder)item).Children,
                HasChildrenSelector = item => ((ManaLevelFolder)item).Children.Count > 0,
                IsExpandedByDefault = _ => true,
                ItemTemplate = item => new TextBlock().Text(((ManaLevelFolder)item).Name).Ellipsis(),
                SelectedItem = root.Find(selectedFolder)
            };
            tree.SelectionChanged += (_, item) => selectedFolder = (item as ManaLevelFolder)?.RelativePath ?? "";
            return tree.Scrollable();
        });
        return new Grid().Rows("Auto, *, Auto", theme.Spacing).Columns("*").Padding(8).Children(
            new Grid().Rows("Auto").Columns("*, Auto", theme.Spacing).Children(
                search, new Button().Label("Refresh").OnClick(levels.Refresh).GridColumn(1)),
            new Grid().Rows("*").Columns("140, *", theme.Spacing).Children(
                new Border().Fill(theme.SurfaceAlt).Content(outliner),
                list.Scrollable().GridColumn(1)).GridRow(1),
            new TextBlock().BindTextPolled(() => $"{Matches().Count} scenes · Open: {levels.CurrentLevelLabel}")
                .Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall).Ellipsis().GridRow(2));
    }

    private static UiElement Tile(UiTheme theme, LevelViewModel levels, string path)
    {
        var image = new UiImage { Fit = UiImageFit.Contain, Visibility = Visibility.Collapsed };
        var placeholder = new TextBlock().Text("NO PREVIEW").Foreground(theme.TextMuted).Center();
        var picture = PreviewPath(path);
        var button = new Button
        {
            Width = 192, Height = 148, Padding = new Thickness(6), IgnoreThemePadding = true,
            CornerRadius = theme.CornerRadiusCompact,
            Child = new Grid().Rows("*, Auto", 6).Columns("*").Children(
                new Border().Fill(theme.SurfaceAlt).Rounded(3).Content(
                    new Grid().Rows("*").Columns("*").Children(image, placeholder)),
                new TextBlock().Text(ManaLevelLibrary.DisplayName(path)).Ellipsis().GridRow(1))
        }.OnClick(() => levels.Open(path)).WithTooltip($"Open {ManaLevelLibrary.DisplayName(path)}\n{path}");
        button.BindPolled(control => control.Tag, () =>
        {
            if (picture != null && button.Root is { } root && MeshImportManaView.IsThumbnailRequestVisible(button))
            {
                var cached = Preview(root.Images, picture);
                if (cached is { } preview)
                {
                    image.SourceUv = preview.Uv;
                    image.SourceSize = preview.Size;
                    image.Visibility = Visibility.Visible;
                    placeholder.Visibility = Visibility.Collapsed;
                }
                else
                {
                    image.Visibility = Visibility.Collapsed;
                    placeholder.Visibility = Visibility.Visible;
                }
            }
            return null;
        });
        return button;
    }

    private static (Vector4 Uv, Vector2 Size)? Preview(ManaImageAtlas atlas, string path)
    {
        var cache = Previews.GetOrCreateValue(atlas);
        if (cache.Generation != atlas.Generation)
        {
            cache.Entries.Clear();
            cache.Generation = atlas.Generation;
        }
        if (cache.Entries.TryGetValue(path, out var entry))
        {
            return entry;
        }
        if (OperatingSystem.IsBrowser())
        {
            if (!Decoding.TryGetValue(path, out var decoding))
            {
                decoding = ManaImageCodec.PrepareFileAsync(path);
                Decoding[path] = decoding;
            }
            if (!decoding.IsCompleted)
            {
                return null;
            }
        }
        try
        {
            var image = ManaImageCodec.Load(path);
            var scale = MathF.Min(1.0f, 192.0f / MathF.Max(image.Width, image.Height));
            var width = Math.Max(1, (int)MathF.Round(image.Width * scale));
            var height = Math.Max(1, (int)MathF.Round(image.Height * scale));
            var resized = image.Resized(width, height);
            entry = atlas.Add(resized.Pixels, width, height) is { } uv ? (uv, new Vector2(width, height)) : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            entry = null;
            // The browser can drop a decoded picture before its tile asks again: decode it again rather than give up.
            if (OperatingSystem.IsBrowser() && Decoding.Remove(path))
            {
                return null;
            }
        }
        cache.Entries[path] = entry;
        return entry;
    }
}
