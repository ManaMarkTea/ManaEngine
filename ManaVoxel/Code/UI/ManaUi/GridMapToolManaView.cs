using Mana.Code.UI;
using ManaCore.Assets;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.GridMaps;
using ManaVoxel.Code.UI.ManaUi.Inspectors;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The Grid Map tool window: the selected map's brush, palette and the brush tile's own settings. The
    /// workspace floats it while a grid map is selected (see ManaUiDockDemo); painting happens in the viewport,
    /// in <c>VoxelEditorGameMode.GridMap</c>.
    /// </summary>
    public static class GridMapToolManaView
    {
        public const string PanelId = "gridmap";

        private const float TileSize = 56.0f;

        private static readonly string[] Turns = { "0°", "90°", "180°", "270°" };
        private static readonly string[] Aligns = { "Model", "Centre", "Edge" };

        public static UiElement Build(UiTheme theme, global::Mana.ManaWorld world, EditorUndoHistory? history)
        {
            var context = new InspectorContext(theme, world.Context, history: history);

            return new DynamicContent()
                .Showing<ManaGridMapNode>(
                    () => world.Selection.SelectedNode as ManaGridMapNode,
                    gridMap => BuildFor(gridMap, context))
                .Empty(new TextBlock().Text("Select a grid map.").Foreground(theme.TextMuted).Margin(theme.Spacing))
                .Scrollable()
                .Scrolls(vertical: ScrollBarVisibility.Auto, horizontal: ScrollBarVisibility.Disabled);
        }

        public static UiElement BuildFor(ManaGridMapNode gridMap, InspectorContext context)
        {
            var theme = context.Theme;

            var paint = new ManaUI.Button().Label("Paint").OnClick(() =>
            {
                if (GridMapEditing.IsPainting(gridMap))
                {
                    GridMapEditing.Stop();
                }
                else
                {
                    GridMapEditing.Begin(gridMap);
                }
            });
            paint.BindPolled(control => control.Variant,
                () => GridMapEditing.IsPainting(gridMap) ? ButtonVariant.Primary : ButtonVariant.Standard);
            paint.WithTooltip("Paint. R turns the brush, T the tile under it, Q picks up, right click or Shift clears, Esc stops.");

            var count = new TextBlock().Foreground(theme.TextMuted).CenterV();
            count.BindPolled(text => text.Text, () => $"{gridMap.TileCount} tiles");

            var collision = new CheckBox().Label("Collision").Checked(gridMap.Collision)
                .OnChanged(value => gridMap.Collision = value);
            collision.BindPolled(control => control.IsChecked, () => gridMap.Collision);

            var paletteTiles = new DynamicContent()
                .Showing<object>(() => gridMap.PaletteToken, _ => Palette(gridMap, context));

            // Also where a model dragged out of the asset browser can be dropped. The editor reads these bounds
            // when a drag lets go (VoxelEditorGameMode.GridMap), and the frame lights while one is over it.
            var palette = new Border().Rounded(3.0f).PaddingOf(2.0f, 2.0f).Content(paletteTiles);
            palette.BindPolled(border => border.BorderThickness, () =>
            {
                PaletteDropBounds = palette.Bounds;
                _paletteDropBoundsAt = Environment.TickCount64;
                return MeshImportRequests.Dragging != null && palette.Root is { } root && IsOverPalette(root.Input.PointerPosition) ? 2.0f : 0.0f;
            });
            palette.BindPolled(border => border.BorderColor, () => theme.Accent);

            var add = new ManaUI.Button().Label("Add").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                if (ManaUiDockDemo.MeshAssets.SelectedAssets is { Count: > 0 } selected)
                {
                    // Through the requests the editor drains while painting, so a model still downloading on the
                    // web joins once it has arrived rather than as a tile that failed to load.
                    GridMapEditing.Begin(gridMap);
                    MeshImportRequests.RequestPaletteAdd(selected);
                }
            });
            add.BindPolled(button => button.IsEnabled, () => !string.IsNullOrWhiteSpace(ManaUiDockDemo.MeshAssets.SelectedAsset));
            add.WithTooltip("Add the models selected in the asset browser (Ctrl or Shift click to pick several).");

            var remove = new ManaUI.Button().Label("Remove").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                var item = GridMapEditing.Brush;
                if ((uint)item < (uint)gridMap.Palette.Count)
                {
                    RecordWholeGrid(context, gridMap, $"Remove {gridMap.PaletteItemName(item)}", () => gridMap.RemovePaletteItem(item));
                }
            });
            remove.BindPolled(button => button.IsEnabled, () => HasBrush(gridMap));
            remove.WithTooltip("Remove the brush tile, and everything painted with it.");

            var clear = new ManaUI.Button().Label("Clear").Variant(ButtonVariant.Subtle).OnClick(() =>
                RecordWholeGrid(context, gridMap, "Clear grid map", gridMap.ClearAll));
            clear.BindPolled(button => button.IsEnabled, () => gridMap.Cells.Count > 0);
            clear.WithTooltip("Remove every tile on this layer.");

            // One field in the vector editor's style: a green Y chip that scrubs, and a number that types.
            var floor = new NumericBox
            {
                DragHandle = NumericDragHandle.Leading,
                ChipText = "Y",
                ChipColor = theme.AxisY,
                IsInteger = true,
                Step = 1.0f,
                MinimumFieldHeight = 0.0f,
                Padding = new Thickness(6.0f, 1.0f)
            };
            floor.BindPolled(box => box.Value, () => GridMapEditing.Floor);
            floor.ValueChanged += (_, value) =>
            {
                if (!floor.IsApplyingBinding)
                {
                    GridMapEditing.Floor = (int)MathF.Round(value);
                }
            };
            floor.WithTooltip("Floor painted on (- / +)");

            var generate = new ManaUI.Button().Label("Generate...").Variant(ButtonVariant.Subtle)
                .OnClick(() => GridMapWfcEditing.WindowOpen = !GridMapWfcEditing.WindowOpen);
            generate.BindPolled(button => button.Variant, () => GridMapWfcEditing.WindowOpen ? ButtonVariant.Primary : ButtonVariant.Subtle);
            generate.WithTooltip("Generate a dungeon (or anything else with rules) into an area of this map.");

            return new StackPanel().Vertical(theme.Spacing).Children(
                    new Grid().Columns("Auto, *, Auto", theme.Spacing).Rows("Auto").Children(
                        paint.GridColumn(0), count.GridColumn(1), generate.GridColumn(2)),
                    Row(theme, "Cell", InspectorControls.Vector(() => gridMap.CellSize, value => gridMap.CellSize = value, 0.25f)),
                    new Grid().Columns("*, Auto", theme.Spacing * 2.0f).Rows("Auto").Children(
                        Row(theme, "Floor", floor).GridColumn(0),
                        collision.CenterV().GridColumn(1)),
                    Layers(gridMap, context),
                    palette,
                    new StackPanel().Horizontal(theme.Spacing).Children(add, remove, clear),
                    AutoTileSets(gridMap, context),
                    TileSettings(gridMap, context),
                    AutoTileSettings(gridMap, context))
                .Margin(theme.Spacing);
        }

        private static bool HasBrush(ManaGridMapNode gridMap) =>
            !GridMapEditing.IsAutoTile && (uint)GridMapEditing.Brush < (uint)gridMap.Palette.Count;

        private static bool IsSetBrush(ManaGridMapNode gridMap, int set) =>
            GridMapEditing.AutoTile == set && (uint)set < (uint)gridMap.AutoTiles.Count;

        /// <summary>
        /// The map's auto-tile sets as brushes -- each paints whichever of its pieces joins the cell to its
        /// neighbours -- and buttons to add one from a preset or empty.
        /// </summary>
        private static UiElement AutoTileSets(ManaGridMapNode gridMap, InspectorContext context)
        {
            var theme = context.Theme;
            var sets = new DynamicContent().Showing<object>(StableKey(() => (gridMap.AutoTilesToken, gridMap.PaletteToken)), _ =>
            {
                var wrap = new WrapPanel { Spacing = theme.Spacing };
                for (var i = 0; i < gridMap.AutoTiles.Count; i++)
                {
                    wrap.Children.Add(AutoTileSetTile(gridMap, i, context));
                }

                return wrap;
            });

            var buttons = new StackPanel().Horizontal(theme.Spacing);
            foreach (var preset in GridMapAutoTilePresets.All)
            {
                var add = new ManaUI.Button().Label($"+ {preset.Name}").Variant(ButtonVariant.Subtle)
                    .OnClick(() => AddPreset(gridMap, preset, context));
                add.WithTooltip($"Add the {preset.Name.ToLowerInvariant()} set: {string.Join(", ", preset.Pieces.Select(piece => ManaGridMapNode.TileName(piece.Asset)))}");
                buttons.Children.Add(add);
            }

            var empty = new ManaUI.Button().Label("+ Set").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                var index = -1;
                RecordWholeGrid(context, gridMap, "Add auto-tile set",
                    () => index = gridMap.AddAutoTileSet(new GridMapAutoTileSet($"Set {gridMap.AutoTiles.Count + 1}", [])));
                GridMapEditing.Begin(gridMap);
                GridMapEditing.AutoTile = index;
            });
            empty.WithTooltip("Add an empty auto-tile set, to fill with pieces from the palette.");
            buttons.Children.Add(empty);

            var heading = new TextBlock().Text("Auto tiles").FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted);
            heading.WithTooltip("Sets whose pieces pick themselves: paint a road and its bends and junctions follow.");
            return new StackPanel().Vertical(2.0f).Children(heading, sets, buttons);
        }

        private static UiElement AutoTileSetTile(ManaGridMapNode gridMap, int set, InspectorContext context)
        {
            var theme = context.Theme;
            var pieces = gridMap.AutoTiles[set].Pieces;

            // Its straight run if it has one -- the piece that reads as the set -- else its first.
            var face = pieces.FirstOrDefault(piece => piece.Sides is (GridMapSides.East | GridMapSides.West) or (GridMapSides.North | GridMapSides.South))
                ?? pieces.FirstOrDefault();
            UiElement picture = face != null && (uint)face.Item < (uint)gridMap.Palette.Count
                ? Thumbnail(gridMap.Palette[face.Item], TileSize)
                : new TextBlock().Text("?").FontSize(theme.FontSizeHeading).Foreground(theme.TextMuted).Center();

            // The corner mark says this is a set and not a single tile.
            var badge = new TextBlock().Text("auto").FontSize(theme.FontSizeSmall).Foreground(theme.TextOnAccent);
            var badgeBox = new Border().Fill(theme.Accent).Rounded(2.0f).PaddingOf(3.0f, 0.0f).Content(badge);
            badgeBox.HorizontalAlignment = HorizontalAlignment.Right;
            badgeBox.VerticalAlignment = VerticalAlignment.Top;
            var stack = new Grid().Children(picture, badgeBox);
            stack.Width = TileSize;
            stack.Height = TileSize;

            var caption = new TextBlock().Text(gridMap.AutoTiles[set].Name).FontSize(theme.FontSizeSmall).Ellipsis().CenterH();
            caption.Width = TileSize;
            var frame = new Border().Rounded(3.0f).Content(new StackPanel().Vertical(2.0f).Children(stack, caption));
            frame.BindPolled(border => border.BorderColor, () => IsSetBrush(gridMap, set) ? theme.Accent : UiColor.Transparent);
            frame.BindPolled(border => border.BorderThickness, () => IsSetBrush(gridMap, set) ? 2.0f : 0.0f);

            return new ManaUI.Button().Variant(ButtonVariant.Subtle).Content(frame).OnClick(() =>
            {
                GridMapEditing.Begin(gridMap);
                GridMapEditing.AutoTile = set;
            }).WithTooltip($"{gridMap.AutoTiles[set].Name}: {pieces.Count} pieces, each picked by the neighbours it joins");
        }

        // A preset's models may still be downloading on the web; the set goes in once they have all arrived,
        // since a palette item that failed to load stays failed.
        private static Task? _presetDownload;

        private static void AddPreset(ManaGridMapNode gridMap, GridMapAutoTilePresets.Preset preset, InspectorContext context)
        {
            void Add()
            {
                var index = -1;
                RecordWholeGrid(context, gridMap, $"Add {preset.Name} set", () => index = GridMapAutoTilePresets.AddTo(gridMap, preset));
                GridMapEditing.Begin(gridMap);
                GridMapEditing.AutoTile = index;
            }

            if (ManaAssetCatalog.Current is { } catalog && preset.Pieces.Any(piece => !catalog.IsReady(piece.Asset)))
            {
                if (_presetDownload is { IsCompleted: false })
                {
                    return;
                }

                var download = Task.WhenAll(preset.Pieces.Select(piece => catalog.EnsureAsync(piece.Asset)));
                _presetDownload = download;
                PendingPresets.Add((download, Add));
                return;
            }

            Add();
        }

        private static readonly List<(Task Download, Action Add)> PendingPresets = new();

        /// <summary>Adds any preset whose download has finished. Polled by the window, on the UI thread.</summary>
        private static void CompletePresetDownloads()
        {
            for (var i = PendingPresets.Count - 1; i >= 0; i--)
            {
                var (download, add) = PendingPresets[i];
                if (!download.IsCompleted)
                {
                    continue;
                }

                PendingPresets.RemoveAt(i);
                if (!download.IsFaulted)
                {
                    add();
                }
            }
        }

        /// <summary>
        /// The brush set's own settings: its name, and each piece -- which palette model it is and which sides
        /// it joins on as it stands unturned. A piece's sides are what decide where it goes.
        /// </summary>
        private static UiElement AutoTileSettings(ManaGridMapNode gridMap, InspectorContext context)
        {
            var theme = context.Theme;
            var body = new DynamicContent().Showing<object>(StableKey(() => (gridMap.AutoTilesToken, gridMap.PaletteToken, GridMapEditing.AutoTile)), _ =>
            {
                var set = GridMapEditing.AutoTile;
                if ((uint)set >= (uint)gridMap.AutoTiles.Count)
                {
                    return null;
                }

                return AutoTileSetEditor(gridMap, set, context);
            });

            var frame = new Border().Outline(theme.Border, 1.0f).Rounded(3.0f).PaddingOf(theme.Spacing, theme.Spacing).Content(body);
            frame.BindPolled(border => border.Visibility, () =>
            {
                CompletePresetDownloads();
                return IsSetBrush(gridMap, GridMapEditing.AutoTile) ? Visibility.Visible : Visibility.Collapsed;
            });
            return frame;
        }

        private static UiElement AutoTileSetEditor(ManaGridMapNode gridMap, int set, InspectorContext context)
        {
            var theme = context.Theme;
            var current = gridMap.AutoTiles[set];

            void Change(string description, Func<GridMapAutoTileSet, GridMapAutoTileSet> edit) =>
                RecordWholeGrid(context, gridMap, description, () => gridMap.SetAutoTileSet(set, edit(gridMap.AutoTiles[set])));

            void ChangePiece(int index, string description, Func<GridMapAutoTilePiece, GridMapAutoTilePiece> edit) =>
                Change(description, value => value with
                {
                    Pieces = value.Pieces.Select((piece, i) => i == index ? edit(piece) : piece).ToList()
                });

            var name = new TextBox { Text = current.Name };
            name.TextCommitted += (_, text) =>
            {
                if (!string.IsNullOrWhiteSpace(text) && text.Trim() != gridMap.AutoTiles[set].Name)
                {
                    Change("Rename auto-tile set", value => value with { Name = text.Trim() });
                }
            };
            name.WithTooltip("Name");

            var removeSet = new ManaUI.Button().Label("Remove").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                RecordWholeGrid(context, gridMap, "Remove auto-tile set", () => gridMap.RemoveAutoTileSet(set));
                GridMapEditing.Brush = GridMapEditing.Brush;
            });
            removeSet.WithTooltip("Remove the set. What it painted stays, as ordinary tiles.");

            var names = Enumerable.Range(0, gridMap.Palette.Count).Select(gridMap.PaletteItemName).ToArray();
            var rows = new StackPanel().Vertical(2.0f);
            for (var i = 0; i < current.Pieces.Count; i++)
            {
                var index = i;
                var piece = current.Pieces[i];

                var model = new ComboBox().Options(names);
                model.SelectedIndex = piece.Item;
                model.SelectionChanged += (_, item) =>
                {
                    if (item >= 0 && item != gridMap.AutoTiles[set].Pieces[index].Item)
                    {
                        ChangePiece(index, "Auto-tile piece", value => value with { Item = item });
                    }
                };
                model.WithTooltip("The palette model this piece is");

                var sides = new GridMapSidesPicker { Sides = piece.Sides };
                sides.SidesChanged += value => ChangePiece(index, "Auto-tile sides", existing => existing with { Sides = value });
                sides.WithTooltip($"Joins on: {GridMapAutoTile.Describe(piece.Sides)}. Seen from above, -Z up; click a side to toggle it.");

                var drop = new ManaUI.Button().Label("-").Variant(ButtonVariant.Subtle).OnClick(() =>
                    Change("Remove auto-tile piece", value => value with { Pieces = value.Pieces.Where((_, i) => i != index).ToList() }));
                drop.WithTooltip("Remove this piece");

                UiElement picture = (uint)piece.Item < (uint)gridMap.Palette.Count ? Thumbnail(gridMap.Palette[piece.Item], 26.0f) : new TextBlock();
                rows.Children.Add(new Grid().Columns("Auto, *, Auto, Auto", theme.Spacing).Rows("Auto").Children(
                    picture.CenterV().GridColumn(0), model.CenterV().GridColumn(1), sides.CenterV().GridColumn(2), drop.CenterV().GridColumn(3)));
            }

            var addPiece = new ManaUI.Button().Label("+ Piece").Variant(ButtonVariant.Subtle).OnClick(() =>
                Change("Add auto-tile piece", value => value with { Pieces = value.Pieces.Append(new GridMapAutoTilePiece(0, GridMapSides.East | GridMapSides.West)).ToList() }));
            addPiece.BindPolled(button => button.IsEnabled, () => gridMap.Palette.Count > 0);
            addPiece.WithTooltip("Add a piece: pick its model from the palette, then the sides it joins on.");

            return new StackPanel().Vertical(theme.Spacing).Children(
                new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").Children(name.GridColumn(0), removeSet.GridColumn(1)),
                rows,
                addPiece);
        }

        /// <summary>
        /// One key object for as long as the parts it is made of stay equal. DynamicContent compares by
        /// reference, so a fresh tuple every poll would rebuild every frame.
        /// </summary>
        private static Func<object> StableKey<T>(Func<T> parts)
        {
            object? key = null;
            T last = default!;
            return () =>
            {
                var now = parts();
                if (key == null || !EqualityComparer<T>.Default.Equals(now, last))
                {
                    last = now;
                    key = new object();
                }

                return key;
            };
        }

        /// <summary>
        /// The brush tile's own settings. Bound to whichever tile is the brush, so picking another tile updates
        /// these in place rather than rebuilding them.
        /// </summary>
        private static UiElement TileSettings(ManaGridMapNode gridMap, InspectorContext context)
        {
            var theme = context.Theme;
            GridMapItemSettings Current() => gridMap.ItemSettings(GridMapEditing.Brush);

            void Change(string description, Func<GridMapItemSettings, GridMapItemSettings> edit)
            {
                var item = GridMapEditing.Brush;
                if ((uint)item < (uint)gridMap.Palette.Count)
                {
                    RecordWholeGrid(context, gridMap, description, () => gridMap.SetItemSettings(item, edit(gridMap.ItemSettings(item))));
                }
            }

            var name = new TextBlock().FontFamily(theme.FontFamilyMedium);
            name.BindPolled(text => text.Text, () => HasBrush(gridMap) ? gridMap.PaletteItemName(GridMapEditing.Brush) : "Blank clears cells");

            var fit = new CheckBox().Label("Fit").OnChanged(value =>
            {
                if (value != Current().Fits)
                {
                    // Unfitting keeps the size it had, so the tile does not jump.
                    var fitted = gridMap.ItemScale(GridMapEditing.Brush);
                    Change("Tile scale", settings => settings with { Scale = value ? 0.0f : fitted });
                }
            });
            fit.BindPolled(control => control.IsChecked, () => Current().Fits);
            fit.WithTooltip("Scale to fill its cells.");

            var scale = InspectorControls.Scalar(
                () => gridMap.ItemScale(GridMapEditing.Brush),
                value =>
                {
                    var item = GridMapEditing.Brush;
                    gridMap.SetItemSettings(item, gridMap.ItemSettings(item) with { Scale = MathF.Max(0.01f, value) });
                },
                0.1f,
                20.0f);
            scale.BindPolled(control => control.IsEnabled, () => !Current().Fits);

            var turn = new ComboBox().Options(Turns);
            turn.BindPolled(control => control.SelectedIndex, () => Current().Turn);
            turn.SelectionChanged += (_, index) =>
            {
                if (!turn.IsApplyingBinding && index >= 0 && index != Current().Turn)
                {
                    Change("Tile turn", settings => settings with { Turn = index });
                }
            };
            turn.WithTooltip("Turn the model within its tile.");

            var align = new ComboBox().Options(Aligns);
            align.BindPolled(control => control.SelectedIndex, () => (int)Current().Align);
            align.SelectionChanged += (_, index) =>
            {
                if (!align.IsApplyingBinding && index >= 0 && index != (int)Current().Align)
                {
                    Change("Tile align", settings => settings with { Align = (GridMapAlign)index });
                }
            };
            align.WithTooltip("Model: as authored. Centre: by its bounds. Edge: against the back.");

            // Cells covered, as one W/D field in the colours of the axes they run along. A drag scrubs through
            // sizes live; the undo step is taken when it settles, not one per cell it passed.
            var size = new VectorBox().Components(2).Step(1.0f).Places(0);
            size.SetAxes(("W", 0), ("D", 2));
            size.IsInteger = true;
            size.Minimum = 1.0f;
            size.Maximum = 32.0f;
            size.BindPolled(box => box.Value2, () => new Vector2(Current().Width, Current().Depth));
            GridMapState? sizeBefore = null;
            size.ValueChanged += (_, value) =>
            {
                var item = GridMapEditing.Brush;
                if (size.IsApplyingBinding || (uint)item >= (uint)gridMap.Palette.Count)
                {
                    return;
                }

                sizeBefore ??= gridMap.CaptureState();
                gridMap.SetItemSettings(item, gridMap.ItemSettings(item) with { Width = (int)MathF.Round(value.X), Depth = (int)MathF.Round(value.Y) });
            };
            size.ValueCommitted += (_, _) =>
            {
                if (sizeBefore is { } before)
                {
                    var after = gridMap.CaptureState();
                    context.History?.Record("Tile size", () => gridMap.RestoreState(before), () => gridMap.RestoreState(after));
                    sizeBefore = null;
                }
            };
            size.WithTooltip("Cells covered, before turning");

            var smooth = new NumericBox { Step = 0.01f, Minimum = 0.0f, MinimumFieldHeight = 0.0f, Padding = new Thickness(6.0f, 1.0f) }.Places(2);
            smooth.BindPolled(box => box.Value, () => Current().Smooth);
            smooth.ValueCommitted += (_, value) =>
            {
                if (!smooth.IsApplyingBinding && MathF.Abs(value - Current().Smooth) > 1e-4f)
                {
                    Change("Tile collision", settings => settings with { Smooth = MathF.Max(0.0f, value) });
                }
            };
            smooth.WithTooltip("Collision is flat below this height, in the model's units: rubble and cracks stop catching feet. 0 keeps every bump.");

            var section = new StackPanel().Vertical(theme.Spacing).Children(
                name,
                Row(theme, "Size", size),
                Row(theme, "Scale", new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").Children(fit.CenterV().GridColumn(0), scale.GridColumn(1))),
                Row(theme, "Turn", turn),
                Row(theme, "Align", align),
                Row(theme, "Smooth", smooth));
            section.BindPolled(panel => panel.IsEnabled, () => HasBrush(gridMap));

            var frame = new Border().Outline(theme.Border, 1.0f).Rounded(3.0f).PaddingOf(theme.Spacing, theme.Spacing).Content(section);
            frame.BindPolled(border => border.Visibility, () => GridMapEditing.IsAutoTile ? Visibility.Collapsed : Visibility.Visible);
            return frame;
        }

        /// <summary>Where the palette was last laid out, for a drag from the asset browser to drop onto.</summary>
        public static UiRect PaletteDropBounds { get; private set; }

        // When those bounds were last laid out. A closed window stops refreshing them, and stale bounds must not
        // catch a drop meant for the scene behind where the window used to be.
        private static long _paletteDropBoundsAt;

        /// <summary>Whether a point, in the UI's own coordinates, is over the palette of an open Grid Map window.</summary>
        public static bool IsOverPalette(Vector2 point) =>
            Environment.TickCount64 - _paletteDropBoundsAt < 250 && PaletteDropBounds.Width > 0.0f && PaletteDropBounds.Contains(point);

        /// <summary>
        /// The map's layers: click one to paint on it, untick it to hide it (hidden layers are not built, in the
        /// editor or in play), double-check the name field to rename the active one.
        /// </summary>
        private static UiElement Layers(ManaGridMapNode gridMap, InspectorContext context)
        {
            var theme = context.Theme;

            var list = new DynamicContent().Showing<object>(() => gridMap.LayersToken, _ =>
            {
                var rows = new StackPanel().Vertical(2.0f);
                for (var i = 0; i < gridMap.LayerCount; i++)
                {
                    var layer = i;
                    var visible = new CheckBox().Checked(gridMap.IsLayerVisible(layer))
                        .OnChanged(value => RecordWholeGrid(context, gridMap, value ? "Show layer" : "Hide layer", () => gridMap.SetLayerVisible(layer, value)));
                    visible.BindPolled(control => control.IsChecked, () => gridMap.IsLayerVisible(layer));
                    visible.WithTooltip("Show");

                    var select = new ManaUI.Button().Label(gridMap.LayerName(layer)).OnClick(() => gridMap.ActiveLayer = layer);
                    select.BindPolled(control => control.Variant,
                        () => gridMap.ActiveLayer == layer ? ButtonVariant.Primary : ButtonVariant.Subtle);
                    select.HorizontalAlignment = HorizontalAlignment.Stretch;

                    var tiles = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).CenterV();
                    tiles.BindPolled(text => text.Text, () => gridMap.LayerCells(layer).Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

                    rows.Children.Add(new Grid().Columns("Auto, *, Auto", theme.Spacing).Rows("Auto").Children(
                        visible.CenterV().GridColumn(0), select.GridColumn(1), tiles.GridColumn(2)));
                }

                return rows;
            });

            var name = new TextBox();
            name.BindPolled(box => box.Text, () => gridMap.LayerName(gridMap.ActiveLayer));
            name.TextCommitted += (_, text) =>
            {
                var layer = gridMap.ActiveLayer;
                if (!string.IsNullOrWhiteSpace(text) && text.Trim() != gridMap.LayerName(layer))
                {
                    RecordWholeGrid(context, gridMap, "Rename layer", () => gridMap.RenameLayer(layer, text));
                }
            };
            name.WithTooltip("Rename the active layer");

            var add = new ManaUI.Button().Label("+").Variant(ButtonVariant.Subtle)
                .OnClick(() => RecordWholeGrid(context, gridMap, "Add layer", () => gridMap.AddLayer()));
            add.WithTooltip("Add a layer");
            var remove = new ManaUI.Button().Label("-").Variant(ButtonVariant.Subtle)
                .OnClick(() => RecordWholeGrid(context, gridMap, "Remove layer", () => gridMap.RemoveLayer(gridMap.ActiveLayer)));
            remove.BindPolled(button => button.IsEnabled, () => gridMap.LayerCount > 1);
            remove.WithTooltip("Remove the active layer and its tiles");

            return new StackPanel().Vertical(2.0f).Children(
                list,
                new Grid().Columns("*, Auto, Auto", 2.0f).Rows("Auto").Children(name.GridColumn(0), add.GridColumn(1), remove.GridColumn(2)));
        }

        /// <summary>A short label and its editor on one line.</summary>
        private static UiElement Row(UiTheme theme, string label, UiElement editor)
        {
            var name = new TextBlock().Text(label).Foreground(theme.TextMuted).CenterV();
            name.Width = 40.0f;
            return new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").Children(name.GridColumn(0), editor.GridColumn(1));
        }

        /// <summary>Blank first -- the brush that clears -- then the map's own tiles.</summary>
        private static UiElement? Palette(ManaGridMapNode gridMap, InspectorContext context)
        {
            var wrap = new WrapPanel { Spacing = context.Theme.Spacing };
            wrap.Children.Add(BlankTile(gridMap, context));
            for (var i = 0; i < gridMap.Palette.Count; i++)
            {
                wrap.Children.Add(PaletteTile(gridMap, i, context));
            }

            return wrap;
        }

        private static UiElement BlankTile(ManaGridMapNode gridMap, InspectorContext context)
        {
            var theme = context.Theme;
            var mark = new TextBlock().Text("x").FontSize(theme.FontSizeHeading).Foreground(theme.TextMuted).Center();
            var swatch = new Border().Outline(theme.Border, 1.0f).Rounded(3.0f).Content(mark);
            swatch.Width = TileSize;
            swatch.Height = TileSize;
            return SelectableTile(gridMap, GridMapEditing.Blank, swatch, "Blank", "Blank: clears cells", theme);
        }

        private static UiElement SelectableTile(ManaGridMapNode gridMap, int item, UiElement picture, string label, string tooltip, UiTheme theme)
        {
            var caption = new TextBlock().Text(label).FontSize(theme.FontSizeSmall).Ellipsis().CenterH();
            caption.Width = TileSize;

            var frame = new Border().Rounded(3.0f).Content(new StackPanel().Vertical(2.0f).Children(picture, caption));
            frame.BindPolled(border => border.BorderColor, () => !GridMapEditing.IsAutoTile && GridMapEditing.Brush == item ? theme.Accent : UiColor.Transparent);
            frame.BindPolled(border => border.BorderThickness, () => !GridMapEditing.IsAutoTile && GridMapEditing.Brush == item ? 2.0f : 0.0f);

            return new ManaUI.Button().Variant(ButtonVariant.Subtle).Content(frame).OnClick(() =>
            {
                GridMapEditing.Brush = item;
                GridMapEditing.Begin(gridMap);
            }).WithTooltip(tooltip);
        }

        private static UiElement PaletteTile(ManaGridMapNode gridMap, int item, InspectorContext context)
        {
            var theme = context.Theme;
            var error = gridMap.PaletteError(item);
            return SelectableTile(gridMap, item, Thumbnail(gridMap.Palette[item], TileSize), gridMap.PaletteItemName(item),
                error == null ? gridMap.PaletteItemName(item) : $"Could not load: {error}", theme);
        }

        /// <summary>An asset's thumbnail, requested from the cache the asset browser uses and shown once it arrives.</summary>
        private static UiImage Thumbnail(string asset, float size)
        {
            var preview = new UiImage { Fit = UiImageFit.Contain, Width = size, Height = size };
            // A UV carries no size of its own, so without this a Contain fit has nothing to fit and draws nothing.
            preview.SourceSize = new Vector2(MeshThumbnailCache.ThumbnailSize, MeshThumbnailCache.ThumbnailSize);

            var requested = false;
            preview.BindPolled(image => image.Visibility, () =>
            {
                if (preview.Root is { } root)
                {
                    MeshThumbnailImages.Touch(root, asset);
                }

                if (!requested && ManaUiDockDemo.MeshThumbnails is { } cache)
                {
                    cache.Request(asset);
                    requested = true;
                }

                if (!MeshThumbnailImages.HasImage(asset))
                {
                    return Visibility.Collapsed;
                }

                preview.SourceUv = MeshThumbnailImages.UvFor(asset);
                return Visibility.Visible;
            });

            return preview;
        }

        /// <summary>One undo step that puts the whole grid back -- for edits that can move or drop many tiles.</summary>
        private static void RecordWholeGrid(InspectorContext context, ManaGridMapNode gridMap, string description, Action change)
        {
            var before = gridMap.CaptureState();
            change();
            var after = gridMap.CaptureState();
            context.History?.Record(description, () => gridMap.RestoreState(before), () => gridMap.RestoreState(after));
        }
    }
}
