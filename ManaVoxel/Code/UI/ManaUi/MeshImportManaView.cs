using ManaUI;
using ManaVoxel.Code.Assets;

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

        public static UiElement Build(UiTheme theme, VoxelEditorViewModel viewModel, MeshAssetLibrary library)
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
                Picker(theme, library).GridColumn(2),
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
                    step: 0.05f)));
        }

        private static UiElement Picker(UiTheme theme, MeshAssetLibrary library)
        {
            var tree = BuildTree(theme, library);

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

            // Clipped, so nothing can leak past the panel whatever the arithmetic says. A grid hands out
            // more than it has when its fixed rows already exceed the space, and the overflow lands on top
            // of whatever is below rather than disappearing.
            return new Grid().Rows("Auto, Auto, Auto, *, Auto, Auto", theme.Spacing).Columns("*").Clip().Children(
                new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").GridRow(0).Children(
                    filter.GridColumn(0),
                    new Button().Label("Rescan").Variant(ButtonVariant.Subtle)
                        .OnClick(() => library.RequestScan(force: true))
                        .GridColumn(1)),

                new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").GridRow(1).Children(
                    count.GridColumn(0),
                    Formats(theme, library).GridColumn(1)),

                warning.GridRow(2),

                // No minimum height. A minimum on the star row does not shrink when the panel does -- it
                // holds its size, the grid overflows, and the tree runs out through the button underneath
                // it. Whatever is left over is the right amount, including none.
                tree.Scrollable().GridRow(3),

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

        private UiDragGhost? _ghost;
        private Vector2 _pressedAt;
        private bool _pressed;
        private bool _dragging;

        public MeshAssetRow(MeshAssetEntry entry)
        {
            _entry = entry;
        }

        /// <summary>
        /// What follows the cursor while this row is being dragged. Built fresh each time rather than
        /// reusing the row's own content, which is still in the list and cannot be in two places.
        /// </summary>
        private UiElement BuildGhost(UiTheme theme)
        {
            var (label, color) = MeshAssetLibrary.Badge(_entry.PrimaryPath);

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

                    new TextBlock().Text(_entry.DisplayName).CenterV()));
        }

        protected override bool IsSelfHitTestVisible => true;

        /// <summary>
        /// Whether the pointer is over any part of the interface, as opposed to the scene behind it.
        /// <para>
        /// A geometric test, deliberately: the ghost is transparent to the pointer and the panels are not,
        /// so this answers what is under the cursor rather than what happens to hold the capture.
        /// </para>
        /// </summary>
        private bool IsOverUi(Vector2 position) => Root?.HitTest(position) != null;

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            base.OnPointerPressed(e);

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
            MeshImportRequests.SetPointerOverUi(IsOverUi(e.Position));

            if (Root is { } root)
            {
                _ghost = new UiDragGhost(root);
                _ghost.Show(BuildGhost(root.Theme), e.Position);
            }
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

            _ghost?.Hide();
            _ghost = null;

            if (_dragging)
            {
                // The last word on where the drop landed. Hit-tested rather than read from the router's
                // own hover, which reports the captured element for the whole gesture and so would say the
                // pointer was over this row no matter where it actually was.
                MeshImportRequests.SetPointerOverUi(IsOverUi(e.Position));
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
        private static string? _drag;

        /// <summary>The asset being dragged, until the drop is taken. Null when nothing is being dragged.</summary>
        public static string? Dragging => _drag;

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

        public static void RequestDrag(string asset) => _drag = asset;

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

        public static void Clear()
        {
            _placement = null;
            _drag = null;
            PointerOverUi = false;
        }
    }
}
