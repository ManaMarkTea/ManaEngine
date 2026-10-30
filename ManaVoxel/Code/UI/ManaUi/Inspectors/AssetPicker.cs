using Mana.Code.Content;
using ManaVoxel.Code.Assets;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// Choosing a user-made asset for a property: a slot with the asset's thumbnail and name, a list of every asset
    /// of that kind in Content/ to pick from, and a button to open the one chosen in its own editor. Stores a
    /// <see cref="ManaAssetRef"/> -- the id and the path -- so the choice survives the file moving.
    /// </summary>
    public static class AssetPicker
    {
        /// <summary>Opens an asset in its editor window, by file; the sprite assets open in the Sprite Asset window.</summary>
        public static Action<string>? OpenInEditor { get; set; } = SpriteAssetEditing.Open;

        public static UiElement Build(UiTheme theme, string label, string extension, Func<ManaAssetRef> read, Action<ManaAssetRef> write)
        {
            var size = theme.ControlHeight;
            var thumb = new UiImage { Fit = UiImageFit.Contain, SourceSize = new Vector2(MeshThumbnailCache.ThumbnailSize, MeshThumbnailCache.ThumbnailSize) }.Size(size - 4.0f, size - 4.0f);
            BindThumbnail(thumb, () => ManaContentAssets.Resolve(read(), extension));

            var name = new TextBlock().Ellipsis().CenterV();
            name.BindPolled(t => t.Text, () => read().IsEmpty ? "none" : ManaContentAssets.Resolve(read(), extension) is { } file
                ? Path.GetFileNameWithoutExtension(file) : $"{read()} (missing)");
            name.BindPolled(t => t.Foreground, () => ManaContentAssets.Resolve(read(), extension) != null ? theme.Text : theme.TextMuted);

            var slot = new Button().Variant(ButtonVariant.Subtle);
            slot.Child = new Grid().Columns($"{size}, *", theme.Spacing).Rows("Auto").Children(
                new Border().Fill(theme.SurfaceAlt).Outline(theme.BorderStrong, 1.0f).Rounded(theme.CornerRadiusCompact)
                    .Size(size, size).Content(thumb.Center()).GridColumn(0),
                name.GridColumn(1));
            slot.BindPolled(b => b.Tooltip, () => read().IsEmpty ? $"{label}: click to choose" : read().AssetPath);
            slot.OnClick(() => Open(theme, slot, label, extension, write));

            var edit = new Button().Label("Edit").Variant(ButtonVariant.Subtle).WithTooltip("Open it in its editor");
            edit.BindPolled(b => b.IsEnabled, () => OpenInEditor != null && ManaContentAssets.Resolve(read(), extension) != null);
            edit.OnClick(() =>
            {
                if (ManaContentAssets.Resolve(read(), extension) is { } file)
                {
                    if (extension == UiMarkup.Extension)
                    {
                        if (UiDesignerDocument.TryOpen(file, out _) && slot.Root != null)
                        {
                            EditorWorkspace.For(slot.Root).RequestPanel(UiDesignerManaView.PanelId);
                        }
                    }
                    else
                    {
                        OpenInEditor?.Invoke(file);
                    }
                }
            });

            return new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").Children(slot.GridColumn(0), edit.GridColumn(1));
        }

        private static void Open(UiTheme theme, UiElement anchor, string label, string extension, Action<ManaAssetRef> write)
        {
            var root = anchor.Root;
            if (root == null)
            {
                return;
            }

            UiPopup? popup = null;
            var list = new StackPanel().Vertical(3.0f);
            list.Children.Add(new Button().Label("None").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                write(ManaAssetRef.None);
                popup?.Close();
            }));

            var content = ManaContentAssets.Root;
            var files = ManaContentMounts.For(content).Where(mount => Directory.Exists(mount.Root))
                .SelectMany(mount => Directory.EnumerateFiles(mount.Root, "*" + extension, SearchOption.AllDirectories))
                .Order(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var file in files)
            {
                var chosen = file;
                var thumb = new UiImage { Fit = UiImageFit.Contain, SourceSize = new Vector2(MeshThumbnailCache.ThumbnailSize, MeshThumbnailCache.ThumbnailSize) }.Size(30.0f, 30.0f);
                BindThumbnail(thumb, () => chosen);
                var option = new Button().Variant(ButtonVariant.Subtle).WithTooltip(Path.GetRelativePath(content, file)).OnClick(() =>
                {
                    write(ManaContentAssets.Reference(chosen));
                    popup?.Close();
                });
                option.Child = new Grid().Columns("30, *", theme.Spacing).Rows("Auto").Children(
                    new Border().Fill(theme.SurfaceAlt).Rounded(2.0f).Size(30.0f, 30.0f).Content(thumb).GridColumn(0),
                    new TextBlock().Text(Path.GetFileNameWithoutExtension(file)).Ellipsis().CenterV().GridColumn(1));
                list.Children.Add(option);
            }

            var body = new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                new TextBlock().Text(files.Count == 0 ? $"{label}: none in Content yet" : $"{label}: {files.Count}")
                    .Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall),
                list.Scrollable().Configure(viewer => viewer.MaxHeight = 360.0f));
            popup = new UiPopup
            {
                Placement = PopupPlacement.Bottom,
                Child = new Border().Fill(theme.SurfaceRaised).Outline(theme.BorderStrong, 1.0f).Rounded(theme.CornerRadius).Shadow(16.0f)
                    .Width(300.0f).Content(body)
            };
            root.Popups.Show(popup, anchor);
        }

        // The same thumbnails the Project panel shows, asked for while the slot is on screen.
        private static void BindThumbnail(UiImage image, Func<string?> file)
        {
            var sync = MeshImportManaView.Throttled<object?>(() =>
            {
                if (file() is { } path && image.Root is { } root)
                {
                    MeshThumbnailImages.For(root).Touch(root, path);
                    EditorWorkspace.For(root).Assets.Thumbnails?.Request(path);
                    image.Visibility = MeshThumbnailImages.For(root).HasImage(path) ? Visibility.Visible : Visibility.Hidden;
                    if (MeshThumbnailImages.For(root).HasImage(path))
                    {
                        image.SourceUv = MeshThumbnailImages.For(root).UvFor(path);
                    }
                }
                else
                {
                    image.Visibility = Visibility.Hidden;
                }
                return null;
            });
            image.BindPolled(i => i.Tag, () => sync());
        }
    }
}
