using Mana.Code;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.UI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The mesh import panel: what gets imported, and the transform it arrives with.
    /// <para>
    /// Three panes, Unreal Content Browser-shaped: a folder tree on the left for navigation, the filtered
    /// asset list in the middle, and the placement transform as its own panel on the right rather than
    /// competing with the list for vertical space above it.
    /// </para>
    /// <para>
    /// The list itself is <see cref="MeshAssetLibrary"/>'s -- this only draws it. What the panel adds is the
    /// two ways of getting a mesh into the scene: drag a row into the viewport to place it under the cursor,
    /// or select and press the button to place it on the next click. Both end at the same request, so the
    /// game mode only has one thing to answer.
    /// </para>
    /// </summary>
    public static class MeshImportManaView
    {
        /// <summary>Wide enough to read a folder name at a glance without stealing more than a sliver from the list.</summary>
        private const float DefaultFolderWidth = 210.0f;

        private const float MinFolderWidth = 140.0f;
        private const float MaxFolderWidth = 420.0f;

        public static UiElement Build(
            UiTheme theme,
            VoxelEditorViewModel viewModel,
            MeshAssetLibrary library,
            MeshThumbnailCache? thumbnails)
        {
            var grid = new Grid().Columns($"{DefaultFolderWidth}, Auto, *, Auto, 220", theme.Spacing).Rows("*").Padding(8.0f);
            grid.ColumnDefinitions[0].MinLength = MinFolderWidth;
            grid.ColumnDefinitions[0].MaxLength = MaxFolderWidth;

            return grid.Children(
                FolderSidebar(theme, library).GridColumn(0),
                new FolderSplitter(
                    () => grid.ColumnDefinitions[0].Length.Value,
                    width =>
                    {
                        grid.ColumnDefinitions[0].Length = GridLength.Pixels(
                            Math.Clamp(width, MinFolderWidth, MaxFolderWidth));
                        grid.InvalidateMeasure();
                    })
                    .GridColumn(1),
                Picker(theme, library, thumbnails).GridColumn(2),
                Divider(theme).GridColumn(3),
                TransformPanel(theme, viewModel).GridColumn(4));
        }

        private static UiElement Divider(UiTheme theme)
        {
            return new Border().Fill(theme.Divider).Width(1.0f);
        }

        /// <summary>The line between the folder tree and the asset list, dragged to resize the folder column.</summary>
        private sealed class FolderSplitter : UiElement
        {
            private const float Thickness = 5.0f;

            private readonly Func<float> _read;
            private readonly Action<float> _write;

            private bool _dragging;
            private float _dragStartPointer;
            private float _dragStartWidth;

            public FolderSplitter(Func<float> read, Action<float> write)
            {
                _read = read;
                _write = write;
                Width = Thickness;
            }

            protected override bool IsSelfHitTestVisible => true;

            protected override Vector2 MeasureOverride(Vector2 availableSize) => new(Thickness, 0.0f);

            protected override void OnPointerPressed(UiPointerEventArgs e)
            {
                if (e.Button != PointerButton.Left)
                {
                    return;
                }

                _dragging = true;
                _dragStartPointer = e.Position.X;
                _dragStartWidth = _read();

                CapturePointer();
                e.Handled = true;
            }

            protected override void OnPointerMoved(UiPointerEventArgs e)
            {
                if (!_dragging)
                {
                    return;
                }

                _write(_dragStartWidth + (e.Position.X - _dragStartPointer));
                e.Handled = true;
            }

            protected override void OnPointerReleased(UiPointerEventArgs e)
            {
                if (!_dragging)
                {
                    return;
                }

                _dragging = false;
                ReleasePointerCapture();
                e.Handled = true;
            }

            public override UiCursor GetCursor(Vector2 point) => UiCursor.SizeWE;

            protected override void OnRender(ManaUiDrawList drawList)
            {
                if (Root?.Theme is not { } theme)
                {
                    return;
                }

                var colour = _dragging ? theme.Accent : IsPointerOver ? theme.BorderStrong : theme.Border;
                var centre = Bounds.X + Bounds.Width * 0.5f;
                drawList.FillRect(new UiRect(centre - 0.5f, Bounds.Y, 1.0f, Bounds.Height), colour);
            }
        }

        /// <summary>
        /// Folders only, like the Content Browser's own tree -- picking one narrows the main list to it, the
        /// same way typing its name into the filter box would, since both are the same substring match
        /// against each asset's full path.
        /// </summary>
        private static UiElement FolderSidebar(UiTheme theme, MeshAssetLibrary library)
        {
            var tree = new TreeView
            {
                ItemsSource = new FolderRoots(library),
                ChildrenSelector = FolderChildrenOf,
                HasChildrenSelector = static item => item is MeshAssetFolder folder && folder.Folders.Count > 0,
                IsExpandedByDefault = static _ => true,
                ItemTemplate = item => BuildFolderRow(theme, item),

                // Ignorant of the text filter on purpose -- see MeshAssetLibrary.FolderTree. Formats still
                // change which folders have anything in them, so those are still part of the signature.
                StructureSignature = () => HashCode.Combine(library.Assets.Count, library.Formats)
            };

            tree.SelectionChanged += (_, item) =>
            {
                // A folder's own (possibly collapsed-chain) name is exactly what the search box would need
                // typed in to show the same assets, so setting it there is what "select a folder" means.
                library.Filter = item is MeshAssetFolder folder ? folder.Name : string.Empty;
            };

            var allAssets = new Button()
                .Label("All Assets")
                .Variant(ButtonVariant.Subtle)
                .StretchH()
                .OnClick(() => library.Filter = string.Empty);

            return new Grid().Rows("Auto, *", theme.Spacing).Columns("*").Clip().Children(
                allAssets.GridRow(0),
                tree.Scrollable().GridRow(1));
        }

        private sealed class FolderRoots : System.Collections.IEnumerable
        {
            private readonly MeshAssetLibrary _library;

            public FolderRoots(MeshAssetLibrary library)
            {
                _library = library;
            }

            public System.Collections.IEnumerator GetEnumerator()
            {
                return _library.FolderTree.Folders.GetEnumerator();
            }
        }

        private static System.Collections.IEnumerable? FolderChildrenOf(object item)
        {
            return item is MeshAssetFolder folder ? folder.Folders : null;
        }

        private static UiElement BuildFolderRow(UiTheme theme, object item)
        {
            if (item is not MeshAssetFolder folder)
            {
                return new TextBlock().Text(item.ToString() ?? string.Empty);
            }

            return new StackPanel().Horizontal(6.0f).Children(
                new TextBlock().Text(folder.Name).Ellipsis().CenterV(),
                new TextBlock()
                    .Text(folder.TotalEntries.ToString())
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .CenterV());
        }

        /// <summary>The transform new placements arrive with, broken out as its own panel rather than
        /// stealing the list's headroom the way it did stacked above it.</summary>
        private static UiElement TransformPanel(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            return new StackPanel().Vertical(theme.Spacing).Children(
                new TextBlock()
                    .Text("Placement")
                    .FontFamily(theme.FontFamilyMedium)
                    .Foreground(theme.Text),

                new TextBlock()
                    .Text("Applied to models dropped onto the window and to anything placed from the list.")
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .Wrap(),

                Row(theme, "Offset", Vector(
                    () => viewModel.ImportPositionOffset,
                    value => viewModel.ImportPositionOffset = value,
                    step: 0.1f)),

                Row(theme, "Rotation", Vector(
                    () => viewModel.ImportRotationDegrees,
                    value => viewModel.ImportRotationDegrees = value,
                    step: 1.0f)),

                Row(theme, "Scale", Vector(
                    () => viewModel.ImportScale,
                    value => viewModel.ImportScale = value,
                    step: 0.05f)),

                SnapGrid(theme, viewModel));
        }

        /// <summary>
        /// The one snap switch, shared with the gizmo and the Edit menu. The sizes live in Edit &gt;
        /// Preferences rather than here, so this panel stays as small as it was.
        /// </summary>
        private static UiElement SnapGrid(UiTheme theme, VoxelEditorViewModel viewModel)
        {
            var snap = new CheckBox("Snap to grid") { IsChecked = viewModel.Snap.Enabled };
            snap.CheckedChanged += (_, value) =>
            {
                if (!snap.IsApplyingBinding)
                {
                    viewModel.Snap.Enabled = value;
                }
            };

            // Polled because the menu and the preferences panel flip the same switch.
            snap.BindPolled(c => c.IsChecked, () => viewModel.Snap.Enabled);

            var hint = new TextBlock()
                .Foreground(theme.TextMuted)
                .FontSize(theme.FontSizeSmall)
                .Wrap();
            hint.BindTextPolled(() =>
                $"Moves in {viewModel.Snap.TranslateStep:0.##} units, turns in {viewModel.Snap.RotationStepDegrees:0.##}°. Change in Edit > Preferences.");

            return new StackPanel().Vertical(theme.Spacing).Children(snap, hint);
        }

        private static UiElement Picker(UiTheme theme, MeshAssetLibrary library, MeshThumbnailCache? thumbnails)
        {
            var grid = ThumbnailGrid(theme, library, thumbnails);

            var filter = new TextBox { Placeholder = "Filter by name or folder" };
            filter.TextChanged += (_, text) =>
            {
                if (!filter.IsApplyingBinding)
                {
                    library.Filter = text;
                }
            };

            // Polled rather than one-way: the folder sidebar can also set the filter (picking a folder is
            // the same as typing its name), and the box has to show that rather than sitting there blank
            // while the list is narrowed to something it never displayed.
            filter.BindPolled(t => t.Text, () => library.Filter);

            var count = new TextBlock()
                .Foreground(theme.TextMuted)
                .FontSize(theme.FontSizeSmall)
                .CenterV();

            // Scanning is driven from here rather than the render loop: the panel is the only thing that
            // wants the list, and a closed panel should not be walking the asset tree every two seconds.
            count.BindTextPolled(() =>
            {
                library.RequestScan();
                library.PollScan();

                if (library.IsScanning)
                {
                    return "scanning…";
                }

                var total = library.Assets.Count;
                return $"{total} asset{(total == 1 ? "" : "s")}";
            });

            var warning = new TextBlock().Foreground(theme.Warning).FontSize(theme.FontSizeSmall).Wrap();
            warning.BindTextPolled(() => library.ScanError ?? string.Empty);
            warning.BindPolled(
                t => t.Visibility,
                () => library.ScanError == null ? Visibility.Collapsed : Visibility.Visible);

            // No footer: placing used to need a select-then-press-Place row of its own, but right-click on
            // any tile already offers Place directly (see ShowContextMenu), and dragging a tile into the
            // scene always has. A dedicated button and a "what's selected" label were both just a slower
            // way to do what right-click already does in one step, and removing them hands their row back
            // to the asset grid above it.
            //
            // Clipped, so nothing can leak past the panel whatever the arithmetic says. A grid hands out
            // more than it has when its fixed rows already exceed the space, and the overflow lands on top
            // of whatever is below rather than disappearing.
            //
            // The fixed rows stay terse: the filter row carries the count, Rescan and the formats dropdown
            // together -- filters are a rare adjustment, not worth a row of their own. The tile grid comes
            // right after that, since it's what the panel is actually for; the size slider sits just below
            // it, reachable without opening anything but out of the way of the assets themselves.
            return new Grid().Rows("Auto, Auto, *, Auto", theme.Spacing).Columns("*").Clip().Children(
                new Grid().Columns("*, Auto, Auto, Auto", theme.Spacing).Rows("Auto").GridRow(0).Children(
                    filter.GridColumn(0),
                    count.GridColumn(1),
                    new Button().Label("Rescan").Variant(ButtonVariant.Subtle)
                        .OnClick(() => library.RequestScan(force: true))
                        .GridColumn(2),
                    FormatsDropdown(theme, library).GridColumn(3)),

                warning.GridRow(1),

                // No minimum height. A minimum on the star row does not shrink when the panel does -- it
                // holds its size, the grid overflows, and the content runs out through whatever is
                // underneath it. Whatever is left over is the right amount, including none.
                grid.GridRow(2),

                TileSizeControl(theme).GridRow(3));
        }

        // ----- thumbnail grid ---------------------------------------------------------------------------

        private const float MinThumbnailDisplaySize = 56.0f;
        private const float DefaultThumbnailDisplaySize = 76.0f;
        private const float MaxThumbnailDisplaySize = MeshThumbnailCache.ThumbnailSize;
        private const float ThumbnailDisplaySizeStep = 4.0f;
        private const float TileHorizontalChrome = 14.0f;
        private const float TileVerticalChrome = 36.0f;

        private static float _thumbnailDisplaySize = DefaultThumbnailDisplaySize;

        private static float ThumbnailDisplaySize =>
            Math.Clamp(_thumbnailDisplaySize, MinThumbnailDisplaySize, MaxThumbnailDisplaySize);

        /// <summary>Tile frame size in the wrapping asset grid, derived from the visible preview size.</summary>
        private static float TileWidth => ThumbnailDisplaySize + TileHorizontalChrome;

        private static float TileHeight => ThumbnailDisplaySize + TileVerticalChrome;

        private static UiElement TileSizeControl(UiTheme theme)
        {
            var readSize = () => ThumbnailDisplaySize;

            var slider = new Slider()
                .Range(MinThumbnailDisplaySize, MaxThumbnailDisplaySize, ThumbnailDisplaySizeStep)
                .Value(readSize())
                .OnChanged(value => _thumbnailDisplaySize = value)
                .BindPolled(s => s.Value, readSize);
            slider.Tooltip = "Thumbnail size";

            var value = new TextBlock()
                .Foreground(theme.TextMuted)
                .FontSize(theme.FontSizeSmall)
                .CenterV()
                .BindTextPolled(readSize, size => $"{size:0}px");

            return new Grid().Columns("Auto, *, 40", 6.0f).Rows("Auto").Children(
                new TextBlock().Text("Size").Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall).CenterV().GridColumn(0),
                slider.GridColumn(1).CenterV(),
                value.GridColumn(2));
        }

        /// <summary>
        /// The asset list as a wrapping grid of previews.
        /// <para>
        /// A tree in a panel this wide left most of its width empty -- one asset per row, and the row was a
        /// short filename. Tiles spend that width instead: the same panel shows roughly ten times as many
        /// assets, and each says what it is by looking like it rather than by being named.
        /// </para>
        /// <para>
        /// Tiles are rebuilt only when the filtered list actually changes. Each tile asks for its thumbnail
        /// once a cache exists; that is deliberately polled because the workspace can be built before the
        /// WebGPU host has installed the shared cache.
        /// </para>
        /// </summary>
        private static UiElement ThumbnailGrid(
            UiTheme theme,
            MeshAssetLibrary library,
            MeshThumbnailCache? thumbnails)
        {
            var content = new DynamicContent()
                .Showing<IReadOnlyList<MeshAssetEntry>>(
                    () => library.FilteredEntries,
                    entries =>
                    {
                        // Only the rows on screen are built: a pack of pictures lists thousands of tiles.
                        return new VirtualWrapPanel
                        {
                            Spacing = theme.Spacing,
                            Items = entries.Cast<object>().ToList(),
                            CreateItem = index => Tile(theme, library, thumbnails, entries[index]),
                            ItemSize = () => new Vector2(TileWidth, TileHeight)
                        };
                    })
                .Empty(new TextBlock()
                    .Text("Nothing matches.")
                    .Foreground(theme.TextMuted)
                    .CenterH());

            return content.Scrollable().Scrolls(
                vertical: ScrollBarVisibility.Auto,
                horizontal: ScrollBarVisibility.Disabled);
        }

        private static UiElement Tile(
            UiTheme theme,
            MeshAssetLibrary library,
            MeshThumbnailCache? thumbnails,
            MeshAssetEntry entry)
        {
            var path = entry.PrimaryPath;
            MeshAssetRow? draggable = null;

            // Asked for by a tile-level poll rather than only here. The workspace is built before the host has
            // a WebGPU cache ready, so construction-time-only requests strand the grid until a manual refresh
            // queues one asset later. The local flag keeps the per-frame poll cheap after the one real request.
            var thumbnailRequested = false;
            void RequestThumbnailIfAvailable()
            {
                if (!IsThumbnailRequestVisible(draggable))
                {
                    return;
                }

                if (draggable?.Root is { } root)
                {
                    MeshThumbnailImages.Touch(root, path);
                }

                if (thumbnailRequested)
                {
                    return;
                }

                var cache = thumbnails ?? ManaUiDockDemo.MeshThumbnails;
                if (cache == null)
                {
                    return;
                }

                // Listing remote assets must never fetch their full models just for a thumbnail, but a pre-built
                // preview is a small separate file, so it is fetched even before the model is downloaded.
                if (ManaCore.Assets.ManaAssetCatalog.Current is { } catalog
                    && !catalog.IsReady(path)
                    && catalog.ThumbnailPathOf(path) == null)
                {
                    return;
                }
                cache.Request(path);
                thumbnailRequested = true;
            }

            var preview = new UiImage { Fit = UiImageFit.Contain };
            preview.Width = ThumbnailDisplaySize;
            preview.Height = ThumbnailDisplaySize;

            // Required, not optional: a UV is normalised against the atlas, so it carries neither the image's
            // size nor its aspect ratio. Without this the Contain fit has nothing to fit and draws nothing --
            // which looks exactly like a thumbnail that was never generated.
            preview.SourceSize = new Vector2(MeshThumbnailCache.ThumbnailSize, MeshThumbnailCache.ThumbnailSize);

            // The placeholder is the format badge, which is what the old list showed anyway -- so a tile is
            // never empty while its preview is being generated, and an asset whose preview fails keeps a
            // readable tile forever rather than a hole.
            var badge = FormatBadge(theme, path);

            // The thumbnail's visibility, its UV and the badge that stands in for it are all settled in one throttled
            // sync at the end of this method -- see there for why.
            // No byte-level progress is available -- ManaAssetCatalog awaits a browser fetch whole rather
            // than streaming it in observable chunks -- so this is indeterminate rather than a real
            // percentage. It still answers "is this one actually doing something", which a static format
            // badge does not.
            var downloadProgress = new ProgressBar { Thickness = 3.0f, IsIndeterminate = true }.Bottom();


            var caption = new TextBlock()
                .Text(entry.DisplayName)
                .FontSize(theme.FontSizeSmall)
                .Ellipsis()
                .CenterH();

            var body = new Grid().Rows("*, Auto", 2.0f).Columns("*").Children(
                new Grid().Rows("*").Columns("*").GridRow(0).Children(preview.Center(), badge.Center(), downloadProgress),
                caption.GridRow(1));

            // Selection is drawn by a frame inside the tile rather than by the button's own state: Button has
            // no checked variant, and an outline is the clearer signal in a grid anyway -- a pressed-looking
            // fill on one tile among two hundred is easy to miss.
            var frame = new Border().Rounded(3.0f).Content(body);
            frame.BindPolled(b => b.BorderColor,
                () => IsSelected(library, path) ? theme.Accent : UiColor.Transparent);
            frame.BindPolled(b => b.BorderThickness, () => IsSelected(library, path) ? 2.0f : 0.0f);

            var tile = frame;

            // Polled rather than assigned once: file size and source are known as soon as the catalog is,
            // but how long the download took is not knowable until it finishes, well after this tile was
            // built. A plain assignment would freeze the tooltip at whatever was true the moment the grid
            // was populated.

            // Wrapped in the same MeshAssetRow the list rows used, so dragging a tile into the scene still
            // works -- that gesture is half of what this panel is for, and the Place button's tooltip promises
            // it. The row handles press, threshold and ghost; the button inside it handles click and hover.
            draggable = new MeshAssetRow(entry) { Child = tile, Library = library };
            var row = draggable;
            draggable.Clicked += (_, _) => library.Click(path,
                toggle: row.ClickModifiers.HasFlag(UiModifiers.Control) || row.ClickModifiers.HasFlag(UiModifiers.Meta),
                range: row.ClickModifiers.HasFlag(UiModifiers.Shift));
            draggable.Width = TileWidth;
            draggable.Height = TileHeight;

            // Mirrored onto the outer element: it is what the pointer meets first, so a tooltip only on the
            // button inside would be shadowed by the drag handler wrapping it.
            // One polled binding per tile does all of the tile's slow-changing work, a few times a second, by setting
            // the properties directly. There is a tile for every model in every pack -- a thousand or more -- and
            // they used to carry about ten polled bindings each, every one read on every frame; that was the biggest
            // share of what the editor spent drawing its own UI, and it grew with every asset pack added. The
            // binding writes nothing itself (it always answers null), so it is only a way to be called each frame,
            // and the throttle makes nearly all of those calls free. A thumbnail turning up, a tile resizing with
            // the slider or a tooltip refreshing a tenth of a second late goes unnoticed. Selection is the one
            // thing that is not throttled: a click should light its tile at once.
            var sync = Throttled<object?>(() =>
            {
                RequestThumbnailIfAvailable();

                var hasImage = MeshThumbnailImages.HasImage(path);
                preview.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
                badge.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
                if (hasImage)
                {
                    preview.SourceUv = MeshThumbnailImages.UvFor(path);
                }

                preview.Width = ThumbnailDisplaySize;
                preview.Height = ThumbnailDisplaySize;
                draggable!.Width = TileWidth;
                draggable.Height = TileHeight;

                var downloading = ManaCore.Assets.ManaAssetCatalog.Current?.IsDownloading(path) == true;
                downloadProgress.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
                if (downloading)
                {
                    downloadProgress.ElapsedSeconds = (float)ManaTime.GlobalTime.GameTime.TotalSeconds;
                }

                var tooltip = TooltipFor(entry);
                tile.Tooltip = tooltip;
                draggable.Tooltip = tooltip;
                return null;
            });
            tile.BindPolled(t => t.Tag, () => sync());
            return draggable;
        }

        /// <summary>
        /// How often a tile does its slow-changing work (thumbnail, size, tooltip, download state). Zero does it
        /// on every poll, which is what tests want: they drive the layout a handful of times in a few
        /// microseconds and expect each pass to see the change they just made.
        /// </summary>
        public static long ThumbnailPollMilliseconds { get; set; } = 120;

        /// <summary>
        /// Wraps a polled read so it is worked out at most once per <paramref name="milliseconds"/> and the last
        /// answer returned in between. Each wrapper starts at a random point in its interval, so a thousand tiles
        /// do not all come due on the same frame and turn the saving into a periodic hitch.
        /// </summary>
        public static Func<T> Throttled<T>(Func<T> read)
        {
            var next = long.MinValue;
            T last = default!;
            return () =>
            {
                var milliseconds = ThumbnailPollMilliseconds;
                var now = Environment.TickCount64;
                if (milliseconds <= 0 || next == long.MinValue || now >= next)
                {
                    last = read();
                    next = now + milliseconds - Random.Shared.NextInt64(0, Math.Max(1, milliseconds / 2));
                }

                return last;
            };
        }

        internal static bool IsThumbnailRequestVisible(UiElement? element)
        {
            if (element == null || element.Root == null || element.Bounds.IsEmpty)
            {
                return false;
            }

            var visible = element.Bounds;
            for (var current = element.Parent; current != null; current = current.Parent)
            {
                if (current.Bounds.IsEmpty)
                {
                    return false;
                }

                if (!current.ClipToBounds)
                {
                    continue;
                }

                visible = visible.Intersect(current.Bounds);
                if (visible.IsEmpty)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSelected(MeshAssetLibrary library, string path) => library.IsSelected(path);

        private static UiElement FormatBadge(UiTheme theme, string path)
        {
            var (label, _) = MeshAssetLibrary.Badge(path);

            return new Border()
                .Fill(theme.SurfaceRaised)
                .Rounded(3.0f)
                .Content(new TextBlock()
                    .Text(label)
                    .FontSize(theme.FontSizeSmall)
                    .Foreground(theme.TextMuted)
                    .Margin(5.0f, 2.0f));
        }

        private static TreeView BuildTree(UiTheme theme, MeshAssetLibrary library)
        {
            var tree = new TreeView
            {
                // Live rather than a captured array: filtering rebuilds the tree into a new root, and an
                // ItemsSource holding the old one would keep showing the list from before the filter.
                ItemsSource = new LibraryRoots(library),
                ChildrenSelector = ChildrenOf,
                HasChildrenSelector = static item => item is MeshAssetFolder folder && !folder.IsEmpty,

                // Folders open on arrival. This is a file list, and one that opens showing nothing but
                // folder names has hidden the only thing it exists to show.
                IsExpandedByDefault = static item => item is MeshAssetFolder,
                ItemTemplate = item => BuildRow(theme, library, item),

                // The library raises Changed, but the tree polls a signature anyway: a scan completes on a
                // worker thread, and the count is the cheapest thing that says the list is not what it was.
                StructureSignature = () => HashCode.Combine(
                    library.Assets.Count,
                    library.Filter,
                    library.Formats)
            };

            tree.SelectionChanged += (_, item) =>
            {
                if (item is MeshAssetEntry entry)
                {
                    library.SelectedAsset = entry.PrimaryPath;
                }
            };

            // Double click places it. The same gesture as opening a file, which is what this is.
            tree.ItemActivated += (_, item) =>
            {
                if (item is MeshAssetEntry entry)
                {
                    MeshImportRequests.RequestPlacement(entry.PrimaryPath);
                }
            };

            return tree;
        }

        /// <summary>
        /// The top level of the library, re-read each time the tree rebuilds. The root folder itself is not
        /// shown -- an "Assets" row wrapping everything is one click to get past and says nothing.
        /// </summary>
        private sealed class LibraryRoots : System.Collections.IEnumerable
        {
            private readonly MeshAssetLibrary _library;

            public LibraryRoots(MeshAssetLibrary library)
            {
                _library = library;
            }

            public System.Collections.IEnumerator GetEnumerator()
            {
                var root = _library.Tree;

                foreach (var folder in root.Folders)
                {
                    yield return folder;
                }

                foreach (var entry in root.Entries)
                {
                    yield return entry;
                }
            }
        }

        private static System.Collections.IEnumerable? ChildrenOf(object item)
        {
            if (item is not MeshAssetFolder folder)
            {
                return null;
            }

            if (folder.Entries.Count == 0)
            {
                return folder.Folders;
            }

            var combined = new List<object>(folder.Folders.Count + folder.Entries.Count);
            combined.AddRange(folder.Folders);
            combined.AddRange(folder.Entries);
            return combined;
        }

        private static UiElement BuildRow(UiTheme theme, MeshAssetLibrary library, object item)
        {
            if (item is MeshAssetFolder folder)
            {
                return new StackPanel().Horizontal(6.0f).Children(
                    new TextBlock().Text(folder.Name).CenterV(),
                    new TextBlock()
                        .Text(folder.TotalEntries.ToString())
                        .Foreground(theme.TextMuted)
                        .FontSize(theme.FontSizeSmall)
                        .CenterV());
            }

            if (item is not MeshAssetEntry entry)
            {
                return new TextBlock().Text(item.ToString() ?? string.Empty);
            }

            var (label, color) = MeshAssetLibrary.Badge(entry.PrimaryPath);

            var name = entry.HasVariants
                ? $"{entry.DisplayName}  +{entry.Variants.Count - 1}"
                : entry.DisplayName;

            return new MeshAssetRow(entry)
            {
                Child = new StackPanel().Horizontal(6.0f).Children(
                    // The format as a coloured chip rather than an extension in the name: the name is what
                    // is being scanned for, and a row of ".glb" repeated is noise in front of it.
                    new Border()
                        .Fill(color)
                        .Rounded(3.0f)
                        .PaddingOf(4.0f, 0.0f)
                        .CenterV()
                        .Content(new TextBlock()
                            .Text(label)
                            .Foreground(theme.TextOnAccent)
                            .FontSize(theme.FontSizeSmall)),

                    new TextBlock().Text(name).Ellipsis().CenterV().WithTooltip(TooltipFor(entry)))
            };
        }

        private static string TooltipFor(MeshAssetEntry entry)
        {
            var tooltip = MeshAssetLibrary.Tooltip(entry.PrimaryPath);

            // Null on desktop (ManaAssetCatalog.Current never gets set there) and for a local user import
            // even in the browser (Key() finds nothing to report on), in which case the path itself is
            // already the whole story and nothing below has anything to add.
            if (ManaCore.Assets.ManaAssetCatalog.Current is { } catalog)
            {
                var lines = new System.Text.StringBuilder(tooltip);
                var wroteAny = false;

                void Line(string text)
                {
                    lines.AppendLine();
                    if (!wroteAny)
                    {
                        lines.AppendLine();
                        wroteAny = true;
                    }

                    lines.Append(text);
                }

                if (catalog.SizeOf(entry.PrimaryPath) is { } size)
                {
                    Line($"Size: {FormatBytes(size)}");
                }

                if (catalog.UrlOf(entry.PrimaryPath) is { } url)
                {
                    Line($"Source: {url}");
                }

                if (ManaCore.Assets.ManaAssetDownloadActivity.DurationOf(entry.PrimaryPath) is { } duration)
                {
                    Line($"Downloaded in {duration.TotalSeconds:0.0}s");
                }

                tooltip = lines.ToString();
            }

            if (!entry.HasVariants)
            {
                return tooltip;
            }

            // Which formats a model ships in decides what gets imported, so the alternatives are worth
            // showing even though picking one is not offered yet.
            var withVariants = new System.Text.StringBuilder(tooltip);
            withVariants.AppendLine();
            withVariants.AppendLine();
            withVariants.Append("Also available as:");

            for (var i = 1; i < entry.Variants.Count; i++)
            {
                withVariants.AppendLine();
                withVariants.Append("  ").Append(MeshAssetLibrary.DisplayName(entry.Variants[i]));
            }

            return withVariants.ToString();
        }

        private static string FormatBytes(long bytes) =>
            bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024.0):0.0} MB" : $"{bytes / 1024.0:0} KB";

        private static readonly MeshAssetFormats[] AllFormats =
        {
            MeshAssetFormats.Glb,
            MeshAssetFormats.Gltf,
            MeshAssetFormats.Fbx,
            MeshAssetFormats.Dae,
            MeshAssetFormats.Obj,
            MeshAssetFormats.Stl,
            MeshAssetFormats.Ply,
            MeshAssetFormats.Png
        };

        /// <summary>
        /// One dropdown button instead of a whole row of checkboxes, which could wrap to two lines and
        /// push the actual asset thumbnails further down before any were visible. Built as a small popup
        /// of checkboxes rather than reusing <see cref="ComboBox"/> (single-select) or <see
        /// cref="MenuFlyout"/> (closes itself the instant any item is clicked, which is right for a menu
        /// command but wrong for ticking several formats in a row).
        /// </summary>
        private static UiElement FormatsDropdown(UiTheme theme, MeshAssetLibrary library)
        {
            // A count rather than the format names themselves: the names' combined width varies with which
            // ones happen to be on, and a button that resizes itself every time a checkbox is ticked reads
            // as broken. A count is bounded ("Formats (7/7)") regardless of which are enabled.
            string Summary()
            {
                var enabledCount = AllFormats.Count(library.IsFormatEnabled);
                return $"Formats ({enabledCount}/{AllFormats.Length})";
            }

            UiPopup? popup = null;
            var button = new Button().Variant(ButtonVariant.Subtle);
            button.BindPolled(b => b.Text, Summary);

            button.OnClick(() =>
            {
                if (popup is { IsOpen: true })
                {
                    popup.Close();
                    popup = null;
                    return;
                }

                var root = button.Root;
                if (root == null)
                {
                    return;
                }

                var list = new StackPanel().Vertical(2.0f);
                foreach (var format in AllFormats)
                {
                    var toggle = new CheckBox(format.ToString().ToUpperInvariant());
                    var captured = format;

                    toggle.IsChecked = library.IsFormatEnabled(captured);
                    toggle.CheckedChanged += (_, value) =>
                    {
                        if (!toggle.IsApplyingBinding)
                        {
                            library.SetFormatEnabled(captured, value);
                        }
                    };

                    toggle.BindPolled(c => c.IsChecked, () => library.IsFormatEnabled(captured));
                    list.Children.Add(toggle);
                }

                popup = new UiPopup
                {
                    Placement = PopupPlacement.Bottom,
                    Child = new Border()
                        .Fill(theme.SurfaceRaised)
                        .Outline(theme.BorderStrong, 1.0f)
                        .Rounded(theme.CornerRadius)
                        .Shadow(14.0f)
                        .PaddingOf(6.0f)
                        .Content(list)
                };
                popup.Closed += (_, _) => popup = null;
                root.Popups.Show(popup, button);
            });

            return button;
        }

        private static UiElement Row(UiTheme theme, string label, UiElement editor)
        {
            return new Grid().Columns("70, *", theme.Spacing).Rows("Auto").Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).Ellipsis().CenterV().GridColumn(0),
                editor.GridColumn(1));
        }

        private static VectorBox Vector(Func<Vector3> read, Action<Vector3> write, float step)
        {
            var box = new VectorBox { Components = 3, Step = step, DecimalPlaces = 3, Value = read() };

            box.ValueChanged += (_, value) =>
            {
                if (!box.IsApplyingBinding)
                {
                    write(new Vector3(value.X, value.Y, value.Z));
                }
            };

            box.BindPolled(b => b.Value, read);
            return box;
        }
    }

    /// <summary>
    /// A tree row that can be dragged out of the panel and into the scene.
    /// <para>
    /// The tree owns pressing and selection, so this only watches for a press that travels far enough to
    /// mean something else -- below that threshold the press is still a click, and the row must not steal
    /// it. Reporting the drag rather than acting on it keeps the panel unaware of the viewport.
    /// </para>
    /// </summary>
    public sealed class MeshAssetRow : Decorator
    {
        private const float DragThreshold = 4.0f;

        // The one row (if any) currently dragging, so a mouse-up the OS delivered outside this window --
        // losing focus mid-drag skips MouseUp entirely -- can still be recovered from. VoxelEditorGameMode
        // polls the real button state every frame for its own placement logic and calls ForceEndActiveDrag
        // the frame it sees the button go up; without this, OnPointerReleased simply never came and the
        // ghost and this row's pointer capture were stuck until something else happened to drag over the
        // same row and let go there.
        private static MeshAssetRow? _activeDraggingRow;

        private readonly MeshAssetEntry _entry;

        private UiDragGhost? _ghost;
        private Vector2 _pressedAt;
        private bool _pressed;
        private bool _dragging;

        public MeshAssetRow(MeshAssetEntry entry)
        {
            _entry = entry;
        }

        /// <summary>
        /// The library whose selection a drag carries: dragging one of several selected assets drags them all.
        /// Null drags just this row's.
        /// </summary>
        public MeshAssetLibrary? Library { get; init; }
        public Func<List<MenuItem>>? ContextMenuItems { get; init; }
        public Func<bool>? CanDrag { get; init; }

        /// <summary>The modifier keys held when the press that became <see cref="Clicked"/> went down.</summary>
        public UiModifiers ClickModifiers { get; private set; }

        // What this drag carries: the whole selection when this row is part of it, else only this row.
        private IReadOnlyList<string> DragAssets =>
            Library is { SelectedAssets.Count: > 1 } library && library.IsSelected(_entry.PrimaryPath)
                ? library.SelectedAssets.ToList()
                : [_entry.PrimaryPath];

        /// <summary>
        /// What follows the cursor while this row is being dragged. Built fresh each time rather than
        /// reusing the row's own content, which is still in the list and cannot be in two places.
        /// </summary>
        private UiElement BuildGhost(UiTheme theme, int count)
        {
            var (label, color) = MeshAssetLibrary.Badge(_entry.PrimaryPath);
            var name = count > 1 ? $"{count} models" : _entry.DisplayName;

            return new Border()
                .Fill(theme.SurfaceRaised)
                .Outline(theme.Accent, 1.0f)
                .Rounded(theme.CornerRadius)
                .Shadow(12.0f)
                .PaddingOf(8.0f, 5.0f)
                .Content(new StackPanel().Horizontal(6.0f).Children(
                    new Border()
                        .Fill(color)
                        .Rounded(3.0f)
                        .PaddingOf(4.0f, 0.0f)
                        .CenterV()
                        .Content(new TextBlock()
                            .Text(label)
                            .Foreground(theme.TextOnAccent)
                            .FontSize(theme.FontSizeSmall)),

                    new TextBlock().Text(name).CenterV()));
        }

        protected override bool IsSelfHitTestVisible => true;

        /// <summary>
        /// A press that ended without becoming a drag. The tree used to supply selection by handling the press
        /// itself, which a grid of tiles has no equivalent of -- and putting a Button inside instead does not
        /// work, because the button consumes the press this row needs in order to start a drag at all.
        /// </summary>
        public event EventHandler? Clicked;

        /// <summary>
        /// Whether the pointer is over any part of the interface, as opposed to the scene behind it.
        /// <para>
        /// A geometric test, deliberately: the ghost is transparent to the pointer and the panels are not,
        /// so this answers what is under the cursor rather than what happens to hold the capture.
        /// </para>
        /// </summary>
        private bool IsOverUi(Vector2 position) => Root?.HitTest(position) != null;

        /// <summary>
        /// Right-click actions for one asset. Resolves the shared thumbnail cache the same way <see
        /// cref="MeshImportManaView.Tile"/> does rather than taking one in the constructor -- this row also
        /// backs a plain tree row (<see cref="MeshImportManaView.BuildRow"/>), which is built before a
        /// cache exists at all.
        /// </summary>
        private void ShowContextMenu(Vector2 position)
        {
            if (Root is not { } root)
            {
                return;
            }

            if (ContextMenuItems != null)
            {
                MenuFlyout.ShowAt(root, position, ContextMenuItems());
                return;
            }
            var path = _entry.PrimaryPath;
            var isZip = ZipAssetReferences.TryDecodeZip(path, out var zipPath, out var entryPath);

            var items = new List<MenuItem>
            {
                new MenuItem("Place", () => MeshImportRequests.RequestPlacement(path)),
                MenuItem.Separator(),

                new MenuItem("Refresh Thumbnail",
                    () => (ManaUiDockDemo.MeshThumbnails)?.ForceRefresh(path)),

                new MenuItem("Show in Explorer",
                    () =>
                    {
                        if (isZip)
                        {
                            FileExplorer.RevealZipEntry(zipPath, entryPath);
                        }
                        else
                        {
                            FileExplorer.Reveal(path);
                        }
                    })
                    .WithTooltip(isZip ? "Attempts to select the asset inside the archive." : string.Empty),

                new MenuItem("Copy Path",
                    () => TextBox.Clipboard.Text = isZip ? $"{zipPath} :: {entryPath}" : path)
            };

            // Pictures picked together can become a sprite asset: frames of a sheet, or the pieces of a 9-slice.
            var pictures = PickedPictures(path);
            if (pictures.Count > 0)
            {
                items.Insert(2, new MenuItem($"Make Sprite Sheet ({pictures.Count} frame{(pictures.Count == 1 ? "" : "s")})",
                    () => MakeSpriteAsset(SpriteAssetFactory.Sheet(pictures), Mana.Code.Content.ManaSpriteSheet.Extension))
                    .WithTooltip("A sheet with these as its frames, in the order shown"));
                items.Insert(3, new MenuItem("Make 9-Slice", () => MakeSpriteAsset(SpriteAssetFactory.Slice(pictures), Mana.Code.Content.ManaSpriteSlice.Extension))
                    .WithTooltip("Each picture goes where its name says (top_left, top, left, center...); the rest can be set in the editor"));
                items.Insert(4, MenuItem.Separator());
            }

            MenuFlyout.ShowAt(root, position, items);
        }

        // The selected pictures when this one is among them, else just this one; none unless they are all pictures.
        private List<string> PickedPictures(string path)
        {
            var picked = Library != null && Library.IsSelected(path) ? Library.SelectedAssets.ToList() : [path];
            return picked.All(asset => MeshAssetLibrary.FormatOf(asset) == MeshAssetFormats.Png)
                ? picked.Select(MeshAssetLibrary.SpritePicture).ToList()
                : [];
        }

        private static void MakeSpriteAsset(object asset, string extension)
        {
            var name = asset switch
            {
                Mana.Code.Content.ManaSpriteSheet sheet => sheet.Name,
                Mana.Code.Content.ManaSpriteSlice slice => slice.Name,
                _ => "Sprite"
            };
            SpriteAssetEditing.Open(SpriteAssetFactory.Save(asset, name, extension));
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            base.OnPointerPressed(e);

            if (e.Button == PointerButton.Right)
            {
                e.Handled = true;
                ShowContextMenu(e.Position);
                return;
            }

            if (e.Button != PointerButton.Left)
            {
                return;
            }

            // Any drag still sitting in the mailbox belongs to a gesture that ended without anyone claiming
            // it. Cleared here rather than on release, because the release is the frame the drop happens on
            // and whoever is going to act on it has not looked yet.
            MeshImportRequests.TakeDrag();

            _pressed = true;
            _pressedAt = e.Position;
            ClickModifiers = e.Modifiers;

            // Captured, because a drag out of the panel is by definition a drag off this row: without it the
            // moves follow the pointer to whatever it is over now, and the gesture ends the instant it starts
            // being one. The press is deliberately left unhandled so the tree still selects the row.
            CapturePointer();
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (_dragging)
            {
                _ghost?.Move(e.Position);
                MeshImportRequests.SetPointerOverUi(IsOverUi(e.Position));
                return;
            }

            if (!_pressed || CanDrag?.Invoke() == false || Vector2.Distance(e.Position, _pressedAt) < DragThreshold)
            {
                return;
            }

            // Capture is kept for the rest of the gesture rather than released here. Released, this row
            // never sees the button come up, so a drag that ended anywhere the game mode did not recognise
            // as a drop left the request set forever -- and every later drag was ignored because something
            // was already being dragged. That is the drag silently dying after a while.
            _dragging = true;
            _activeDraggingRow = this;
            var assets = DragAssets;
            MeshImportRequests.RequestDrag(_entry.PrimaryPath, assets);
            MeshImportRequests.SetPointerOverUi(IsOverUi(e.Position));

            if (Root is { } root)
            {
                _ghost = new UiDragGhost(root);
                _ghost.Show(BuildGhost(root.Theme, assets.Count), e.Position);
            }
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            base.OnPointerReleased(e);

            if (e.Button == PointerButton.Left && _pressed && !_dragging && Bounds.Contains(e.Position))
            {
                Clicked?.Invoke(this, EventArgs.Empty);
            }

            if (!_pressed && !_dragging)
            {
                return;
            }

            // The last word on where the drop landed. Hit-tested rather than read from the router's own
            // hover, which reports the captured element for the whole gesture and so would say the pointer
            // was over this row no matter where it actually was.
            EndDrag(_dragging ? IsOverUi(e.Position) : MeshImportRequests.PointerOverUi);
        }

        /// <summary>
        /// Ends whichever row is mid-drag, even though its own OnPointerReleased never fired -- see
        /// <see cref="_activeDraggingRow"/>. Idempotent: a no-op once the row has already ended normally.
        /// </summary>
        public static void ForceEndActiveDrag()
        {
            _activeDraggingRow?.EndDrag(MeshImportRequests.PointerOverUi);
        }

        private void EndDrag(bool pointerOverUi)
        {
            _pressed = false;
            ReleasePointerCapture();

            _ghost?.Hide();
            _ghost = null;

            if (_dragging)
            {
                MeshImportRequests.SetPointerOverUi(pointerOverUi);
            }

            if (ReferenceEquals(_activeDraggingRow, this))
            {
                _activeDraggingRow = null;
            }

            // Deliberately left set. The release is the drop: whoever acts on it reads the mailbox later in
            // the same frame, and clearing here meant the drop was gone before it was seen -- so it took a
            // second click to place anything. A stale one cannot accumulate, because the next press clears
            // it before starting.
            _dragging = false;
        }
    }

    /// <summary>
    /// What the import panel is asking for, as a mailbox the game mode drains each frame.
    /// <para>
    /// The panel is built once and lives in the dock; the game mode owns the scene. Rather than handing the
    /// view a reference to the editor, it leaves a request here and the game mode takes it -- which is also
    /// what lets a test assert what a click asked for without a game mode existing at all.
    /// </para>
    /// </summary>
    public static class MeshImportRequests
    {
        private static string? _placement;
        public static string? PendingPlacement => _placement;
        private static string? _drag;
        private static IReadOnlyList<string> _dragAll = [];
        private static IReadOnlyList<string> _paletteAdd = [];

        /// <summary>The asset being dragged, until the drop is taken. Null when nothing is being dragged.</summary>
        public static string? Dragging => _drag;

        /// <summary>
        /// Every asset being dragged -- several when a multi-selection is -- with <see cref="Dragging"/> among
        /// them. A drop onto a grid map palette takes them all; a drop into the scene places the one.
        /// </summary>
        public static IReadOnlyList<string> DraggingAll => _drag == null ? [] : _dragAll;

        /// <summary>Models to add to the selected grid map's palette -- its Add button, with several selected.</summary>
        public static void RequestPaletteAdd(IReadOnlyList<string> assets) => _paletteAdd = assets.ToList();

        public static IReadOnlyList<string> TakePaletteAdd()
        {
            var pending = _paletteAdd;
            _paletteAdd = [];
            return pending;
        }

        /// <summary>
        /// True while the dragged mesh is over the interface rather than the scene.
        /// <para>
        /// A drop here is a cancel, not a placement. The editor works the scene out from the ImGui
        /// viewport, which spans the whole window -- so without this, letting go over the import list put
        /// the mesh in the world behind it.
        /// </para>
        /// </summary>
        public static bool PointerOverUi { get; private set; }

        public static void SetPointerOverUi(bool overUi) => PointerOverUi = overUi;

        public static void RequestPlacement(string asset) => _placement = asset;

        public static void RequestDrag(string asset, IReadOnlyList<string>? all = null)
        {
            _drag = asset;
            _dragAll = all is { Count: > 0 } ? all : [asset];
        }

        /// <summary>Whether a drop should place anything: something dragged, and not over the interface.</summary>
        public static bool CanDrop => _drag != null && !PointerOverUi;

        /// <summary>Takes a pending placement, if any. Reading it clears it -- a request is acted on once.</summary>
        public static string? TakePlacement()
        {
            var pending = _placement;
            _placement = null;
            return pending;
        }

        public static string? TakeDrag()
        {
            var pending = _drag;
            _drag = null;
            return pending;
        }

        /// <summary>Takes the drop with everything in it -- see <see cref="DraggingAll"/>.</summary>
        public static IReadOnlyList<string> TakeDragAll()
        {
            var all = DraggingAll;
            _drag = null;
            _dragAll = [];
            return all;
        }

        public static void Clear()
        {
            _placement = null;
            _drag = null;
            _dragAll = [];
            _paletteAdd = [];
            PointerOverUi = false;
        }
    }
}
