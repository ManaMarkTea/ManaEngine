using Mana.Code.UI;
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
                if (ManaUiDockDemo.MeshAssets.SelectedAsset is { Length: > 0 } selected)
                {
                    // Through the request double-clicking makes, so a model still downloading on the web joins
                    // once it has arrived rather than as a tile that failed to load.
                    GridMapEditing.Begin(gridMap);
                    MeshImportRequests.RequestPlacement(selected);
                }
            });
            add.BindPolled(button => button.IsEnabled, () => !string.IsNullOrWhiteSpace(ManaUiDockDemo.MeshAssets.SelectedAsset));
            add.WithTooltip("Add the model selected in the asset browser.");

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

            return new StackPanel().Vertical(theme.Spacing).Children(
                    new StackPanel().Horizontal(theme.Spacing).Children(paint, count),
                    Row(theme, "Cell", InspectorControls.Vector(() => gridMap.CellSize, value => gridMap.CellSize = value, 0.25f)),
                    new Grid().Columns("*, Auto", theme.Spacing * 2.0f).Rows("Auto").Children(
                        Row(theme, "Floor", floor).GridColumn(0),
                        collision.CenterV().GridColumn(1)),
                    Layers(gridMap, context),
                    palette,
                    new StackPanel().Horizontal(theme.Spacing).Children(add, remove, clear),
                    TileSettings(gridMap, context))
                .Margin(theme.Spacing);
        }

        private static bool HasBrush(ManaGridMapNode gridMap) => (uint)GridMapEditing.Brush < (uint)gridMap.Palette.Count;

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

            var section = new StackPanel().Vertical(theme.Spacing).Children(
                name,
                Row(theme, "Size", size),
                Row(theme, "Scale", new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").Children(fit.CenterV().GridColumn(0), scale.GridColumn(1))),
                Row(theme, "Turn", turn),
                Row(theme, "Align", align));
            section.BindPolled(panel => panel.IsEnabled, () => HasBrush(gridMap));

            return new Border().Outline(theme.Border, 1.0f).Rounded(3.0f).PaddingOf(theme.Spacing, theme.Spacing).Content(section);
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
            frame.BindPolled(border => border.BorderColor, () => GridMapEditing.Brush == item ? theme.Accent : UiColor.Transparent);
            frame.BindPolled(border => border.BorderThickness, () => GridMapEditing.Brush == item ? 2.0f : 0.0f);

            return new ManaUI.Button().Variant(ButtonVariant.Subtle).Content(frame).OnClick(() =>
            {
                GridMapEditing.Brush = item;
                GridMapEditing.Begin(gridMap);
            }).WithTooltip(tooltip);
        }

        private static UiElement PaletteTile(ManaGridMapNode gridMap, int item, InspectorContext context)
        {
            var theme = context.Theme;
            var asset = gridMap.Palette[item];

            var preview = new UiImage { Fit = UiImageFit.Contain, Width = TileSize, Height = TileSize };
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

            var error = gridMap.PaletteError(item);
            return SelectableTile(gridMap, item, preview, gridMap.PaletteItemName(item),
                error == null ? gridMap.PaletteItemName(item) : $"Could not load: {error}", theme);
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
