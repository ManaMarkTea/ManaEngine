using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.GridMaps;
using ManaVoxel.Code.UI.ManaUi.Inspectors;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The Generator window: rules for <see cref="GridMapWfc"/>, read from and saved to .manawfc files in
    /// Content/Generators/, and the area of the selected grid map to generate into. Every tile of the rules can be
    /// edited here -- its model, the cells it covers, how often it comes up, which turns it may take and the
    /// labels on its sides -- so a kit other than Kenney's is a new rules file, not new code.
    /// </summary>
    public static class GridMapWfcManaView
    {
        public const string PanelId = "gridmapgen";

        private static readonly string[] RotationNames = { "All turns", "No turns", "Half turns" };

        private static readonly GridMapWfcRotations[] RotationValues = { GridMapWfcRotations.All, GridMapWfcRotations.None, GridMapWfcRotations.Half };

        public static UiElement Build(UiTheme theme, global::Mana.ManaWorld world, EditorUndoHistory? history)
        {
            var context = new InspectorContext(theme, world.Context, history: history);
            return new DynamicContent()
                .Showing<ManaGridMapNode>(
                    () => Target(world),
                    gridMap => BuildFor(gridMap, context))
                .Empty(new TextBlock().Text("Select a grid map to generate into.").Foreground(theme.TextMuted).Margin(theme.Spacing))
                .Scrollable()
                .Scrolls(vertical: ScrollBarVisibility.Auto, horizontal: ScrollBarVisibility.Disabled);
        }

        /// <summary>The map the window generates into; see <see cref="GridMapWfcEditing.MapFor"/>.</summary>
        public static ManaGridMapNode? Target(global::Mana.ManaWorld world) =>
            GridMapWfcEditing.MapFor(world.Selection.SelectedNode, map => IsInWorld(world, map));

        private static bool IsInWorld(global::Mana.ManaWorld world, ManaGridMapNode map)
        {
            global::Mana.Code.Nodes.ManaSceneNode root = map;
            while (root.Parent != null)
            {
                root = root.Parent;
            }

            lock (world.SceneLock)
            {
                return world.Scenes.Any(scene => scene.Children.Contains(root));
            }
        }

        public static UiElement BuildFor(ManaGridMapNode gridMap, InspectorContext context)
        {
            var theme = context.Theme;
            var library = EditorAssetServices.For(gridMap.Context.World).Library;
            var target = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Ellipsis();
            target.BindPolled(text => text.Text, () => $"Into {gridMap.Name}");
            target.WithTooltip("The grid map selected, or the last one generated into while something else is");
            return new StackPanel().Vertical(theme.Spacing).Margin(theme.Spacing).Children(
                target,
                RulesFile(theme),
                RulesSettings(theme),
                AreaSettings(gridMap, context),
                new DynamicContent().Showing<object>(() => GridMapWfcEditing.RulesToken, _ => BigRoomSettings(theme, library)),
                new DynamicContent().Showing<object>(() => GridMapWfcEditing.RulesToken, _ => TileList(theme, library)));
        }

        // ----- the rules file --------------------------------------------------------------------------

        private static UiElement RulesFile(UiTheme theme)
        {
            var files = new DynamicContent().Showing<object>(() => GridMapWfcEditing.RulesToken, _ =>
            {
                var paths = GridMapWfcGenerator.List().ToList();
                var names = paths.Select(System.IO.Path.GetFileNameWithoutExtension).Select(name => name ?? "").ToList();
                var current = GridMapWfcEditing.Path == null ? -1 : paths.FindIndex(path => string.Equals(path, GridMapWfcEditing.Path, StringComparison.OrdinalIgnoreCase));
                if (current < 0)
                {
                    names.Insert(0, $"{GridMapWfcEditing.Rules.Name} (not saved)");
                    paths.Insert(0, string.Empty);
                    current = 0;
                }

                var combo = new ComboBox().Options(names.ToArray());
                combo.SelectedIndex = current;
                combo.SelectionChanged += (_, index) =>
                {
                    if (index >= 0 && index < paths.Count && paths[index].Length > 0 && paths[index] != GridMapWfcEditing.Path)
                    {
                        if (!GridMapWfcEditing.TryOpen(paths[index], out var error))
                        {
                            GridMapWfcEditing.Status = $"Could not open: {error}";
                        }
                    }
                };
                combo.WithTooltip($"Rules files in {GridMapWfcGenerator.FolderName}/");
                return combo;
            });

            var save = new ManaUI.Button().Label("Save").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                try
                {
                    GridMapWfcEditing.Save();
                    GridMapWfcEditing.Status = $"Saved {System.IO.Path.GetFileName(GridMapWfcEditing.Path)}";
                    GridMapWfcEditing.Changed();
                    GridMapWfcEditing.Dirty = false;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    GridMapWfcEditing.Status = $"Could not save: {exception.Message}";
                }
            });
            save.BindPolled(button => button.Text, () => GridMapWfcEditing.Dirty ? "Save *" : "Save");
            save.WithTooltip("Save the rules to their file, named after them when new.");

            var presets = new ManaUI.Button().Label("New").Variant(ButtonVariant.Subtle);
            presets.Click += (_, _) =>
            {
                if (presets.Root is not { } root)
                {
                    return;
                }

                var items = GridMapWfcPresets.All
                    .Select(preset => new MenuItem($"From {preset.Name} preset", () => GridMapWfcEditing.Replace(preset.Create(), null)))
                    .Append(new MenuItem("Empty", () => GridMapWfcEditing.Replace(new GridMapWfcRules
                    {
                        Name = "New Rules",
                        Tiles = [new GridMapWfcTile { Name = "Empty", Rotations = GridMapWfcRotations.None, Weight = 4.0f }]
                    }, null)))
                    .ToList();
                MenuFlyout.ShowBelow(root, presets, items);
            };
            presets.WithTooltip("Start new rules, from a preset or empty. Save writes them as a new file.");

            return new Grid().Columns("*, Auto, Auto", theme.Spacing).Rows("Auto").Children(
                files.GridColumn(0), save.GridColumn(1), presets.GridColumn(2));
        }

        private static UiElement RulesSettings(UiTheme theme)
        {
            var name = new TextBox();
            name.BindPolled(box => box.Text, () => GridMapWfcEditing.Rules.Name);
            name.TextCommitted += (_, text) => Edit(() => GridMapWfcEditing.Rules.Name = text.Trim());
            name.WithTooltip("The rules' name, and the file Save writes when they are new");

            var boundary = new TextBox();
            boundary.BindPolled(box => box.Text, () => GridMapWfcEditing.Rules.Boundary);
            boundary.TextCommitted += (_, text) => Edit(() => GridMapWfcEditing.Rules.Boundary = text.Trim());
            boundary.WithTooltip("The label every side facing out of the area must have; empty lets anything face out");

            var joins = new TextBox();
            joins.BindPolled(box => box.Text, () => GridMapWfcEditing.Rules.Connects);
            joins.TextCommitted += (_, text) => Edit(() => GridMapWfcEditing.Rules.Connects = text.Trim());
            joins.WithTooltip("The label that joins two tiles into one dungeon, for keeping it in one piece");

            // A dungeon names the props rules to furnish it with; props name the dungeon rules to read it by.
            var link = new TextBox();
            link.BindPolled(box => box.Text, () => (GridMapWfcEditing.Rules.IsProps ? GridMapWfcEditing.Rules.Base : GridMapWfcEditing.Rules.Then) ?? string.Empty);
            link.TextCommitted += (_, text) => Edit(() =>
            {
                var value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                if (GridMapWfcEditing.Rules.IsProps)
                {
                    GridMapWfcEditing.Rules.Base = value;
                }
                else
                {
                    GridMapWfcEditing.Rules.Then = value;
                }
            });
            link.BindPolled(box => box.Tooltip, () => GridMapWfcEditing.Rules.IsProps
                ? "The rules the dungeon underneath was laid with, by file name -- how its tiles are read"
                : "Props rules to furnish the dungeon with afterwards, by file name. Empty lays no props.");
            var linkLabel = new TextBlock().Foreground(theme.TextMuted).CenterV();
            linkLabel.BindPolled(text => text.Text, () => GridMapWfcEditing.Rules.IsProps ? "Base" : "Then");
            linkLabel.Width = 46.0f;

            // Which labels join which (the rules' Pairs) is left to the file: a person picks a side's kind by name
            // in each tile instead -- see Sockets.
            return new StackPanel().Vertical(2.0f).Children(
                Row(theme, "Name", name),
                new Grid().Columns("*, *", theme.Spacing).Rows("Auto").Children(
                    Row(theme, "Edge", boundary).GridColumn(0),
                    Row(theme, "Joins", joins).GridColumn(1)),
                new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").Children(linkLabel.GridColumn(0), link.GridColumn(1)));
        }

        // ----- where to generate ------------------------------------------------------------------------

        private static UiElement AreaSettings(ManaGridMapNode gridMap, InspectorContext context)
        {
            var theme = context.Theme;

            var origin = new VectorBox().Components(2).Step(1.0f).Places(0);
            origin.SetAxes(("X", 0), ("Z", 2));
            origin.IsInteger = true;
            origin.BindPolled(box => box.Value2, () => new Vector2(GridMapWfcEditing.X, GridMapWfcEditing.Z));
            origin.ValueChanged += (_, value) =>
            {
                if (!origin.IsApplyingBinding)
                {
                    GridMapWfcEditing.X = (int)MathF.Round(value.X);
                    GridMapWfcEditing.Z = (int)MathF.Round(value.Y);
                }
            };
            origin.WithTooltip("The area's first cell. It is outlined in the viewport while this window is up.");

            var size = new VectorBox().Components(2).Step(1.0f).Places(0);
            size.SetAxes(("W", 0), ("D", 2));
            size.IsInteger = true;
            size.Minimum = 1.0f;
            size.Maximum = 96.0f;
            size.BindPolled(box => box.Value2, () => new Vector2(GridMapWfcEditing.Width, GridMapWfcEditing.Depth));
            size.ValueChanged += (_, value) =>
            {
                if (!size.IsApplyingBinding)
                {
                    GridMapWfcEditing.Width = Math.Clamp((int)MathF.Round(value.X), 1, 96);
                    GridMapWfcEditing.Depth = Math.Clamp((int)MathF.Round(value.Y), 1, 96);
                }
            };
            size.WithTooltip("Cells across and deep");

            var seed = new NumericBox { IsInteger = true, Step = 1.0f, MinimumFieldHeight = 0.0f, Padding = new Thickness(6.0f, 1.0f) };
            seed.BindPolled(box => box.Value, () => GridMapWfcEditing.Seed);
            seed.ValueChanged += (_, value) =>
            {
                if (!seed.IsApplyingBinding)
                {
                    GridMapWfcEditing.Seed = (int)MathF.Round(value);
                }
            };
            seed.WithTooltip("The same seed lays the same dungeon");

            var rooms = new NumericBox { IsInteger = true, Step = 1.0f, Minimum = 0.0f, Maximum = 40.0f, MinimumFieldHeight = 0.0f, Padding = new Thickness(6.0f, 1.0f) };
            rooms.BindPolled(box => box.Value, () => GridMapWfcEditing.Rooms);
            rooms.ValueChanged += (_, value) =>
            {
                if (!rooms.IsApplyingBinding)
                {
                    GridMapWfcEditing.Rooms = Math.Clamp((int)MathF.Round(value), 0, 40);
                }
            };
            rooms.WithTooltip("Rooms to lay first, spread out, before corridors are generated to join them. 0 leaves it all to chance, which mostly makes corridors.");

            var bigRooms = new NumericBox { IsInteger = true, Step = 1.0f, Minimum = 0.0f, Maximum = 20.0f, MinimumFieldHeight = 0.0f, Padding = new Thickness(6.0f, 1.0f) };
            bigRooms.BindPolled(box => box.Value, () => GridMapWfcEditing.BigRooms);
            bigRooms.ValueChanged += (_, value) =>
            {
                if (!bigRooms.IsApplyingBinding)
                {
                    GridMapWfcEditing.BigRooms = Math.Clamp((int)MathF.Round(value), 0, 20);
                }
            };
            bigRooms.WithTooltip("Big rooms, built from the Big rooms pieces at any size in their range, laid before everything else.");

            var keep = new CheckBox().Label("Keep painted").OnChanged(value => GridMapWfcEditing.KeepPainted = value);
            keep.BindPolled(box => box.IsChecked, () => GridMapWfcEditing.KeepPainted);
            keep.WithTooltip("Build round the rules' tiles already in the area -- paint a room first, generate the rest round it.");

            var connected = new CheckBox().Label("One piece").OnChanged(value => GridMapWfcEditing.ConnectedOnly = value);
            connected.BindPolled(box => box.IsChecked, () => GridMapWfcEditing.ConnectedOnly);
            connected.WithTooltip("Keep only the largest joined part, so there are no stray corridors.");

            var generate = new ManaUI.Button().Label("Generate").Variant(ButtonVariant.Primary)
                .OnClick(() => Generate(gridMap, context, reroll: false));
            generate.WithTooltip("Fill the area on the floor being painted, on the active layer. One undo step.");

            var reroll = new ManaUI.Button().Label("Re-roll").OnClick(() => Generate(gridMap, context, reroll: true));
            reroll.WithTooltip("A new seed, and generate again");

            var props = new ManaUI.Button().Label("Props").OnClick(() => RerollProps(gridMap, context));
            props.BindPolled(button => button.Visibility, () =>
                !GridMapWfcEditing.Rules.IsProps && !string.IsNullOrWhiteSpace(GridMapWfcEditing.Rules.Then) ? Visibility.Visible : Visibility.Collapsed);
            props.WithTooltip("Furnish the dungeon again, with a new seed, leaving the dungeon as it is");

            var status = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Wrap();
            status.BindPolled(text => text.Text, () => GridMapWfcEditing.Status);

            return new StackPanel().Vertical(2.0f).Children(
                Row(theme, "From", origin),
                Row(theme, "Size", size),
                new Grid().Columns("*, *", theme.Spacing).Rows("Auto").Children(
                    Row(theme, "Seed", seed).GridColumn(0), Row(theme, "Rooms", rooms).GridColumn(1)),
                Row(theme, "Big", bigRooms),
                new StackPanel().Horizontal(theme.Spacing * 2.0f).Children(keep, connected),
                new StackPanel().Horizontal(theme.Spacing).Children(generate, reroll, props),
                status);
        }

        private static void Generate(ManaGridMapNode gridMap, InspectorContext context, bool reroll)
        {
            if (reroll)
            {
                GridMapWfcEditing.Seed = Random.Shared.Next(1, 100000);
            }

            var area = new GridMapWfcGenerator.Area(
                GridMapWfcEditing.X, GridMapWfcEditing.Z, GridMapWfcEditing.Width, GridMapWfcEditing.Depth, GridMapEditing.Floor);
            var before = gridMap.CaptureState();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = GridMapWfcGenerator.Generate(
                gridMap, GridMapWfcEditing.Rules, area, GridMapWfcEditing.Seed, GridMapWfcEditing.KeepPainted, GridMapWfcEditing.ConnectedOnly,
                GridMapWfcEditing.Rooms, GridMapWfcEditing.BigRooms);
            if (!result.Succeeded)
            {
                gridMap.RestoreState(before);
                GridMapWfcEditing.Status = result.Error ?? "Could not generate.";
                return;
            }

            var after = gridMap.CaptureState();
            context.History?.Record("Generate", () => gridMap.RestoreState(before), () => gridMap.RestoreState(after));
            var rooms = result.Placements.Count(p => GridMapWfcEditing.Rules.Tiles[p.Tile] is { Width: > 1 } or { Depth: > 1 });
            // Says when fewer big rooms fitted than were asked for, which otherwise just looks like none were made.
            var big = GridMapWfcEditing.Rules.BigRooms is { IsUsable: true } && GridMapWfcEditing.BigRooms > 0
                ? result.BigRooms.Count < GridMapWfcEditing.BigRooms
                    ? $", {result.BigRooms.Count} of {GridMapWfcEditing.BigRooms} big (no room for more at that size)"
                    : $", {result.BigRooms.Count} big"
                : string.Empty;
            var furnished = result.Props switch
            {
                null => string.Empty,
                { Succeeded: true } laid => $", {laid.Placements.Count} props",
                { } failed => $", no props ({failed.Error})"
            };
            GridMapWfcEditing.Status = GridMapWfcEditing.Rules.IsProps
                ? $"{result.Placements.Count} props. Seed {GridMapWfcEditing.Seed}, {clock.ElapsedMilliseconds} ms."
                : $"{result.Placements.Count} tiles, {rooms} rooms{big}{furnished}. Seed {GridMapWfcEditing.Seed}, {clock.ElapsedMilliseconds} ms"
                    + (result.Attempts > 1 ? $", {result.Attempts} tries." : ".");
        }

        /// <summary>The dungeon's props alone, again with a new seed: the rules the open ones name to follow them.</summary>
        private static void RerollProps(ManaGridMapNode gridMap, InspectorContext context)
        {
            if (GridMapWfcGenerator.FindRules(GridMapWfcEditing.Rules.Then) is not { IsProps: true } props)
            {
                GridMapWfcEditing.Status = $"No props rules called '{GridMapWfcEditing.Rules.Then}' in {GridMapWfcGenerator.FolderName}/.";
                return;
            }

            var area = new GridMapWfcGenerator.Area(
                GridMapWfcEditing.X, GridMapWfcEditing.Z, GridMapWfcEditing.Width, GridMapWfcEditing.Depth, GridMapEditing.Floor);
            var before = gridMap.CaptureState();
            var result = GridMapWfcGenerator.GenerateProps(
                gridMap, props, GridMapWfcEditing.Rules, area, Random.Shared.Next(1, 100000), GridMapWfcEditing.KeepPainted);
            if (!result.Succeeded)
            {
                gridMap.RestoreState(before);
                GridMapWfcEditing.Status = result.Error ?? "Could not lay props.";
                return;
            }

            var after = gridMap.CaptureState();
            context.History?.Record("Generate props", () => gridMap.RestoreState(before), () => gridMap.RestoreState(after));
            GridMapWfcEditing.Status = $"{result.Placements.Count} props.";
        }

        // ----- big rooms ------------------------------------------------------------------------------

        /// <summary>The pieces big rooms are built from, and how big they come -- folded away, as it is set once per kit.</summary>
        private static UiElement BigRoomSettings(UiTheme theme, MeshAssetLibrary library)
        {
            var rules = GridMapWfcEditing.Rules;
            var kit = rules.BigRooms ??= new GridMapWfcBigRooms();

            UiElement Model(Func<string?> read, Action<string?> write, string tooltip)
            {
                var name = new TextBlock().Ellipsis().CenterV();
                name.BindPolled(text => text.Text, () => read() is { } asset ? ManaGridMapNode.TileName(asset) : "none");
                name.BindPolled(text => text.Foreground, () => read() == null ? theme.TextMuted : theme.Text);
                var use = new ManaUI.Button().Label("Use selected").Variant(ButtonVariant.Subtle).OnClick(() =>
                {
                    if (library.SelectedAsset is { Length: > 0 } selected)
                    {
                        write(selected);
                        GridMapWfcEditing.Dirty = true;
                    }
                });
                use.BindPolled(button => button.IsEnabled, () => !string.IsNullOrWhiteSpace(library.SelectedAsset));
                var clear = new ManaUI.Button().Label("x").Variant(ButtonVariant.Subtle).OnClick(() =>
                {
                    write(null);
                    GridMapWfcEditing.Dirty = true;
                });
                return new Grid().Columns("*, Auto, Auto", 2.0f).Rows("Auto").Children(
                    name.GridColumn(0), use.GridColumn(1), clear.GridColumn(2)).WithTooltip(tooltip);
            }

            var size = new VectorBox().Components(2).Step(1.0f).Places(0);
            size.SetAxes(("Min", 0), ("Max", 2));
            size.IsInteger = true;
            size.Minimum = 3.0f;
            size.Maximum = 40.0f;
            size.Value2 = new Vector2(kit.MinSize, kit.MaxSize);
            size.ValueChanged += (_, value) =>
            {
                kit.MinSize = Math.Clamp((int)MathF.Round(value.X), 3, 40);
                kit.MaxSize = Math.Clamp((int)MathF.Round(value.Y), kit.MinSize, 40);
                GridMapWfcEditing.Dirty = true;
            };
            size.WithTooltip("Cells across and deep a big room can be, each picked at random between these");

            var offset = new NumericBox { Step = 0.05f, MinimumFieldHeight = 0.0f, Padding = new Thickness(6.0f, 1.0f) }.Places(2).Value(kit.WallOffset);
            offset.ValueChanged += (_, value) =>
            {
                kit.WallOffset = value;
                GridMapWfcEditing.Dirty = true;
            };
            offset.WithTooltip("How far the wall moves to its outside, in cells: 0.5 puts a kit wall built for a floor tile's edge on the cell's edge");

            ComboBox Turn(Func<int> read, Action<int> write, string tooltip)
            {
                var combo = new ComboBox().Options(new[] { "0°", "90°", "180°", "270°" });
                combo.SelectedIndex = ManaGridMapNode.WrapOrientation(read());
                combo.SelectionChanged += (_, index) =>
                {
                    if (index >= 0)
                    {
                        write(index);
                        GridMapWfcEditing.Dirty = true;
                    }
                };
                combo.WithTooltip(tooltip);
                return combo;
            }

            var body = new StackPanel().Vertical(2.0f).Children(
                Row(theme, "Floor", Model(() => kit.Floor, value => kit.Floor = value, "Laid in every cell but the corners. None turns big rooms off.")),
                Row(theme, "Wall", Model(() => kit.Wall, value => kit.Wall = value, "Along each edge cell's outside, on a walls layer, but at the doors")),
                Row(theme, "Corner", Model(() => kit.Corner, value => kit.Corner = value, "In each corner cell, with its own floor")),
                Row(theme, "Size", size),
                new Grid().Columns("*, *, *", theme.Spacing).Rows("Auto").Children(
                    Row(theme, "Out", offset).GridColumn(0),
                    Turn(() => kit.WallTurn, value => kit.WallTurn = value, "Turns that face the wall's outside to +Z").GridColumn(1),
                    Turn(() => kit.CornerTurn, value => kit.CornerTurn = value, "Turns that face the corner's outsides to +X and +Z").GridColumn(2)));

            return new Expander().Header("Big rooms").Content(body).Configure(expander => expander.IsExpanded = false);
        }

        // ----- the tiles --------------------------------------------------------------------------------

        private static UiElement TileList(UiTheme theme, MeshAssetLibrary library)
        {
            var rules = GridMapWfcEditing.Rules;
            var list = new StackPanel().Vertical(2.0f);
            var heading = new TextBlock().Text($"Tiles ({rules.Tiles.Count})").FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).CenterV();
            var add = new ManaUI.Button().Label("+ Tile").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                rules.Tiles.Add(new GridMapWfcTile
                {
                    Name = "Tile",
                    Asset = library.SelectedAsset is { Length: > 0 } selected ? selected : null,
                    Sockets = new() { ["e"] = ["open"], ["n"] = ["wall"], ["w"] = ["open"], ["s"] = ["wall"] }
                });
                GridMapWfcEditing.Changed();
            });
            add.WithTooltip("Add a tile, with the model selected in the asset browser");
            list.Children.Add(new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").Children(heading.GridColumn(0), add.GridColumn(1)));

            for (var i = 0; i < rules.Tiles.Count; i++)
            {
                list.Children.Add(TileEditor(theme, rules, i, library));
            }

            return list;
        }

        private static UiElement TileEditor(UiTheme theme, GridMapWfcRules rules, int index, MeshAssetLibrary library)
        {
            var tile = rules.Tiles[index];
            var expander = new Expander().Header(Summary(tile));
            expander.IsExpanded = false;
            expander.ContentPadding = new Thickness(6.0f, 2.0f, 0.0f, 4.0f);

            void Changed() => GridMapWfcEditing.Dirty = true;

            var name = new TextBox { Text = tile.Name };
            name.TextCommitted += (_, text) =>
            {
                tile.Name = string.IsNullOrWhiteSpace(text) ? tile.Name : text.Trim();
                expander.Header = Summary(tile);
                Changed();
            };

            var model = new TextBlock().Ellipsis().CenterV();
            model.BindPolled(text => text.Text, () => tile.Asset == null ? "none: lays nothing" : ManaGridMapNode.TileName(tile.Asset));
            model.BindPolled(text => text.Foreground, () => tile.Asset == null ? theme.TextMuted : theme.Text);
            var use = new ManaUI.Button().Label("Use selected").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                if (library.SelectedAsset is { Length: > 0 } selected)
                {
                    tile.Asset = selected;
                    Changed();
                }
            });
            use.BindPolled(button => button.IsEnabled, () => !string.IsNullOrWhiteSpace(library.SelectedAsset));
            use.WithTooltip("Use the model selected in the asset browser");
            var clear = new ManaUI.Button().Label("x").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                tile.Asset = null;
                Changed();
            });
            clear.WithTooltip("No model: the tile is empty space");

            var size = new VectorBox().Components(2).Step(1.0f).Places(0);
            size.SetAxes(("W", 0), ("D", 2));
            size.IsInteger = true;
            size.Minimum = 1.0f;
            size.Maximum = 16.0f;
            size.Value2 = new Vector2(tile.Width, tile.Depth);
            size.ValueChanged += (_, value) =>
            {
                tile.Size = [Math.Clamp((int)MathF.Round(value.X), 1, 16), Math.Clamp((int)MathF.Round(value.Y), 1, 16)];
                expander.Header = Summary(tile);
                Changed();
            };
            size.WithTooltip("Cells covered, as modelled");

            var weight = new NumericBox { Step = 0.1f, Minimum = 0.0f, MinimumFieldHeight = 0.0f, Padding = new Thickness(6.0f, 1.0f) }.Places(2).Value(tile.Weight);
            weight.ValueChanged += (_, value) =>
            {
                tile.Weight = MathF.Max(0.0f, value);
                expander.Header = Summary(tile);
                Changed();
            };
            weight.WithTooltip("How often it is picked, against the others");

            var smooth = new NumericBox { Step = 0.01f, Minimum = 0.0f, MinimumFieldHeight = 0.0f, Padding = new Thickness(6.0f, 1.0f) }.Places(2).Value(tile.Smooth);
            smooth.ValueChanged += (_, value) =>
            {
                tile.Smooth = MathF.Max(0.0f, value);
                Changed();
            };
            smooth.WithTooltip("Collision is flat below this height, in the model's units, so rubble does not catch feet");

            var turns = new ComboBox().Options(RotationNames);
            turns.SelectedIndex = Array.IndexOf(RotationValues, tile.Rotations);
            turns.SelectionChanged += (_, choice) =>
            {
                if (choice >= 0)
                {
                    tile.Rotations = RotationValues[choice];
                    Changed();
                }
            };

            var remove = new ManaUI.Button().Label("Remove").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                rules.Tiles.Remove(tile);
                GridMapWfcEditing.Changed();
            });

            return expander.Content(new StackPanel().Vertical(2.0f).Children(
                Row(theme, "Name", name),
                Row(theme, "Model", new Grid().Columns("*, Auto, Auto", 2.0f).Rows("Auto").Children(
                    model.GridColumn(0), use.GridColumn(1), clear.GridColumn(2))),
                Row(theme, "Size", size),
                new Grid().Columns("*, *", theme.Spacing).Rows("Auto").Children(
                    Row(theme, "Weight", weight).GridColumn(0), turns.GridColumn(1)),
                Row(theme, "Smooth", smooth),
                Row(theme, "Tag", TagField(tile, Changed)),
                Row(theme, "On", OnField(tile, Changed)),
                new Grid().Columns("*, *", theme.Spacing).Rows("Auto").Children(
                    Row(theme, "Unit", UnitField(tile, Changed)).GridColumn(0),
                    Row(theme, "Offset", OffsetField(tile, Changed)).GridColumn(1)),
                Sockets(theme, rules, tile, Changed),
                remove));
        }

        private static UiElement TagField(GridMapWfcTile tile, Action changed)
        {
            var field = new TextBox { Text = tile.Tag ?? string.Empty }.Hint(tile.EffectiveTag);
            field.TextCommitted += (_, text) =>
            {
                tile.Tag = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                changed();
            };
            field.WithTooltip("What its cells are to a props pass: room, corridor, empty... Unset, it is guessed from the tile.");
            return field;
        }

        private static UiElement OnField(GridMapWfcTile tile, Action changed)
        {
            var field = new TextBox { Text = tile.On is { } on ? string.Join(", ", on) : string.Empty }.Hint("anywhere");
            field.TextCommitted += (_, text) =>
            {
                var tags = GridMapWfcPresets.Split(text);
                tile.On = tags.Length == 0 ? null : tags;
                changed();
            };
            field.WithTooltip("For props: the tags of the cells it may stand in, comma separated. Empty is anywhere.");
            return field;
        }

        private static UiElement UnitField(GridMapWfcTile tile, Action changed)
        {
            var field = new NumericBox { Step = 0.1f, Minimum = 0.0f, MinimumFieldHeight = 0.0f, Padding = new Thickness(6.0f, 1.0f) }.Places(2).Value(tile.ModelCell);
            field.ValueChanged += (_, value) =>
            {
                tile.ModelCell = MathF.Max(0.0f, value);
                changed();
            };
            field.WithTooltip("One cell in the model's units, to keep a prop its own size. 0 fits the model to its cells.");
            return field;
        }

        private static UiElement OffsetField(GridMapWfcTile tile, Action changed)
        {
            var offset = tile.Offset is { Length: >= 3 } o ? new Vector3(o[0], o[1], o[2]) : Vector3.Zero;
            var field = new VectorBox().Step(0.05f).Places(2).Value(offset);
            field.ValueChanged += (_, value) =>
            {
                tile.Offset = value.X == 0.0f && value.Y == 0.0f && value.Z == 0.0f ? null : [value.X, value.Y, value.Z];
                changed();
            };
            field.WithTooltip("Shift the model within its tile, in cells, before it is turned");
            return field;
        }

        private static string Summary(GridMapWfcTile tile) =>
            $"{tile.Name}  {tile.Width}x{tile.Depth}  x{tile.Weight:0.##}";

        /// <summary>
        /// The labels on each side, laid out as the sides are seen from above with -Z up. A side covering several
        /// cells takes one label per cell, separated by commas, in order of rising X or Z.
        /// </summary>
        private static UiElement Sockets(UiTheme theme, GridMapWfcRules rules, GridMapWfcTile tile, Action changed)
        {
            // Rules that name their kinds of side get a list to pick from for a one-cell tile's sides; a label
            // the list does not know, or a side covering several cells, is typed.
            UiElement Side(int side, string tooltip)
            {
                var key = GridMapWfc.SideKeys[side];
                var current = tile.Sockets.TryGetValue(key, out var have) && have.Length == 1 ? have[0] : null;
                if (rules.SideNames.Count > 0 && tile.Width == 1 && tile.Depth == 1 && (current == null || rules.SideNames.ContainsKey(current)))
                {
                    var kinds = rules.SideNames.ToList();
                    var combo = new ComboBox().Options(kinds.Select(kind => kind.Value).ToArray());
                    combo.SelectedIndex = Math.Max(0, kinds.FindIndex(kind => kind.Key == current));
                    combo.SelectionChanged += (_, index) =>
                    {
                        if (index >= 0)
                        {
                            tile.Sockets[key] = [kinds[index].Key];
                            changed();
                        }
                    };
                    combo.WithTooltip(tooltip.Split(':')[0] + ": what this side of it needs next to it");
                    return combo;
                }

                var box = new TextBox { Text = string.Join(",", tile.Sockets.TryGetValue(key, out var labels) ? labels : [GridMapWfc.DefaultSocket]) };
                box.TextCommitted += (_, text) =>
                {
                    tile.Sockets[key] = GridMapWfcPresets.Split(text);
                    changed();
                };
                box.WithTooltip(tooltip);
                return box;
            }

            var middle = new TextBlock().Text("sides").FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Center();
            return new Grid().Columns("*, Auto, *", 2.0f).Rows("Auto, Auto, Auto", 2.0f).Children(
                Side(1, "-Z side: one label per cell, left to right (rising X)").GridColumn(1).GridRow(0),
                Side(2, "-X side: one label per cell, top to bottom (rising Z)").GridColumn(0).GridRow(1),
                middle.GridColumn(1).GridRow(1),
                Side(0, "+X side: one label per cell, top to bottom (rising Z)").GridColumn(2).GridRow(1),
                Side(3, "+Z side: one label per cell, left to right (rising X)").GridColumn(1).GridRow(2));
        }

        private static void Edit(Action change)
        {
            change();
            GridMapWfcEditing.Dirty = true;
        }

        private static UiElement Row(UiTheme theme, string label, UiElement editor)
        {
            var name = new TextBlock().Text(label).Foreground(theme.TextMuted).CenterV();
            name.Width = 46.0f;
            return new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").Children(name.GridColumn(0), editor.GridColumn(1));
        }
    }
}
