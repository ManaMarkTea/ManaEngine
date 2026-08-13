using ManaUI;
using ManaVoxel.Code.Assets;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The mesh import panel: what gets imported, and the transform it arrives with.
    /// <para>
    /// The list itself is <see cref="MeshAssetLibrary"/>'s -- this only draws it. What the panel adds is the
    /// two ways of getting a mesh into the scene: drag a row into the viewport to place it under the cursor,
    /// or select and press the button to place it on the next click. Both end at the same request, so the
    /// game mode only has one thing to answer.
    /// </para>
    /// </summary>
    public static class MeshImportManaView
    {
        public static UiElement Build(UiTheme theme, VoxelEditorViewModel viewModel, MeshAssetLibrary library)
        {
            // A Grid rather than a stack: the tree needs a scroll viewer, and a viewer inside a vertical
            // stack is measured with unbounded height, so it grows to fit and never scrolls.
            return new Grid().Rows("Auto, *", theme.Spacing).Columns("*").Padding(8.0f).Children(
                new StackPanel().Vertical(theme.Spacing).GridRow(0).Children(
                    new TextBlock()
                        .Text("Applied to models dropped onto the window and to anything placed from the list below.")
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

                    new Border().Fill(theme.Divider).Height(1.0f).Margin(0.0f, 4.0f, 0.0f, 0.0f)),

                Picker(theme, library).GridRow(1));
        }

        private static UiElement Picker(UiTheme theme, MeshAssetLibrary library)
        {
            var tree = BuildTree(theme, library);

            var filter = new TextBox { Placeholder = "Filter by name or folder" };
            filter.TextChanged += (_, text) => library.Filter = text;

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

            var place = new Button()
                .Label("Place selected")
                .Variant(ButtonVariant.Primary)
                .StretchH()
                .OnClick(() =>
                {
                    if (library.SelectedAsset is { } asset)
                    {
                        MeshImportRequests.RequestPlacement(asset);
                    }
                });

            // Disabled rather than hidden: the button is what tells you the list is for placing things, and
            // a button that vanishes when nothing is selected teaches nothing.
            place.BindPolled(b => b.IsEnabled, () => library.SelectedAsset != null);

            return new Grid().Rows("Auto, Auto, Auto, *, Auto, Auto", theme.Spacing).Columns("*").Children(
                new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").GridRow(0).Children(
                    filter.GridColumn(0),
                    new Button().Label("Rescan").Variant(ButtonVariant.Subtle)
                        .OnClick(() => library.RequestScan(force: true))
                        .GridColumn(1)),

                new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").GridRow(1).Children(
                    count.GridColumn(0),
                    Formats(theme, library).GridColumn(1)),

                warning.GridRow(2),

                tree.Scrollable().Configure(viewer => viewer.MinHeight = 140.0f).GridRow(3),

                new TextBlock()
                    .Text("Drag a mesh into the scene to place it under the cursor, or select one and press "
                        + "Place. Escape cancels a placement in progress.")
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .Wrap()
                    .GridRow(4),

                place.GridRow(5));
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
            if (!entry.HasVariants)
            {
                return tooltip;
            }

            // Which formats a model ships in decides what gets imported, so the alternatives are worth
            // showing even though picking one is not offered yet.
            var lines = new System.Text.StringBuilder(tooltip);
            lines.AppendLine();
            lines.AppendLine();
            lines.Append("Also available as:");

            for (var i = 1; i < entry.Variants.Count; i++)
            {
                lines.AppendLine();
                lines.Append("  ").Append(MeshAssetLibrary.FileName(entry.Variants[i]));
            }

            return lines.ToString();
        }

        private static UiElement Formats(UiTheme theme, MeshAssetLibrary library)
        {
            var row = new WrapPanel { HorizontalSpacing = theme.Spacing, VerticalSpacing = 2.0f };

            foreach (var format in new[]
            {
                MeshAssetFormats.Glb,
                MeshAssetFormats.Gltf,
                MeshAssetFormats.Fbx,
                MeshAssetFormats.Dae,
                MeshAssetFormats.Obj,
                MeshAssetFormats.Stl,
                MeshAssetFormats.Ply
            })
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
                row.Children.Add(toggle);
            }

            return row;
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

        private readonly MeshAssetEntry _entry;

        private Vector2 _pressedAt;
        private bool _pressed;
        private bool _dragging;

        public MeshAssetRow(MeshAssetEntry entry)
        {
            _entry = entry;
        }

        protected override bool IsSelfHitTestVisible => true;

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            base.OnPointerPressed(e);

            if (e.Button != PointerButton.Left)
            {
                return;
            }

            _pressed = true;
            _pressedAt = e.Position;

            // Captured, because a drag out of the panel is by definition a drag off this row: without it the
            // moves follow the pointer to whatever it is over now, and the gesture ends the instant it starts
            // being one. The press is deliberately left unhandled so the tree still selects the row.
            CapturePointer();
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (!_pressed || Vector2.Distance(e.Position, _pressedAt) < DragThreshold)
            {
                return;
            }

            // Capture is kept for the rest of the gesture rather than released here. Released, this row
            // never sees the button come up, so a drag that ended anywhere the game mode did not recognise
            // as a drop left the request set forever -- and every later drag was ignored because something
            // was already being dragged. That is the drag silently dying after a while.
            _dragging = true;
            MeshImportRequests.RequestDrag(_entry.PrimaryPath);
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            base.OnPointerReleased(e);

            if (!_pressed && !_dragging)
            {
                return;
            }

            _pressed = false;
            ReleasePointerCapture();

            if (_dragging)
            {
                _dragging = false;

                // Whatever wanted the drop has had every frame since the pointer moved to take it. Anything
                // still sitting here was never claimed, and leaving it would block the next drag.
                MeshImportRequests.TakeDrag();
            }
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
        private static string? _drag;

        /// <summary>The asset being dragged, until the drop is taken. Null when nothing is being dragged.</summary>
        public static string? Dragging => _drag;

        public static void RequestPlacement(string asset) => _placement = asset;

        public static void RequestDrag(string asset) => _drag = asset;

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

        public static void Clear()
        {
            _placement = null;
            _drag = null;
        }
    }
}
