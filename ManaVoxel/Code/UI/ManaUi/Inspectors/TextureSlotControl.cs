using Mana;
using Mana.Code.UI.ManaUi;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// A texture channel as a control: a square showing what is assigned, which opens a picker when
    /// clicked. Knows nothing about materials -- it is given a way to read the channel and a way to write
    /// it, so anything with a texture-shaped hole can use one.
    /// </summary>
    public static class TextureSlotControl
    {
        /// <summary>Line break for the multi-line tooltip.</summary>
        private const string NewLine = "\n";

        /// <summary>
        /// The slot. <paramref name="channel"/> names it in the tooltip and in the picker's heading.
        /// <para>
        /// The thumbnail is built the first frame the slot is shown, which means decoding an image. It is
        /// cached from then on, so the cost lands once per texture rather than on every rebuild.
        /// </para>
        /// </summary>
        public static UiElement Build(
            UiTheme theme,
            string channel,
            Func<ManaTexture?> read,
            Action<ManaTexture?> write,
            ManaContext? context)
        {
            // Sized so the thumbnail fills the chip. Every element in the chain carries an explicit size:
            // a star track takes its size from the budget it is given and never from its children, so an
            // overlay grid inside a content-sized border collapsed to the width of the "+" and cropped the
            // thumbnail down to a few pixels -- which looked exactly like a pick that had not worked.
            var size = theme.ControlHeight;
            var inner = size - 2.0f;

            var preview = new UiImage { Fit = UiImageFit.Cover }.Size(inner, inner);

            // Polled, because the pixels may not exist yet -- a texture uploaded straight to the GPU has no
            // CPU copy until something asks -- and because the channel can be reassigned underneath.
            preview.BindPolled(image => image.SourceUv, () => Resolve(preview, read()).Uv);
            preview.BindPolled(image => image.SourceSize, () => Resolve(preview, read()).Size);

            // The two are alternatives rather than a stack -- a filled slot is its thumbnail and nothing
            // else -- so each is hidden when the other applies.
            preview.BindPolled(
                image => image.Visibility,
                () => read() == null ? Visibility.Collapsed : Visibility.Visible);

            var empty = new TextBlock()
                .Text("+")
                .Foreground(theme.TextMuted)
                .Center()
                .Size(inner, inner)
                .Configure(plus => plus.BindPolled(
                    t => t.Visibility,
                    () => read() == null ? Visibility.Visible : Visibility.Collapsed));

            // Outlined and filled rather than a bare subtle button: an empty slot had no edge and no hint
            // that it was a target, so there was nowhere visible to aim at.
            var slot = new Button()
                .Variant(ButtonVariant.Subtle)
                .Size(size, size)
                .Configure(button =>
                {
                    button.Padding = new Thickness(0.0f);
                    button.IgnoreThemePadding = true;
                    button.CornerRadius = theme.CornerRadius;
                    button.Child = new Border()
                        .Fill(theme.SurfaceAlt)
                        .Outline(theme.BorderStrong, 1.0f)
                        .Rounded(theme.CornerRadius)
                        .Size(size, size)
                        .Content(new Grid()
                            .Rows("*")
                            .Columns("*")
                            .Size(inner, inner)
                            .Children(empty, preview));
                });

            slot.OnClick(() => OpenPicker(theme, slot, channel, write, context));

            // The full path, because a slot this small can show a picture and nothing else, and one grey
            // noise map looks much like another.
            slot.BindPolled(
                button => button.Tooltip,
                () => read() is { } assigned
                    ? $"{channel} map" + NewLine + PathOf(assigned) + SizeOf(preview, assigned)
                    : $"{channel} map" + NewLine + "Empty -- click to choose a texture.");

            return slot;
        }

        /// <summary>
        /// Every texture in the project, in a popup. Choosing one assigns it to the channel.
        /// <para>
        /// Thumbnails come from the file rather than from a loaded texture, decoded at a fraction of full
        /// resolution: the picker needs dozens at once, and decoding them whole would stall on opening.
        /// </para>
        /// </summary>
        public static void OpenPicker(
            UiTheme theme,
            UiElement anchor,
            string channel,
            Action<ManaTexture?> write,
            ManaContext? context)
        {
            var root = anchor.Root;
            if (root == null || context == null)
            {
                return;
            }

            UiPopup? popup = null;
            var list = new StackPanel().Vertical(3.0f);
            var rows = new List<(UiElement Row, ManaUiTextureAsset Asset)>();

            list.Children.Add(new Button()
                .Label("None")
                .Variant(ButtonVariant.Subtle)
                .OnClick(() =>
                {
                    write(null);
                    popup?.Close();
                }));

            foreach (var asset in ManaUiTextureLibrary.Assets)
            {
                var entry = asset;

                var row = Option(theme, root, entry, () =>
                {
                    // Pointed at a relative path, so a scene saved with this material still resolves it on
                    // another machine.
                    write(new ManaTexture(context) { OriginalFilename = entry.RelativePath });
                    popup?.Close();
                });

                rows.Add((row, entry));
                list.Children.Add(row);
            }

            var count = new TextBlock()
                .Text($"{channel}: {rows.Count} textures")
                .Foreground(theme.TextMuted)
                .FontSize(theme.FontSizeSmall);

            // Filtering by hiding rather than rebuilding: the thumbnails are already packed and laid out,
            // and a list this size re-filters faster than it would rebuild.
            var filter = new TextBox().Hint("Filter by name or folder");
            filter.TextChanged += (_, text) =>
            {
                var matched = 0;

                foreach (var (row, asset) in rows)
                {
                    var visible = text.Length == 0 || Matches(asset, text);

                    row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    matched += visible ? 1 : 0;
                }

                count.Text = text.Length == 0
                    ? $"{channel}: {rows.Count} textures"
                    : $"{channel}: {matched} of {rows.Count} match";
            };

            var body = new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                filter,
                count,
                list.Scrollable().Configure(viewer => viewer.MaxHeight = 360.0f));

            popup = new UiPopup
            {
                Placement = PopupPlacement.Bottom,
                Child = new Border()
                    .Fill(theme.SurfaceRaised)
                    .Outline(theme.BorderStrong, 1.0f)
                    .Rounded(theme.CornerRadius)
                    .Shadow(16.0f)
                    .Width(300.0f)
                    .Content(body)
            };

            root.Popups.Show(popup, anchor);
        }

        private static bool Matches(ManaUiTextureAsset asset, string text)
        {
            return asset.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || asset.Folder.Contains(text, StringComparison.OrdinalIgnoreCase)
                || asset.RelativePath.Contains(text, StringComparison.OrdinalIgnoreCase);
        }

        private static UiElement Option(UiTheme theme, UiRoot root, ManaUiTextureAsset asset, Action choose)
        {
            var thumb = new UiImage { Fit = UiImageFit.Cover }.Size(30.0f, 30.0f);

            if (ManaUiTexturePreviews.TryGetForFile(root.Images, asset.Path, out var preview))
            {
                thumb.SetSource(preview.Uv, preview.Size);
            }

            // The full path, because a folder and a file name are often not enough to tell two maps apart.
            var button = new Button()
                .Variant(ButtonVariant.Subtle)
                .WithTooltip(asset.RelativePath)
                .OnClick(choose);

            button.Child = new Grid().Columns("30, *", theme.Spacing).Rows("Auto").Children(
                new Border()
                    .Fill(theme.SurfaceAlt)
                    .Rounded(2.0f)
                    .Size(30.0f, 30.0f)
                    .Content(thumb)
                    .GridColumn(0),

                new StackPanel().Vertical(0.0f).CenterV().GridColumn(1).Children(
                    new TextBlock().Text(asset.Name).Ellipsis().FontSize(theme.FontSizeSmall),
                    new TextBlock()
                        .Text(asset.Folder)
                        .Foreground(theme.TextMuted)
                        .FontSize(theme.FontSizeSmall)
                        .Ellipsis()));

            return button;
        }

        /// <summary>
        /// Looks the thumbnail up through the element's own root, which is the only way to reach the atlas
        /// the renderer uploads. Cheap after the first call.
        /// </summary>
        private static ManaUiTexturePreview Resolve(UiImage image, ManaTexture? texture)
        {
            var atlas = image.Root?.Images;
            if (atlas == null || !ManaUiTexturePreviews.TryGet(atlas, texture, out var preview))
            {
                return default;
            }

            return preview;
        }

        /// <summary>The dimensions of an assigned map, if its thumbnail has been built. Blank if not.</summary>
        private static string SizeOf(UiImage preview, ManaTexture texture)
        {
            var atlas = preview.Root?.Images;
            return atlas != null && ManaUiTexturePreviews.TryGet(atlas, texture, out var found)
                ? NewLine + $"{found.SourceWidth} x {found.SourceHeight}"
                : string.Empty;
        }

        private static string PathOf(ManaTexture texture)
        {
            var name = texture.OriginalFilename;
            return string.IsNullOrWhiteSpace(name) ? "embedded" : name;
        }
    }
}
