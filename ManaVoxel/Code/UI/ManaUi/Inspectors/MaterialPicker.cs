using Mana.Code.Materials;
using Mana.Code.UI.ManaUi;
using ManaUI;
using System.IO.Compression;
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    public static class MaterialPicker
    {
        public static UiElement Build(ManaMaterial material, InspectorContext context)
        {
            var theme = context.Theme;
            var button = new Button().Label("Choose material ZIP...").Variant(ButtonVariant.Subtle);
            button.BindPolled(b => b.Text, () => material.SourceZip is { } path
                ? Path.GetFileNameWithoutExtension(path).Replace('-', ' ') : "Choose material ZIP...");
            button.OnClick(() =>
            {
                if (button.Root is not { } root)
                {
                    return;
                }
                UiPopup? popup = null;
                var list = new StackPanel().Vertical(3.0f);
                var status = new TextBlock().Foreground(theme.TextMuted).Wrap();
                var filter = new TextBox().Hint("Filter materials");
                var entries = MaterialFiles();
                void Show(string text)
                {
                    list.Children.Clear();
                    foreach (var path in entries.Where(p => p.Contains(text, StringComparison.OrdinalIgnoreCase)))
                    {
                        var chosen = path;
                        var thumbnail = new UiImage { Fit = UiImageFit.Cover }.Size(36.0f, 36.0f);
                        string? previewPath = null;
                        var previewChecked = false;
                        ManaUiTexturePreview Preview()
                        {
                            if (!previewChecked)
                            {
                                previewChecked = true;
                                var disk = ManaCore.Assets.ManaAssetCatalog.Current?.LocalFileOf(chosen) ?? Mana.ManaTexture.ResolveTexturePath(chosen);
                                if (File.Exists(disk))
                                {
                                    try
                                    {
                                        using var archive = ZipFile.OpenRead(disk);
                                        if (archive.Entries.FirstOrDefault(e => ManaMaterialLoaderZip.ChannelOf(e.FullName) == "Albedo") is { } albedo)
                                        {
                                            previewPath = Mana.ManaSpritePictures.Resolve($"zip:{Path.GetFileName(chosen)}/{albedo.FullName}");
                                        }
                                    }
                                    catch (Exception exception) when (exception is IOException or InvalidDataException)
                                    {
                                        // Keep invalid packs selectable so their load error can be shown.
                                    }
                                }
                            }
                            return previewPath != null && ManaUiTexturePreviews.TryGetForFile(root.Images, previewPath, out var preview) ? preview : default;
                        }
                        thumbnail.BindPolled(image => image.SourceUv, () => Preview().Uv);
                        thumbnail.BindPolled(image => image.SourceSize, () => Preview().Size);
                        var option = new Button().Variant(ButtonVariant.Subtle).WithTooltip(chosen).OnClick(async () =>
                            {
                                try
                                {
                                    if (ManaCore.Assets.ManaAssetCatalog.Current is { } catalog)
                                    {
                                        await catalog.EnsureAsync(chosen);
                                        using var archive = ZipFile.OpenRead(catalog.LocalFileOf(chosen) ?? chosen);
                                        foreach (var entry in archive.Entries.Where(e => ManaMaterialLoaderZip.ChannelOf(e.FullName) != null))
                                        {
                                            await catalog.EnsureAsync($"zip:{Path.GetFileName(chosen)}/{entry.FullName}");
                                        }
                                    }
                                    ManaMaterialLoaderZip.ApplyTo(material, chosen);
                                    ManaUiTextureLibrary.Invalidate();
                                    popup?.Close();
                                }
                                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                                {
                                    status.Text = $"Could not load material: {exception.Message}";
                                }
                            });
                        option.Child = new Grid().Columns("36, *", theme.Spacing).Rows("Auto").Children(
                            thumbnail.GridColumn(0),
                            new StackPanel().Vertical(0.0f).CenterV().GridColumn(1).Children(
                                new TextBlock().Text(Path.GetFileNameWithoutExtension(chosen).Replace('-', ' ')).Ellipsis(),
                                new TextBlock().Text(Path.GetFileName(Path.GetDirectoryName(chosen)) is { } category && category != "Materials"
                                    ? category : "General").Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall).Ellipsis()));
                        list.Children.Add(option);
                    }
                    status.Text = entries.Count == 0 ? "Add material ZIPs to Assets/Materials." : $"{list.Children.Count} materials";
                }
                filter.TextChanged += (_, text) => Show(text);
                Show(string.Empty);
                popup = new UiPopup
                {
                    Placement = PopupPlacement.Bottom,
                    Child = new Border().Fill(theme.SurfaceRaised).Outline(theme.BorderStrong, 1.0f)
                        .Rounded(theme.CornerRadius).Shadow(16.0f).Width(320.0f)
                        .Content(new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                            filter, status, list.Scrollable().Configure(viewer => viewer.MaxHeight = 360.0f)))
                };
                root.Popups.Show(popup, button);
            });
            return button;
        }

        private static List<string> MaterialFiles()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                var folder = Path.Combine(root, "Assets", "Materials");
                if (!Directory.Exists(folder))
                {
                    continue;
                }
                foreach (var file in Directory.EnumerateFiles(folder, "*.zip", SearchOption.AllDirectories))
                {
                    found.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
                }
            }
            foreach (var path in ManaCore.Assets.ManaAssetCatalog.Current?.MaterialZips ?? [])
            {
                found.Add(path);
            }
            return found.Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
